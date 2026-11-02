using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace VoiceHorror.Core
{
    /// <summary>
    /// Thin input facade. Uses the new Input System (spec §6.3) when it is the active handler,
    /// and falls back to the legacy Input manager otherwise, so the project compiles either way.
    /// </summary>
    public static class InputReader
    {
        public enum Action
        {
            Interact,
            Sprint,
            Crouch,
            PushToMute,
            Pause,
            DebugOverlay,
            Weapon1,
            Weapon2,
            Weapon3,
            Fire,
            Cancel,
            Journal,
        }

        public static Vector2 Move
        {
            get
            {
                float x = 0f, y = 0f;
                if (Held(KeyW()) || Held(KeyUp())) y += 1f;
                if (Held(KeyS()) || Held(KeyDown())) y -= 1f;
                if (Held(KeyD()) || Held(KeyRight())) x += 1f;
                if (Held(KeyA()) || Held(KeyLeft())) x -= 1f;
                var v = new Vector2(x, y);
                return v.sqrMagnitude > 1f ? v.normalized : v;
            }
        }

        public static Vector2 Look
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var m = Mouse.current;
                return m != null ? m.delta.ReadValue() : Vector2.zero;
#else
                return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
#endif
            }
        }

        public static bool IsHeld(Action a) => Query(a, 0);
        public static bool WasPressed(Action a) => Query(a, 1);
        public static bool WasReleased(Action a) => Query(a, 2);

        // mode: 0 held, 1 pressed this frame, 2 released this frame
        static bool Query(Action a, int mode)
        {
            switch (a)
            {
                case Action.Fire: return MouseButton(0, mode);
                case Action.Cancel: return MouseButton(1, mode);
                default: return KeyQuery(a, mode);
            }
        }

#if ENABLE_INPUT_SYSTEM
        static bool MouseButton(int button, int mode)
        {
            var m = Mouse.current;
            if (m == null) return false;
            var b = button == 0 ? m.leftButton : m.rightButton;
            return mode == 0 ? b.isPressed : mode == 1 ? b.wasPressedThisFrame : b.wasReleasedThisFrame;
        }

        static bool KeyQuery(Action a, int mode)
        {
            var k = Keyboard.current;
            if (k == null) return false;
            Key key = KeyFor(a);
            Key alt = AltKeyFor(a);
            return Check(k, key, mode) || (alt != Key.None && Check(k, alt, mode));
        }

        static bool Check(Keyboard k, Key key, int mode)
        {
            var c = k[key];
            return mode == 0 ? c.isPressed : mode == 1 ? c.wasPressedThisFrame : c.wasReleasedThisFrame;
        }

        static Key KeyFor(Action a)
        {
            switch (a)
            {
                case Action.Interact: return Key.E;
                case Action.Sprint: return Key.LeftShift;
                case Action.Crouch: return Key.LeftCtrl;
                case Action.PushToMute: return Key.V;
                case Action.Pause: return Key.Escape;
                case Action.DebugOverlay: return Key.F3;
                case Action.Weapon1: return Key.Digit1;
                case Action.Weapon2: return Key.Digit2;
                case Action.Weapon3: return Key.Digit3;
                case Action.Journal: return Key.Tab;
                default: return Key.None;
            }
        }

        static Key AltKeyFor(Action a)
        {
            switch (a)
            {
                case Action.Crouch: return Key.C;
                case Action.Pause: return Key.P;
                default: return Key.None;
            }
        }

        static bool Held(Key key) { var k = Keyboard.current; return k != null && k[key].isPressed; }
        static Key KeyW() => Key.W;
        static Key KeyA() => Key.A;
        static Key KeyS() => Key.S;
        static Key KeyD() => Key.D;
        static Key KeyUp() => Key.UpArrow;
        static Key KeyDown() => Key.DownArrow;
        static Key KeyLeft() => Key.LeftArrow;
        static Key KeyRight() => Key.RightArrow;
#else
        static bool MouseButton(int button, int mode)
            => mode == 0 ? Input.GetMouseButton(button) : mode == 1 ? Input.GetMouseButtonDown(button) : Input.GetMouseButtonUp(button);

        static bool KeyQuery(Action a, int mode)
        {
            KeyCode key = KeyFor(a), alt = AltKeyFor(a);
            return Check(key, mode) || (alt != KeyCode.None && Check(alt, mode));
        }

        static bool Check(KeyCode key, int mode)
            => mode == 0 ? Input.GetKey(key) : mode == 1 ? Input.GetKeyDown(key) : Input.GetKeyUp(key);

        static KeyCode KeyFor(Action a)
        {
            switch (a)
            {
                case Action.Interact: return KeyCode.E;
                case Action.Sprint: return KeyCode.LeftShift;
                case Action.Crouch: return KeyCode.LeftControl;
                case Action.PushToMute: return KeyCode.V;
                case Action.Pause: return KeyCode.Escape;
                case Action.DebugOverlay: return KeyCode.F3;
                case Action.Weapon1: return KeyCode.Alpha1;
                case Action.Weapon2: return KeyCode.Alpha2;
                case Action.Weapon3: return KeyCode.Alpha3;
                case Action.Journal: return KeyCode.Tab;
                default: return KeyCode.None;
            }
        }

        static KeyCode AltKeyFor(Action a)
        {
            switch (a)
            {
                case Action.Crouch: return KeyCode.C;
                case Action.Pause: return KeyCode.P;
                default: return KeyCode.None;
            }
        }

        static bool Held(KeyCode key) => Input.GetKey(key);
        static KeyCode KeyW() => KeyCode.W;
        static KeyCode KeyA() => KeyCode.A;
        static KeyCode KeyS() => KeyCode.S;
        static KeyCode KeyD() => KeyCode.D;
        static KeyCode KeyUp() => KeyCode.UpArrow;
        static KeyCode KeyDown() => KeyCode.DownArrow;
        static KeyCode KeyLeft() => KeyCode.LeftArrow;
        static KeyCode KeyRight() => KeyCode.RightArrow;
#endif
    }
}
