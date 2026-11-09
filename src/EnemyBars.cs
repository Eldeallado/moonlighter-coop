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
            Ui.Load();
            float k = Ui.K;
            foreach (var e in _enemies)
            {
                if (e == null) continue;
                float hp, max;
                try { hp = e.CurrentHealth; max = e.maxHealth; } catch { continue; }
                if (max <= 0f || hp <= 0f) continue;
                var p = cam.WorldToScreenPoint(e.transform.position + Vector3.up * 30f);
                if (p.z < 0f) continue;
                // A tiny parchment tag: the numbers in ink and a thin bar under them.
                var r = new Rect(p.x - 38 * k, Screen.height - p.y - 12 * k, 76 * k, 24 * k);
                Ui.Plate(r, 0.9f);
                float f = Mathf.Clamp01(hp / max);
                Ui.Label(new Rect(r.x, r.y + 1 * k, r.width, 14 * k), Mathf.CeilToInt(hp) + " / " + Mathf.CeilToInt(max), 12.5f, Ui.Ink, TextAnchor.MiddleCenter);
                Ui.Bar(new Rect(r.x + 5 * k, r.yMax - 8 * k, r.width - 10 * k, 5 * k), f, f > 0.5f ? Ui.Good : f > 0.25f ? new Color(0.9f, 0.55f, 0.15f) : Ui.Bad);
            }
        }
    }
}
