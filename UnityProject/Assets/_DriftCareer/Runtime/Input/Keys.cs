using UnityEngine;
#if ENABLE_INPUT_SYSTEM && DC_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DriftCareer.Runtime.Input
{
    public enum DcKey { Q, E, R, Delete, Backspace, Z, Y, D, G, Ctrl, Shift, Escape, Enter, Tab, F1 }

    /// <summary>Backend-independent keyboard queries for tools such as the layout editor.</summary>
    public static class Keys
    {
#if ENABLE_INPUT_SYSTEM && DC_INPUT_SYSTEM
        private static UnityEngine.InputSystem.Controls.KeyControl Control(Keyboard kb, DcKey k)
        {
            switch (k)
            {
                case DcKey.Q: return kb.qKey;
                case DcKey.E: return kb.eKey;
                case DcKey.R: return kb.rKey;
                case DcKey.Delete: return kb.deleteKey;
                case DcKey.Backspace: return kb.backspaceKey;
                case DcKey.Z: return kb.zKey;
                case DcKey.Y: return kb.yKey;
                case DcKey.D: return kb.dKey;
                case DcKey.G: return kb.gKey;
                case DcKey.Ctrl: return kb.leftCtrlKey;
                case DcKey.Shift: return kb.leftShiftKey;
                case DcKey.Escape: return kb.escapeKey;
                case DcKey.Enter: return kb.enterKey;
                case DcKey.Tab: return kb.tabKey;
                default: return kb.f1Key;
            }
        }

        public static bool Held(DcKey k)
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            if (k == DcKey.Ctrl) return kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed || kb.leftCommandKey.isPressed;
            return Control(kb, k).isPressed;
        }

        public static bool Down(DcKey k)
        {
            var kb = Keyboard.current;
            return kb != null && Control(kb, k).wasPressedThisFrame;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        private static KeyCode Code(DcKey k)
        {
            switch (k)
            {
                case DcKey.Q: return KeyCode.Q;
                case DcKey.E: return KeyCode.E;
                case DcKey.R: return KeyCode.R;
                case DcKey.Delete: return KeyCode.Delete;
                case DcKey.Backspace: return KeyCode.Backspace;
                case DcKey.Z: return KeyCode.Z;
                case DcKey.Y: return KeyCode.Y;
                case DcKey.D: return KeyCode.D;
                case DcKey.G: return KeyCode.G;
                case DcKey.Ctrl: return KeyCode.LeftControl;
                case DcKey.Shift: return KeyCode.LeftShift;
                case DcKey.Escape: return KeyCode.Escape;
                case DcKey.Enter: return KeyCode.Return;
                case DcKey.Tab: return KeyCode.Tab;
                default: return KeyCode.F1;
            }
        }

        public static bool Held(DcKey k)
        {
            if (k == DcKey.Ctrl) return UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl) || UnityEngine.Input.GetKey(KeyCode.LeftCommand);
            return UnityEngine.Input.GetKey(Code(k));
        }

        public static bool Down(DcKey k) => UnityEngine.Input.GetKeyDown(Code(k));
#else
        public static bool Held(DcKey k) => false;
        public static bool Down(DcKey k) => false;
#endif
    }
}
