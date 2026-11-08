using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Host-authoritative enemies. Both sides spawn the same enemies in the same order (same floor, same packs),
    /// so "room#n" names the same enemy on both. The host runs the AI; the guest's copies stop thinking and copy
    /// the host's position and animation (their attack hitboxes still hurt the guest). Guest hits go to the host;
    /// deaths come back from the host and each player gets their own drops.
    public static class EnemySync
    {
        private static readonly Dictionary<string, Enemy> _byId = new Dictionary<string, Enemy>();
        private static readonly Dictionary<Enemy, string> _idOf = new Dictionary<Enemy, string>();
        private static readonly Dictionary<DungeonRoom, int> _counter = new Dictionary<DungeonRoom, int>();
        private static float _nextSend;
        public static bool ApplyingDeath;
        public static int HitsSent, HitsApplied, DeathsSent, DeathsApplied, StatesApplied;

        public static void Reset()
        {
            _byId.Clear(); _idOf.Clear(); _counter.Clear(); Dead.Clear();
        }

        public static void Register(DungeonRoom room, Enemy e)
        {
            if (room == null || e == null) return;
            _counter.TryGetValue(room, out int n);
            _counter[room] = n + 1;
            string id = $"{room.x},{room.y}{(room.isSecret ? "s" : "")}{(room is DungeonBossRoom ? "b" : "")}#{n}";
            _byId[id] = e;
            _idOf[e] = id;
        }

        /// Bosses are part of their room (not spawned), so they get a fixed id: "boss:<culture>:<floor>".
        public static void RegisterBoss(DungeonBossRoom room)
        {
            var e = room != null && room.Boss != null ? room.Boss.enemy : null;
            if (e == null) return;
            var dm = DungeonManager.Instance;
            string id = $"boss:{(dm != null ? dm.culture.ToString() : "?")}:{(dm != null ? dm.level : 0)}";
            _byId[id] = e; _idOf[e] = id;
            Plugin.Log.LogInfo($"[enemy] boss {e.name} is {id}");
        }

        /// Host: enemies already killed on this floor (a guest joining mid-floor must not see them alive).
        public static readonly HashSet<string> Dead = new HashSet<string>();
        private static readonly List<string> _pendingDead = new List<string>();

        public static void SendDeadTo(IPeer p)
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(Dead.Count); foreach (var id in Dead) w.Write(id);
            p.Send(Msg.DeadEnemies, ms.ToArray());
        }

        public static void OnDeadEnemies(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            int n = r.ReadInt32();
            _pendingDead.Clear();
            for (int i = 0; i < n; i++) _pendingDead.Add(r.ReadString());
        }

        /// Guest: once the floor is built, quietly take away the enemies the host already killed (no drops).
        public static void GuestTick()
        {
            if (_pendingDead.Count == 0 || GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded() || !GameManager.Instance.NotLoadingScene || _byId.Count == 0) return;
            int gone = 0;
            foreach (var id in _pendingDead)
            {
                var e = Find(id);
                if (e == null) continue;
                if (e.enemyRoom != null) e.enemyRoom.roomEnemies.Remove(e);
                e.gameObject.SetActive(false);
                gone++;
            }
            Plugin.Log.LogInfo($"[enemy] removed {gone} enemies the host had already killed");
            _pendingDead.Clear();
        }

        public static string IdOf(Enemy e) => e != null && _idOf.TryGetValue(e, out var id) ? id : null;

        private static Enemy Find(string id) => _byId.TryGetValue(id, out var e) && e != null ? e : null;

        // ---------- host ----------

        public static void HostTick()
        {
            if (!Session.IsHost || !Net.Connected) return;
            float now = Time.unscaledTime;
            if (now < _nextSend) return;
            _nextSend = now + 1f / 15f;
            var dm = DungeonManager.Instance;
            if (dm == null || dm.currentDungeonRoom == null || !GameManager.Instance.IsDungeonSceneLoaded()) return;
            var list = new List<(string, Enemy)>();
            foreach (var e in dm.currentDungeonRoom.roomEnemies)
            {
                var id = IdOf(e);
                if (id != null && e != null && e.gameObject.activeInHierarchy) list.Add((id, e));
            }
            // The boss is not in the room's enemy list.
            if (dm.currentDungeonRoom is DungeonBossRoom br && br.Boss != null && br.Boss.enemy != null && br.Boss.gameObject.activeInHierarchy)
            {
                var bid = IdOf(br.Boss.enemy);
                if (bid != null && !list.Exists(x => x.Item1 == bid)) list.Add((bid, br.Boss.enemy));
            }
            if (list.Count == 0) return;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((short)list.Count);
            foreach (var (id, e) in list)
            {
                var t = e.transform;
                w.Write(id);
                w.Write(t.position.x); w.Write(t.position.y);
                w.Write(t.localScale.x);
                w.Write(e.enemyStats != null ? e.enemyStats.CurrentHealth : 0f);
                var sr = e.enemySprite;
                w.Write(sr != null && sr.flipX);
                var anims = e.GetComponentsInChildren<Animator>();
                w.Write((byte)anims.Length);
                foreach (var a in anims)
                {
                    if (a.runtimeAnimatorController == null || !a.isActiveAndEnabled) { w.Write(0); w.Write(0f); continue; }
                    var st = a.GetCurrentAnimatorStateInfo(0);
                    w.Write(st.fullPathHash);
                    w.Write(st.normalizedTime);
                }
            }
            Net.Send(Msg.Enemies, ms.ToArray());
        }

        public static void HostDied(Enemy e)
        {
            var id = IdOf(e);
            if (id == null || !Session.IsHost || !Net.Connected) return;
            Dead.Add(id);
            Net.Send(Msg.EnemyDeath, System.Text.Encoding.UTF8.GetBytes(id));
            DeathsSent++;
        }

        public static void OnHit(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            string id = r.ReadString(); float strength = r.ReadSingle(); int type = r.ReadInt32();
            var e = Find(id);
            if (e == null) { Plugin.Log.LogInfo($"[enemy] guest hit unknown {id}"); return; }
            HitsApplied++;
            e.DealDamageToEnemy(strength, null, (Enemy.EnemyDamageReceivedType)type);
        }

        // ---------- guest ----------

        public static void SendHit(string id, float strength, Enemy.EnemyDamageReceivedType type)
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write(id); w.Write(strength); w.Write((int)type);
            Net.Send(Msg.EnemyHit, ms.ToArray());
            HitsSent++;
        }

        public static void OnStates(byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data));
            int n = r.ReadInt16();
            for (int i = 0; i < n; i++)
            {
                string id = r.ReadString();
                var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), 0f);
                float scaleX = r.ReadSingle();
                float hp = r.ReadSingle();
                bool flip = r.ReadBoolean();
                int na = r.ReadByte();
                var hashes = new int[na]; var times = new float[na];
                for (int k = 0; k < na; k++) { hashes[k] = r.ReadInt32(); times[k] = r.ReadSingle(); }

                var e = Find(id);
                if (e == null || !e.gameObject.activeInHierarchy) continue;
                StatesApplied++;
                var mirror = e.GetComponent<EnemyMirror>() ?? e.gameObject.AddComponent<EnemyMirror>();
                mirror.Apply(e, pos, scaleX, hp, flip, hashes, times);
            }
        }

        public static void OnDeath(byte[] data)
        {
            string id = System.Text.Encoding.UTF8.GetString(data);
            var e = Find(id);
            if (e == null || e.enemyStats == null || !e.enemyStats.isAlive) return;
            var mirror = e.GetComponent<EnemyMirror>();
            if (mirror != null) mirror.Release(e);
            ApplyingDeath = true;
            try { e.enemyStats.CurrentHealth = 0f; if (e.enemyStats.isAlive) e.enemyStats.Die(); }
            finally { ApplyingDeath = false; }
            DeathsApplied++;
            Plugin.Log.LogInfo($"[enemy] {id} died on the host");
        }
    }

    /// On the guest: an enemy that stopped thinking and copies the host's.
    public class EnemyMirror : MonoBehaviour
    {
        private Vector3 _target;
        private bool _taken;
        private readonly List<Behaviour> _disabled = new List<Behaviour>();

        public void Apply(Enemy e, Vector3 pos, float scaleX, float hp, bool flip, int[] hashes, float[] times)
        {
            if (!_taken)
            {
                _taken = true;
                transform.position = pos;
                foreach (var b in GetComponentsInChildren<EnemyBehaviour>(true))
                    if (b.enabled) { b.enabled = false; _disabled.Add(b); }
                foreach (var rb in GetComponentsInChildren<Rigidbody2D>(true)) { rb.velocity = Vector2.zero; rb.bodyType = RigidbodyType2D.Kinematic; }
            }
            _target = pos;
            var s = transform.localScale; s.x = scaleX; transform.localScale = s;
            if (e.enemySprite != null) e.enemySprite.flipX = flip;
            if (e.enemyStats != null && hp > 0f) Traverse.Create(e.enemyStats).Field("_currentHealth").SetValue(hp);
            var anims = GetComponentsInChildren<Animator>();
            for (int k = 0; k < anims.Length && k < hashes.Length; k++)
            {
                var a = anims[k];
                if (hashes[k] == 0 || a.runtimeAnimatorController == null || !a.isActiveAndEnabled) continue;
                var st = a.GetCurrentAnimatorStateInfo(0);
                if (st.fullPathHash != hashes[k] || Mathf.Abs(st.normalizedTime - times[k]) > 0.3f)
                    a.Play(hashes[k], 0, times[k] % 1f + Mathf.Floor(times[k]));
            }
        }

        /// Give the enemy its brain back so the game's own death runs normally.
        public void Release(Enemy e)
        {
            foreach (var b in _disabled) if (b != null) b.enabled = true;
            _disabled.Clear();
        }

        private void Update()
        {
            if (!_taken) return;
            transform.position = Vector3.Lerp(transform.position, _target, 1f - Mathf.Exp(-18f * Time.deltaTime));
        }
    }

    [HarmonyPatch(typeof(DungeonRoom), nameof(DungeonRoom.SpawnEnemy))]
    static class RegisterSpawnedEnemy
    {
        static void Postfix(DungeonRoom __instance, Enemy __result) => EnemySync.Register(__instance, __result);
    }

    [HarmonyPatch(typeof(DungeonBossRoom), nameof(DungeonBossRoom.Start))]
    static class RegisterBoss
    {
        static void Postfix(DungeonBossRoom __instance) { if (!__instance.IsBossDefeated) EnemySync.RegisterBoss(__instance); }
    }

    [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.GenerateDungeon), typeof(ItemMaster.Culture), typeof(int), typeof(bool))]
    static class ResetIdsOnNewFloor
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix() => EnemySync.Reset();
    }

    [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.GenerateSavedDungeon))]
    static class ResetIdsOnSavedFloor
    {
        static void Prefix() => EnemySync.Reset();
    }

    // Guest: a hit on an enemy is the host's business.
    [HarmonyPatch(typeof(Enemy), nameof(Enemy.DealDamageToEnemy))]
    static class GuestHitsGoToHost
    {
        static bool Prefix(Enemy __instance, float attackStrength, Enemy.EnemyDamageReceivedType enemyDamageReceivedType)
        {
            if (!Session.IsGuest || !Net.Connected) return true;
            var id = EnemySync.IdOf(__instance);
            if (id == null) return true;   // not a shared enemy (spawned by another enemy): local only
            EnemySync.SendHit(id, attackStrength, enemyDamageReceivedType);
            return false;
        }
    }

    // Host: tell the guest. Guest: shared enemies only die when the host says so (but let floor loading kill freely).
    [HarmonyPatch(typeof(EnemyStats), nameof(EnemyStats.Die))]
    static class EnemyDeathSync
    {
        static bool Prefix(EnemyStats __instance)
        {
            if (!Session.IsGuest || !Net.Connected || EnemySync.ApplyingDeath) return true;
            if (!GameManager.Instance.NotLoadingScene) return true;
            var e = __instance.GetComponent<Enemy>();
            return EnemySync.IdOf(e) == null;
        }
        static void Postfix(EnemyStats __instance)
        {
            if (Session.IsHost) EnemySync.HostDied(__instance.GetComponent<Enemy>());
        }
    }
}

