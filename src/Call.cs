using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Ready checks. "Coming?": when the host walks into a dungeon portal, the others get a countdown (8 s)
    /// instead of being dragged at once. "Ready for the boss?" (host setting): whoever reaches the boss door
    /// waits until everybody is ready (or 20 s). Answers: H = ready / on my way, G = wait (15 s more, once each).
    public static class Call
    {
        public enum Kind : byte { Dungeon = 0, Boss = 1 }
        public const float Extra = 15f;
        public static bool Approved;               // our own action, after the check

        // ---------- the player who started it ----------
        private static bool _calling;
        private static Kind _kind;
        private static ItemMaster.Culture _culture;
        private static DungeonBossRoomEnterDoor _door;
        private static float _deadline;
        private static readonly HashSet<int> _ready = new HashSet<int>(), _waited = new HashSet<int>();

        public static bool Start(Kind kind, ItemMaster.Culture culture, DungeonBossRoomEnterDoor door = null)
        {
            if (_calling) return true;
            _calling = true; _kind = kind; _culture = culture; _door = door;
            _deadline = Time.unscaledTime + (kind == Kind.Boss ? 20f : 8f);
            _ready.Clear(); _waited.Clear();
            Send();
            Banner.Show(kind == Kind.Boss ? "Sala del jefe: esperando a que estéis todos listos" : "Avisando a tus compañeros: entráis a la mazmorra en 8 s");
            Plugin.Log.LogInfo($"[call] {kind} check started by me");
            return true;
        }

        private static void Send()
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)Session.MyId); w.Write((byte)_kind); w.Write((int)_culture); w.Write(Mathf.Max(0f, _deadline - Time.unscaledTime));
            Net.Send(Msg.Call, ms.ToArray());
        }

        public static void OnReply(byte[] data)
        {
            if (data.Length < 3) return;
            int to = data[0], from = data[1], answer = data[2];
            if (!_calling || to != Session.MyId) return;
            if (answer == 1) { if (_ready.Add(from)) Banner.Show($"Jugador {from + 1}: ¡listo!"); }
            else if (answer == 2 && _waited.Add(from))
            {
                _deadline += Extra; Send();
                Banner.Show($"Jugador {from + 1} pide que esperes: {Mathf.CeilToInt(Extra)} s más");
            }
        }

        public static void Tick()
        {
            if (!_calling) return;
            var hero = HeroMerchant.Instance;
            if (hero == null || !Net.Juego) { _calling = false; return; }
            int others = Session.Players - 1 - (_kind == Kind.Boss ? Downed.Ghosts.Count : 0);
            if (_ready.Count < others && Time.unscaledTime < _deadline) return;
            _calling = false;
            Approved = true;
            try
            {
                if (_kind == Kind.Dungeon) hero.LoadDungeonScene(_culture);
                else if (_door != null) _door.OnPlayerEnter(hero.gameObject);
            }
            finally { Approved = false; }
            Net.Send(Msg.Call, new byte[] { (byte)Session.MyId, 255 });   // close the boxes
        }

        public static bool Calling => _calling;

        // ---------- everybody else ----------
        public static int ShownFrom;
        public static Kind ShownKind;
        public static ItemMaster.Culture ShownCulture;
        public static float ShownUntil;
        public static bool Answered;

        public static void OnCall(byte[] data)
        {
            if (data.Length >= 2 && data[1] == 255) { if (data[0] == ShownFrom) ShownUntil = 0f; return; }
            var r = new BinaryReader(new MemoryStream(data));
            int from = r.ReadByte(); if (from == Session.MyId) return;
            var kind = (Kind)r.ReadByte(); var culture = (ItemMaster.Culture)r.ReadInt32(); float left = r.ReadSingle();
            if (Time.unscaledTime > ShownUntil || from != ShownFrom) Answered = false;   // a new check
            ShownFrom = from; ShownKind = kind; ShownCulture = culture;
            ShownUntil = Time.unscaledTime + left;
        }

        public static void Reply(bool ready)
        {
            if (Answered && ready) return;
            Answered = true;
            Net.Send(Msg.CallReply, new[] { (byte)ShownFrom, (byte)Session.MyId, (byte)(ready ? 1 : 2) });
        }

        public static string CultureName(ItemMaster.Culture c)
        {
            switch (c)
            {
                case ItemMaster.Culture.Golem: return "los Gólems";
                case ItemMaster.Culture.Forest: return "el Bosque";
                case ItemMaster.Culture.Desert: return "el Desierto";
                case ItemMaster.Culture.Tech: return "la Tecnología";
                default: return "lo Desconocido";
            }
        }
    }

    // Host walks into a portal from town: ask first (the travel itself happens in Call.Tick).
    [HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.LoadDungeonScene))]
    static class CallBeforeDungeon
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(ItemMaster.Culture culture)
        {
            if (Call.Approved || !Session.IsHost || !Net.Juego || Net.ConnectedCount == 0 || Session.TutorialActivo) return true;
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsTownSceneLoaded() || gm.IsDungeonSceneLoaded()) return true;
            return !Call.Start(Call.Kind.Dungeon, culture);
        }
    }

    // The boss door (host setting): wait until everybody is ready, then the one who touched it goes in (and,
    // as with every door, the others come along).
    [HarmonyPatch(typeof(DungeonBossRoomEnterDoor), nameof(DungeonBossRoomEnterDoor.OnPlayerEnter))]
    static class CallBeforeBoss
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(DungeonBossRoomEnterDoor __instance, GameObject player)
        {
            if (Call.Approved || !HostSettings.BossWait || !Net.Juego || Session.Players < 2 || Downed.Me) return true;
            if (HeroMerchant.Instance == null || player != HeroMerchant.Instance.gameObject || __instance.isBossDefeated || __instance.willChallengedBoss) return true;
            if (Dungeon.Following || Dungeon.ApplyingRemote) return true;   // coming along with someone else
            var dm = DungeonManager.Instance;
            return !Call.Start(Call.Kind.Boss, dm != null ? dm.culture : ItemMaster.Culture.Golem, __instance);
        }
    }

    /// The check's box on the others' screens.
    public class CallPanel : MonoBehaviour
    {
        private bool Showing => Time.unscaledTime < Call.ShownUntil;

        private void Update()
        {
            if (!Showing) return;
            PadNav.Poll();
            if (Input.GetKeyDown(KeyCode.H) || PadNav.Up) Call.Reply(true);
            if (Input.GetKeyDown(KeyCode.G) || PadNav.Down) Call.Reply(false);
        }

        private void OnGUI()
        {
            if (!Showing) return;
            Ui.Load();
            float k = Ui.K;
            float w = 620 * k, h = 118 * k;
            var r = new Rect((Screen.width - w) / 2f, 60 * k, w, h);
            Ui.Scroll(r);
            int left = Mathf.CeilToInt(Call.ShownUntil - Time.unscaledTime);
            string who = Call.ShownFrom == 0 ? "El anfitrión" : "El jugador " + (Call.ShownFrom + 1);
            string text = Call.ShownKind == Call.Kind.Boss
                ? $"{who} está en la puerta del jefe: entráis en {left} s"
                : $"{who} entra en la mazmorra de {Call.CultureName(Call.ShownCulture)}: vais todos en {left} s";
            Ui.Label(new Rect(r.x + 40 * k, r.y + 24 * k, r.width - 80 * k, 46 * k), text, 19f, Ui.Ink, TextAnchor.MiddleCenter, true);
            Ui.Divider(r.x + 120 * k, r.y + 74 * k, r.width - 240 * k);
            Ui.Label(new Rect(r.x + 40 * k, r.y + 82 * k, r.width - 80 * k, 26 * k),
                Call.Answered ? "Respuesta enviada" : (PadNav.Active ? "Cruceta arriba: ¡Listo!  ·  abajo: ¡Espera! (15 s más)" : "H: ¡Listo! ¡Voy!   ·   G: ¡Espera! (15 s más)"), 15.5f, Ui.Ink, TextAnchor.MiddleCenter);
        }
    }
}

