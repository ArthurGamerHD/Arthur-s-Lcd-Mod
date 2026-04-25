using System;
using System.Text;
using Graph.System.Config;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using VRageMath;

namespace Graph.System.TerminalControls.Generic
{
    /// <summary>
    /// Slider that tilts the ore-scanner cone direction.
    /// Range [-100, 100], default 0 = straight up from the ore detector
    /// (its WorldMatrix.Up). Positive values tilt the cone forward (toward
    /// the detector's Forward axis), negative values tilt it backward.
    /// At ±100 the cone points fully forward / backward (90° pitch).
    /// </summary>
    public sealed partial class SliderOreScannerConeAngle : TerminalControlsWrapper
    {
        public const float MIN_BIAS = -100f;
        public const float MAX_BIAS = 100f;
        public const float DEFAULT_BIAS = 0f;
        public const float MAX_TILT_DEG = 90f; // value at ±100

        public override IMyTerminalControl TerminalControl { get; }

        public SliderOreScannerConeAngle()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("OreScannerConeAngleSlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(MIN_BIAS, MAX_BIAS);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("LCDMod_OreScanner_ConeAngle");
            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder sb)
        {
            int bias = (int)Math.Round(Getter(b));
            if (bias > 0) sb.Append('+');
            sb.Append(bias);
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var cfg = ConfigManager.GetConfigForCurrentScreen(block);
            if (cfg == null) return;
            cfg.OreScannerConeBias = MathHelper.Clamp((float)Math.Round(value), MIN_BIAS, MAX_BIAS);
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var cfg = ConfigManager.GetConfigForCurrentScreen(block);
            return cfg != null ? cfg.OreScannerConeBias : DEFAULT_BIAS;
        }

        /// <summary>
        /// Converts the slider value [-100, 100] into a pitch in degrees
        /// from the detector's Up axis toward (positive) or away from
        /// (negative) the detector's Forward axis.
        /// </summary>
        public static float BiasToTiltDeg(float bias)
        {
            return MathHelper.Clamp(bias, MIN_BIAS, MAX_BIAS) / MAX_BIAS * MAX_TILT_DEG;
        }
    }
}
