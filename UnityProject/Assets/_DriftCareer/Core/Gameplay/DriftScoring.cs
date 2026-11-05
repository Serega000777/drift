using System;

namespace DriftCareer.Core.Gameplay
{
    public enum ChainEndReason { None, Completed, Collision, Spin, OffTrack, TooSlow, Reset }

    [Serializable]
    public sealed class DriftScoringConfig
    {
        public float minSpeedMs = 5f;
        public float minAngleDeg = 12f;
        /// <summary>Beyond this angle the car is considered spinning out (chain lost).</summary>
        public float spinAngleDeg = 115f;
        /// <summary>Time the car may be below thresholds before the chain is banked.</summary>
        public float graceSeconds = 0.9f;
        public float basePointsPerSecond = 100f;
        /// <summary>Angle contribution: points scale with (angle/referenceAngle)^angleExponent.</summary>
        public float referenceAngleDeg = 45f;
        public float angleExponent = 1.2f;
        public float referenceSpeedMs = 20f;
        public float speedExponent = 1.1f;
        /// <summary>Seconds of continuous drifting per +1 combo step.</summary>
        public float comboStepSeconds = 2.5f;
        public float comboIncrement = 0.5f;
        public float maxMultiplier = 6f;
        /// <summary>Bonus points for a direction change (перекладка) while the chain is alive.</summary>
        public float transitionBonus = 250f;
        /// <summary>A transition counts only if the previous direction was held at least this long.</summary>
        public float transitionMinHoldSeconds = 0.6f;
        /// <summary>Collision impulse (N*s) that breaks a chain.</summary>
        public float collisionImpulseFail = 3500f;
        /// <summary>Clipping proximity bonus multiplier at distance 0 (fades to 0 at clip radius).</summary>
        public float clippingBonusMultiplier = 1.5f;
    }

    public struct DriftSample
    {
        public float dt;
        public float speedMs;
        public float driftAngleDeg;
        public bool onTrack;
        /// <summary>Largest collision impulse since last sample (N*s), 0 if none.</summary>
        public float collisionImpulse;
        /// <summary>0..1 proximity to an active clipping point (1 = touching).</summary>
        public float clippingProximity;
    }

    /// <summary>
    /// Transparent drift scoring: points/sec = base * angleFactor * speedFactor * (1 + clipping) * combo.
    /// A chain ends after the grace period (banked) or by collision/spin/off-track (lost).
    /// Not an imitation of any official judging system.
    /// </summary>
    public sealed class DriftScoring
    {
        private readonly DriftScoringConfig _c;
        private float _graceTimer;
        private float _driftTime;
        private float _directionHold;
        private int _direction;

        public DriftScoring(DriftScoringConfig config = null) { _c = config ?? new DriftScoringConfig(); }

        public DriftScoringConfig Config => _c;
        public bool ChainActive { get; private set; }
        public bool IsDrifting { get; private set; }
        public float ChainPoints { get; private set; }
        public float Multiplier { get; private set; } = 1f;
        public int Transitions { get; private set; }
        public float TotalPoints { get; private set; }
        public float BestChain { get; private set; }
        public float PointsPerSecond { get; private set; }
        public float LongestDriftSeconds { get; private set; }

        public event Action ChainStarted;
        public event Action<float, ChainEndReason> ChainEnded;
        public event Action<int> TransitionScored;
        public event Action<float> MultiplierChanged;

        public void Reset()
        {
            ChainActive = false; IsDrifting = false; ChainPoints = 0f; Multiplier = 1f; Transitions = 0;
            TotalPoints = 0f; BestChain = 0f; PointsPerSecond = 0f; LongestDriftSeconds = 0f;
            _graceTimer = 0f; _driftTime = 0f; _directionHold = 0f; _direction = 0;
        }

        public void Update(in DriftSample s)
        {
            if (!(s.dt > 0f)) return;
            float angle = MathUtil.Abs(MathUtil.Finite(s.driftAngleDeg));
            float speed = MathUtil.Finite(s.speedMs);

            if (ChainActive)
            {
                if (s.collisionImpulse >= _c.collisionImpulseFail) { EndChain(ChainEndReason.Collision); return; }
                if (!s.onTrack) { EndChain(ChainEndReason.OffTrack); return; }
                if (angle > _c.spinAngleDeg && speed > 2f) { EndChain(ChainEndReason.Spin); return; }
            }

            IsDrifting = s.onTrack && speed >= _c.minSpeedMs && angle >= _c.minAngleDeg && angle <= _c.spinAngleDeg;
            if (!IsDrifting)
            {
                PointsPerSecond = 0f;
                if (ChainActive)
                {
                    _graceTimer += s.dt;
                    if (_graceTimer >= _c.graceSeconds) EndChain(ChainEndReason.Completed);
                }
                return;
            }

            if (!ChainActive)
            {
                ChainActive = true;
                ChainPoints = 0f;
                Multiplier = 1f;
                Transitions = 0;
                _driftTime = 0f;
                _direction = 0;
                _directionHold = 0f;
                ChainStarted?.Invoke();
            }
            _graceTimer = 0f;
            _driftTime += s.dt;
            if (_driftTime > LongestDriftSeconds) LongestDriftSeconds = _driftTime;

            // Transition (перекладка): sign flip of the drift angle within a live chain.
            int dir = Math.Sign(s.driftAngleDeg);
            if (dir != 0)
            {
                if (_direction != 0 && dir != _direction && _directionHold >= _c.transitionMinHoldSeconds)
                {
                    Transitions++;
                    ChainPoints += _c.transitionBonus * Multiplier;
                    TransitionScored?.Invoke(Transitions);
                    _directionHold = 0f;
                }
                else if (dir == _direction) _directionHold += s.dt;
                else _directionHold = 0f;
                _direction = dir;
            }

            float newMult = MathUtil.Min(_c.maxMultiplier, 1f + _c.comboIncrement * (float)Math.Floor(_driftTime / _c.comboStepSeconds) + 0.25f * Transitions);
            if (newMult != Multiplier) { Multiplier = newMult; MultiplierChanged?.Invoke(newMult); }

            float angleFactor = (float)Math.Pow(angle / _c.referenceAngleDeg, _c.angleExponent);
            float speedFactor = (float)Math.Pow(speed / _c.referenceSpeedMs, _c.speedExponent);
            float clip = 1f + _c.clippingBonusMultiplier * MathUtil.Clamp01(s.clippingProximity);
            PointsPerSecond = _c.basePointsPerSecond * angleFactor * speedFactor * clip * Multiplier;
            ChainPoints += PointsPerSecond * s.dt;
        }

        /// <summary>Forces the current chain to end (e.g. car reset).</summary>
        public void EndChain(ChainEndReason reason)
        {
            if (!ChainActive) return;
            float banked = reason == ChainEndReason.Completed ? ChainPoints : 0f;
            TotalPoints += banked;
            if (banked > BestChain) BestChain = banked;
            ChainActive = false;
            IsDrifting = false;
            _graceTimer = 0f;
            PointsPerSecond = 0f;
            ChainEnded?.Invoke(banked, reason);
            ChainPoints = 0f;
            Multiplier = 1f;
        }
    }
}
