using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MoonlighterCoop
{
    /// The last co-op runs (F2 → Historial): when, which dungeon and how deep, the gold and enemies of the whole
    /// group, and who fell. Kept in the BepInEx config (Cooperativo/Historial), newest first, 20 at most.
    public static class History
    {
        public class Run { public string When = "", Dungeon = "", Fell = ""; public int Floor, Gold, Kills, Players; }
        public static readonly List<Run> Runs = new List<Run>();
        private const char F = '¦', R = '§';

        private static string _dungeon = "";
        private static int _floor;

        public static void Load(string text)
        {
            Runs.Clear();
            foreach (var line in (text ?? "").Split(R))
            {
                var f = line.Split(F);
                if (f.Length < 7) continue;
                int.TryParse(f[2], out var floor); int.TryParse(f[3], out var gold); int.TryParse(f[4], out var kills); int.TryParse(f[6], out var pl);
                Runs.Add(new Run { When = f[0], Dungeon = f[1], Floor = floor, Gold = gold, Kills = kills, Fell = f[5], Players = pl });
            }
        }

        private static string Clean(string s) => (s ?? "").Replace(F, ' ').Replace(R, ' ');
        private static string Text() => string.Join(R.ToString(), Runs.Select(r => string.Join(F.ToString(), new[] { Clean(r.When), Clean(r.Dungeon), r.Floor.ToString(), r.Gold.ToString(), r.Kills.ToString(), Clean(r.Fell), r.Players.ToString() })).ToArray());

        /// A run starts (into the dungeon from town).
        public static void Begin() { _dungeon = ""; _floor = 0; }

        /// While in the dungeon: remember which one and the deepest floor reached.
        public static void Track()
        {
            var dm = DungeonManager.Instance;
            if (dm == null) return;
            try { _dungeon = Call.CultureName(dm.culture); _floor = Mathf.Max(_floor, dm.level + 1); } catch { }
        }

        /// Back in town: write the run down.
        public static void End(int[] mine)
        {
            if (_floor <= 0 && _dungeon == "") return;
            var others = Stats.Others.Values.ToList();
            int gold = mine[Stats.Gold], kills = mine[Stats.Kills];
            foreach (var o in others) { gold += o.Run[Stats.Gold]; kills += o.Run[Stats.Kills]; }
            var fell = new List<string>();
            if (mine[Stats.Deaths] > 0) fell.Add(Nombres.Mio("Tú") + (mine[Stats.Deaths] > 1 ? $" ({mine[Stats.Deaths]})" : ""));
            foreach (var o in others) if (o.Run[Stats.Deaths] > 0) fell.Add(o.Name + (o.Run[Stats.Deaths] > 1 ? $" ({o.Run[Stats.Deaths]})" : ""));
            Runs.Insert(0, new Run { When = When(DateTime.Now), Dungeon = _dungeon, Floor = _floor, Gold = gold, Kills = kills, Fell = string.Join(", ", fell.ToArray()), Players = 1 + others.Count });
            while (Runs.Count > 20) Runs.RemoveAt(Runs.Count - 1);
            Plugin.SaveHistory(Text());
            Plugin.Log.LogInfo($"[history] {_dungeon} floor {_floor}, gold {gold}");
            _dungeon = ""; _floor = 0;
        }

        /// "9/11 9:15 p. m." (12-hour clock, as the player prefers).
        public static string When(DateTime t)
        {
            int h = t.Hour % 12; if (h == 0) h = 12;
            return $"{t.Day}/{t.Month} {h}:{t.Minute:00} {(t.Hour < 12 ? "a. m." : "p. m.")}";
        }
    }
}
