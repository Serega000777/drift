using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Layout
{
    /// <summary>
    /// Obstacle layout editing with validation and snapshot-based undo/redo.
    /// Continuous gestures (drag/rotate) use Begin/Preview/Commit so one gesture = one undo step.
    /// Pure logic: the Unity controller only translates pointer input into these calls.
    /// </summary>
    public sealed class LayoutEditorModel
    {
        public const int MaxHistory = 100;

        private readonly List<List<ObstaclePlacement>> _undo = new List<List<ObstaclePlacement>>();
        private readonly List<List<ObstaclePlacement>> _redo = new List<List<ObstaclePlacement>>();
        private List<ObstaclePlacement> _gestureSnapshot;

        public ObstacleLayout Layout { get; private set; }
        public ObstacleLibrary Library { get; }
        public PlacementValidator Validator { get; }
        public float GridSize { get; set; } = 0.5f;
        public bool SnapToGrid { get; set; } = true;
        public int SelectedUid { get; set; } = -1;

        /// <summary>Raised after any change of the placements list (place/move/delete/undo...).</summary>
        public event Action Changed;

        public LayoutEditorModel(ObstacleLibrary library, PlacementRules rules, ObstacleLayout layout = null)
        {
            Library = library;
            Validator = new PlacementValidator(library, rules);
            Layout = layout ?? new ObstacleLayout();
        }

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public int Count => Layout.placements.Count;
        public float BudgetUsed => Validator.UsedBudget(Layout);
        public float BudgetTotal => Validator.Rules.budget;
        public bool InGesture => _gestureSnapshot != null;

        public Vec2 Snap(Vec2 p)
        {
            if (!SnapToGrid || GridSize <= 0f) return p;
            return new Vec2((float)Math.Round(p.X / GridSize) * GridSize, (float)Math.Round(p.Y / GridSize) * GridSize);
        }

        public float SnapYaw(string defId, float yawDeg)
        {
            var d = Library.Get(defId);
            float step = d != null && d.snapRotationDeg > 0f ? d.snapRotationDeg : 15f;
            return MathUtil.WrapAngleDeg((float)Math.Round(yawDeg / step) * step);
        }

        public PlacementResult CanPlace(string defId, Vec2 pos, float yaw) => Validator.Validate(Layout, defId, Snap(pos), yaw);

        public void ReplaceLayout(ObstacleLayout layout)
        {
            Layout = layout ?? new ObstacleLayout();
            _undo.Clear();
            _redo.Clear();
            _gestureSnapshot = null;
            SelectedUid = -1;
            Changed?.Invoke();
        }

        private void PushUndo(List<ObstaclePlacement> snapshot)
        {
            _undo.Add(snapshot);
            if (_undo.Count > MaxHistory) _undo.RemoveAt(0);
            _redo.Clear();
        }

        private List<ObstaclePlacement> Snapshot() => new List<ObstaclePlacement>(Layout.placements);

        private void Touch()
        {
            Layout.modifiedUtc = DateTime.UtcNow.ToString("o");
            Changed?.Invoke();
        }

        public PlacementResult Place(string defId, Vec2 pos, float yawDeg, out int uid)
        {
            uid = -1;
            pos = Snap(pos);
            var r = Validator.Validate(Layout, defId, pos, yawDeg);
            if (!r.Ok) return r;
            PushUndo(Snapshot());
            uid = Layout.NextUid();
            Layout.placements.Add(new ObstaclePlacement { uid = uid, defId = defId, position = pos, yawDeg = MathUtil.WrapAngleDeg(yawDeg) });
            SelectedUid = uid;
            Touch();
            return r;
        }

        public bool Delete(int uid)
        {
            int i = Layout.IndexOf(uid);
            if (i < 0) return false;
            PushUndo(Snapshot());
            Layout.placements.RemoveAt(i);
            if (SelectedUid == uid) SelectedUid = -1;
            Touch();
            return true;
        }

        public PlacementResult Duplicate(int uid, Vec2 offset, out int newUid)
        {
            newUid = -1;
            int i = Layout.IndexOf(uid);
            if (i < 0) return PlacementResult.Fail(PlacementError.NotFound);
            var src = Layout.placements[i];
            return Place(src.defId, src.position + offset, src.yawDeg, out newUid);
        }

        public void Clear()
        {
            if (Layout.placements.Count == 0) return;
            PushUndo(Snapshot());
            Layout.placements.Clear();
            SelectedUid = -1;
            Touch();
        }

        // ---- Gesture API (move/rotate one object; validated live, committed once) ----

        public void BeginGesture()
        {
            if (_gestureSnapshot == null) _gestureSnapshot = Snapshot();
        }

        /// <summary>Applies a move/rotation preview if valid. Returns the validation result.</summary>
        public PlacementResult PreviewTransform(int uid, Vec2 pos, float yawDeg)
        {
            int i = Layout.IndexOf(uid);
            if (i < 0) return PlacementResult.Fail(PlacementError.NotFound);
            var p = Layout.placements[i];
            pos = Snap(pos);
            var r = Validator.Validate(Layout, p.defId, pos, yawDeg, uid);
            if (!r.Ok) return r;
            p.position = pos;
            p.yawDeg = MathUtil.WrapAngleDeg(yawDeg);
            Layout.placements[i] = p;
            Changed?.Invoke();
            return r;
        }

        public void CommitGesture()
        {
            if (_gestureSnapshot == null) return;
            if (!SameAs(_gestureSnapshot, Layout.placements)) { PushUndo(_gestureSnapshot); Touch(); }
            _gestureSnapshot = null;
        }

        public void CancelGesture()
        {
            if (_gestureSnapshot == null) return;
            Layout.placements = _gestureSnapshot;
            _gestureSnapshot = null;
            Changed?.Invoke();
        }

        public PlacementResult Move(int uid, Vec2 pos)
        {
            int i = Layout.IndexOf(uid);
            if (i < 0) return PlacementResult.Fail(PlacementError.NotFound);
            BeginGesture();
            var r = PreviewTransform(uid, pos, Layout.placements[i].yawDeg);
            if (r.Ok) CommitGesture(); else CancelGesture();
            return r;
        }

        public PlacementResult Rotate(int uid, float deltaDeg)
        {
            int i = Layout.IndexOf(uid);
            if (i < 0) return PlacementResult.Fail(PlacementError.NotFound);
            var p = Layout.placements[i];
            BeginGesture();
            var r = PreviewTransform(uid, p.position, SnapYaw(p.defId, p.yawDeg + deltaDeg));
            if (r.Ok) CommitGesture(); else CancelGesture();
            return r;
        }

        private static bool SameAs(List<ObstaclePlacement> a, List<ObstaclePlacement> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].uid != b[i].uid || a[i].position != b[i].position || a[i].yawDeg != b[i].yawDeg || a[i].defId != b[i].defId) return false;
            return true;
        }

        public bool Undo()
        {
            if (_undo.Count == 0) return false;
            CommitGesture();
            _redo.Add(Snapshot());
            Layout.placements = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            if (Layout.IndexOf(SelectedUid) < 0) SelectedUid = -1;
            Touch();
            return true;
        }

        public bool Redo()
        {
            if (_redo.Count == 0) return false;
            _undo.Add(Snapshot());
            Layout.placements = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            if (Layout.IndexOf(SelectedUid) < 0) SelectedUid = -1;
            Touch();
            return true;
        }

        /// <summary>Replaces all objects with a generated preset (invalid entries are skipped). Undoable.</summary>
        public int ApplyPreset(IList<ObstaclePlacement> preset)
        {
            PushUndo(Snapshot());
            Layout.placements.Clear();
            int skipped = 0;
            foreach (var p in preset)
            {
                var r = Validator.Validate(Layout, p.defId, p.position, p.yawDeg);
                if (!r.Ok) { skipped++; continue; }
                var copy = p;
                copy.uid = Layout.NextUid();
                Layout.placements.Add(copy);
            }
            SelectedUid = -1;
            Touch();
            return skipped;
        }

        /// <summary>Finds the topmost obstacle under a ground point (for selection).</summary>
        public int Pick(Vec2 point, float tolerance = 0.4f)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            foreach (var p in Layout.placements)
            {
                var d = Library.Get(p.defId);
                if (d == null) continue;
                float dist = Vec2.Distance(point, p.position);
                if (dist > d.footprint.BoundingRadius + tolerance) continue;
                if (dist < bestDist) { bestDist = dist; best = p.uid; }
            }
            return best;
        }
    }
}
