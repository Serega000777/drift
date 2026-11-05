using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Gameplay
{
    public struct DrillSample
    {
        public float dt;
        public Vec2 position;
        public float speedMs;
        public float driftAngleDeg;
        public bool isDrifting;
    }

    public enum DrillState { Idle, Running, Completed }

    /// <summary>Training exercise logic. Pure, testable; scene markers are presentation only.</summary>
    public abstract class TrainingDrill
    {
        public string Id { get; protected set; }
        public string TitleKey { get; protected set; }
        public DrillState State { get; protected set; } = DrillState.Idle;
        /// <summary>0..1.</summary>
        public float Progress { get; protected set; }
        public float ElapsedSeconds { get; protected set; }
        public event Action<TrainingDrill> Completed;

        public abstract void Update(in DrillSample s);

        public virtual void Restart()
        {
            State = DrillState.Idle;
            Progress = 0f;
            ElapsedSeconds = 0f;
        }

        protected void Complete()
        {
            if (State == DrillState.Completed) return;
            State = DrillState.Completed;
            Progress = 1f;
            Completed?.Invoke(this);
        }
    }

    /// <summary>Accumulate drifting rotations around a center while staying inside the ring.</summary>
    public sealed class DonutDrill : TrainingDrill
    {
        private readonly Vec2 _center;
        private readonly float _radius;
        private readonly float _requiredTurns;
        private float _lastAngle;
        private bool _hasLast;
        private float _accumulated;

        public DonutDrill(Vec2 center, float radius, float requiredTurns = 3f)
        {
            Id = "drill.donut"; TitleKey = "drill.donut.title";
            _center = center; _radius = radius; _requiredTurns = requiredTurns;
        }

        public float Turns => _accumulated / MathUtil.TwoPi;

        public override void Restart() { base.Restart(); _hasLast = false; _accumulated = 0f; }

        public override void Update(in DrillSample s)
        {
            if (State == DrillState.Completed) return;
            Vec2 d = s.position - _center;
            bool inside = d.Length <= _radius;
            if (!inside || !s.isDrifting)
            {
                _hasLast = false;
                // Leaving the ring or stopping the drift slowly bleeds progress.
                _accumulated = MathUtil.Max(0f, MathUtil.Abs(_accumulated) - s.dt * 0.5f) * MathUtil.Sign(_accumulated);
            }
            else
            {
                State = DrillState.Running;
                ElapsedSeconds += s.dt;
                float a = MathUtil.Atan2(d.X, d.Y);
                if (_hasLast) _accumulated += MathUtil.WrapAngle(a - _lastAngle);
                _lastAngle = a;
                _hasLast = true;
            }
            Progress = MathUtil.Clamp01(MathUtil.Abs(Turns) / _requiredTurns);
            if (Progress >= 1f) Complete();
        }
    }

    /// <summary>Alternate drifting loops around two centers (figure eight) - needs transitions.</summary>
    public sealed class FigureEightDrill : TrainingDrill
    {
        private readonly Vec2 _a, _b;
        private readonly float _radius;
        private readonly int _requiredLoops;
        private float _acc;
        private float _last;
        private bool _hasLast;
        private int _currentCenter = -1;
        private int _lastCompletedCenter = -1;
        private int _loops;

        public FigureEightDrill(Vec2 centerA, Vec2 centerB, float radius, int requiredLoops = 4)
        {
            Id = "drill.figure8"; TitleKey = "drill.figure8.title";
            _a = centerA; _b = centerB; _radius = radius; _requiredLoops = requiredLoops;
        }

        public int Loops => _loops;

        public override void Restart()
        {
            base.Restart();
            _acc = 0f; _hasLast = false; _currentCenter = -1; _lastCompletedCenter = -1; _loops = 0;
        }

        public override void Update(in DrillSample s)
        {
            if (State == DrillState.Completed) return;
            float da = Vec2.Distance(s.position, _a), db = Vec2.Distance(s.position, _b);
            int center = da <= db ? 0 : 1;
            float dist = center == 0 ? da : db;
            if (center != _currentCenter) { _currentCenter = center; _acc = 0f; _hasLast = false; }
            if (dist > _radius || !s.isDrifting) { _hasLast = false; return; }
            State = DrillState.Running;
            ElapsedSeconds += s.dt;
            Vec2 d = s.position - (center == 0 ? _a : _b);
            float ang = MathUtil.Atan2(d.X, d.Y);
            if (_hasLast) _acc += MathUtil.WrapAngle(ang - _last);
            _last = ang; _hasLast = true;
            // A loop counts at ~300 degrees around one center, and must alternate centers.
            if (MathUtil.Abs(_acc) >= MathUtil.TwoPi * 0.83f && center != _lastCompletedCenter)
            {
                _loops++;
                _lastCompletedCenter = center;
                _acc = 0f;
            }
            Progress = MathUtil.Clamp01((float)_loops / _requiredLoops);
            if (_loops >= _requiredLoops) Complete();
        }
    }

    /// <summary>Pass gates in order on alternating sides (slalom).</summary>
    public sealed class SlalomDrill : TrainingDrill
    {
        private readonly List<Vec2> _gates;
        private readonly Vec2 _direction;
        private readonly float _gateHalfWidth;
        private int _next;
        private float _prevAlong;
        private bool _hasPrev;

        public SlalomDrill(IList<Vec2> conePositions, Vec2 direction, float gateHalfWidth = 6f)
        {
            Id = "drill.slalom"; TitleKey = "drill.slalom.title";
            _gates = new List<Vec2>(conePositions);
            _direction = direction.Normalized;
            _gateHalfWidth = gateHalfWidth;
        }

        public int GatesPassed => _next;
        public int GateCount => _gates.Count;
        public int Misses { get; private set; }

        public override void Restart() { base.Restart(); _next = 0; Misses = 0; _hasPrev = false; }

        public override void Update(in DrillSample s)
        {
            if (State == DrillState.Completed || _gates.Count == 0) return;
            Vec2 gate = _gates[_next];
            Vec2 rel = s.position - gate;
            float along = Vec2.Dot(rel, _direction);
            float side = Vec2.Cross(_direction, rel); // >0 left of direction, <0 right
            if (_hasPrev && _prevAlong < 0f && along >= 0f)
            {
                if (MathUtil.Abs(side) <= _gateHalfWidth)
                {
                    bool wantLeft = _next % 2 == 0;
                    bool ok = wantLeft ? side > 0f : side < 0f;
                    if (!ok) Misses++;
                    _next++;
                    State = DrillState.Running;
                }
                _hasPrev = false;
            }
            else
            {
                _prevAlong = along;
                _hasPrev = true;
            }
            if (State == DrillState.Running) ElapsedSeconds += s.dt;
            Progress = (float)_next / _gates.Count;
            if (_next >= _gates.Count) Complete();
            else if (_hasPrev == false) { _prevAlong = Vec2.Dot(s.position - _gates[_next], _direction); _hasPrev = true; }
        }
    }

    /// <summary>Drift close to clipping points. Each point needs one drifting pass inside its radius.</summary>
    public sealed class ClippingDrill : TrainingDrill
    {
        private readonly List<Vec2> _points;
        private readonly float _radius;
        private readonly bool[] _hit;

        public ClippingDrill(IList<Vec2> points, float radius = 2.5f)
        {
            Id = "drill.clipping"; TitleKey = "drill.clipping.title";
            _points = new List<Vec2>(points);
            _radius = radius;
            _hit = new bool[_points.Count];
        }

        public float BestProximity { get; private set; }

        /// <summary>0..1 proximity to the nearest point for the score multiplier.</summary>
        public float Proximity(Vec2 p)
        {
            float best = 0f;
            foreach (var c in _points) best = MathUtil.Max(best, 1f - Vec2.Distance(p, c) / _radius);
            return MathUtil.Clamp01(best);
        }

        public override void Restart() { base.Restart(); Array.Clear(_hit, 0, _hit.Length); BestProximity = 0f; }

        public override void Update(in DrillSample s)
        {
            if (State == DrillState.Completed || _points.Count == 0) return;
            int count = 0;
            for (int i = 0; i < _points.Count; i++)
            {
                if (!_hit[i] && s.isDrifting && Vec2.Distance(s.position, _points[i]) <= _radius)
                {
                    _hit[i] = true;
                    State = DrillState.Running;
                }
                if (_hit[i]) count++;
            }
            BestProximity = MathUtil.Max(BestProximity, s.isDrifting ? Proximity(s.position) : 0f);
            if (State == DrillState.Running) ElapsedSeconds += s.dt;
            Progress = (float)count / _points.Count;
            if (count == _points.Count) Complete();
        }
    }
}