namespace MoonlighterCoop
{
    /// The pendant (go home) used by a guest: everyone goes home together, so the guest asks the host to use it
    /// (the host leads every trip; gold is shared, so it does not matter whose purse pays).
    [HarmonyLib.HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.UseMerchantPendant))]
    static class GuestPendant
    {
        static bool Prefix(HeroMerchant __instance)
        {
            if (!Session.IsGuest || !Net.Juego || !__instance.CanUsePendant) return true;
            Net.Send(Msg.PendantRequest, new byte[0]);
            Banner.Show("Has usado el colgante: volvéis todos a casa");
            Plugin.Log.LogInfo("[pendant] guest asks the host to go home");
            return false;
        }

        public static void OnRequest(IPeer from)
        {
            var hero = HeroMerchant.Instance;
            var gm = GameManager.Instance;
            if (hero == null || gm == null || !gm.IsDungeonSceneLoaded() || !gm.NotLoadingScene) return;
            Banner.Show($"Jugador {from.Id + 1} ha usado el colgante: volvéis todos a casa");
            Plugin.Log.LogInfo($"[pendant] player {from.Id} used the pendant: the host takes everyone home");
            if (hero.CanUsePendant) hero.UseMerchantPendant();
            else hero.LoadTownScene();
        }
    }
}
