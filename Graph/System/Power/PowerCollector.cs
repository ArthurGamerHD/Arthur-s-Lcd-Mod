using Graph.System;
using Graph.System.Config;
using Sandbox.ModAPI;
using System;
using System.Collections.Generic;
using VRageMath;

namespace Graph.System.Power
{
    internal abstract class PowerCollector
    {
        const float CENTER_ICON_MAX_SPIN_RPS = 0.35f;
        const float CENTER_ICON_VELOCITY_EASE_SECONDS = 0.4f;

        readonly Dictionary<long, CenterSpinState> _centerSpinByEntryId = new Dictionary<long, CenterSpinState>();
        readonly HashSet<long> _activeSpinEntryIds = new HashSet<long>();
        readonly List<long> _spinEntriesToRemove = new List<long>();

        float _spinDeltaSeconds = 1f / 6f;
        long _lastSpinFrameCounter = -1;

        struct CenterSpinState
        {
            public float Angle;
            public float Velocity;
        }

        protected readonly ScreenConfig ScreenConfig;

        protected PowerCollector(ScreenConfig screenConfig)
        {
            ScreenConfig = screenConfig;
        }

        public abstract void Collect(GridLogic grid, List<PowerEntry> entries);

        protected bool HideEmpty => ScreenConfig == null || ScreenConfig.HideEmpty;

        public abstract string FooterPrefix { get; }
        public abstract FillableTexture FillableTexture { get; }
        public abstract string StatusText { get; }
        public abstract Color StatusColor { get; }
        public abstract PowerStatusKind StatusKind { get; }
        public abstract float AverageCharge { get; }
        public abstract bool HasVisibleItems { get; }
        public virtual string ChargingLabel { get; set; } = "Untranslated_Charging";
        public virtual string DischargingLabel { get; set; } = "Untranslated_Discharging";
        public virtual string ReadyLabel { get; set; } = "Untranslated_Ready";
        public virtual string NotReadyLabel { get; set; } = "Untranslated_Not_Ready";
        public virtual string FullLabel { get; set; } = "Untranslated_Full";
        public virtual string RightSideText => string.Empty;
        public virtual Color RightSideColor => StatusColor;
        public virtual bool HasRightSideText => !string.IsNullOrEmpty(RightSideText);

        protected void BeginCenterIconSpinFrame()
        {
            UpdateSpinTiming();
            _activeSpinEntryIds.Clear();
        }

        protected float GetCenterIconRotation(long entryId, bool spinCenterIcon, float ratio, float spinRatioOverride = -1f)
        {
            if (entryId == 0)
                return 0f;

            _activeSpinEntryIds.Add(entryId);

            float effectiveRatio = spinRatioOverride >= 0f
                ? MathHelper.Clamp(spinRatioOverride, 0f, 1f)
                : MathHelper.Clamp(ratio, 0f, 1f);
            float targetVelocity = spinCenterIcon ? effectiveRatio * CENTER_ICON_MAX_SPIN_RPS * MathHelper.TwoPi : 0f;

            CenterSpinState spinState;
            if (!_centerSpinByEntryId.TryGetValue(entryId, out spinState))
                spinState = new CenterSpinState();

            float alpha = 1f - (float)Math.Exp(-Math.Max(0f, _spinDeltaSeconds) / CENTER_ICON_VELOCITY_EASE_SECONDS);
            spinState.Velocity += (targetVelocity - spinState.Velocity) * alpha;
            spinState.Angle += spinState.Velocity * _spinDeltaSeconds;

            if (spinState.Angle >= MathHelper.TwoPi || spinState.Angle <= -MathHelper.TwoPi)
                spinState.Angle %= MathHelper.TwoPi;

            _centerSpinByEntryId[entryId] = spinState;
            return spinState.Angle;
        }

        protected void EndCenterIconSpinFrame()
        {
            _spinEntriesToRemove.Clear();
            foreach (var kv in _centerSpinByEntryId)
            {
                if (!_activeSpinEntryIds.Contains(kv.Key))
                    _spinEntriesToRemove.Add(kv.Key);
            }

            for (int i = 0; i < _spinEntriesToRemove.Count; i++)
                _centerSpinByEntryId.Remove(_spinEntriesToRemove[i]);
        }

        void UpdateSpinTiming()
        {
            var session = MyAPIGateway.Session;
            if (session == null)
            {
                _spinDeltaSeconds = 1f / 6f;
                _lastSpinFrameCounter = -1;
                return;
            }

            long frameCounter = session.GameplayFrameCounter;
            if (_lastSpinFrameCounter < 0 || frameCounter < _lastSpinFrameCounter)
            {
                _spinDeltaSeconds = 1f / 6f;
            }
            else
            {
                long deltaFrames = frameCounter - _lastSpinFrameCounter;
                _spinDeltaSeconds = Math.Max(1f / 120f, Math.Min(0.5f, deltaFrames / 60f));
            }

            _lastSpinFrameCounter = frameCounter;
        }

        protected static string FormatTimeHours(float hours)
        {
            if (hours < 0f) return "--:--";
            if (hours > 99.99f) return ">99h";
            int totalSeconds = Math.Max(0, (int)(hours * 3600f));
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;
            return minutes.ToString("00") + ":" + seconds.ToString("00");
        }
    }
}
