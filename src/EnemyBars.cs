using System.Collections.Generic;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Optional (F2): each enemy's health as numbers over its head, in the dungeon. Off by default; only for you.
    public class EnemyBars : MonoBehaviour
    {
        public static bool On;
        public static void Toggle() { On = !On; Plugin.SaveEnemyBars(On); }

        private readonly List<EnemyStats> _enemies = new List<EnemyStats>();
        private float _rescan;
        private GUIStyle _style;

        private void LateUpdate()
        {
            if (!On || GameManager.Instance == null || !GameManager.Instance.IsDungeonSceneLoaded()) { _enemies.Clear(); return; }
            if (Time.unscaledTime < _rescan) return;
            _rescan = Time.unscaledTime + 0.5f;
            _enemies.Clear();
            var room = DungeonManager.Instance != null ? DungeonManager.Instance.currentDungeonRoom : null;
            foreach (var e in FindObjectsOfType<EnemyStats>())
                if (e != null && e.isActiveAndEnabled && (room == null || e.transform.IsChildOf(room.transform) || Vector2.Distance(e.transform.position, room.transform.position) < 300f))
                    _enemies.Add(e);
        }

        private void OnGUI()
        {
            if (!On || _enemies.Count == 0) return;
            var cam = Camera.main; if (cam == null) return;
            float k = Mathf.Clamp(Screen.height / 720f, 0.5f, 2f);
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _style.fontSize = (int)(13 * k);
            foreach (var e in _enemies)
            {
                if (e == null) continue;
                float hp, max;
                try { hp = e.CurrentHealth; max = e.maxHealth; } catch { continue; }
                if (max <= 0f || hp <= 0f) continue;
                var p = cam.WorldToScreenPoint(e.transform.position + Vector3.up * 30f);
                if (p.z < 0f) continue;
                var r = new Rect(p.x - 40 * k, Screen.height - p.y - 10 * k, 80 * k, 20 * k);
                var o = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f); GUI.DrawTexture(new Rect(r.x, r.y + 2 * k, r.width, r.height - 4 * k), Texture2D.whiteTexture);
                float f = Mathf.Clamp01(hp / max);
                GUI.color = new Color(1f - f, 0.25f + 0.6f * f, 0.2f, 0.75f); GUI.DrawTexture(new Rect(r.x, r.yMax - 5 * k, r.width * f, 3 * k), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(r, Mathf.CeilToInt(hp) + " / " + Mathf.CeilToInt(max), _style);
                GUI.color = o;
            }
        }
    }
}
