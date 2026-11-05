using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Gameplay
{
    public enum ChallengeKind { RunScore, ChainScore, HoldAngle, Transitions, DrillComplete, DriftDistance, CleanRun }

    [Serializable]
    public sealed class ChallengeDefinition
    {
        public string id;
        public ChallengeKind kind;
        public float target;
        /// <summary>Extra parameter (e.g. angle for HoldAngle, drill id for DrillComplete).</summary>
        public float param;
        public string drillId;
        public int reward;
        public bool daily;
        public string TitleKey => "challenge." + kind;
    }

    [Serializable]
    public sealed class ChallengeProgress
    {
        public string id;
        public float value;
        public bool completed;
        public bool claimed;
        public string dayKey;
    }

    /// <summary>Events a run reports to the challenge tracker (pure data, from DriftSession).</summary>
    public struct RunStats
    {
        public float dt;
        public float runScore;
        public float bankedChain;
        public float driftAngleDeg;
        public bool drifting;
        public int transitionsDelta;
        public float speedMs;
        public bool collision;
        public string drillCompleted;
    }

    /// <summary>
    /// Tasks that pay credits. Daily set is chosen deterministically from the UTC date (same for everyone, no server needed);
    /// permanent challenges never rotate. Progress persists in the profile.
    /// </summary>
    public sealed class ChallengeTracker
    {
        public static readonly IReadOnlyList<ChallengeDefinition> Permanent = new List<ChallengeDefinition>
        {
            new ChallengeDefinition { id = "p.first_chain", kind = ChallengeKind.ChainScore, target = 1000, reward = 500 },
            new ChallengeDefinition { id = "p.big_chain", kind = ChallengeKind.ChainScore, target = 15000, reward = 3000 },
            new ChallengeDefinition { id = "p.angle_60", kind = ChallengeKind.HoldAngle, target = 3, param = 60, reward = 1500 },
            new ChallengeDefinition { id = "p.transitions_10", kind = ChallengeKind.Transitions, target = 10, reward = 1200 },
            new ChallengeDefinition { id = "p.donut", kind = ChallengeKind.DrillComplete, target = 1, drillId = "drill.donut", reward = 1000 },
            new ChallengeDefinition { id = "p.figure8", kind = ChallengeKind.DrillComplete, target = 1, drillId = "drill.figure8", reward = 2000 },
            new ChallengeDefinition { id = "p.slalom", kind = ChallengeKind.DrillComplete, target = 1, drillId = "drill.slalom", reward = 1500 },
            new ChallengeDefinition { id = "p.distance_5k", kind = ChallengeKind.DriftDistance, target = 5000, reward = 4000 },
        };

        private static readonly ChallengeDefinition[] DailyPool =
        {
            new ChallengeDefinition { id = "d.run_20k", kind = ChallengeKind.RunScore, target = 20000, reward = 800, daily = true },
            new ChallengeDefinition { id = "d.run_50k", kind = ChallengeKind.RunScore, target = 50000, reward = 1800, daily = true },
            new ChallengeDefinition { id = "d.chain_8k", kind = ChallengeKind.ChainScore, target = 8000, reward = 1000, daily = true },
            new ChallengeDefinition { id = "d.angle_45", kind = ChallengeKind.HoldAngle, target = 5, param = 45, reward = 700, daily = true },
            new ChallengeDefinition { id = "d.transitions_6", kind = ChallengeKind.Transitions, target = 6, reward = 900, daily = true },
            new ChallengeDefinition { id = "d.distance_1k", kind = ChallengeKind.DriftDistance, target = 1000, reward = 900, daily = true },
            new ChallengeDefinition { id = "d.clean_60", kind = ChallengeKind.CleanRun, target = 60, reward = 1100, daily = true },
        };

        private readonly Dictionary<string, ChallengeProgress> _progress;
        private readonly List<ChallengeDefinition> _active = new List<ChallengeDefinition>();
        private float _holdTimer, _cleanTimer;
        public string DayKey { get; private set; }

        public event Action<ChallengeDefinition> Completed;

        public IReadOnlyList<ChallengeDefinition> Active => _active;

        public ChallengeTracker(Dictionary<string, ChallengeProgress> progressStore, DateTime utcNow)
        {
            _progress = progressStore;
            SetDay(utcNow);
        }

        public static List<ChallengeDefinition> DailyFor(DateTime utcDate, int count = 3)
        {
            int seed = utcDate.Year * 10000 + utcDate.Month * 100 + utcDate.Day;
            var rng = new Random(seed);
            var pool = new List<ChallengeDefinition>(DailyPool);
            var result = new List<ChallengeDefinition>();
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                int k = rng.Next(pool.Count);
                result.Add(pool[k]);
                pool.RemoveAt(k);
            }
            return result;
        }

        public void SetDay(DateTime utcNow)
        {
            DayKey = utcNow.ToString("yyyy-MM-dd");
            _active.Clear();
            _active.AddRange(Permanent);
            _active.AddRange(DailyFor(utcNow.Date));
            foreach (var d in _active)
            {
                var p = Progress(d);
                if (d.daily && p.dayKey != DayKey) { p.value = 0f; p.completed = false; p.claimed = false; p.dayKey = DayKey; }
            }
        }

        public ChallengeProgress Progress(ChallengeDefinition d)
        {
            if (!_progress.TryGetValue(d.id, out var p)) _progress[d.id] = p = new ChallengeProgress { id = d.id, dayKey = DayKey };
            return p;
        }

        public void Report(in RunStats s)
        {
            _cleanTimer = s.collision ? 0f : _cleanTimer + s.dt;
            foreach (var d in _active)
            {
                var p = Progress(d);
                if (p.completed) continue;
                switch (d.kind)
                {
                    case ChallengeKind.RunScore: p.value = MathUtil.Max(p.value, s.runScore); break;
                    case ChallengeKind.ChainScore: p.value = MathUtil.Max(p.value, s.bankedChain); break;
                    case ChallengeKind.Transitions: p.value += s.transitionsDelta; break;
                    case ChallengeKind.DriftDistance: if (s.drifting) p.value += s.speedMs * s.dt; break;
                    case ChallengeKind.CleanRun: p.value = MathUtil.Max(p.value, _cleanTimer); break;
                    case ChallengeKind.DrillComplete: if (s.drillCompleted == d.drillId) p.value = 1f; break;
                    case ChallengeKind.HoldAngle:
                        p.value = MathUtil.Max(p.value, s.drifting && MathUtil.Abs(s.driftAngleDeg) >= d.param ? _holdTimer + s.dt : 0f);
                        break;
                }
                if (p.value >= d.target) { p.completed = true; Completed?.Invoke(d); }
            }
            bool holding = false;
            foreach (var d in _active) if (d.kind == ChallengeKind.HoldAngle && s.drifting && MathUtil.Abs(s.driftAngleDeg) >= d.param) holding = true;
            _holdTimer = holding ? _holdTimer + s.dt : 0f;
        }

        /// <summary>Pays the reward once. Returns credits granted.</summary>
        public int Claim(ChallengeDefinition d, Wallet wallet)
        {
            var p = Progress(d);
            if (!p.completed || p.claimed) return 0;
            p.claimed = true;
            wallet.Earn(d.reward);
            return d.reward;
        }
    }
}
