using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MoonlighterCoop
{
    /// "Carrera por separado" (host rule): on the normal floors each player plays his own copy of the same floor
    /// (same rooms and things, his own enemies), going through doors on his own. What anyone picks up still goes
    /// to the shared bag, and gold is shared. Reaching the way down, a player waits there until everyone alive
    /// has arrived; then all go down together and the order of arrival is shown. A player who falls watches a
    /// partner as a ghost (moving freely; taken to his room when he changes room) and comes back on the next floor.
    /// The boss floor is played together, as always.
    public static class Race
    {
        /// The current floor is played separately.
        public static bool Separate
        {
            get
            {
                if (!HostSettings.Race || !Net.Juego || Session.TutorialActivo) return false;
                var gm = GameManager.Instance; var dm = DungeonManager.Instance;
                if (gm == null || dm == null || !gm.IsDungeonSceneLoaded()) return false;
                return dm.bossRoom == null;   // the boss floor: together
            }
        }

        public static bool Going;          // the real trip down, once everyone is at the door
        public static bool WaitingMe;
        private static readonly Dictionary<int, float> _arrived = new Dictionary<int, float>();
        private static string _floor = "";
        private static float _floorStart;

        private static string FloorKey() { var dm = DungeonManager.Instance; return dm == null ? "" : $"{dm.culture}:{dm.level}"; }
        private static string NameOf(int id) => id == Session.MyId ? "Tú" : Give.PlayerName(id);

        public static void Tick()
        {
            var gm = GameManager.Instance;
            if (gm == null || !gm.IsDungeonSceneLoaded()) { _floor = ""; WaitingMe = false; return; }
            if (!gm.NotLoadingScene) return;
            string key = FloorKey();
            if (key != _floor)
            {
                // A new floor: everybody starts again.
                _floor = key; _arrived.Clear(); _floorStart = Time.unscaledTime; WaitingMe = false; _hidden.Clear();
                if (Separate) Banner.Show("Carrera: cada uno su piso — nos vemos en la puerta de bajada");
            }
            if (!Separate) return;
            if (WaitingMe && HeroMerchant.Instance != null) HeroMerchant.Instance.heroMerchantController.DisableMoveInput();
            if (Downed.Me) Spectate();
        }

        /// Someone used the way down on a separate floor: instead of going, he waits there.
        public static void Arrive()
        {
            if (WaitingMe) return;
            WaitingMe = true;
            float t = Time.unscaledTime - _floorStart;
            _arrived[Session.MyId] = t;
            var hero = HeroMerchant.Instance;
            if (hero != null) { hero.heroMerchantController.DisableMoveInput(); hero.heroMerchantController.DisableAttackInput(); }
            var ms = new MemoryStream(); var w = new BinaryWriter(ms); w.Write((byte)Session.MyId); w.Write(t);
            Net.Send(Msg.RaceArrive, ms.ToArray());
            Banner.Show($"Has llegado a la puerta en {Clock(t)}: esperando a los demás");
            Plugin.Log.LogInfo($"[race] I arrived in {t:F1} s");
            if (Session.IsHost) HostCheck();
        }

        public static void OnArrive(byte[] data)
        {
            try
            {
                var r = new BinaryReader(new MemoryStream(data));
                int id = r.ReadByte(); float t = r.ReadSingle();
                if (_arrived.ContainsKey(id)) return;
                _arrived[id] = t;
                Banner.Show($"{NameOf(id)} ha llegado a la puerta en {Clock(t)}");
                if (Session.IsHost) HostCheck();
            }
            catch { }
        }

        /// Host: when everyone alive is at the door, the results, and down we all go.
        private static void HostCheck()
        {
            var everyone = new List<int> { Session.MyId };
            everyone.AddRange(Net.Live.Select(p => p.Id));
            var alive = everyone.Where(id => id == Session.MyId ? !Downed.Me : !Downed.IsGhost(id)).ToList();
            if (alive.Count == 0 || !alive.All(id => _arrived.ContainsKey(id))) return;
            var order = _arrived.OrderBy(kv => kv.Value).ToList();
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)order.Count);
            foreach (var kv in order) { w.Write((byte)kv.Key); w.Write(kv.Value); }
            Net.Send(Msg.RaceDone, ms.ToArray());
            ShowResults(order);
            Plugin.Instance.StartCoroutine(GoDown());
        }

        private static System.Collections.IEnumerator GoDown()
        {
            yield return new WaitForSecondsRealtime(2.5f);   // time to read who won
            var hero = HeroMerchant.Instance; var dm = DungeonManager.Instance;
            if (hero == null || dm == null) yield break;
            WaitingMe = false;
            hero.heroMerchantController.EnableMoveInput(); hero.heroMerchantController.EnableAttackInput();
            Going = true;
            try { hero.LoadNextLevelDungeonScene(dm.culture); } finally { Going = false; }
        }

        public static void OnDone(byte[] data)
        {
            try
            {
                var r = new BinaryReader(new MemoryStream(data));
                int n = r.ReadByte();
                var order = new List<KeyValuePair<int, float>>();
                for (int i = 0; i < n; i++) order.Add(new KeyValuePair<int, float>(r.ReadByte(), r.ReadSingle()));
                ShowResults(order);
                WaitingMe = false;
                var hero = HeroMerchant.Instance;
                if (hero != null) { hero.heroMerchantController.EnableMoveInput(); if (!Downed.Me) hero.heroMerchantController.EnableAttackInput(); }
            }
            catch { }
        }

        private static void ShowResults(List<KeyValuePair<int, float>> order)
        {
            if (order.Count == 0) return;
            float first = order[0].Value;
            var parts = order.Select((kv, i) => i == 0 ? $"1º {NameOf(kv.Key)} ({Clock(kv.Value)})" : $"{i + 1}º {NameOf(kv.Key)} (+{Mathf.CeilToInt(kv.Value - first)} s)");
            Banner.Show("Carrera: " + string.Join("  ·  ", parts.ToArray()));
            if (order[0].Key == Session.MyId && order.Count > 1) Achievements.RaceWon();
        }

        private static string Clock(float t) { int s = Mathf.Max(0, Mathf.RoundToInt(t)); return $"{s / 60}:{s % 60:00}"; }

        // ---------- a ghost watches a partner: goes with him from room to room, his own enemies hidden ----------

        private static readonly List<GameObject> _hidden = new List<GameObject>();
        private static float _nextFollow;

        private static void Spectate()
        {
            var hero = HeroMerchant.Instance; var dm = DungeonManager.Instance;
            if (hero == null || dm == null || dm.currentDungeonRoom == null) return;
            var target = Puppet.All.Values.Where(p => p != null && !Downed.IsGhost(p.PlayerId) && p.Zone.StartsWith("dungeon:")).OrderBy(p => p.PlayerId).FirstOrDefault();
            if (target == null) return;
            // Same room as the partner (in my copy of the floor).
            if (Time.unscaledTime >= _nextFollow && target.Zone != Dungeon.RoomZone())
            {
                // El cambio de sala dura 1,5 s (fundido): no volver a intentarlo a mitad, que lo deja enganchado
                _nextFollow = Time.unscaledTime + 2.5f;
                var parts = target.Zone.Split(':');
                if (parts.Length >= 4)
                {
                    string k = parts[3]; bool secret = k.Contains("s"), boss = k.Contains("b");
                    var xy = k.TrimEnd('s', 'b').Split(',');
                    if (xy.Length == 2 && int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y))
                    {
                        var room = boss ? dm.bossRoom : dm.dungeonRooms.FirstOrDefault(r => r != null && r.x == x && r.y == y && r.isSecret == secret && !(r is DungeonBossRoom));
                        if (room != null)
                        {
                            Dungeon.Following = true;
                            try
                            {
                                dm.SpecialChangeRoomTo(room);
                                hero.heroMerchantController.TeleportTo(target.transform.position);
                                CameraManager.Instance.TeleportCameraToPoint(hero.gameObject);
                                CameraManager.Instance.SetFollowTarget();
                            }
                            finally { Dungeon.Following = false; }
                        }
                    }
                }
            }
            // El fantasma se mueve libre por la sala (antes iba pegado al compañero y no se podía mover).
            // Solo se le lleva a la sala del compañero cuando este cambia de sala, para seguir viéndole.
            // Y los enemigos de esta copia del piso, fuera de la vista.
            foreach (var e in dm.currentDungeonRoom.roomEnemies)
                if (e != null && e.gameObject.activeSelf) { e.gameObject.SetActive(false); _hidden.Add(e.gameObject); }
        }
    }
}
