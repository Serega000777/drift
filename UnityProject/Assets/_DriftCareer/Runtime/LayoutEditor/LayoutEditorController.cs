using System;
using System.Collections.Generic;
using DriftCareer.Core;
using DriftCareer.Core.Layout;
using DriftCareer.Runtime.Cameras;
using DriftCareer.Runtime.Input;
using DriftCareer.Runtime.Track;
using UnityEngine;

namespace DriftCareer.Runtime.LayoutEditor
{
    /// <summary>
    /// Top-down obstacle editor: translates pointer gestures (mouse or multi-touch, delivered by the UI viewport)
    /// and keyboard shortcuts into <see cref="LayoutEditorModel"/> commands, with a live ghost preview.
    /// The car is frozen while editing; dynamic props are held kinematic at their placed poses.
    /// </summary>
    public sealed class LayoutEditorController : MonoBehaviour
    {
        private TrackSession _session;
        private CameraRig _cam;
        private GameObject _ghost;
        private string _ghostDef;
        private float _ghostYaw;
        private Material _ghostOk, _ghostBad, _selectMat;
        private GameObject _selectionRing;
        private readonly Dictionary<int, Vector2> _pointers = new Dictionary<int, Vector2>();
        private int _dragUid = -1;
        private bool _panning;
        private Vector2 _lastPan;
        private float _lastPinchDist;
        private float _lastPinchAngle;
        private Vector2 _downPos;
        private bool _movedSinceDown;
        private Vec2 _dragOffset;

        public bool Active { get; private set; }
        public string SelectedDef { get; private set; } = "cone";
        public LayoutEditorModel Model => _session?.Layout;
        public bool IsTouch { get; set; }

        /// <summary>Localized-key status message (validation errors, saves).</summary>
        public event Action<string, string> Status;
        public event Action SelectionChanged;

        public void Initialize(TrackSession session, CameraRig cam)
        {
            _session = session;
            _cam = cam;
            _ghostOk = MaterialLibrary.Transparent("ghost_ok", new Color(0.2f, 1f, 0.45f, 0.45f));
            _ghostBad = MaterialLibrary.Transparent("ghost_bad", new Color(1f, 0.2f, 0.15f, 0.45f));
            _selectMat = MaterialLibrary.UnlitColor("select_ring", new Color(1f, 0.85f, 0.1f));
        }

        public void Enter()
        {
            if (_session == null || Active) return;
            Active = true;
            _session.Car.SetFrozen(true);
            _session.Obstacles.SetEditing(true);
            var carPos = _session.Car.transform.position;
            Model.Validator.DynamicZones.Clear();
            Model.Validator.DynamicZones.Add(new OrientedRect("car", new Vec2(carPos.x, carPos.z), new Vec2(1.6f, 2.9f), _session.Car.transform.eulerAngles.y));
            _cam.topDownFocus = new Vector3(carPos.x, 0f, carPos.z + 20f);
            _cam.topDownSize = 40f;
            _cam.topDownYaw = 0f;
            _cam.SetMode(RigMode.TopDown);
            SelectDef(SelectedDef);
        }

        public void Exit()
        {
            if (!Active) return;
            Model.CommitGesture();
            Active = false;
            DestroyGhost();
            if (_selectionRing != null) _selectionRing.SetActive(false);
            Model.SelectedUid = -1;
            _session.Obstacles.SetEditing(false);
            _session.Car.SetFrozen(false);
            _pointers.Clear();
        }

        public void SelectDef(string defId)
        {
            var def = Model.Library.Get(defId);
            if (def == null) return;
            SelectedDef = defId;
            DestroyGhost();
            _ghost = ObstacleFactory.Create(def, transform, true);
            _ghostDef = defId;
            ObstacleFactory.MakeGhost(_ghost, _ghostOk);
            _ghost.SetActive(Active);
            SelectionChanged?.Invoke();
        }

        private void DestroyGhost()
        {
            if (_ghost != null) Destroy(_ghost);
            _ghost = null;
        }

        // ---------------- UI commands ----------------

        public void RotateSelectedOrGhost(float deltaDeg)
        {
            if (Model.SelectedUid >= 0) Report(Model.Rotate(Model.SelectedUid, deltaDeg));
            else _ghostYaw = Model.SnapYaw(SelectedDef, _ghostYaw + deltaDeg);
        }

        public void DeleteSelected()
        {
            if (Model.SelectedUid >= 0 && Model.Delete(Model.SelectedUid)) SelectionChanged?.Invoke();
        }

