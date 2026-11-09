using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// "Coming?": when the host walks into a dungeon portal, the others are asked first instead of being dragged
    /// at once. The host waits inside the portal for up to 8 s (or until everyone answers "on my way"); each other
    /// player can ask once for 15 s more. Then everybody goes, as always.
    public static class Call
    {
        public const float Wait = 8f, Extra = 15f;
        public static bool Approved;               // the host's own travel, after the call
        private static bool _calling;
        private static ItemMaster.Culture _culture;
        private static float _deadline;
        private static readonly HashSet<int> _coming = new HashSet<int>(), _waited = new HashSet<int>();

        // ---------- host ----------

        public static bool Start(ItemMaster.Culture culture)
        {
            if (_calling) return true;
            _calling = true; _culture = culture; _deadline = Time.unscaledTime + Wait;
            _coming.Clear(); _waited.Clear();
            Send();
            Banner.Show("Avisando a tus compañeros: entráis a la mazmorra en " + Mathf.CeilToInt(Wait) + " s");
            Plugin.Log.LogInfo($"[call] host enters {culture}: asking the others first");
            return true;
        }

        private static void Send()
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((int)_culture); w.Write(Mathf.Max(0f, _deadline - Time.unscaledTime));
            Net.Send(Msg.Call, ms.ToArray());
        }

        public static void OnReply(IPeer from, byte[] data)
        {
            if (!_calling || data.Length < 1) return;
            if (data[0] == 1) { _coming.Add(from.Id); Banner.Show($"Jugador {from.Id + 1}: ¡voy!"); }
            else if (data[0] == 2 && _waited.Add(from.Id))
            {
                _deadline += Extra; Send();
                Banner.Show($"Jugador {from.Id + 1} pide que esperes: {Mathf.CeilToInt(Extra)} s más");
            }
        }

        public static void Tick()
        {
            if (!_calling) return;
            var hero = HeroMerchant.Instance;
            if (hero == null || !Net.Juego) { _calling = false; return; }
            bool all = _coming.Count >= Net.ConnectedCount;
            if (!all && Time.unscaledTime < _deadline) return;
            _calling = false;
            Approved = true;
            try { hero.LoadDungeonScene(_culture); }
            finally { Approved = false; }
        }

        public static bool Calling => _calling;

        // ---------- others ----------

        public static ItemMaster.Culture ShownCulture;
        public static float ShownUntil;
        public static bool Answered;

        public static void OnCall(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            ShownCulture = (ItemMaster.Culture)r.ReadInt32();
            float left = r.ReadSingle();
            if (Time.unscaledTime > ShownUntil) Answered = false;   // a new call
            ShownUntil = Time.unscaledTime + left;
        }

        public static void Reply(bool coming)
        {
            if (Answered && coming) return;
            Answered = true;
            Net.Send(Msg.CallReply, new[] { (byte)(coming ? 1 : 2) });
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
            return !Call.Start(culture);
        }
    }

    /// The "Coming?" box on the others' screens.
    public class CallPanel : MonoBehaviour
    {
        private GUIStyle _t, _l;
        private bool Showing => Session.IsGuest && Time.unscaledTime < Call.ShownUntil;

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
            float k = Mathf.Clamp(Screen.height / 720f, 0.5f, 2f);
            if (_t == null) { _t = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true }; _l = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true }; }
            _t.fontSize = (int)(19 * k); _l.fontSize = (int)(14 * k);
            _t.normal.textColor = new Color(1f, 0.85f, 0.45f); _l.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            float w = 520 * k, h = 120 * k;
            var r = new Rect((Screen.width - w) / 2f, 70 * k, w, h);
            var o = GUI.color; GUI.color = new Color(0.09f, 0.07f, 0.06f, 0.94f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o;
            int left = Mathf.CeilToInt(Call.ShownUntil - Time.unscaledTime);
            GUI.Label(new Rect(r.x + 10 * k, r.y + 8 * k, r.width - 20 * k, 52 * k), $"El anfitrión entra en la mazmorra de {Call.CultureName(Call.ShownCulture)}: vais todos en {left} s", _t);
            GUI.Label(new Rect(r.x + 10 * k, r.y + 62 * k, r.width - 20 * k, 50 * k),
                Call.Answered ? "Respuesta enviada" : (PadNav.Active ? "Cruceta ↑ ¡Voy! · ↓ ¡Espera! (15 s más)" : "H = ¡Voy!   ·   G = ¡Espera! (15 s más)"), _l);
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
