using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Roles (host setting): on the way into a dungeon each player picks one, and keeps it for the whole run.
    /// Guardian: +30 % health, 15 % less damage taken, enemies prefer him. Fighter: +20 % damage, −15 % health.
    /// Seeker: an extra roll of loot in each chest he opens first, secret rooms marked on the map, picks up from
    /// further away, 30 % more gold. Healer: his potion heals the others 30 %, he revives with a potion from
    /// further and with twice the health, and fountains heal him twice as fast.
    /// With 4 players or fewer, a role can only be taken once.
    public static class Roles
    {
        public const int None = 0, Guardian = 1, Fighter = 2, Seeker = 3, Healer = 4;
        public static readonly string[] Names = { "Sin rol", "Guardián", "Luchador", "Buscador", "Sanador" };
        public static readonly string[] Texts = { "",
            "+30 % de vida, recibe un 15 % menos de daño y los enemigos le prefieren",
            "+20 % de daño, pero un 15 % menos de vida",
            "Más botín en los cofres que abre, ve las salas secretas en el mapa, recoge desde más lejos y +30 % de oro",
            "Su poción cura a todos, revive con poción desde más lejos y con el doble de vida, y la fuente le cura el doble" };

        public static bool Is(int role) => Active && Mine == role;

        public static int Mine;
        public static readonly Dictionary<int, int> Of = new Dictionary<int, int>();
        public static bool Choosing;
        public static float ChooseUntil;

        public static int RoleOf(int player) => player == Session.MyId ? Mine : Of.TryGetValue(player, out var r) ? r : None;
        public static bool Active => HostSettings.Roles && Net.Juego && GameManager.Instance != null && GameManager.Instance.IsDungeonSceneLoaded();

        /// For the enemies' choice of target: the Guardian counts as if he were closer.
        public static float Pull(int player) => Active && RoleOf(player) == Guardian ? 0.35f : 1f;
        public static float DamageDealt => Is(Fighter) ? 1.2f : 1f;
        public static float DamageTaken => Is(Guardian) ? 0.85f : 1f;
        private static float HealthFactor => Is(Guardian) ? 0.3f : Is(Fighter) ? -0.15f : 0f;

        // Max health: added on top like the game's amulets do (equipment changes add/subtract their own part),
        // and taken back off when the role stops counting (back in town, or a different role).
        private static float _healthAdded;
        private static HeroMerchant _healthHero;
        private static void TickHealth()
        {
            var hero = HeroMerchant.Instance;
            if (hero != _healthHero) { _healthHero = hero; _healthAdded = 0f; }   // a new Will starts clean
            if (hero == null || GameManager.Instance == null || !GameManager.Instance.NotLoadingScene || Downed.Me) return;
            var hp = hero.heroMerchantStats.currentHealth;
            float baseMax = (float)hp.maxValue - _healthAdded;
            float want = Mathf.Round(baseMax * HealthFactor);
            if (Mathf.Approximately(want, _healthAdded)) return;
            float change = want - _healthAdded;
            bool v = hp.variableStat; hp.variableStat = true;
            hp.maxValue = (float)hp.maxValue + change;
            // Gaining health fills the new part; losing it only trims what no longer fits.
            hp.Value = Mathf.Clamp((float)hp.Value + Mathf.Max(0f, change), 1f, (float)hp.maxValue);
            hp.variableStat = v;
            _healthAdded = want;
            Plugin.Log.LogInfo($"[roles] max health {change:+0;-0} ({Names[Mine]})");
        }

        public static bool Taken(int role)
        {
            if (Session.Players > 4 || role == None) return false;
            foreach (var kv in Of) if (kv.Key != Session.MyId && kv.Value == role) return true;
            return false;
        }

        public static void Pick(int role)
        {
            if (Taken(role)) { Banner.Show(Names[role] + " ya lo ha elegido otro"); return; }
            Mine = role; Choosing = false;
            Plugin.SaveRole(role);
            Send();
            Banner.Show("Tu rol: " + Names[role] + ". " + Texts[role]);
        }

        public static void Send() { if (Net.Connected) Net.Send(Msg.Role, new[] { (byte)Session.MyId, (byte)Mine }); }
        public static void OnRole(byte[] data) { if (data.Length >= 2 && data[0] != Session.MyId) Of[data[0]] = data[1]; }

        private static string _lastZone = "";
        public static void Tick()
        {
            string zone = Session.LocalZone();
            // Entering a dungeon from town: choose (25 s, then the last role stays).
            if (HostSettings.Roles && Net.Juego && zone.StartsWith("dungeon:") && !_lastZone.StartsWith("dungeon") && GameManager.Instance.NotLoadingScene && !Session.TutorialActivo)
            {
                Choosing = true; ChooseUntil = Time.unscaledTime + 25f;
                if (Mine == None || Taken(Mine)) Mine = FirstFree();
                Send();
            }
            // Remember where we are only once loaded (not the loading in-between), or the entry would be missed.
            if ((zone.StartsWith("town") || zone.StartsWith("dungeon:")) && GameManager.Instance != null && GameManager.Instance.NotLoadingScene) _lastZone = zone;
            if (Choosing && Time.unscaledTime > ChooseUntil) { Choosing = false; Pick(Taken(Mine) ? FirstFree() : Mine); }
            TickHealth();
        }

        private static int FirstFree() { for (int r = 1; r <= 4; r++) if (!Taken(r)) return r; return Guardian; }

        // Healer: a potion heals the others too.
        public static void OnHeal(byte[] data)
        {
            var hero = HeroMerchant.Instance;
            if (hero == null || Downed.Me || data.Length < 4) return;
            float f = new BinaryReader(new MemoryStream(data)).ReadSingle();
            var st = hero.heroMerchantStats;
            st.currentHealth.Value = Mathf.Min(st.currentHealth.maxValue, st.currentHealth.Value + st.currentHealth.maxValue * Mathf.Clamp01(f));
            Banner.Show("El Sanador te ha curado");
        }
    }

    [HarmonyPatch(typeof(Enemy), nameof(Enemy.DealDamageToEnemy))]
    static class RoleDamageDealt
    {
        [HarmonyPriority(Priority.First + 1)]
        static void Prefix(ref float attackStrength) { if (!EnemySync.ApplyingRemoteHit) attackStrength *= Roles.DamageDealt; }
    }

    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.DealDamageToPlayer))]
    static class RoleDamageTaken
    {
        static void Prefix(ref float attackStrength) { attackStrength *= Roles.DamageTaken; }
    }

    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.addGold))]
    static class RoleSeekerGold
    {
        static void Prefix(ref int amount)
        {
            if (amount > 0 && Roles.Is(Roles.Seeker) && !Shared.ApplyingGold) amount = Mathf.RoundToInt(amount * 1.3f);
        }
    }

    [HarmonyPatch(typeof(HeroMerchantController), nameof(HeroMerchantController.ConsumeHotkeyItem))]
    static class RoleHealerPotion
    {
        static void Postfix()
        {
            if (!Roles.Active || Roles.Mine != Roles.Healer) return;
            var ms = new MemoryStream(); new BinaryWriter(ms).Write(0.3f);
            Net.Send(Msg.RoleHeal, ms.ToArray());
        }
    }

    // Seeker: the first time he opens a chest, it gets a second roll of the game's own loot for that chest.
    [HarmonyPatch(typeof(Chest), nameof(Chest.OpenChest))]
    static class RoleSeekerChest
    {
        static void Prefix(Chest __instance, out bool __state) { __state = Traverse.Create(__instance).Field("_firstTimeOpened").GetValue<bool>(); }
        static void Postfix(Chest __instance, bool __state)
        {
            if (!__state || Traverse.Create(__instance).Field("_firstTimeOpened").GetValue<bool>() || !Roles.Is(Roles.Seeker)) return;
            var t = __instance.type;
            if (t != Chest.chestType.WOOD && t != Chest.chestType.IRON && t != Chest.chestType.GOLD && t != Chest.chestType.PURPLE && t != Chest.chestType.BLUE) return;
            // Its own random numbers, so the shared (seeded) ones stay the same for everyone.
            var outer = Random.state;
            Random.InitState(System.Environment.TickCount);
            List<ItemStack> extra = null;
            try
            {
                extra = Moonlighter.DungeonGeneration.ChestGeneration.GenerateChestContent(DungeonManager.Instance.culture, t, __instance.capacity,
                    Constants.GetIntArray("maxNumberOfDifferentItemsPerChest")[(int)t], Constants.GetIntArray("maxItemStackPerChest")[(int)t], __instance.dropIncrement);
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[roles] seeker chest: " + e.Message); }
            finally { Random.state = outer; }
            if (extra == null) return;
            int added = 0;
            foreach (var st in extra)
            {
                if (st == null) continue;
                if (added < 2 && __instance.TryAddItem(st)) { added++; CultureManager.Instance.DiscoverItem(st.master); }
                else Object.Destroy(st.gameObject);
            }
            if (added > 0) Banner.Show("Buscador: has encontrado algo más en el cofre");
            Plugin.Log.LogInfo($"[roles] seeker chest +{added}");
        }
    }

    // Seeker: rooms with a secret room show its mark on the map before it is found.
    [HarmonyPatch(typeof(MinimapPanel), "GetRoomIconSprites")]
    static class RoleSeekerMap
    {
        static void Postfix(MinimapPanel __instance, DungeonRoom room, List<Sprite> __result)
        {
            if (Roles.Is(Roles.Seeker) && room != null && room.secretRoom != null && !room.secretRoom.hasBeenVisited && !__result.Contains(__instance.SecretIcon))
                __result.Add(__instance.SecretIcon);
        }
    }

    // Seeker: things on the floor fly to him from further away.
    [HarmonyPatch(typeof(PickOnWalk), "FixedUpdate")]
    static class RoleSeekerMagnet
    {
        const float Range = 60f, Speed = 220f;
        static void Postfix(PickOnWalk __instance)
        {
            if (!__instance.CanPick || !Roles.Is(Roles.Seeker) || Downed.Me) return;
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            Vector2 to = hero.transform.position - __instance.transform.position;
            if (to.magnitude > Range || to.magnitude < 1f) return;
            __instance.isBeingAttracted = true;
            var stack = __instance.GetComponentInParent<ItemStack>();
            var root = stack != null ? stack.transform : __instance.transform;
            root.position += (Vector3)Vector2.ClampMagnitude(to.normalized * Speed * Time.fixedDeltaTime, to.magnitude);
        }
    }

    // Healer: fountains heal him twice as fast (the pool empties at its usual pace).
    [HarmonyPatch(typeof(HealingArea), "ApplyHealingEffect")]
    static class RoleHealerFountain
    {
        static void Prefix(out float __state) { __state = HeroMerchant.Instance != null ? (float)HeroMerchant.Instance.heroMerchantStats.currentHealth.Value : 0f; }
        static void Postfix(float __state)
        {
            var hero = HeroMerchant.Instance;
            if (hero == null || !Roles.Is(Roles.Healer) || Downed.Me) return;
            var hp = hero.heroMerchantStats.currentHealth;
            float gained = (float)hp.Value - __state;
            if (gained > 0f) hp.Value = Mathf.Min((float)hp.maxValue, (float)hp.Value + gained);
        }
    }

    /// The role picker, on the way into a dungeon.
    public class RolePanel : MonoBehaviour
    {
        private GUIStyle _t, _b, _bOn, _s;
        private int _sel = 1;

        private void Update()
        {
            if (!Roles.Choosing) return;
            PadNav.Poll();
            for (int r = 1; r <= 4; r++) if (Input.GetKeyDown(KeyCode.Alpha0 + r)) Roles.Pick(r);
            if (PadNav.Left) _sel = _sel <= 1 ? 4 : _sel - 1;
            if (PadNav.Right) _sel = _sel >= 4 ? 1 : _sel + 1;
            if (PadNav.A) Roles.Pick(_sel);
        }

        private void OnGUI()
        {
            if (!Roles.Choosing) return;
            Ui.Load();
            float k = Ui.K;
            float w = 840 * k, h = 290 * k;
            var r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f - 40 * k, w, h);   // above the notices at the bottom
            Ui.Sheet(r);
            Ui.Label(new Rect(r.x, r.y + 38 * k, r.width, 30 * k), $"Elige tu rol para esta expedición  ({Mathf.CeilToInt(Roles.ChooseUntil - Time.unscaledTime)} s)", 22f, Ui.Title, TextAnchor.MiddleCenter);
            float bw = (w - 70 * k) / 4f;
            for (int i = 1; i <= 4; i++)
            {
                var br = new Rect(r.x + 20 * k + (i - 1) * (bw + 10 * k), r.y + 74 * k, bw, 150 * k);
                bool taken = Roles.Taken(i);
                bool mine = Roles.Mine == i, focus = PadNav.Active && _sel == i;
                var o = GUI.color; if (taken) GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Ui.Card(br, mine, focus);
                // Number key in a little wooden cap, then the name and what it does.
                var cap = new Rect(br.x + 8 * k, br.y + 8 * k, 22 * k, 22 * k);
                Ui.Box(cap, Ui.Outline); Ui.Box(new Rect(cap.x + Ui.U, cap.y + Ui.U, cap.width - 2 * Ui.U, cap.height - 2 * Ui.U), Ui.Wood);
                Ui.Label(cap, i.ToString(), 15f, Ui.Cream, TextAnchor.MiddleCenter);
                Ui.Label(new Rect(cap.xMax + 8 * k, br.y + 6 * k, br.width - 40 * k, 26 * k), Roles.Names[i], 19f, mine ? Ui.Title : Ui.Ink);
                Ui.Box(new Rect(br.x + 8 * k, br.y + 36 * k, br.width - 16 * k, Mathf.Max(1f, Ui.U * 0.5f)), Ui.InkFaint);
                Ui.Label(new Rect(br.x + 10 * k, br.y + 42 * k, br.width - 20 * k, br.height - 50 * k), Roles.Texts[i] + (taken ? "\n\n(ya lo tiene otro)" : ""), 14.5f, Ui.Ink, TextAnchor.UpperLeft, true);
                GUI.color = o;
                if (!taken && GUI.Button(br, GUIContent.none, GUIStyle.none)) Roles.Pick(i);
            }
            Ui.Label(new Rect(r.x, r.yMax - 58 * k, r.width, 22 * k), PadNav.Active ? "Cruceta izquierda y derecha, y A para elegir" : "Pulsa 1, 2, 3 o 4, o haz clic", 14.5f, Ui.InkSoft, TextAnchor.MiddleCenter);
        }
    }
}