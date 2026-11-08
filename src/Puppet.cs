using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MoonlighterCoop
{
    /// What one player looks like this frame: every visible sprite under Will, relative to his root.
    public static class PoseCapture
    {
        private static readonly List<SpriteRenderer> _renderers = new List<SpriteRenderer>();
        private static float _nextRescan;

        public static byte[] Capture(string zone, int playerId)
        {
            var hero = HeroMerchant.Instance;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((byte)playerId);
            w.Write(Looks.Mine);
            w.Write(zone);
            if (hero == null) { w.Write(false); return ms.ToArray(); }
            w.Write(true);
            var root = hero.transform;
            w.Write(root.position.x);
            w.Write(root.position.y);
            if (Time.unscaledTime > _nextRescan)
            {
                _nextRescan = Time.unscaledTime + 1f;
                _renderers.Clear();
                root.GetComponentsInChildren(true, _renderers);
            }
            int baseOrder = SpriteDepthOrderRender.baseOrderValue - (int)(root.position.y * 10f);
            var parts = new List<SpriteRenderer>();
            foreach (var sr in _renderers)
            {
                if (sr == null || !sr.enabled || sr.sprite == null || !sr.gameObject.activeInHierarchy) continue;
                if (sr.color.a <= 0.01f) continue;
                if (sr.name.Contains("Light")) continue;   // Will's own light glow: a lighting effect, not art
                parts.Add(sr);
            }
            w.Write((short)parts.Count);
            foreach (var sr in parts)
            {
                var t = sr.transform;
                w.Write(PathOf(t, root));
                var spr = Recolor.Original(sr.sprite);   // the guest's red copies travel as the original art
                w.Write(spr.texture != null ? spr.texture.name + "/" + spr.name : spr.name);
                var rel = t.position - root.position;
                w.Write(rel.x); w.Write(rel.y);
                var s = t.lossyScale;
                w.Write(s.x); w.Write(s.y);
                w.Write(t.rotation.eulerAngles.z);
                byte flags = 0;
                if (sr.flipX) flags |= 1;
                if (sr.flipY) flags |= 2;
                w.Write(flags);
                Color32 c = sr.color;
                w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a);
                w.Write(sr.sortingLayerName);
                w.Write(sr.sortingOrder - baseOrder);
                w.Write(sr.sharedMaterial != null ? sr.sharedMaterial.name : "");
                w.Write(sr.sharedMaterial != null && sr.sharedMaterial.HasProperty("_FlashAmount") ? sr.sharedMaterial.GetFloat("_FlashAmount") : 0f);
            }
            return ms.ToArray();
        }

        private static readonly Dictionary<Transform, string> _paths = new Dictionary<Transform, string>();

        private static string PathOf(Transform t, Transform root)
        {
            if (_paths.TryGetValue(t, out var p)) return p;
            p = t.name;
            for (var x = t.parent; x != null && x != root; x = x.parent) p = x.name + "/" + p;
            _paths[t] = p;
            return p;
        }
    }

    /// The other player, drawn from the sprites they send.
    public class Puppet : MonoBehaviour
    {
        /// Every other player's Will, by player number (0 = host).
        public static readonly Dictionary<int, Puppet> All = new Dictionary<int, Puppet>();
        /// "The partner" for code written for two: the host on a guest, the first guest on the host.
        public static Puppet Instance
        {
            get
            {
                if (Session.IsGuest) return All.TryGetValue(0, out var h) ? h : null;
                Puppet first = null;
                foreach (var kv in All) if (kv.Value != null && (first == null || kv.Key < first.PlayerId)) first = kv.Value;
                return first;
            }
        }
        public int PlayerId;
        public int Look;
        private readonly Dictionary<string, SpriteRenderer> _parts = new Dictionary<string, SpriteRenderer>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private Vector3 _target;
        private bool _hasTarget;
        public string Zone = "";
        public bool Visible;

        public static Puppet Ensure(int id)
        {
            if (All.TryGetValue(id, out var p) && p != null) return p;
            var go = new GameObject("CoopPuppet " + id);
            DontDestroyOnLoad(go);
            p = go.AddComponent<Puppet>();
            p.PlayerId = id;
            All[id] = p;
            return p;
        }

        public void Apply(byte[] data, string localZone)
        {
            var r = new BinaryReader(new MemoryStream(data));
            r.ReadByte();   // player number (already used to pick this puppet)
            Look = r.ReadInt32();
            Looks.Of[PlayerId] = Look;
            Zone = r.ReadString();
            bool hasHero = r.ReadBoolean();
            Visible = hasHero && Zone == localZone && Zone != "";
            if (!hasHero) { gameObject.SetActive(Visible); return; }
            var pos = new Vector3(r.ReadSingle(), r.ReadSingle(), 0f);
            if (!_hasTarget || (pos - transform.position).sqrMagnitude > 200f * 200f) transform.position = pos;
            _target = pos;
            _hasTarget = true;
            int baseOrder = SpriteDepthOrderRender.baseOrderValue - (int)(pos.y * 10f);
            _seen.Clear();
            int n = r.ReadInt16();
            for (int i = 0; i < n; i++)
            {
                string path = r.ReadString();
                string sprite = r.ReadString();
                var rel = new Vector3(r.ReadSingle(), r.ReadSingle(), 0f);
                var scale = new Vector3(r.ReadSingle(), r.ReadSingle(), 1f);
                float rot = r.ReadSingle();
                byte flags = r.ReadByte();
                var col = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                string layer = r.ReadString();
                int relOrder = r.ReadInt32();
                string mat = r.ReadString();
                float flash = r.ReadSingle();
                _seen.Add(path);
                if (!_parts.TryGetValue(path, out var sr))
                {
                    var go = new GameObject(path);
                    go.transform.SetParent(transform, false);
                    sr = go.AddComponent<SpriteRenderer>();
                    _parts[path] = sr;
                }
                sr.gameObject.SetActive(true);
                sr.sprite = SpriteCache.Get(sprite);
                // On the host, the guest's Will is the red one.
                // Each guest has its colour (1 red, 2 yellow...); the host's Will stays as it is.
                if (path.StartsWith("HeroMerchantArt") && !path.Contains("Light")) sr.sprite = Recolor.Tint(sr.sprite, Look);
                var m = MaterialFor(path, mat);
                if (m != null && sr.sharedMaterial != m) sr.sharedMaterial = m;
                if (m != null && m.name.StartsWith("CoopPuppet") && m.HasProperty("_FlashAmount")) m.SetFloat("_FlashAmount", flash);
                sr.transform.localPosition = rel;
                sr.transform.localScale = scale;
                sr.transform.localRotation = Quaternion.Euler(0, 0, rot);
                sr.flipX = (flags & 1) != 0;
                sr.flipY = (flags & 2) != 0;
                sr.color = col;
                sr.sortingLayerName = layer;
                sr.sortingOrder = baseOrder + relOrder;
            }
            foreach (var kv in _parts)
                if (!_seen.Contains(kv.Key)) kv.Value.gameObject.SetActive(false);
            gameObject.SetActive(Visible);
        }

        // Will's body uses a per-object material instance ("... (Instance)"); a lookup by name finds some other
        // object's copy (flashing, faded). Use our own copy of the local Will's material for the same part.
        private readonly Dictionary<string, Material> _ownMaterials = new Dictionary<string, Material>();
        private Material MaterialFor(string path, string remoteName)
        {
            if (string.IsNullOrEmpty(remoteName)) return null;
            if (remoteName.EndsWith(" (Instance)"))
            {
                if (_ownMaterials.TryGetValue(path, out var own) && own != null) return own;
                var hero = HeroMerchant.Instance;
                var local = hero != null ? hero.transform.Find(path)?.GetComponent<SpriteRenderer>() : null;
                // The clean asset, not the local Will's instance (which may be mid red flash).
                Material src = SpriteCache.GetMaterial(remoteName.Substring(0, remoteName.Length - " (Instance)".Length));
                if (src == null && local != null) src = local.sharedMaterial;
                if (src == null) return null;
                own = new Material(src) { name = "CoopPuppet " + src.name };
                if (own.HasProperty("_FlashAmount")) own.SetFloat("_FlashAmount", 0f);
                _ownMaterials[path] = own;
                return own;
            }
            return SpriteCache.GetMaterial(remoteName);
        }

        private void Update()
        {
            if (!_hasTarget) return;
            transform.position = Vector3.Lerp(transform.position, _target, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
        }
    }

    public static class SpriteCache
    {
        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private static float _nextSpriteScan = -1f, _nextMatScan = -1f;
        public static int Misses;

        public static Sprite Get(string name)
        {
            if (_sprites.TryGetValue(name, out var s) && s != null) return s;
            if (Time.unscaledTime >= _nextSpriteScan)
            {
                _nextSpriteScan = Time.unscaledTime + 2f;
                // Sprite names repeat across sheets (Will and enemies both have "Idle_..."): key by sheet too.
                foreach (var sp in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (sp != null) _sprites[(sp.texture != null ? sp.texture.name + "/" : "") + sp.name] = sp;
                if (_sprites.TryGetValue(name, out s) && s != null) return s;
            }
            Misses++;
            return null;
        }

        public static Material GetMaterial(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_materials.TryGetValue(name, out var m) && m != null) return m;
            if (Time.unscaledTime >= _nextMatScan)
            {
                _nextMatScan = Time.unscaledTime + 5f;
                foreach (var mm in Resources.FindObjectsOfTypeAll<Material>())
                    if (mm != null) _materials[mm.name] = mm;
                if (_materials.TryGetValue(name, out m)) return m;
            }
            return null;
        }
    }
}

