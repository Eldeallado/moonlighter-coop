using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Shots, arrows and bullets on every screen. The game has one class per projectile and no common base, so they
    /// are recognised by name. Each player sends what flies on their screen (on a guest, only its own shots: its
    /// enemies are copies that never fire); the others draw copies. A guest also takes the hit when an enemy
    /// bullet from the host's screen reaches its Will, with that enemy's strength.
    public static class Projectiles
    {
        public static int Sent, Received, HitsTaken;

        private static readonly List<MonoBehaviour> _live = new List<MonoBehaviour>();
        private static float _nextScan, _nextSend;
        private static bool _sentEmpty = true;

        // Boss attacks that are separate objects (falling rocks, slime traps, roots, portals...).
        private static readonly HashSet<string> BossAttacks = new HashSet<string>
        {
            "EvilWillArrowBehaviour", "GolemKingRockBehaviour", "GolemKingPunchBehaviour", "GolemKingSlimeArm", "GolemKingSlimeTrap",
            "GolemKingForcePushAttack", "CarnivorousMutaeBossRoots", "CarnivorousMutaeBossTentacle", "CarnivorousMutaeSpores", "GizmoBossPortalAttack",
        };

        private static readonly string[] PlayerKinds = { "HeroMerchant", "BowArrow", "ExplosiveArrow", "TrickWeapon", "Familiar", "Amulet" };

        private static bool IsProjectile(MonoBehaviour b)
        {
            if (b == null) return false;
            string n = b.GetType().Name;
            if (n.Contains("Pool") || n.StartsWith("Sound")) return false;
            if (!(n.EndsWith("Projectile") || n.EndsWith("Proyectile") || n.EndsWith("Arrow") || BossAttacks.Contains(n))) return false;
            // Pieces that hang from an enemy (a boss's arm) are already drawn by that enemy's copy.
            return b.GetComponentInParent<Enemy>() == null || n.EndsWith("Projectile") || n.EndsWith("Proyectile");
        }

        private static bool FromPlayer(MonoBehaviour b) { string n = b.GetType().Name; return PlayerKinds.Any(k => n.Contains(k)); }

        private static float DamageOf(MonoBehaviour b)
        {
            try
            {
                var owner = HarmonyLib.Traverse.Create(b).Field("projectileOwner").GetValue<GameObject>();
                var st = owner != null ? owner.GetComponentInParent<EnemyStats>() : null;
                if (st != null) return Mathf.Max(1f, st.strength);
            }
            catch { }
            // Boss attacks: the boss's strength.
            var dm = DungeonManager.Instance;
            if (BossAttacks.Contains(b.GetType().Name) && dm != null && dm.bossRoom != null && dm.bossRoom.Boss != null && dm.bossRoom.Boss.enemy != null && dm.bossRoom.Boss.enemy.enemyStats != null)
                return Mathf.Max(1f, dm.bossRoom.Boss.enemy.enemyStats.strength);
            return 10f;
        }

        public static void Tick()
        {
            if (!Net.Juego || GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded()) { if (_copies.Count > 0) Clear(); return; }
            float now = Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + 0.2f;
                _live.Clear();
                foreach (var b in Object.FindObjectsOfType<MonoBehaviour>())
                    if (IsProjectile(b) && b.isActiveAndEnabled && (Session.IsHost || FromPlayer(b))) _live.Add(b);
            }
            if (now >= _nextSend) { _nextSend = now + 0.08f; Send(); }
            Expire(now);
        }

        private static readonly List<SpriteRenderer> _tmp = new List<SpriteRenderer>();

        private static void Send()
        {
            _live.RemoveAll(b => b == null || !b.isActiveAndEnabled);
            if (_live.Count == 0 && _sentEmpty) return;
            _sentEmpty = _live.Count == 0;
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write((byte)Session.MyId);
            w.Write(Session.LocalZone());
            w.Write((short)_live.Count);
            foreach (var b in _live)
            {
                var root = b.transform;
                w.Write(b.GetInstanceID());
                w.Write(root.position.x); w.Write(root.position.y);
                bool enemy = !FromPlayer(b);
                w.Write(enemy);
                w.Write(enemy ? DamageOf(b) : 0f);
                _tmp.Clear(); root.GetComponentsInChildren(false, _tmp);
                var parts = _tmp.Where(sr => sr.enabled && sr.sprite != null && sr.color.a > 0.01f).Take(12).ToList();
                w.Write((byte)parts.Count);
                foreach (var sr in parts)
                {
                    var t = sr.transform; var spr = sr.sprite;
                    w.Write(spr.texture != null ? spr.texture.name + "/" + spr.name : spr.name);
                    var rel = t.position - root.position; w.Write(rel.x); w.Write(rel.y);
                    var s = t.lossyScale; w.Write(s.x); w.Write(s.y);
                    w.Write(t.rotation.eulerAngles.z);
                    w.Write((byte)((sr.flipX ? 1 : 0) | (sr.flipY ? 2 : 0)));
                    Color32 c = sr.color; w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a);
                    w.Write(sr.sortingLayerName); w.Write(sr.sortingOrder);
                    w.Write(sr.sharedMaterial != null ? sr.sharedMaterial.name.Replace(" (Instance)", "") : "");
                }
            }
            Net.Send(Msg.Projectiles, ms.ToArray());
            Sent++;
        }

        private class Copy { public GameObject go; public readonly List<SpriteRenderer> parts = new List<SpriteRenderer>(); public float seen; public bool hit; }
        private static readonly Dictionary<(int, int), Copy> _copies = new Dictionary<(int, int), Copy>();
        private static GameObject _root;

        public static void OnProjectiles(byte[] data)
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded()) return;
            var r = new BinaryReader(new MemoryStream(data));
            int sender = r.ReadByte();
            string zone = r.ReadString();
            bool here = zone == Session.LocalZone();
            if (_root == null) { _root = new GameObject("CoopProjectiles"); Object.DontDestroyOnLoad(_root); }
            var hero = HeroMerchant.Instance;
            float now = Time.unscaledTime;
            int n = r.ReadInt16();
            for (int k = 0; k < n; k++)
            {
                int id = r.ReadInt32();
                var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), 0f);
                bool enemy = r.ReadBoolean(); float dmg = r.ReadSingle();
                if (!_copies.TryGetValue((sender, id), out var cp) || cp.go == null)
                {
                    cp = new Copy { go = new GameObject("CoopShot") };
                    cp.go.transform.SetParent(_root.transform, false);
                    _copies[(sender, id)] = cp;
                }
                cp.seen = now;
                cp.go.transform.position = pos;
                int m = r.ReadByte();
                for (int j = 0; j < m; j++)
                {
                    string sprite = r.ReadString();
                    var rel = new Vector3(r.ReadSingle(), r.ReadSingle(), 0f);
                    var scale = new Vector3(r.ReadSingle(), r.ReadSingle(), 1f);
                    float rot = r.ReadSingle(); byte flags = r.ReadByte();
                    var col = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                    string layer = r.ReadString(); int order = r.ReadInt32(); string mat = r.ReadString();
                    if (j >= cp.parts.Count) { var g = new GameObject("part"); g.transform.SetParent(cp.go.transform, false); cp.parts.Add(g.AddComponent<SpriteRenderer>()); }
                    var sr = cp.parts[j];
                    sr.gameObject.SetActive(true);
                    sr.sprite = SpriteCache.Get(sprite);
                    var mm = mat == "Sprites/Diffuse Flash" ? ShopSync.CleanMaterial() : SpriteCache.GetMaterial(mat);
                    if (mm != null && sr.sharedMaterial != mm) sr.sharedMaterial = mm;
                    sr.transform.localPosition = rel; sr.transform.localScale = scale; sr.transform.localRotation = Quaternion.Euler(0, 0, rot);
                    sr.flipX = (flags & 1) != 0; sr.flipY = (flags & 2) != 0;
                    sr.color = col; sr.sortingLayerName = layer; sr.sortingOrder = order;
                }
                for (int j = m; j < cp.parts.Count; j++) cp.parts[j].gameObject.SetActive(false);
                cp.go.SetActive(here && !cp.hit);
                // An enemy bullet from the host reaching my Will hits me once.
                if (here && enemy && sender == 0 && !cp.hit && hero != null && !Downed.Me && !(bool)hero.heroMerchantStats.isDead
                    && ((Vector2)(hero.transform.position + Vector3.up * 10f - pos)).magnitude < 14f)
                {
                    cp.hit = true;
                    cp.go.SetActive(false);
                    hero.DealDamageToPlayer(dmg, "CoopProjectile");
                    HitsTaken++;
                }
            }
            // This sender's shots that are gone.
            foreach (var key in _copies.Where(kv => kv.Key.Item1 == sender && kv.Value.seen < now).Select(kv => kv.Key).ToList())
            { if (_copies[key].go != null) Object.Destroy(_copies[key].go); _copies.Remove(key); }
            Received++;
        }

        private static void Expire(float now)
        {
            if (_copies.Count == 0) return;
            foreach (var key in _copies.Where(kv => now - kv.Value.seen > 1f).Select(kv => kv.Key).ToList())
            { if (_copies[key].go != null) Object.Destroy(_copies[key].go); _copies.Remove(key); }
        }

        public static void Clear()
        {
            foreach (var cp in _copies.Values) if (cp.go != null) Object.Destroy(cp.go);
            _copies.Clear();
        }
    }
}
