using DriftCareer.Core;
using DriftCareer.Core.Assists;
using DriftCareer.Core.Persistence;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && DC_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DriftCareer.Runtime.Input
{
    public enum InputDevice { KeyboardMouse = 0, Gamepad = 1, Touch = 2 }

    /// <summary>Menu/navigation intents (gamepad, keyboard, TV remotes on consoles).</summary>
    public struct MenuInput
    {
        public int moveX;
        public int moveY;
        public bool submit;
        public bool back;
        public bool tabLeft;
        public bool tabRight;
    }

    /// <summary>Game action buttons (edge-triggered for this frame).</summary>
    public struct ActionInput
    {
        public bool pause;
        public bool reset;
        public bool camera;
        public bool editor;
        public bool telemetry;
        public bool repair;
        public bool skip;
    }

    /// <summary>Touch driving controls state, written by the UI (multi-touch handled in UI Toolkit).</summary>
    public sealed class TouchDriveState
    {
        public float steer;
        public float throttle;
        public float brake;
        public float handbrake;
        public bool shiftUp;
        public bool shiftDown;
        public bool anyActive;
    }

    /// <summary>
    /// Device abstraction: reads keyboard/gamepad (new Input System when enabled, legacy otherwise), touch UI and tilt,
    /// and outputs device-independent <see cref="DriverInput"/>. The last used device wins (for HUD prompts and touch UI).
    /// </summary>
    public sealed class InputRouter
    {
        private float _kbSteer;
        private float _tiltCenter;
        private bool _tiltEnabledDevice;

        public InputDevice ActiveDevice { get; private set; } = Application.isMobilePlatform ? InputDevice.Touch : InputDevice.KeyboardMouse;
        public TouchDriveState Touch { get; } = new TouchDriveState();
        public SettingsData Settings { get; set; }
        public DriverInput Drive { get; private set; }
        public MenuInput Menu { get; private set; }
        public ActionInput Actions { get; private set; }
        public float TiltSteer { get; private set; }

        public event System.Action<InputDevice> DeviceChanged;

        public void Update(float dt)
        {
            var s = Settings ?? new SettingsData();
            var drive = new DriverInput();
            var menu = new MenuInput();
            var actions = new ActionInput();
            bool kbUsed = false, padUsed = false;

#if ENABLE_INPUT_SYSTEM && DC_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                bool left = kb.aKey.isPressed || kb.leftArrowKey.isPressed;
                bool right = kb.dKey.isPressed || kb.rightArrowKey.isPressed;
                bool up = kb.wKey.isPressed || kb.upArrowKey.isPressed;
                bool down = kb.sKey.isPressed || kb.downArrowKey.isPressed;
                float target = (right ? 1f : 0f) - (left ? 1f : 0f);
                _kbSteer = MathUtil.MoveTowards(_kbSteer, target, (target == 0f ? 7f : 4.5f) * dt);
                if (left || right || up || down || kb.spaceKey.isPressed) kbUsed = true;
                drive.steer = _kbSteer;
                drive.digitalSteer = true;
                drive.throttle = up ? 1f : 0f;
                drive.brake = down ? 1f : 0f;
                drive.handbrake = kb.spaceKey.isPressed ? 1f : 0f;
                drive.clutch = kb.leftCtrlKey.isPressed && s.manualTransmission ? 1f : 0f;
                drive.shiftUp = kb.eKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame;
                drive.shiftDown = kb.qKey.wasPressedThisFrame;
                actions.pause = kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame;
                actions.reset = kb.rKey.wasPressedThisFrame;
                actions.camera = kb.cKey.wasPressedThisFrame;
                actions.editor = kb.tabKey.wasPressedThisFrame;
                actions.telemetry = kb.f1Key.wasPressedThisFrame || kb.tKey.wasPressedThisFrame;
                actions.repair = kb.f5Key.wasPressedThisFrame;
                actions.skip = kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame;
                menu.moveX = (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame ? 1 : 0) - (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame ? 1 : 0);
                menu.moveY = (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame ? 1 : 0) - (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame ? 1 : 0);
                menu.submit = kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                menu.back = kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame;
                menu.tabLeft = kb.qKey.wasPressedThisFrame;
                menu.tabRight = kb.eKey.wasPressedThisFrame;
                if (kb.anyKey.wasPressedThisFrame) kbUsed = true;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                float dz = s.steeringDeadzone;
                float sx = Mathf.Abs(stick.x) < dz ? 0f : Mathf.Sign(stick.x) * (Mathf.Abs(stick.x) - dz) / (1f - dz);
                float rt = pad.rightTrigger.ReadValue(), lt = pad.leftTrigger.ReadValue();
                bool hb = pad.buttonSouth.isPressed || pad.rightShoulder.isPressed;
                if (Mathf.Abs(sx) > 0.05f || rt > 0.05f || lt > 0.05f || hb) padUsed = true;
                if (padUsed || ActiveDevice == InputDevice.Gamepad)
                {
                    drive.steer = Mathf.Abs(sx) > Mathf.Abs(drive.steer) ? sx : drive.steer;
                    drive.digitalSteer = Mathf.Abs(sx) < 0.01f && drive.digitalSteer;
                    drive.throttle = Mathf.Max(drive.throttle, rt);
                    drive.brake = Mathf.Max(drive.brake, lt);
                    drive.handbrake = Mathf.Max(drive.handbrake, hb ? 1f : 0f);
                    drive.clutch = Mathf.Max(drive.clutch, pad.leftShoulder.isPressed && s.manualTransmission ? 1f : 0f);
                }
                drive.shiftUp |= pad.buttonNorth.wasPressedThisFrame;
                drive.shiftDown |= pad.buttonWest.wasPressedThisFrame;
                actions.pause |= pad.startButton.wasPressedThisFrame;
                actions.reset |= pad.selectButton.wasPressedThisFrame;
                actions.camera |= pad.rightStickButton.wasPressedThisFrame;
                actions.editor |= pad.dpad.up.wasPressedThisFrame;
                actions.telemetry |= pad.dpad.down.wasPressedThisFrame;
                actions.skip |= pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame;
                menu.moveX += (pad.dpad.right.wasPressedThisFrame ? 1 : 0) - (pad.dpad.left.wasPressedThisFrame ? 1 : 0);
                menu.moveY += (pad.dpad.up.wasPressedThisFrame ? 1 : 0) - (pad.dpad.down.wasPressedThisFrame ? 1 : 0);
                menu.submit |= pad.buttonSouth.wasPressedThisFrame;
                menu.back |= pad.buttonEast.wasPressedThisFrame;
                menu.tabLeft |= pad.leftShoulder.wasPressedThisFrame;
                menu.tabRight |= pad.rightShoulder.wasPressedThisFrame;
                if (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame || pad.dpad.ReadValue().sqrMagnitude > 0.1f) padUsed = true;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            bool left = UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow);
            bool right = UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow);
            bool up = UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow);
            bool down = UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow);
            float target = (right ? 1f : 0f) - (left ? 1f : 0f);
            _kbSteer = MathUtil.MoveTowards(_kbSteer, target, (target == 0f ? 7f : 4.5f) * dt);
            if (left || right || up || down || UnityEngine.Input.anyKeyDown) kbUsed = true;
            drive.steer = _kbSteer;
            drive.digitalSteer = true;
            drive.throttle = up ? 1f : 0f;
            drive.brake = down ? 1f : 0f;
            drive.handbrake = UnityEngine.Input.GetKey(KeyCode.Space) ? 1f : 0f;
            drive.shiftUp = UnityEngine.Input.GetKeyDown(KeyCode.E) || UnityEngine.Input.GetKeyDown(KeyCode.LeftShift);
            drive.shiftDown = UnityEngine.Input.GetKeyDown(KeyCode.Q);
            actions.pause = UnityEngine.Input.GetKeyDown(KeyCode.Escape) || UnityEngine.Input.GetKeyDown(KeyCode.P);
            actions.reset = UnityEngine.Input.GetKeyDown(KeyCode.R);
            actions.camera = UnityEngine.Input.GetKeyDown(KeyCode.C);
            actions.editor = UnityEngine.Input.GetKeyDown(KeyCode.Tab);
            actions.telemetry = UnityEngine.Input.GetKeyDown(KeyCode.F1) || UnityEngine.Input.GetKeyDown(KeyCode.T);
            actions.repair = UnityEngine.Input.GetKeyDown(KeyCode.F5);
            actions.skip = UnityEngine.Input.GetKeyDown(KeyCode.Space) || UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.Escape);
            menu.moveX = (UnityEngine.Input.GetKeyDown(KeyCode.RightArrow) ? 1 : 0) - (UnityEngine.Input.GetKeyDown(KeyCode.LeftArrow) ? 1 : 0);
            menu.moveY = (UnityEngine.Input.GetKeyDown(KeyCode.UpArrow) ? 1 : 0) - (UnityEngine.Input.GetKeyDown(KeyCode.DownArrow) ? 1 : 0);
            menu.submit = UnityEngine.Input.GetKeyDown(KeyCode.Return);
            menu.back = UnityEngine.Input.GetKeyDown(KeyCode.Escape) || UnityEngine.Input.GetKeyDown(KeyCode.Backspace);
