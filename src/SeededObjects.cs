using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MoonlighterCoop
{
    /// Dungeon props roll their look in Start/Awake, a frame after generation and outside its seeded dice
    /// (camp tents, breakable skins, notes, rocks...). Run those with dice made from the floor seed and the
    /// object's position, so both PCs roll the same.
    public static class SeededObjects
    {
        private static readonly string[] Types =
        {
            "CampGenerator", "UnlockUtilityCampGenerator", "BreakableObjectBehaviour", "BreakableDetail",
            "AbandonedItemsGenerator", "CrazyPeteNotes", "SkeletonNotes", "RandomSprite", "RockRandomSprite",
            "FallingFloor", "HealingArea", "SlimePool", "StartAtRandomFrame", "LightsRandomAnimationBehaviour",
        };

        public static void Patch(Harmony h)
        {
            var asm = typeof(HeroMerchant).Assembly;
            var pre = new HarmonyMethod(typeof(SeededObjects), nameof(Prefix));
            var post = new HarmonyMethod(typeof(SeededObjects), nameof(Postfix));
            var fin = new HarmonyMethod(typeof(SeededObjects), nameof(Finalizer));
            int n = 0;
            foreach (var name in Types)
            {
                var t = asm.GetType(name);
                if (t == null) continue;
                foreach (var m in new[] { "Awake", "Start", "OnEnable" })
                {
                    var mi = t.GetMethod(m, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (mi == null || mi.ReturnType != typeof(void)) continue;
                    h.Patch(mi, prefix: pre, postfix: post, finalizer: fin);
                    n++;
                }
            }
            Plugin.Log.LogInfo($"[seed] {n} prop methods roll with the floor seed");
        }

        static void Prefix(MonoBehaviour __instance, out Random.State? __state)
        {
            __state = null;
            if (!Net.Juego || Dungeon.FloorSeed == 0 || GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded()) return;
            var p = __instance.transform.position;
            int seed = Dungeon.FloorSeed;
            unchecked
            {
                seed = seed * 31 + Mathf.RoundToInt(p.x * 4f);
                seed = seed * 31 + Mathf.RoundToInt(p.y * 4f);
                seed = seed * 31 + __instance.GetType().Name.GetHashCode();
            }
            __state = Random.state;
            Random.InitState(seed);
        }

        static void Postfix(Random.State? __state) { if (__state.HasValue) Random.state = __state.Value; }
        static Exception Finalizer(Exception __exception, Random.State? __state)
        {
            if (__state.HasValue) Random.state = __state.Value;
            return __exception;
        }
    }

    /// Boxes, pots and other breakables are Enemies the game does not spawn through rooms (no shared id).
    /// When one breaks on either PC, the one at the same spot breaks on the other.
    public static class BreakableSync
    {
        public static bool Applying;
        public static int Sent, Applied;

        private static string BaseName(Enemy e) => e.name.Replace("(Clone)", "").Trim();

        /// Any Enemy without a shared id (boxes, pots, slimes split from a slime, turrets' spawns...):
        /// when it dies on one PC, the nearest living one with the same name dies on the other.
        public static void Broke(Enemy e)
        {
            if (Applying || EnemySync.ApplyingDeath || !Net.Juego || e == null) return;
            if (GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded()) return;
            bool breakable = e.GetComponent<BreakableObjectBehaviour>() != null;
            // Unshared real enemies: only the host decides they die.
            if (!breakable && !Session.IsHost) return;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(e.transform.position.x); w.Write(e.transform.position.y); w.Write(BaseName(e)); w.Write(breakable);
            Net.Send(Msg.BreakAt, ms.ToArray());
            Sent++;
        }

        // Grass, small pots and other "details" break on touch and are not Enemies.
        public static void DetailBroke(BreakableDetail d)
        {
            if (Applying || !Net.Juego || d == null) return;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(d.transform.position.x); w.Write(d.transform.position.y);
            Net.Send(Msg.BreakDetailAt, ms.ToArray());
            Sent++;
        }

        public static void OnBreakDetailAt(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            var pos = new Vector2(r.ReadSingle(), r.ReadSingle());
            BreakableDetail best = null; float bestD = 4f * 4f;
            foreach (var b in UnityEngine.Object.FindObjectsOfType<BreakableDetail>())
            {
                float d = ((Vector2)b.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = b; }
            }
            if (best == null) return;
            Applying = true;
            try { Traverse.Create(best).Method("PlayBreakAnimation").GetValue(); }
            finally { Applying = false; }
            Applied++;
        }

        public static void OnBreakAt(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            var pos = new Vector2(r.ReadSingle(), r.ReadSingle());
            string name = r.ReadString(); bool breakable = r.ReadBoolean();
            Enemy best = null; float bestD = breakable ? 6f : 80f; bestD *= bestD;
            foreach (var en in UnityEngine.Object.FindObjectsOfType<Enemy>())
            {
                if (en == null || en.enemyStats == null || !en.enemyStats.isAlive || EnemySync.IdOf(en) != null) continue;
                if (BaseName(en) != name) continue;
                float d = ((Vector2)en.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = en; }
            }
            if (best == null) return;
            var e = best;
            Applying = true;
            try { e.enemyStats.CurrentHealth = 0f; if (e.enemyStats.isAlive) e.enemyStats.Die(); }
            finally { Applying = false; }
            Applied++;
        }
    }

    [HarmonyPatch(typeof(BreakableDetail), "PlayBreakAnimation")]
    static class BreakableDetailSync
    {
        static bool Prefix(BreakableDetail __instance, out bool __state)
        {
            // Only report the first break (it can be touched again while breaking).
            __state = __instance.GetComponent<Collider2D>() == null || __instance.GetComponent<Collider2D>().enabled;
            return true;
        }
        static void Postfix(BreakableDetail __instance, bool __state) { if (__state) BreakableSync.DetailBroke(__instance); }
    }

    [HarmonyPatch(typeof(EnemyStats), nameof(EnemyStats.Die))]
    static class BreakableDeathSync
    {
        static void Postfix(EnemyStats __instance)
        {
            var e = __instance.GetComponent<Enemy>();
            if (EnemySync.IdOf(e) == null) BreakableSync.Broke(e);
        }
    }
}