        public void DuplicateSelected()
        {
            if (Model.SelectedUid < 0) return;
            var r = Model.Duplicate(Model.SelectedUid, new Vec2(1.5f, 0f), out _);
            Report(r);
            SelectionChanged?.Invoke();
        }

        public void Undo() { Model.Undo(); SelectionChanged?.Invoke(); }
        public void Redo() { Model.Redo(); SelectionChanged?.Invoke(); }
        public void Clear() { Model.Clear(); SelectionChanged?.Invoke(); }
        public void ToggleSnap() => Model.SnapToGrid = !Model.SnapToGrid;

        public int ApplyPreset(string id)
        {
            int skipped = Model.ApplyPreset(LayoutPresets.Create(id, _session.Site));
            Model.Layout.name = id;
            SelectionChanged?.Invoke();
            return skipped;
        }

        /// <summary>Places the ghost at the screen center (mobile "place" button).</summary>
        public void PlaceAtCenter()
        {
            if (!ScreenToGround(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out var p)) return;
            Report(Model.Place(SelectedDef, p, _ghostYaw, out _));
        }

        // ---------------- pointer gestures (screen pixels, origin bottom-left) ----------------

        public void PointerDown(int id, Vector2 screen, int button)
        {
            if (!Active) return;
            _pointers[id] = screen;
            if (_pointers.Count == 2)
            {
                // Second finger: cancel any drag and start pinch/twist.
                Model.CancelGesture();
                _dragUid = -1;
                _panning = false;
                StartPinch();
                return;
            }
            _downPos = screen;
            _movedSinceDown = false;
            if (button == 1 || button == 2) { _panning = true; _lastPan = screen; return; }
            if (!ScreenToGround(screen, out var g)) return;
            int hit = Model.Pick(g, IsTouch ? 0.9f : 0.4f);
            if (hit >= 0)
            {
                Model.SelectedUid = hit;
                _dragUid = hit;
                var p = Model.Layout.placements[Model.Layout.IndexOf(hit)];
                _dragOffset = p.position - g;
                Model.BeginGesture();
                SelectionChanged?.Invoke();
            }
            else { _panning = IsTouch; _lastPan = screen; }
        }

        public void PointerMove(int id, Vector2 screen)
        {
            if (!Active) return;
            if (_pointers.ContainsKey(id)) _pointers[id] = screen;
            if ((screen - _downPos).sqrMagnitude > 36f) _movedSinceDown = true;
            if (_pointers.Count >= 2) { UpdatePinch(); return; }
            if (_dragUid >= 0)
            {
                if (ScreenToGround(screen, out var g))
                {
                    var p = Model.Layout.placements[Model.Layout.IndexOf(_dragUid)];
                    var r = Model.PreviewTransform(_dragUid, g + _dragOffset, p.yawDeg);
                    if (!r.Ok) Report(r);
                }
                return;
            }
            if (_panning && _pointers.ContainsKey(id))
            {
                Vector2 d = screen - _lastPan;
                _lastPan = screen;
                float metersPerPixel = _cam.topDownSize * 2f / Mathf.Max(1, Screen.height);
                _cam.topDownFocus -= Quaternion.Euler(0f, _cam.topDownYaw, 0f) * new Vector3(d.x, 0f, d.y) * metersPerPixel;
                ClampFocus();
            }
        }

        public void PointerUp(int id, Vector2 screen, int button)
        {
            if (!Active) return;
            bool wasPinch = _pointers.Count >= 2;
            _pointers.Remove(id);
            if (wasPinch) return;
            if (_dragUid >= 0)
            {
                Model.CommitGesture();
                _dragUid = -1;
                return;
            }
            if (_panning)
            {
                _panning = false;
                if (_movedSinceDown || button != 0) return;
            }
            if (button != 0 || _movedSinceDown) return;
            // Tap on empty ground: deselect or place.
            if (Model.SelectedUid >= 0) { Model.SelectedUid = -1; SelectionChanged?.Invoke(); return; }
            if (ScreenToGround(screen, out var g)) Report(Model.Place(SelectedDef, g, _ghostYaw, out _));
        }

        public void Scroll(float delta)
        {
            if (!Active) return;
            _cam.topDownSize = Mathf.Clamp(_cam.topDownSize * (1f - delta * 0.1f), 8f, 140f);
        }

        private void StartPinch()
        {
            GetTwo(out var a, out var b);
            _lastPinchDist = Vector2.Distance(a, b);
            _lastPinchAngle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        }

