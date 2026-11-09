using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Enemies only know one Will (HeroMerchant.Instance). On the host, while an enemy thinks (its Update and
    /// FixedUpdate), Will is moved to the living player closest to that enemy, and put back right after:
    /// so each enemy chases whoever is nearest, like co-op in Isaac. Ghosts are never chased.
    public static class TargetSwap
    {
        public static int Swaps;

        public static void Patch(Harmony h)
        {
            var baseType = typeof(EnemyBehaviour);
            var pre = new HarmonyMethod(typeof(TargetSwap), nameof(Prefix));
            var post = new HarmonyMethod(typeof(TargetSwap), nameof(Postfix));
            var fin = new HarmonyMethod(typeof(TargetSwap), nameof(Finalizer));
            int n = 0;
            foreach (var t in baseType.Assembly.GetTypes().Where(t => baseType.IsAssignableFrom(t)))
                foreach (var name in new[] { "Update", "FixedUpdate", "LateUpdate" })
                {
                    var mi = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (mi == null || mi.IsAbstract || mi.ReturnType != typeof(void)) continue;
                    try { h.Patch(mi, prefix: pre, postfix: post, finalizer: fin); n++; } catch (Exception e) { Plugin.Log.LogWarning($"[target] {t.Name}.{name}: {e.Message}"); }
                }
            Plugin.Log.LogInfo($"[target] {n} enemy think methods aim at the nearest player");
        }

        static void Prefix(EnemyBehaviour __instance, out Vector3? __state)
        {
            __state = null;
            if (!Session.IsHost || !Net.Juego) return;
            var hero = HeroMerchant.Instance;
            if (hero == null) return;
            var me = hero.transform.position;
            var at = __instance.transform.position;
            // Nearest living player in this room: the host itself, or any guest's Will here that is not a ghost.
            // The Guardian (role) draws enemies: counts as closer.
            float best = Downed.Me ? float.MaxValue : (me - at).sqrMagnitude * Roles.Pull(Session.MyId);
            Vector3? target = null;
            foreach (var p in Puppet.All.Values)
            {
                if (p == null || !p.Visible || Downed.IsGhost(p.PlayerId)) continue;
                float d = (p.transform.position - at).sqrMagnitude * Roles.Pull(p.PlayerId);
                if (d < best) { best = d; target = p.transform.position; }
            }
            if (!target.HasValue) return;
            __state = me;
            hero.transform.position = new Vector3(target.Value.x, target.Value.y, me.z);
            Swaps++;
        }

        static void Postfix(Vector3? __state)
        {
            if (__state.HasValue && HeroMerchant.Instance != null) HeroMerchant.Instance.transform.position = __state.Value;
        }

        static Exception Finalizer(Exception __exception, Vector3? __state)
        {
            if (__state.HasValue && HeroMerchant.Instance != null) HeroMerchant.Instance.transform.position = __state.Value;
            return __exception;
        }
    }

    /// The host's room decides whether its doors are open (fight on / room cleared); the guest copies it.
    public static class DoorSync
    {
        private static float _next;

        public static void HostTick()
        {
            if (!Session.IsHost || !Net.Juego || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.3f;
            var dm = DungeonManager.Instance;
            if (dm == null || dm.currentDungeonRoom == null || !GameManager.Instance.IsDungeonSceneLoaded()) return;
            bool closed = dm.currentDungeonRoom.roomDoors.Any(d => d != null && d.destinationRoom != null && !d.isDisabled && !d.isOpened);
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(Dungeon.RoomZone()); w.Write(closed);
            Net.Send(Msg.RoomDoors, ms.ToArray());
        }

        public static void OnRoomDoors(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            string zone = r.ReadString(); bool closed = r.ReadBoolean();
            var dm = DungeonManager.Instance;
            if (dm == null || dm.currentDungeonRoom == null || Dungeon.RoomZone() != zone) return;
            var room = dm.currentDungeonRoom;
            var doors = room.roomDoors.Where(d => d != null && d.destinationRoom != null && !d.isDisabled).ToList();
            if (!closed && doors.Any(d => !d.isOpened)) room.OpenAllDoors();
            else if (closed) foreach (var d in doors.Where(d => d.isOpened)) d.CloseDoor();
        }
    }
}
