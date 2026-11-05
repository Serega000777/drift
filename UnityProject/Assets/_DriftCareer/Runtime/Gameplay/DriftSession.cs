using System;
using DriftCareer.Core;
using DriftCareer.Core.Gameplay;
using DriftCareer.Core.Layout;
using DriftCareer.Runtime.LayoutEditor;
using DriftCareer.Runtime.Vehicle;
using UnityEngine;

namespace DriftCareer.Runtime.Gameplay
{
    public enum DrillKind { None, Donut, FigureEight, Slalom, Clipping }

    /// <summary>
    /// Per-run gameplay glue: feeds vehicle telemetry into <see cref="DriftScoring"/> and the active training drill,
    /// tracks on-track state and statistics. No physics here.
    /// </summary>
    public sealed class DriftSession
    {
        private readonly AcademySite _site;
        private VehicleController _car;
        private ObstacleField _obstacles;

        public DriftScoring Scoring { get; } = new DriftScoring();
        public TrainingDrill Drill { get; private set; }
        public DrillKind DrillKind { get; private set; }
        public float DistanceMeters { get; private set; }
        public float SessionSeconds { get; private set; }
        public bool OnTrack { get; private set; } = true;

        public event Action<TrainingDrill> DrillCompleted;

        public DriftSession(AcademySite site) { _site = site; }

        public void Bind(VehicleController car, ObstacleField obstacles)
        {
            _car = car;
            _obstacles = obstacles;
        }

        public void StartDrill(DrillKind kind)
        {
            DrillKind = kind;
            switch (kind)
            {
                case DrillKind.Donut: Drill = new DonutDrill(_site.DonutCenter, _site.DonutRadius + 3f, 3f); break;
                case DrillKind.FigureEight: Drill = new FigureEightDrill(_site.FigureEightA, _site.FigureEightB, _site.FigureEightRadius + 3f, 4); break;
                case DrillKind.Slalom:
                {
                    var cones = new System.Collections.Generic.List<Vec2>();
                    for (int i = 0; i < _site.SlalomCones; i++) cones.Add(_site.SlalomStart + _site.SlalomDirection * (_site.SlalomSpacing * i));
                    Drill = new SlalomDrill(cones, _site.SlalomDirection, 7f);
                    break;
                }
                case DrillKind.Clipping:
                {
                    var pts = new System.Collections.Generic.List<Vec2>();
                    if (_obstacles != null)
                        foreach (var c in _obstacles.ClippingTargets) pts.Add(new Vec2(c.x, c.z));
                    if (pts.Count == 0)
                        foreach (var p in LayoutPresets.Create("clipping", _site))
                            if (p.defId == "clipping_point") pts.Add(p.position);
                    Drill = new ClippingDrill(pts, 2.5f);
                    break;
                }
                default: Drill = null; break;
            }
            if (Drill != null) Drill.Completed += d => DrillCompleted?.Invoke(d);
        }

        public void ResetRun()
        {
            Scoring.EndChain(ChainEndReason.Reset);
            Drill?.Restart();
        }

        public void Update(float dt)
        {
            if (_car == null || dt <= 0f) return;
            var t = _car.Telemetry;
            Vector3 p = _car.transform.position;
            var pos = new Vec2(p.x, p.z);
            OnTrack = _site.LotBounds.Contains(pos) && p.y > -2f;
            SessionSeconds += dt;
            DistanceMeters += t.speedMs * dt;
            float impulse = _car.ConsumeImpactImpulse();
            float clip = _obstacles != null ? _obstacles.ClippingProximity(p) : 0f;
            Scoring.Update(new DriftSample
            {
                dt = dt,
                speedMs = t.speedMs,
                driftAngleDeg = t.driftAngleDeg,
                onTrack = OnTrack,
                collisionImpulse = impulse,
                clippingProximity = clip
            });
            Drill?.Update(new DrillSample { dt = dt, position = pos, speedMs = t.speedMs, driftAngleDeg = t.driftAngleDeg, isDrifting = Scoring.IsDrifting });
        }
    }
}
