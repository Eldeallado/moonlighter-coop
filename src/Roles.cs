using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Roles (host setting): on the way into a dungeon each player picks one, and keeps it for the whole run.
    /// Guardian: takes 30 % less damage and enemies prefer him. Fighter: 25 % more damage, takes 15 % more.
    /// Seeker: 30 % more gold in the dungeon. Healer: drinking a potion also heals everyone else 30 %.
    /// With 4 players or fewer, a role can only be taken once.
    public static class Roles
    {
        public const int None = 0, Guardian = 1, Fighter = 2, Seeker = 3, Healer = 4;
        public static readonly string[] Names = { "Sin rol", "Guardián", "Luchador", "Buscador", "Sanador" };
        public static readonly string[] Texts = { "", "Recibe un 30 % menos de daño y los enemigos le prefieren", "Hace un 25 % más de daño, pero recibe un 15 % más", "Un 30 % más de oro en la mazmorra", "Al beber una poción, cura también un 30 % a los demás" };

        public static int Mine;
        public static readonly Dictionary<int, int> Of = new Dictionary<int, int>();
        public static bool Choosing;
        public static float ChooseUntil;

        public static int RoleOf(int player) => player == Session.MyId ? Mine : Of.TryGetValue(player, out var r) ? r : None;
        public static bool Active => HostSettings.Roles && Net.Juego && GameManager.Instance != null && GameManager.Instance.IsDungeonSceneLoaded();

        /// For the enemies' choice of target: the Guardian counts as if he were closer.
        public static float Pull(int player) => Active && RoleOf(player) == Guardian ? 0.35f : 1f;
        public static float DamageDealt => Active && Mine == Fighter ? 1.25f : 1f;
        public static float DamageTaken => !Active ? 1f : Mine == Guardian ? 0.7f : Mine == Fighter ? 1.15f : 1f;

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
            if (amount > 0 && Roles.Active && Roles.Mine == Roles.Seeker && !Shared.ApplyingGold) amount = Mathf.RoundToInt(amount * 1.3f);
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
            float k = Mathf.Clamp(Screen.height / 720f, 0.5f, 2f);
            if (_t == null)
            {
                _t = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _b = new GUIStyle(GUI.skin.button) { wordWrap = true }; _bOn = new GUIStyle(GUI.skin.button) { wordWrap = true, fontStyle = FontStyle.Bold };
                _s = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            }
            _t.fontSize = (int)(20 * k); _b.fontSize = _bOn.fontSize = (int)(13 * k); _s.fontSize = (int)(12 * k);
            _t.normal.textColor = new Color(1f, 0.85f, 0.45f); _bOn.normal.textColor = new Color(1f, 0.85f, 0.45f);
            float w = 760 * k, h = 190 * k;
            var r = new Rect((Screen.width - w) / 2f, Screen.height - h - 40 * k, w, h);
            var o = GUI.color; GUI.color = new Color(0.09f, 0.07f, 0.06f, 0.95f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o;
            GUI.Label(new Rect(r.x, r.y + 6 * k, r.width, 30 * k), $"Elige tu rol para esta expedición ({Mathf.CeilToInt(Roles.ChooseUntil - Time.unscaledTime)} s)", _t);
            float bw = (w - 50 * k) / 4f;
            for (int i = 1; i <= 4; i++)
            {
                var br = new Rect(r.x + 10 * k + (i - 1) * (bw + 10 * k), r.y + 42 * k, bw, 110 * k);
                bool taken = Roles.Taken(i);
                GUI.enabled = !taken;
                bool on = Roles.Mine == i || (PadNav.Active && _sel == i);
                if (GUI.Button(br, $"{i}. {Roles.Names[i]}\n\n{Roles.Texts[i]}" + (taken ? "\n(ya elegido)" : ""), on ? _bOn : _b)) Roles.Pick(i);
                GUI.enabled = true;
            }
            GUI.Label(new Rect(r.x, r.yMax - 32 * k, r.width, 26 * k), PadNav.Active ? "Cruceta ←→ y A" : "Pulsa 1, 2, 3 o 4, o haz clic", _s);
        }
    }
}
