using System.Collections.Generic;
using System.Linq;

namespace MoonlighterCoop
{
    /// Co-op achievements, from this player's lifetime totals (kept in the BepInEx config, on this PC only).
    public static class Achievements
    {
        public class A { public string Id, Name, Text, Key; public int Goal; public bool Done => V(Key) >= Goal; public int Count => System.Math.Min(V(Key), Goal); }

        // Lifetime totals: Stats counters + runs, days, flawless bosses.
        private static readonly Dictionary<string, int> T = new Dictionary<string, int>();
        private static readonly HashSet<string> Got = new HashSet<string>();
        private static int V(string k) => T.TryGetValue(k, out var v) ? v : 0;

        public static readonly A[] All = {
            new A { Id = "run1", Name = "Primera expedición", Text = "Volved juntos de una mazmorra", Key = "runs", Goal = 1 },
            new A { Id = "run25", Name = "Compañeros de mazmorra", Text = "25 expediciones juntos", Key = "runs", Goal = 25 },
            new A { Id = "kill100", Name = "Limpieza", Text = "Derrota a 100 enemigos jugando en cooperativo", Key = "k0", Goal = 100 },
            new A { Id = "kill1000", Name = "Exterminio", Text = "Derrota a 1000 enemigos jugando en cooperativo", Key = "k0", Goal = 1000 },
            new A { Id = "items500", Name = "Mochila llena", Text = "Recoge 500 objetos en cooperativo", Key = "k1", Goal = 500 },
            new A { Id = "rev10", Name = "Equipo de rescate", Text = "Revive a compañeros 10 veces", Key = "k3", Goal = 10 },
            new A { Id = "ghost10", Name = "Fantasma veterano", Text = "Cae 10 veces y vuelve", Key = "k2", Goal = 10 },
            new A { Id = "boss1", Name = "Jefe derrotado", Text = "Derrota a un jefe en cooperativo", Key = "k4", Goal = 1 },
            new A { Id = "bossclean", Name = "Sin bajas", Text = "Derrota a un jefe sin que caiga nadie", Key = "clean", Goal = 1 },
            new A { Id = "gold50k", Name = "Socios", Text = "Ganad 50.000 de oro juntos", Key = "k5", Goal = 50000 },
            new A { Id = "days10", Name = "Diez días en Rynoka", Text = "Jugad 10 días juntos", Key = "days", Goal = 10 },
        };

        public static int Unlocked => Got.Count;
        public static bool Has(string id) => Got.Contains(id);
        public static int Total(string key) => V(key);

        public static void Load(string text)
        {
            T.Clear(); Got.Clear();
            foreach (var part in (text ?? "").Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && int.TryParse(kv[1], out var n)) T[kv[0]] = n;
                else if (part.StartsWith("+")) Got.Add(part.Substring(1));
            }
        }
        private static string Save() => string.Join(";", T.Select(kv => kv.Key + "=" + kv.Value).Concat(Got.Select(g => "+" + g)).ToArray());

        private static void Bump(string key, int n)
        {
            T[key] = V(key) + n;
            foreach (var a in All)
                if (!Got.Contains(a.Id) && a.Done)
                {
                    Got.Add(a.Id);
                    Banner.Show("¡Logro! " + a.Name + ": " + a.Text);
                    Plugin.Log.LogInfo("[achievements] " + a.Id);
                }
            Plugin.SaveAchievements(Save());
        }

        public static void Add(int stat, int n) => Bump("k" + stat, n);
        public static void RunDone() => Bump("runs", 1);
        public static void DayDone() => Bump("days", 1);
        public static void BossDown(bool clean) { if (clean) Bump("clean", 1); }
    }
}