#endif

            // Touch (from UI) and tilt.
            var t = Touch;
            if (t.anyActive)
            {
                ActiveDevice = InputDevice.Touch;
                float steer = t.steer;
                if (s.controlScheme == ControlScheme.Tilt) steer = ReadTilt(s);
                drive.steer = MathUtil.Clamp(steer * s.steeringSensitivity, -1f, 1f);
                drive.digitalSteer = s.controlScheme == ControlScheme.TouchButtons;
                drive.throttle = Mathf.Max(drive.throttle, t.throttle);
                drive.brake = Mathf.Max(drive.brake, t.brake);
                drive.handbrake = Mathf.Max(drive.handbrake, t.handbrake);
                drive.shiftUp |= t.shiftUp;
                drive.shiftDown |= t.shiftDown;
            }
            else if (s.controlScheme == ControlScheme.Tilt && ActiveDevice == InputDevice.Touch)
            {
                drive.steer = MathUtil.Clamp(ReadTilt(s) * s.steeringSensitivity, -1f, 1f);
            }
            t.shiftUp = t.shiftDown = false;

            if (padUsed) SetDevice(InputDevice.Gamepad);
            else if (kbUsed) SetDevice(InputDevice.KeyboardMouse);

            if (ActiveDevice != InputDevice.Touch) drive.steer = MathUtil.Clamp(drive.steer * (drive.digitalSteer ? 1f : s.steeringSensitivity), -1f, 1f);
            Drive = drive;
            Menu = menu;
            Actions = actions;
        }

        public void NotifyTouch() => SetDevice(InputDevice.Touch);

        private void SetDevice(InputDevice d)
        {
            if (d == ActiveDevice) return;
            ActiveDevice = d;
            DeviceChanged?.Invoke(d);
        }

        /// <summary>Re-centers tilt steering at the current device orientation.</summary>
        public void CalibrateTilt() => _tiltCenter = RawTilt();

        private float RawTilt()
        {
#if ENABLE_INPUT_SYSTEM && DC_INPUT_SYSTEM
            var acc = Accelerometer.current;
            if (acc == null) return 0f;
            if (!_tiltEnabledDevice) { InputSystem.EnableDevice(acc); _tiltEnabledDevice = true; }
            return acc.acceleration.ReadValue().x;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return UnityEngine.Input.acceleration.x;
#else
            return 0f;
#endif
        }

        private float ReadTilt(SettingsData s)
        {
            float x = (RawTilt() - _tiltCenter) * 2.2f * s.tiltSensitivity;
            TiltSteer = MathUtil.Clamp(x, -1f, 1f);
            return TiltSteer;
        }

        /// <summary>Gamepad rumble / phone vibration for impacts and blowouts.</summary>
        public void Rumble(float strength, float seconds, MonoBehaviour host)
        {
            if (Settings != null && !Settings.vibration) return;
#if ENABLE_INPUT_SYSTEM && DC_INPUT_SYSTEM
            var pad = Gamepad.current;
            if (pad != null && host != null)
            {
                pad.SetMotorSpeeds(strength * 0.6f, strength);
                host.StartCoroutine(StopRumble(pad, seconds));
                return;
            }
#endif
#if UNITY_ANDROID || UNITY_IOS
            if (strength > 0.5f) Handheld.Vibrate();
#endif
        }

#if ENABLE_INPUT_SYSTEM && DC_INPUT_SYSTEM
        private static System.Collections.IEnumerator StopRumble(Gamepad pad, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (pad != null) pad.SetMotorSpeeds(0f, 0f);
        }
#endif
    }
}
