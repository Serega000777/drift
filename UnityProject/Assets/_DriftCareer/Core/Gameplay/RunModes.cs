using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Gameplay
{
    public enum RunKind { FreeDrive, Training, ScoreAttack, Gymkhana, ClippingAttack, GhostTandem, TournamentRound }

    public enum RunPhase { Idle, Countdown, Running, Finished }

    public enum Medal { None = 0, Bronze = 1, Silver = 2, Gold = 3 }

    [Serializable]
    public sealed class RunRules
    {
        public RunKind kind;
        public float timeLimitSec;
        public float countdownSec = 3f;
        public string layoutPreset;
        public float bronze, silver, gold;
        public int rewardBronze = 400, rewardSilver = 900, rewardGold = 1800;

        public Medal MedalFor(float score) => score >= gold ? Medal.Gold : score >= silver ? Medal.Silver : score >= bronze ? Medal.Bronze : Medal.None;

        public int RewardFor(Medal m) => m == Medal.Gold ? rewardGold : m == Medal.Silver ? rewardSilver : m == Medal.Bronze ? rewardBronze : 0;

        public static RunRules For(RunKind kind)
        {
            switch (kind)
            {
                case RunKind.ScoreAttack: return new RunRules { kind = kind, timeLimitSec = 90f, bronze = 15000, silver = 35000, gold = 60000 };
                case RunKind.Gymkhana: return new RunRules { kind = kind, timeLimitSec = 75f, layoutPreset = "gymkhana", bronze = 10000, silver = 25000, gold = 45000 };
                case RunKind.ClippingAttack: return new RunRules { kind = kind, timeLimitSec = 60f, layoutPreset = "clipping", bronze = 8000, silver = 20000, gold = 38000 };
                case RunKind.GhostTandem: return new RunRules { kind = kind, timeLimitSec = 60f, bronze = 40, silver = 65, gold = 85 };
                default: return new RunRules { kind = kind, timeLimitSec = 0f, countdownSec = 0f };
            }
        }
    }

    /// <summary>Timer/phase logic for timed runs. Pure; HUD and session read it.</summary>
    public sealed class RunClock
    {
        public RunRules Rules { get; private set; }
        public RunPhase Phase { get; private set; } = RunPhase.Idle;
        public float Time { get; private set; }
        public float Remaining => Rules.timeLimitSec > 0f ? MathUtil.Max(0f, Rules.timeLimitSec - Time) : 0f;
        public float CountdownLeft { get; private set; }
        public event Action Started;
        public event Action Finished;

        public void Begin(RunRules rules)
        {
            Rules = rules;
            Time = 0f;
            CountdownLeft = rules.countdownSec;
            Phase = rules.countdownSec > 0f ? RunPhase.Countdown : RunPhase.Running;
            if (Phase == RunPhase.Running) Started?.Invoke();
        }

        public void Update(float dt)
        {
            if (Rules == null) return;
            if (Phase == RunPhase.Countdown)
            {
                CountdownLeft -= dt;
                if (CountdownLeft <= 0f) { Phase = RunPhase.Running; Started?.Invoke(); }
                return;
            }
            if (Phase != RunPhase.Running) return;
            Time += dt;
            if (Rules.timeLimitSec > 0f && Time >= Rules.timeLimitSec) Finish();
        }

        public void Finish()
        {
            if (Phase == RunPhase.Finished) return;
            Phase = RunPhase.Finished;
            Finished?.Invoke();
        }
    }

    [Serializable]
    public sealed class TournamentDefinition
    {
        public string id;
        public string titleKey;
        public List<RunRules> rounds = new List<RunRules>();
        public int entryFee;
        public int prizeFirst = 6000;
        public bool online;
    }

    /// <summary>Offline tournament: sum of round scores, total medal from the summed thresholds.</summary>
    public sealed class TournamentRun
    {
        public TournamentDefinition Definition { get; }
        public int RoundIndex { get; private set; }
        public readonly List<float> Scores = new List<float>();
        public bool Done => RoundIndex >= Definition.rounds.Count;
        public RunRules CurrentRound => Done ? null : Definition.rounds[RoundIndex];

        public TournamentRun(TournamentDefinition d) { Definition = d; }

        public void SubmitRound(float score)
        {
            if (Done) return;
            Scores.Add(MathUtil.Max(0f, score));
            RoundIndex++;
        }

        public float Total
        {
            get { float s = 0f; foreach (var x in Scores) s += x; return s; }
        }

        public Medal FinalMedal()
        {
            float b = 0, si = 0, g = 0;
            foreach (var r in Definition.rounds) { b += r.bronze; si += r.silver; g += r.gold; }
            float t = Total;
            return t >= g ? Medal.Gold : t >= si ? Medal.Silver : t >= b ? Medal.Bronze : Medal.None;
        }

        public int Prize()
        {
            var m = FinalMedal();
            return m == Medal.Gold ? Definition.prizeFirst : m == Medal.Silver ? Definition.prizeFirst / 2 : m == Medal.Bronze ? Definition.prizeFirst / 5 : 0;
        }

        public static TournamentDefinition AcademyCup() => new TournamentDefinition
        {
            id = "academy_cup", titleKey = "tournament.academy_cup",
            rounds = { RunRules.For(RunKind.ScoreAttack), RunRules.For(RunKind.Gymkhana), RunRules.For(RunKind.ClippingAttack) },
            entryFee = 500, prizeFirst = 8000
        };
    }
}