        private void UpdatePinch()
        {
            GetTwo(out var a, out var b);
            float dist = Vector2.Distance(a, b);
            float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
            if (_lastPinchDist > 1f) _cam.topDownSize = Mathf.Clamp(_cam.topDownSize * _lastPinchDist / Mathf.Max(1f, dist), 8f, 140f);
            float dAng = Mathf.DeltaAngle(_lastPinchAngle, ang);
            if (Model.SelectedUid >= 0 && Mathf.Abs(dAng) > 12f)
            {
                Model.Rotate(Model.SelectedUid, -Mathf.Sign(dAng) * 15f);
                _lastPinchAngle = ang;
            }
            else if (Model.SelectedUid < 0) { _cam.topDownYaw -= dAng; _lastPinchAngle = ang; }
            _lastPinchDist = dist;
        }

        private void GetTwo(out Vector2 a, out Vector2 b)
        {
            a = b = Vector2.zero;
            int i = 0;
            foreach (var p in _pointers.Values)
            {
                if (i == 0) a = p; else if (i == 1) b = p;
                i++;
            }
        }

        private void ClampFocus()
        {
            float h = _session.Site.lotSize * 0.5f;
            var f = _cam.topDownFocus;
            _cam.topDownFocus = new Vector3(Mathf.Clamp(f.x, -h, h), 0f, Mathf.Clamp(f.z, -h, h));
        }

        public bool ScreenToGround(Vector2 screen, out Vec2 ground)
        {
            ground = Vec2.Zero;
            if (_cam == null || _cam.Cam == null) return false;
            var ray = _cam.Cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            var p = ray.origin + ray.direction * t;
            ground = new Vec2(p.x, p.z);
            return true;
        }

        private void Report(PlacementResult r)
        {
            if (r.Ok) return;
            string detail = r.detail;
            if (r.error == PlacementError.ForbiddenZone && detail != null) detail = "editor.zone." + detail;
            Status?.Invoke("editor.invalid." + r.error, detail);
        }

        private void Update()
        {
            if (!Active || Model == null) return;
            HandleKeys();
            UpdateGhost();
            UpdateSelectionRing();
        }

        private void HandleKeys()
        {
            if (Keys.Down(DcKey.Q)) RotateSelectedOrGhost(-15f);
            if (Keys.Down(DcKey.E)) RotateSelectedOrGhost(15f);
            if (Keys.Down(DcKey.Delete) || Keys.Down(DcKey.Backspace)) DeleteSelected();
            if (Keys.Held(DcKey.Ctrl) && Keys.Down(DcKey.Z)) { if (Keys.Held(DcKey.Shift)) Redo(); else Undo(); }
            if (Keys.Held(DcKey.Ctrl) && Keys.Down(DcKey.Y)) Redo();
            if (Keys.Held(DcKey.Ctrl) && Keys.Down(DcKey.D)) DuplicateSelected();
            if (Keys.Down(DcKey.G)) ToggleSnap();
        }

        private Vector2 _hover = new Vector2(-1f, -1f);

        /// <summary>Desktop hover position for the ghost (from UI pointer-move without buttons).</summary>
        public void Hover(Vector2 screen) => _hover = screen;

        private void UpdateGhost()
        {
            if (_ghost == null) return;
            bool show = Model.SelectedUid < 0 && _dragUid < 0;
            Vector2 screen = IsTouch || _hover.x < 0f ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) : _hover;
            if (!show || !ScreenToGround(screen, out var g)) { _ghost.SetActive(false); return; }
            _ghost.SetActive(true);
            var snapped = Model.Snap(g);
            _ghost.transform.SetPositionAndRotation(new Vector3(snapped.X, 0.01f, snapped.Y), Quaternion.Euler(0f, _ghostYaw, 0f));
            var r = Model.CanPlace(SelectedDef, g, _ghostYaw);
            ObstacleFactory.MakeGhost(_ghost, r.Ok ? _ghostOk : _ghostBad);
        }

        private void UpdateSelectionRing()
        {
            int uid = Model.SelectedUid;
            if (uid < 0 || Model.Layout.IndexOf(uid) < 0)
            {
                if (_selectionRing != null) _selectionRing.SetActive(false);
                return;
            }
            var p = Model.Layout.placements[Model.Layout.IndexOf(uid)];
            var def = Model.Library.Get(p.defId);
            if (_selectionRing == null)
                _selectionRing = ProceduralMeshes.Spawn("SelectionRing", transform, ProceduralMeshes.Ring(1f, 0.08f, 48), _selectMat, Vector3.zero, Quaternion.identity, false);
            _selectionRing.SetActive(true);
            float r = def != null ? def.footprint.BoundingRadius + 0.3f : 1f;
            _selectionRing.transform.position = new Vector3(p.position.X, 0.03f, p.position.Y);
            _selectionRing.transform.localScale = new Vector3(r, 1f, r);
        }
    }
}
