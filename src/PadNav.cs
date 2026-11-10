using System.Collections.Generic;
using InControl;
using UnityEngine;

namespace MoonlighterCoop
{
    /// Gamepad for the mod's menus. L3 + R3 together opens the co-op menu (F2 on the keyboard); inside the menus
    /// the D-pad or left stick moves, A accepts, B goes back. Read once per frame from InControl, so it works with
    /// any pad the game supports.
    public static class PadNav
    {
        public static bool Up, Down, Left, Right, A, B, Combo;
        /// L3 alone (revive a partner with a potion).
        public static bool L3Down;
        /// The player is using a pad (last menu input came from it): menus show the pad focus.
        public static bool Active;
        private static int _frame = -1;
        private static float _stickRepeat;
        private static readonly Queue<string> _sim = new Queue<string>();

        /// For the QA remote: queue presses ("up", "a", "combo"...).
        public static void Simulate(string what) { lock (_sim) _sim.Enqueue(what); }

        public static void Poll()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            Up = Down = Left = Right = A = B = Combo = L3Down = false;
            var d = InputManager.ActiveDevice;
            if (d != null)
            {
                Up = d.DPadUp.WasPressed; Down = d.DPadDown.WasPressed; Left = d.DPadLeft.WasPressed; Right = d.DPadRight.WasPressed;
                // Left stick, with repeat while held.
                var s = d.LeftStick.Vector;
                if (s.magnitude > 0.6f)
                {
                    if (Time.unscaledTime >= _stickRepeat)
                    {
                        _stickRepeat = Time.unscaledTime + 0.22f;
                        if (Mathf.Abs(s.y) > Mathf.Abs(s.x)) { if (s.y > 0) Up = true; else Down = true; }
                        else { if (s.x > 0) Right = true; else Left = true; }
                    }
                }
                else _stickRepeat = 0f;
                A = d.Action1.WasPressed; B = d.Action2.WasPressed;
                L3Down = d.LeftStickButton.WasPressed && !d.RightStickButton.IsPressed;
                Combo = (d.LeftStickButton.WasPressed && d.RightStickButton.IsPressed) || (d.RightStickButton.WasPressed && d.LeftStickButton.IsPressed);
            }
            lock (_sim)
                while (_sim.Count > 0)
                    switch (_sim.Dequeue())
                    {
                        case "up": Up = true; break; case "down": Down = true; break;
                        case "left": Left = true; break; case "right": Right = true; break;
                        case "a": A = true; break; case "b": B = true; break; case "combo": Combo = true; break;
                    }
            if (Up || Down || Left || Right || A || B || Combo) Active = true;
            if (Input.anyKeyDown && !(Up || Down || Left || Right || A || B || Combo) && !Teclas.Abajo(KeyCode.F2) && !Teclas.Abajo(KeyCode.F3)) Active = Active && !Input.GetMouseButtonDown(0);
        }
    }
}
