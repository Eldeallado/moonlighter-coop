using System;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonlighterCoop
{
    [BepInPlugin(Guid, "Moonlighter Coop", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "charly.moonlighter.coop";
        public const string Version = "0.1.0";

        public static Plugin Instance;
        public static ManualLogSource Log;

        // Command line: -coop host | -coop join <ip>  ; -coopbot (auto-load save, for QA)
        public static string Role = "";
        public static string JoinAddress = "127.0.0.1";
        public static int Port = 7795;
        public static bool Bot;
        public static string Tag = "solo";

        private float _menuSeenAt = -1f;
        private bool _botLoaded;
        private bool _dumped;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ParseArgs();
            new Harmony(Guid).PatchAll();
            Session.Start();
            SceneManager.sceneLoaded += (s, m) => Log.LogInfo($"[scene] loaded {s.name} ({m})");
            Log.LogInfo($"Moonlighter Coop {Version} role={Role} bot={Bot} tag={Tag}");
        }

        private static void ParseArgs()
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length; i++)
            {
                switch (a[i].ToLowerInvariant())
                {
                    case "-coop":
                        if (i + 1 < a.Length) Role = a[++i].ToLowerInvariant();
                        if (Role == "join" && i + 1 < a.Length && !a[i + 1].StartsWith("-")) JoinAddress = a[++i];
                        break;
                    case "-coopport":
                        if (i + 1 < a.Length) int.TryParse(a[++i], out Port);
                        break;
                    case "-coopbot":
                        Bot = true;
                        break;
                }
            }
            Tag = Role == "" ? "solo" : Role;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8)) Dump();
            Session.Tick();
            if (Bot) BotTick();
        }

        private void OnGUI()
        {
            if (Plugin.Role == "") return;
            var p = Puppet.Instance;
            GUI.Label(new Rect(8, 8, 600, 22),
                $"Coop {Version} [{Role}] {(Net.Connected ? $"conectado ({Session.LastPing:F0} ms)" : "esperando conexión…")}" +
                (p != null ? $"  compañero: {(p.Visible ? "aquí" : p.Zone)}" : ""));
        }

        private void BotTick()
        {
            if (Session.IsHost) BotMoveTick();
            if (Session.IsGuest) return; // the guest loads the host's save, not its own
            if (_botLoaded)
            {
                if (!_dumped && HeroMerchant.Instance != null && Time.realtimeSinceStartup - _menuSeenAt > 25f)
                {
                    _dumped = true;
                    Dump();
                }
                return;
            }
            var menu = FindObjectOfType<MainMenuPanel>();
            if (menu == null || GameManager.Instance == null) return;
            if (_menuSeenAt < 0f) { _menuSeenAt = Time.realtimeSinceStartup; Log.LogInfo("[bot] main menu seen"); }
            if (Time.realtimeSinceStartup - _menuSeenAt < 8f) return;
            _botLoaded = true;
            var slot = GameManager.Instance.GetMostRecentSlot();
            Log.LogInfo($"[bot] loading most recent slot: {(slot == null ? "NULL" : slot.seed.ToString())}");
            if (slot == null) return;
            LoadSlotFromMenu(slot);
        }

        public static void LoadSlotFromMenu(GameSlot slot)
        {
            GameManager.Instance.currentGameSlot = slot;
            GUIManager.Instance.AllowPanelChange();
            GUIManager.Instance.DisableCurrentPanel();
            GUIManager.Instance.currentInventory = null;
            GUIManager.Instance.GoToDungeonLoadingPanel();
            PlaylistManager.Instance.FadeOutAllMusic();
            GameManager.Instance.LoadSlot(slot);
        }

        // QA: the host walks Will around a square so the guest can check the puppet follows.
        private Vector3 _botOrigin;
        private float _botNextStep;
        private int _botStep;
        private void BotMoveTick()
        {
            var hero = HeroMerchant.Instance;
            if (hero == null || Session.LocalZone() != "town" || !Net.Connected) return;
            if (_botOrigin == Vector3.zero) { _botOrigin = hero.transform.position; _botNextStep = Time.unscaledTime + 5f; }
            if (Time.unscaledTime < _botNextStep) return;
            _botNextStep = Time.unscaledTime + 3f;
            var offs = new[] { new Vector3(0, 0), new Vector3(30, 0), new Vector3(30, -30), new Vector3(0, -30) };
            var target = _botOrigin + offs[_botStep++ % offs.Length];
            hero.heroMerchantController.TeleportTo(target);
            Log.LogInfo($"[bot] moved hero to {target.x:F1},{target.y:F1}");
        }

        public static string StatusFile(string name) =>
            Path.Combine(Paths.BepInExRootPath, $"coop_{Tag}_{name}");

        private void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"scene(s): {string.Join(", ", Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name))}");
            var hero = HeroMerchant.Instance;
            if (hero == null) { sb.AppendLine("no hero"); }
            else
            {
                sb.AppendLine($"hero pos {hero.transform.position}");
                DumpGo(sb, hero.transform, 0);
            }
            File.WriteAllText(StatusFile("dump.txt"), sb.ToString());
            Log.LogInfo("[dump] written " + StatusFile("dump.txt"));
        }

        private static void DumpGo(StringBuilder sb, Transform t, int depth)
        {
            var pad = new string(' ', depth * 2);
            sb.AppendLine($"{pad}- {t.name} active={t.gameObject.activeSelf} local={t.localPosition}");
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                sb.AppendLine($"{pad}    [{c.GetType().FullName}]");
                if (c is Animator an && an.runtimeAnimatorController != null)
                {
                    sb.AppendLine($"{pad}      controller={an.runtimeAnimatorController.name} layers={an.layerCount}");
                    foreach (var p in an.parameters) sb.AppendLine($"{pad}      param {p.type} {p.name}");
                    foreach (var clip in an.runtimeAnimatorController.animationClips.Take(400))
                        sb.AppendLine($"{pad}      clip {clip.name}");
                }
                if (c is SpriteRenderer sr)
                    sb.AppendLine($"{pad}      sprite={(sr.sprite ? sr.sprite.name : "null")} order={sr.sortingLayerName}/{sr.sortingOrder}");
            }
            if (depth < 4)
                foreach (Transform ch in t) DumpGo(sb, ch, depth + 1);
        }
    }
}
