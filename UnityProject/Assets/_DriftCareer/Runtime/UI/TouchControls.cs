using DriftCareer.Core.Persistence;
using DriftCareer.Runtime.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace DriftCareer.Runtime.UI
{
    /// <summary>
    /// Multi-touch driving controls in UI Toolkit: steering wheel / slider / buttons / tilt, analog throttle
    /// (vertical position = pedal depth), brake, handbrake, shift. Left-handed layout mirrors sides.
    /// Each control captures its own pointer so several fingers work at once.
    /// </summary>
    public sealed class TouchControls
    {
        private readonly TouchDriveState _state;
        private SettingsData _settings;
        private VisualElement _wheel;
        private VisualElement _knob;
        private float _wheelAngle;
        private int _steerPointer = -1;
        private Vector2 _steerOrigin;
        private bool _leftDown, _rightDown;

        public VisualElement Root { get; } = Ui.El("tc-root");

        public TouchControls(TouchDriveState state, SettingsData settings)
        {
            _state = state;
            _settings = settings;
            Root.pickingMode = PickingMode.Ignore;
            Rebuild(settings);
        }

        public void Rebuild(SettingsData settings)
        {
            _settings = settings;
            Root.Clear();
            var steerArea = Ui.El("tc-steer-area");
            Root.Add(steerArea);
            switch (settings.controlScheme)
            {
                case ControlScheme.TouchButtons:
                {
                    var l = Pad("◄", "tc-btn-left", v => _leftDown = v > 0f);
                    var r = Pad("►", "tc-btn-right", v => _rightDown = v > 0f);
                    Root.Add(l); Root.Add(r);
                    break;
                }
                case ControlScheme.TouchSlider:
                {
                    var track = Ui.El("tc-slider-track");
                    _knob = Ui.El("tc-slider-knob");
                    track.Add(_knob);
                    steerArea.Add(track);
                    BindSteer(steerArea, true);
                    break;
                }
                case ControlScheme.Tilt:
                {
                    var calibrate = Ui.Button("0°", () => GameServices.Instance.Input.CalibrateTilt(), "dc-btn--icon");
                    calibrate.style.position = Position.Absolute; calibrate.style.left = 40; calibrate.style.bottom = 40;
                    Root.Add(calibrate);
                    break;
                }
                default:
                {
                    _wheel = Ui.El("tc-wheel");
                    _wheel.Add(Ui.El("tc-wheel-top"));
                    steerArea.Add(_wheel);
                    BindSteer(steerArea, false);
                    break;
                }
            }
            Root.Add(Pad("", "tc-throttle", v => _state.throttle = v, true));
            Root.Add(Pad("", "tc-brake", v => _state.brake = v, true));
            Root.Add(Pad("", "tc-handbrake", v => _state.handbrake = v > 0f ? 1f : 0f));
            Root.Add(Pad("+", "tc-shift-up", v => { if (v > 0f) _state.shiftUp = true; }));
            Root.Add(Pad("−", "tc-shift-down", v => { if (v > 0f) _state.shiftDown = true; }));
            Label(Root, "tc-throttle", "hud.throttle");
            Label(Root, "tc-brake", "hud.brake");
            Label(Root, "tc-handbrake", "hud.handbrake");
            // Left-handed: mirror the layout, then un-mirror each control so labels stay readable.
            var flip = new Scale(new Vector3(settings.leftHanded ? -1f : 1f, 1f, 1f));
            Root.style.scale = flip;
            foreach (var child in Root.Children()) child.style.scale = flip;
        }

        private void Label(VisualElement root, string cls, string key)
        {
            foreach (var e in root.Children())
                if (e.ClassListContains(cls) && e is Label l && string.IsNullOrEmpty(l.text)) l.text = GameServices.Instance.T(key);
        }

        private VisualElement Pad(string text, string cls, System.Action<float> set, bool analog = false)
        {
            var pad = new Label(text);
            pad.AddToClassList("tc-pad");
            pad.AddToClassList(cls);
            int pointer = -1;
            pad.RegisterCallback<PointerDownEvent>(e =>
            {
                pointer = e.pointerId;
                pad.CapturePointer(pointer);
                pad.AddToClassList("tc-pad--down");
                GameServices.Instance.Input.NotifyTouch();
                _state.anyActive = true;
                set(analog ? Depth(pad, e.localPosition) : 1f);
                e.StopPropagation();
            });
            pad.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != pointer || !analog) return;
                set(Depth(pad, e.localPosition));
            });
            void Release(int id)
            {
                if (id != pointer) return;
                if (pad.HasPointerCapture(pointer)) pad.ReleasePointer(pointer);
                pointer = -1;
                pad.RemoveFromClassList("tc-pad--down");
                set(0f);
            }
            pad.RegisterCallback<PointerUpEvent>(e => Release(e.pointerId));
            pad.RegisterCallback<PointerCancelEvent>(e => Release(e.pointerId));
            return pad;
        }

        /// <summary>Analog pedal: pressing near the top of the pad = full, near the bottom = light.</summary>
        private static float Depth(VisualElement pad, Vector3 local)
        {
            float h = Mathf.Max(1f, pad.layout.height);
            return Mathf.Clamp(1.15f - local.y / h * 0.6f, 0.35f, 1f);
        }

        private void BindSteer(VisualElement area, bool slider)
        {
            area.RegisterCallback<PointerDownEvent>(e =>
            {
                _steerPointer = e.pointerId;
                _steerOrigin = e.localPosition;
                area.CapturePointer(_steerPointer);
                GameServices.Instance.Input.NotifyTouch();
                _state.anyActive = true;
                UpdateSteer(area, e.localPosition, slider);
                e.StopPropagation();
            });
            area.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId == _steerPointer) UpdateSteer(area, e.localPosition, slider);
            });
            void End(int id)
            {
                if (id != _steerPointer) return;
                if (area.HasPointerCapture(id)) area.ReleasePointer(id);
                _steerPointer = -1;
            }
            area.RegisterCallback<PointerUpEvent>(e => End(e.pointerId));
            area.RegisterCallback<PointerCancelEvent>(e => End(e.pointerId));
        }

        private void UpdateSteer(VisualElement area, Vector2 local, bool slider)
        {
            float w = Mathf.Max(1f, area.layout.width);
            if (slider)
            {
                _state.steer = Mathf.Clamp((local.x - w * 0.5f) / (w * 0.42f), -1f, 1f);
            }
            else
            {
                // Virtual wheel: horizontal drag from the touch origin turns the wheel (more forgiving than angles).
                _state.steer = Mathf.Clamp((local.x - _steerOrigin.x) / (w * 0.38f), -1f, 1f);
            }
        }

        public void Tick(float dt)
        {
            if (_settings.controlScheme == ControlScheme.TouchButtons)
            {
                float target = (_rightDown ? 1f : 0f) - (_leftDown ? 1f : 0f);
                _state.steer = Mathf.MoveTowards(_state.steer, target, (target == 0f ? 6f : 3.5f) * dt);
            }
            else if (_steerPointer < 0)
            {
                _state.steer = Mathf.MoveTowards(_state.steer, 0f, 6f * dt);
            }
            _wheelAngle = Mathf.Lerp(_wheelAngle, _state.steer * 135f, 1f - Mathf.Exp(-18f * dt));
            if (_wheel != null) _wheel.style.rotate = new Rotate(_wheelAngle);
            if (_knob != null)
            {
                float w = _knob.parent != null ? _knob.parent.layout.width : 480f;
                _knob.style.left = (w - 96f) * (0.5f + _state.steer * 0.5f);
            }
            _state.anyActive = _steerPointer >= 0 || _state.throttle > 0f || _state.brake > 0f || _state.handbrake > 0f || _leftDown || _rightDown;
        }
    }
}
