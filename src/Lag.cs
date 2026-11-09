using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Who has a slow connection. The host measures every guest's ping (the Ping/Pong every 2 s) and passes the
    /// table on; a guest also knows its own ping to the host. The party HUD shows a small signal mark on the
    /// portrait of anyone who is slow (yellow) or very slow / not answering (red); nothing when all is well.
    public static class Lag
    {
        private static readonly Dictionary<int, float> _ms = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> _at = new Dictionary<int, float>();
        public const float Slow = 150f, VerySlow = 300f;

        public static void Set(int id, float ms) { _ms[id] = ms; _at[id] = Time.unscaledTime; }

        /// The last ping of a player (ms), very high if nothing came for a while, -1 if unknown.
        public static float Of(int id)
        {
            if (!_ms.TryGetValue(id, out var ms)) return -1f;
            return Time.unscaledTime - _at[id] > 8f ? 9999f : ms;
        }

        /// Host, every 2 s: the table of pings, so every guest sees everyone's.
        public static void Broadcast()
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            var ids = new List<int>(_ms.Keys);
            w.Write((byte)ids.Count);
            foreach (var id in ids) { w.Write((byte)id); w.Write((short)Mathf.Min(Of(id), 30000f)); }
            Net.Send(Msg.Lag, ms.ToArray());
        }

        public static void OnLag(byte[] data)
        {
            try
            {
                var r = new BinaryReader(new MemoryStream(data));
                int n = r.ReadByte();
                for (int i = 0; i < n; i++) { int id = r.ReadByte(); float v = r.ReadInt16(); if (id != Session.MyId) Set(id, v); }
            }
            catch { }
        }

        /// Three small signal bars in a dark rim, only when that player is slow.
        public static void Icon(Rect r, int id)
        {
            float ms = Of(id);
            if (ms < Slow) return;
            bool bad = ms >= VerySlow;
            var c = bad ? Ui.Bad : Ui.Gold;
            if (bad && Mathf.Repeat(Time.unscaledTime, 1f) > 0.6f) c = new Color(c.r, c.g, c.b, 0.45f);   // blinks when very slow
            Ui.Round(Ui.Shrink(r, -1f), Ui.Outline, 1f);
            float bw = r.width / 4f;
            for (int i = 0; i < 3; i++)
            {
                float h = r.height * (0.4f + 0.3f * i);
                bool lit = i == 0 || (!bad && i == 1);
                Ui.Box(new Rect(r.x + bw * 0.5f + i * bw, r.yMax - h, bw * 0.8f, h), lit ? c : Ui.Line);
            }
        }
    }
}
