using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Enemies get tougher with more players. The host picks the mode in the F2 menu and it travels with the
    /// player count, so every PC builds its enemies with the same numbers.
    ///   0 "Como un jugador": the game's own numbers.
    ///   1 "Ajustada" (default): +45% life and +12% strength per extra player.
    ///   2 "Difícil": the above, then x1.3 life and x1.25 strength.
    public static class Difficulty
    {
        public static int Mode = 1;               // the host's choice (a guest gets it from the host)
        public static readonly string[] Names = { "Como un jugador", "Ajustada a los jugadores", "Difícil" };

        public static void Load(int saved) { Mode = Mathf.Clamp(saved, 0, 2); }

        public static float Life(int players)
        {
            if (Mode == 0 || players <= 1) return 1f;
            float f = 1f + 0.45f * (players - 1);
            return Mode == 2 ? f * 1.3f : f;
        }

        public static float Strength(int players)
        {
            if (Mode == 0 || players <= 1) return 1f;
            float f = 1f + 0.12f * (players - 1);
            return Mode == 2 ? f * 1.25f : f;
        }

        /// Corto, para el selector del menú (el largo no cabe con 2 o más jugadores).
        public static string Short(int players)
        {
            if (Mode == 0 || players <= 1) return Names[Mode];
            return $"{Names[Mode].Split(' ')[0]} (+{Mathf.RoundToInt((Life(players) - 1f) * 100f)}% vida)";
        }

        public static string Describe(int players)
        {
            if (Mode == 0 || players <= 1) return Names[Mode];
            return $"{Names[Mode]}: enemigos con +{Mathf.RoundToInt((Life(players) - 1f) * 100f)}% vida y +{Mathf.RoundToInt((Strength(players) - 1f) * 100f)}% fuerza";
        }
    }

    [HarmonyPatch(typeof(EnemyStats), nameof(EnemyStats.Init))]
    static class EnemyStatsScale
    {
        static void Postfix(EnemyStats __instance)
        {
            if (!Net.Juego || GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded()) return;
            int n = Session.Players;
            float life = Difficulty.Life(n), strength = Difficulty.Strength(n);
            if (life == 1f && strength == 1f) return;
            var tr = Traverse.Create(__instance);
            var stats = tr.Field("_currentStats").GetValue<EnemyStatModificator>();
            if (stats == null) return;
            stats.maxHealth *= life;
            stats.strength *= strength;
            float cur = tr.Field("_currentHealth").GetValue<float>();
            if (cur > 0f) tr.Field("_currentHealth").SetValue(Mathf.Min(cur * life, stats.maxHealth));
            if (_logged.Add(__instance.name)) Plugin.Log.LogInfo($"[difficulty] {__instance.name}: {n} players, life x{life:F2} -> {stats.maxHealth:F0}, strength x{strength:F2}");
        }
        private static readonly System.Collections.Generic.HashSet<string> _logged = new System.Collections.Generic.HashSet<string>();
    }
}
