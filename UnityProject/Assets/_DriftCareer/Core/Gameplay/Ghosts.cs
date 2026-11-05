using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Gameplay
{
    [Serializable]
    public struct GhostFrame
    {
        public float t;
        public float x, y, z;
        public float yawDeg;
        public float driftAngleDeg;
        public float speedMs;
        public float steer;
    }

    /// <summary>
    /// Recorded run (20 Hz) for ghost tandem now and replays / server-side validation later.
    /// Serialized as compact JSON arrays; versioned like every other save.
    /// </summary>
    [Serializable]
    public sealed class GhostRecording
    {
        public const int CurrentVersion = 1;
        public const float SampleInterval = 0.05f;
        public int version = CurrentVersion;
        public string trackId = "academy";
        public string vehicleId = "placeholder_rwd";
        public string playerName = "player";
        public float score;
        public List<GhostFrame> frames = new List<GhostFrame>();

        public float Duration => frames.Count > 0 ? frames[frames.Count - 1].t : 0f;

        /// <summary>Interpolated pose at time t (clamped).</summary>
        public GhostFrame Sample(float t)
        {
            int n = frames.Count;
            if (n == 0) return default;
            if (t <= frames[0].t) return frames[0];
            if (t >= frames[n - 1].t) return frames[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (frames[mid].t <= t) lo = mid; else hi = mid;
            }
            var a = frames[lo]; var b = frames[hi];
            float k = (t - a.t) / MathUtil.Max(1e-5f, b.t - a.t);
            return new GhostFrame
            {
                t = t,
                x = MathUtil.LerpUnclamped(a.x, b.x, k), y = MathUtil.LerpUnclamped(a.y, b.y, k), z = MathUtil.LerpUnclamped(a.z, b.z, k),
                yawDeg = a.yawDeg + MathUtil.WrapAngleDeg(b.yawDeg - a.yawDeg) * k,
                driftAngleDeg = MathUtil.LerpUnclamped(a.driftAngleDeg, b.driftAngleDeg, k),
                speedMs = MathUtil.LerpUnclamped(a.speedMs, b.speedMs, k),
                steer = MathUtil.LerpUnclamped(a.steer, b.steer, k)
            };
        }

        public JsonValue ToJson()
        {
            var arr = JsonValue.NewArray();
            foreach (var f in frames)
            {
                var a = JsonValue.NewArray();
                a.Add(JsonValue.Number(f.t)).Add(JsonValue.Number(f.x)).Add(JsonValue.Number(f.y)).Add(JsonValue.Number(f.z))
                 .Add(JsonValue.Number(f.yawDeg)).Add(JsonValue.Number(f.driftAngleDeg)).Add(JsonValue.Number(f.speedMs)).Add(JsonValue.Number(f.steer));
                arr.Add(a);
            }
            return JsonValue.NewObject().Set("version", version).Set("track", trackId).Set("vehicle", vehicleId).Set("player", playerName)
                .Set("score", score).Set("frames", arr);
        }

        public static GhostRecording FromJson(JsonValue j, int maxFrames = 20 * 60 * 10)
        {
            var g = new GhostRecording
            {
                trackId = j.GetString("track", "academy"), vehicleId = j.GetString("vehicle", "placeholder_rwd"),
                playerName = j.GetString("player", "player"), score = j.GetFloat("score")
            };
            var arr = j.GetArray("frames");
            if (arr == null) return g;
            float lastT = -1f;
            foreach (var a in arr.Items)
            {
                if (g.frames.Count >= maxFrames) break;
                if (!a.IsArray || a.Count < 8) continue;
                bool ok = true;
                var vals = new float[8];
                for (int i = 0; i < 8; i++)
                {
                    if (a[i].Type != JsonType.Number) { ok = false; break; }
                    vals[i] = a[i].AsFloat;
                    if (!MathUtil.IsFinite(vals[i])) ok = false;
                }
                if (!ok || vals[0] <= lastT) continue;
                lastT = vals[0];
                g.frames.Add(new GhostFrame { t = vals[0], x = vals[1], y = vals[2], z = vals[3], yawDeg = vals[4], driftAngleDeg = vals[5], speedMs = vals[6], steer = vals[7] });
            }
            return g;
        }
    }

    public sealed class GhostRecorder
    {
        private float _time, _next;
        public GhostRecording Recording { get; private set; } = new GhostRecording();
        public bool IsRecording { get; private set; }

        public void Start(string trackId, string vehicleId, string player)
        {
            Recording = new GhostRecording { trackId = trackId, vehicleId = vehicleId, playerName = player };
            _time = 0f; _next = 0f; IsRecording = true;
        }

        public void Stop(float score) { Recording.score = score; IsRecording = false; }

        public void Update(float dt, float x, float y, float z, float yawDeg, float drift, float speed, float steer)
        {
            if (!IsRecording) return;
            _time += dt;
            if (_time < _next) return;
            _next = _time + GhostRecording.SampleInterval;
            if (Recording.frames.Count < 20 * 60 * 10)
                Recording.frames.Add(new GhostFrame { t = _time, x = x, y = y, z = z, yawDeg = yawDeg, driftAngleDeg = drift, speedMs = speed, steer = steer });
        }
    }

    /// <summary>
    /// Chase-car tandem scoring vs a lead (ghost now, network player later): proximity in a target window,
    /// drift-angle matching and being in a drift at all. 0..100 per run (average of per-second scores).
    /// </summary>
    public sealed class TandemScoring
    {
        public float idealGapM = 3.5f;
        public float maxGapM = 15f;
        private float _sum, _time;

        public float Score => _time > 0f ? _sum / _time : 0f;
        public float LastProximity { get; private set; }
        public float LastAngleMatch { get; private set; }

        public void Reset() { _sum = 0f; _time = 0f; }

        public void Update(float dt, Vec2 chasePos, float chaseDrift, bool chaseDrifting, Vec2 leadPos, float leadDrift, bool leadDrifting)
        {
            if (!(dt > 0f) || !leadDrifting) return;
            float gap = Vec2.Distance(chasePos, leadPos);
            LastProximity = gap <= idealGapM ? 1f : MathUtil.Clamp01(1f - (gap - idealGapM) / (maxGapM - idealGapM));
            float angleErr = MathUtil.Abs(MathUtil.Abs(chaseDrift) - MathUtil.Abs(leadDrift));
            LastAngleMatch = MathUtil.Clamp01(1f - angleErr / 40f);
            bool sameDirection = Math.Sign(chaseDrift) == Math.Sign(leadDrift);
            float s = chaseDrifting ? (0.55f * LastProximity + 0.35f * LastAngleMatch + (sameDirection ? 0.1f : 0f)) : 0f;
            _sum += s * 100f * dt;
            _time += dt;
        }
    }

    /// <summary>Future real-time multiplayer transport (Netcode/Relay or a custom VPS server). Not implemented in MVP.</summary>
    public interface ITandemTransport
    {
        bool Connected { get; }
        void Connect(string lobbyCode, Action<bool, string> done);
        void SendState(in GhostFrame localState);
        bool TryReceiveRemote(out GhostFrame remoteState);
        void Disconnect();
    }
}
