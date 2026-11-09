using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Generation draws from UnityEngine.Random, which every enemy, sound and particle also uses between frames.
    /// To get the same floor on both PCs, the generator runs on its own Random state, seeded by the host:
    /// before each step of the coroutine we swap the generator's state in, and swap the game's back after.
    public static class SeededGeneration
    {
        public static IEnumerator Isolated(IEnumerator inner, int seed, string what)
        {
            var outer = Random.state;
            Random.InitState(seed);
            var gen = Random.state;
            Random.state = outer;
            Plugin.Log.LogInfo($"[seed] {what} runs with seed {seed}");

            // Drive nested IEnumerators ourselves so they also use the generator's state.
            var stack = new Stack<IEnumerator>();
            stack.Push(inner);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool more;
                outer = Random.state;
                Random.state = gen;
                try { more = top.MoveNext(); }
                finally { gen = Random.state; Random.state = outer; }
                if (!more) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                yield return top.Current;
            }
        }
    }

    [HarmonyPatch(typeof(DungeonGenerator), "GenerateDungeonCoroutine")]
    static class SeedNormalGeneration
    {
        static void Postfix(ref IEnumerator __result)
        {
            if (Net.Juego && Dungeon.FloorSeed != 0)
                __result = SeededGeneration.Isolated(__result, Dungeon.FloorSeed, "floor");
        }
    }

    [HarmonyPatch(typeof(DungeonGenerator), "GeneratePrefabDungeonCoroutine")]
    static class SeedPrefabGeneration
    {
        static void Postfix(ref IEnumerator __result)
        {
            if (Net.Juego && Dungeon.FloorSeed != 0)
                __result = SeededGeneration.Isolated(__result, Dungeon.FloorSeed, "prefab floor");
        }
    }
}
