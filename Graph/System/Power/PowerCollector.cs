using Graph.System;
using Graph.System.Config;
using System;
using System.Collections.Generic;
using Graph.Helpers;
using VRageMath;

namespace Graph.System.Power
{
    internal abstract class PowerCollector
    {
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

        protected static string FormatStoredPowerText(double storedPowerMegawattHours)
        {
            return FormatingHelper.WattsToString(storedPowerMegawattHours * 1000000.0);
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
