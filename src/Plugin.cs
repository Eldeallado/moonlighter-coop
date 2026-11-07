using System;
using System.Collections.Generic;
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
        public static bool Windowed;
        public static bool Drive;
        public static bool TcpMode;   // -coop host|join on the command line (same PC / LAN / VPN); otherwise Steam
        public static string Tag = "solo";

        private float _menuSeenAt = -1f;
        private bool _botLoaded;
        private bool _dumped;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ParseArgs();
            var harmony = new Harmony(Guid);
            harmony.PatchAll();
            SeededObjects.Patch(harmony);
            TargetSwap.Patch(harmony);
            if (Drive) Driver.Start(Role == "join" ? 7801 : 7800);
            Session.Start();
            gameObject.AddComponent<RecolorLocalWill>();
            SceneManager.sceneLoaded += (s, m) => Log.LogInfo($"[scene] loaded {s.name} ({m})");
            // The game turns its own log off; keep errors and the loading/room flow for debugging.
            Debug.unityLogger.logEnabled = true;
            Application.logMessageReceived += (msg, stack, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error && !msg.StartsWith("Check for DLC"))
                    Log.LogWarning($"[unity {type}] {msg}\n{stack}");
                else if (msg.Contains("DungeonLoading") || msg.Contains("DisableCurrentPanel") || msg.Contains("ChangeRoomTo"))
                    Log.LogInfo("[unity] " + msg.Split('\n')[0]);
            };
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
                    case "-coopdrive":
                        Drive = true;
                        break;
                    case "-coopwindowed":
                        Windowed = true;
                        break;
                    case "-coopbot":
                        Bot = true;
                        break;
                    case "-coopscenario":
                        if (i + 1 < a.Length) Scenario = a[++i].ToLowerInvariant();
                        break;
                }
            }
            Tag = Role == "" ? "solo" : Role;
            TcpMode = Role != "";
        }

        private bool _windowed;
        private void Update()
        {
            // QA copies: a small window, whatever the game's own display option says.
            if ((Bot || Windowed) && !_windowed && Time.frameCount > 30)
            {
                _windowed = true;
                Screen.SetResolution(800, 450, FullScreenMode.Windowed);
            }
            if (Input.GetKeyDown(KeyCode.F8)) Dump();
            if (Input.GetKeyDown(KeyCode.F2) && !TcpMode) SteamCoop.Host();
            Session.Tick();
            if (Bot) BotTick();
        }

        private void OnGUI()
        {
            if (Plugin.Role == "")
            {
                GUI.Label(new Rect(8, 8, 700, 22), SteamCoop.Available ? "Coop online: pulsa F2 para crear partida e invitar a un amigo de Steam" : "");
                return;
            }
            var p = Puppet.Instance;
            GUI.Label(new Rect(8, 8, 600, 22),
                $"Coop {Version} [{Role}] {(Net.Connected ? $"conectado ({Session.LastPing:F0} ms)" : "esperando conexión…")}" +
                (p != null ? $"  compañero: {(p.Visible ? "aquí" : p.Zone)}" : ""));
            if (!string.IsNullOrEmpty(SteamCoop.Status) && !Net.Connected) GUI.Label(new Rect(8, 28, 800, 22), SteamCoop.Status);
            var hint = Vote.Hint() ?? Downed.Hint();
            if (hint != null)
            {
                var style = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                style.normal.textColor = Color.yellow;
                GUI.Label(new Rect(20, Screen.height * 0.12f, Screen.width - 40, 60), hint, style);
            }
        }

        private void BotTick()
        {
            BotMoveTick();
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
        public static string Scenario = "walk";
        public static int ScenarioStep;
        private float _scenarioAt = -1f;

        // QA "dungeon": host enters the Golem dungeon, changes room twice, goes home.
        private void BotDungeonTick()
        {
            var hero = HeroMerchant.Instance;
            var gm = GameManager.Instance;
            if (hero == null || gm == null || !Net.Connected) return;
            float now = Time.unscaledTime;
            bool settled = gm.NotLoadingScene && !(GUIManager.Instance.dungeonLoadingPanel?.IsActive() ?? false);
            if (_scenarioAt < 0f) { if (Session.LocalZone().StartsWith("town") && Puppet.Instance != null && Puppet.Instance.Visible) _scenarioAt = now + 8f; return; }
            if (now < _scenarioAt || !settled) return;
            switch (ScenarioStep)
            {
                case 0:
                    Log.LogInfo("[bot] step 0: enter Golem dungeon");
                    hero.LoadDungeonScene(ItemMaster.Culture.Golem);
                    ScenarioStep = 1; _scenarioAt = now + 15f; break;
                case 1:
                case 2:
                    if (!gm.IsDungeonSceneLoaded() || DungeonManager.Instance == null) return;
                    var room = DungeonManager.Instance.currentDungeonRoom;
                    var door = room.roomDoors.FirstOrDefault(d => d != null && d.destinationRoom != null && !(d.destinationRoom is DungeonBossRoom) && d.isOpened && !d.isDisabled);
                    if (door == null) { Log.LogWarning("[bot] no open door here"); ScenarioStep = 3; return; }
                    Log.LogInfo($"[bot] step {ScenarioStep}: go through {door.name} to {door.destinationRoom.x},{door.destinationRoom.y}");
                    door.OnPlayerEnter(hero.gameObject);
                    ScenarioStep++; _scenarioAt = now + (ScenarioStep == 2 ? 28f : 12f); break;
                case 3:
                    Log.LogInfo("[bot] step 3: go home");
                    hero.LoadTownScene();
                    ScenarioStep = 4; _scenarioAt = now + 20f; break;
            }
        }

        // QA "dungeon", guest side: once the host has led into a second room, the guest leads once.
        private readonly HashSet<string> _guestRooms = new HashSet<string>();
        private float _guestSameRoomSince = -1f;
        private bool _guestLed;
        private void BotGuestDungeonTick()
        {
            var hero = HeroMerchant.Instance;
            var dm = DungeonManager.Instance;
            var gm = GameManager.Instance;
            if (_guestLed || hero == null || dm == null || dm.currentDungeonRoom == null || gm == null || !gm.IsDungeonSceneLoaded() || !gm.NotLoadingScene) return;
            string zone = Session.LocalZone();
            _guestRooms.Add(zone);
            if (_guestRooms.Count < 2 || Puppet.Instance == null || Puppet.Instance.Zone != zone) { _guestSameRoomSince = -1f; return; }
            float now = Time.unscaledTime;
            if (_guestSameRoomSince < 0f) { _guestSameRoomSince = now; return; }
            if (now - _guestSameRoomSince < 6f) return;
            var door = dm.currentDungeonRoom.roomDoors.FirstOrDefault(d => d != null && d.destinationRoom != null && !(d.destinationRoom is DungeonBossRoom) && d.isOpened && !d.isDisabled);
            if (door == null) return;
            _guestLed = true;
            Log.LogInfo($"[bot] guest walks through {door.name} to {door.destinationRoom.x},{door.destinationRoom.y}");
            door.OnPlayerEnter(hero.gameObject);
        }

        // QA "combat": the host walks into rooms until one has enemies; only the guest attacks them.
        public static bool CombatRoomSeen, CombatRoomCleared;
        private float _combatNext;
        private void BotCombatTick()
        {
            var hero = HeroMerchant.Instance; var gm = GameManager.Instance; var dm = DungeonManager.Instance;
            if (hero == null || gm == null || !Net.Connected) return;
            float now = Time.unscaledTime;
            if (now < _combatNext) return;
            bool settled = gm.NotLoadingScene && !(GUIManager.Instance.dungeonLoadingPanel?.IsActive() ?? false);
            if (!settled) return;
            if (Session.IsHost)
            {
                if (!gm.IsDungeonSceneLoaded())
                {
                    if (ScenarioStep == 0 && Session.LocalZone().StartsWith("town") && Puppet.Instance != null && Puppet.Instance.Visible)
                    {
                        if (_scenarioAt < 0f) { _scenarioAt = now + 8f; return; }
                        if (now < _scenarioAt) return;
                        Log.LogInfo("[bot] combat: enter Golem dungeon"); ScenarioStep = 1; hero.LoadDungeonScene(ItemMaster.Culture.Golem); _combatNext = now + 15f;
                    }
                    return;
                }
                if (dm == null || dm.currentDungeonRoom == null) return;
                var room = dm.currentDungeonRoom;
                int alive = room.roomEnemies.Count(e => e != null && e.enemyStats != null && e.enemyStats.isAlive);
                if (alive > 0) { CombatRoomSeen = true; hero.SetGodMode(); return; }
                if (EnemySync.DeathsSent > 0) { CombatRoomSeen = true; CombatRoomCleared = true; return; }
                var door = room.roomDoors.Where(d => d != null && d.destinationRoom != null && !(d.destinationRoom is DungeonBossRoom) && d.isOpened && !d.isDisabled)
                    .OrderBy(d => d.destinationRoom.hasBeenVisited ? 1 : 0).FirstOrDefault();
                if (door == null) return;
                ScenarioStep++;
                Log.LogInfo($"[bot] combat: look for enemies in {door.destinationRoom.x},{door.destinationRoom.y}");
                door.OnPlayerEnter(hero.gameObject);
                _combatNext = now + 7f;
            }
            else
            {
                if (dm == null || dm.currentDungeonRoom == null || !gm.IsDungeonSceneLoaded()) return;
                if (Puppet.Instance == null || Puppet.Instance.Zone != Session.LocalZone()) return;
                hero.SetGodMode();
                var target = dm.currentDungeonRoom.roomEnemies.FirstOrDefault(e => e != null && e.enemyStats != null && e.enemyStats.isAlive && EnemySync.IdOf(e) != null && e.gameObject.activeInHierarchy);
                if (target == null)
                {
                    if (BreakableSync.Sent < 2)
                    {
                        var box = FindObjectsOfType<BreakableObjectBehaviour>().Select(b => b.GetComponent<Enemy>())
                            .FirstOrDefault(en => en != null && en.enemyStats != null && en.enemyStats.isAlive && en.enemyRoom == dm.currentDungeonRoom && en.enemyBehaviour != null && en.enemyBehaviour.isActiveAndEnabled);
                        if (box != null) { Log.LogInfo($"[bot] guest breaks {box.name}"); box.DealDamageToEnemy(999f); _combatNext = now + 1.5f; }
                    }
                    return;
                }
                Log.LogInfo($"[bot] guest hits {EnemySync.IdOf(target)}");
                target.DealDamageToEnemy(60f);
                _combatNext = now + 0.6f;
            }
        }

        // QA "coop": combat (guest mortal), shared loot pick-up, guest downed + revived by the host, both down = home.
        private float _coopAt = -1f;
        private int _coopPhase;
        private bool _sawPartnerDown;
        private void BotCoopTick()
        {
            var hero = HeroMerchant.Instance; var gm = GameManager.Instance;
            float now = Time.unscaledTime;
            if (hero == null || gm == null || !Net.Connected) return;
            bool inDungeon = gm.IsDungeonSceneLoaded();
            if (Session.IsGuest && Downed.Me && _coopPhase < 2) _coopPhase = 2;            // fell in the fight: skip ahead
            if (Session.IsHost && inDungeon && Loot.Taken == 0 && !Downed.Me)
            {
                var own = FindObjectsOfType<ItemStack>().FirstOrDefault(s => Loot.IdOf(s) != 0 && s.gameObject.activeInHierarchy && s.pickOnWalk != null && s.pickOnWalk.CanPick);
                if (own != null) { Log.LogInfo($"[bot] host picks up {own.master.name}"); own.pickOnWalk.TryToAdd(); }
            }
            bool hostReviving = Session.IsHost && inDungeon && (Downed.Partner || _coopPhase > 0);
            if (!hostReviving && (!inDungeon || !Plugin.CombatRoomCleared && Session.IsHost || EnemySync.DeathsApplied == 0 && Session.IsGuest && _coopPhase < 2))
            {
                // Phase 0: reuse the combat bot to find and clear a room (the guest is not god mode here).
                if (Session.IsHost || !inDungeon) { BotCombatTick(); return; }
                var dm0 = DungeonManager.Instance;
                if (dm0 == null || dm0.currentDungeonRoom == null || Puppet.Instance == null || Puppet.Instance.Zone != Session.LocalZone()) return;
                if (now < _combatNext) return;
                var t0 = dm0.currentDungeonRoom.roomEnemies.FirstOrDefault(e => e != null && e.enemyStats != null && e.enemyStats.isAlive && EnemySync.IdOf(e) != null && e.gameObject.activeInHierarchy);
                if (t0 == null) return;
                if (_coopAt < 0f) { _coopAt = now + 6f; return; }   // let the enemies hurt the guest a bit first
                if (now < _coopAt) return;
                t0.DealDamageToEnemy(40f);
                _combatNext = now + 0.8f;
                return;
            }
            if (Session.IsGuest)
            {
                switch (_coopPhase)
                {
                    case 0: // pick up a shared item once the host's loot has landed here
                        var item = FindObjectsOfType<ItemStack>().FirstOrDefault(s => Loot.IdOf(s) != 0 && s.gameObject.activeInHierarchy && s.pickOnWalk != null);
                        if (item == null) { if (_coopAt < 0f || now - _coopAt > 25f) { _coopAt = now; } else if (now - _coopAt > 20f) _coopPhase = 1; return; }
                        Log.LogInfo($"[bot] guest picks up {item.master.name}");
                        item.pickOnWalk.TryToAdd();
                        _coopPhase = 1; _coopAt = now + 4f; return;
                    case 1: // fall
                        if (now < _coopAt) return;
                        Log.LogInfo("[bot] guest dies");
                        hero.DealDamageToPlayer(99999f, "QA");
                        _coopPhase = 2; return;
                    case 2: // wait to be revived, then fall again
                        if (Downed.Revives > 0 && !Downed.Me) { _coopPhase = Loot.Taken == 0 ? 5 : 3; _coopAt = now + 6f; }
                        return;
                    case 5: // revived before picking anything up: pick up now
                        var it = FindObjectsOfType<ItemStack>().FirstOrDefault(s => Loot.IdOf(s) != 0 && s.gameObject.activeInHierarchy && s.pickOnWalk != null);
                        if (it != null) { Log.LogInfo($"[bot] guest picks up {it.master.name}"); it.pickOnWalk.TryToAdd(); }
                        if (it != null || now > _coopAt + 15f) { _coopPhase = 3; _coopAt = now + 5f; }
                        return;
                    case 3:
                        if (now < _coopAt) return;
                        Log.LogInfo("[bot] guest dies again");
                        hero.DealDamageToPlayer(99999f, "QA");
                        _coopPhase = 4; return;
                }
            }
            else
            {
                if (Downed.Partner && Puppet.Instance != null)
                {
                    if (_coopPhase == 0)
                    {
                        Log.LogInfo("[bot] host goes to the downed guest");
                        hero.heroMerchantController.TeleportTo(Puppet.Instance.transform.position + new Vector3(12f, 0f));
                        _coopPhase = 1;
                    }
                    else if (_coopPhase >= 2 && !_sawPartnerDown)
                    {
                        _sawPartnerDown = true;
                        Log.LogInfo("[bot] guest is down again: host dies too");
                        hero.heroMerchantController.TeleportTo(Puppet.Instance.transform.position + new Vector3(200f, 0f));
                        hero.SetNormalMode();
                        hero.heroMerchantStats.SetVencible();
                        hero.DealDamageToPlayer(99999f, "QA");
                    }
                }
                else if (_coopPhase == 1) { _coopPhase = 2; hero.heroMerchantController.TeleportTo(hero.transform.position + new Vector3(150f, 0f)); }
            }
        }

        private void BotMoveTick()
        {
            if (Scenario == "none") return;
            if (Scenario == "coop") { BotCoopTick(); return; }
            if (Scenario == "combat") { BotCombatTick(); return; }
            if (Scenario == "dungeon" && Session.IsGuest) { BotGuestDungeonTick(); return; }
            if (Scenario == "dungeon") { BotDungeonTick(); return; }
            var hero = HeroMerchant.Instance;
            if (hero == null || !Session.LocalZone().StartsWith("town") || !Net.Connected) return;
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








