using System;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Modules.Defense
{
    public sealed partial class ShieldInfo : ObservableObject
    {
        const long CHARGE_GHOST_STABLE_FRAMES = 100L;
        const float CHARGE_RATIO_EPSILON = 0.0001f;
        const long GAMEPLAY_FRAMES_PER_SECOND = 60L;

        [ObservableProperty] string _providerName;
        [ObservableProperty] long _representativeEntityId;
        [ObservableProperty] string _representativeName;
        [ObservableProperty] string _valueUnit;
        [ObservableProperty] bool _useSiPrefixes;
        [ObservableProperty] float _currentPoints;
        [ObservableProperty] float _maximumPoints;
        [ObservableProperty] float _rechargePointsPerSecond;
        [ObservableProperty] float _maximumRechargePointsPerSecond;
        [ObservableProperty] float _effectivenessRatio;
        [ObservableProperty] int _ticksUntilRecharge;
        [ObservableProperty] bool _isWorking;
        [ObservableProperty] bool _hasCapacity;
        [ObservableProperty] bool _hasRecharge;
        [ObservableProperty] bool _hasMaximumRecharge;
        [ObservableProperty] bool _hasEffectiveness;
        [ObservableProperty] bool _hasRechargeDelay;
        [ObservableProperty] bool _usesLiveData;
        [ObservableProperty] long _lastLiveDataFrame;
        [ObservableProperty] long _lastCachedDataFrame;
        [ObservableProperty] float _ghostChargeRatio;
        [ObservableProperty] bool _hasGhostCharge;

        float _observedChargeRatio;
        long _lastChargeChangeFrame;
        long _lastChargeObservationFrame = long.MinValue;
        bool _hasChargeObservation;
        float _lastRechargeDelayPoints;
        long _rechargeReadyFrame;
        long _lastRechargeDelayObservationFrame = long.MinValue;
        bool _hasRechargeDelayObservation;
        bool _isRechargeDelayCountingDown;

        public float ChargeRatio
        {
            get
            {
                if (!HasCapacity || MaximumPoints <= 0f)
                    return 0f;

                var ratio = CurrentPoints / MaximumPoints;
                if (ratio < 0f)
                    return 0f;
                return ratio > 1f ? 1f : ratio;
            }
        }

        internal void UpdateChargeGhost(long gameplayFrame)
        {
            float ratio = ChargeRatio;
            if (!HasCapacity || gameplayFrame < 0L)
            {
                ResetChargeGhostObservation();
                return;
            }

            bool observationExpired = _lastChargeObservationFrame != long.MinValue &&
                                      gameplayFrame - _lastChargeObservationFrame >
                                      CHARGE_GHOST_STABLE_FRAMES;
            if (!_hasChargeObservation || gameplayFrame < _lastChargeObservationFrame || observationExpired)
            {
                _observedChargeRatio = ratio;
                _lastChargeChangeFrame = gameplayFrame;
                _lastChargeObservationFrame = gameplayFrame;
                _hasChargeObservation = true;
                HasGhostCharge = false;
                return;
            }

            bool changed = Math.Abs(ratio - _observedChargeRatio) > CHARGE_RATIO_EPSILON;
            if (changed)
            {
                if (ratio < _observedChargeRatio - CHARGE_RATIO_EPSILON && !HasGhostCharge)
                {
                    GhostChargeRatio = _observedChargeRatio;
                    HasGhostCharge = true;
                }

                _observedChargeRatio = ratio;
                _lastChargeChangeFrame = gameplayFrame;
            }

            _lastChargeObservationFrame = gameplayFrame;
            if (HasGhostCharge &&
                (ratio >= GhostChargeRatio - CHARGE_RATIO_EPSILON ||
                 gameplayFrame - _lastChargeChangeFrame >= CHARGE_GHOST_STABLE_FRAMES))
                HasGhostCharge = false;
        }

        internal void UpdateRechargeDelayCountdown(
            int durationTicks,
            float currentPoints,
            float maximumPoints,
            float rechargePointsPerSecond,
            long gameplayFrame)
        {
            durationTicks = Math.Max(0, durationTicks);
            float pointEpsilon = Math.Max(0.001f, maximumPoints * 0.000001f);
            bool hasCapacity = maximumPoints > 0f;
            bool isFull = hasCapacity && currentPoints >= maximumPoints - pointEpsilon;
            bool observationInterrupted = _lastRechargeDelayObservationFrame != long.MinValue &&
                                          (gameplayFrame < _lastRechargeDelayObservationFrame ||
                                           gameplayFrame - _lastRechargeDelayObservationFrame > 1L);
            bool pointsDropped = _hasRechargeDelayObservation && !observationInterrupted &&
                                 currentPoints < _lastRechargeDelayPoints - pointEpsilon;
            bool pointsIncreased = _hasRechargeDelayObservation && !observationInterrupted &&
                                   currentPoints > _lastRechargeDelayPoints + pointEpsilon;
            bool isInitialObservation = !_hasRechargeDelayObservation || observationInterrupted;

            if (durationTicks <= 0 || !hasCapacity || isFull || gameplayFrame < 0L)
            {
                _isRechargeDelayCountingDown = false;
            }
            else if (pointsDropped || isInitialObservation && rechargePointsPerSecond <= 0f)
            {
                _rechargeReadyFrame = gameplayFrame + durationTicks;
                _isRechargeDelayCountingDown = true;
            }
            else if (pointsIncreased || isInitialObservation && rechargePointsPerSecond > 0f)
            {
                // NerdShield may report a tiny stale positive rate on the damage frame.
                // Once a countdown starts, require actual HP growth before considering it recharging.
                _isRechargeDelayCountingDown = false;
            }

            _lastRechargeDelayPoints = currentPoints;
            _lastRechargeDelayObservationFrame = gameplayFrame;
            _hasRechargeDelayObservation = gameplayFrame >= 0L;

            long remainingFrames = _isRechargeDelayCountingDown
                ? Math.Max(0L, _rechargeReadyFrame - gameplayFrame)
                : 0L;
            int remainingSeconds = remainingFrames > 0L
                ? (int)Math.Min(int.MaxValue,
                    (remainingFrames + GAMEPLAY_FRAMES_PER_SECOND - 1L) / GAMEPLAY_FRAMES_PER_SECOND)
                : 0;

            if (remainingSeconds == 0)
                _isRechargeDelayCountingDown = false;

            // Quantizing the observable value prevents a redraw every simulation frame.
            TicksUntilRecharge = (int)Math.Min(
                int.MaxValue,
                remainingSeconds * GAMEPLAY_FRAMES_PER_SECOND);
            HasRechargeDelay = remainingSeconds > 0;
        }

        void ResetChargeGhostObservation()
        {
            _hasChargeObservation = false;
            _lastChargeObservationFrame = long.MinValue;
            HasGhostCharge = false;
        }

    }
}
