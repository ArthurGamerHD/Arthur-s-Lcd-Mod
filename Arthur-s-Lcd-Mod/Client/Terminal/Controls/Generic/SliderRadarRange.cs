using System.Text;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using ScreenConfigRadar = LcdMod.Common.Config.Models.Apps.ScreenConfigRadar;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class SliderRadarRange : TerminalControlsWrapper
    {
        public const float MIN_SCALE = 0.1f;
        public const float MAX_SCALE = 10f;
        public const float DEFAULT_SCALE = 1f;
        public const float BASE_RANGE_METERS = 3000f;

        public override IMyTerminalControl TerminalControl { get; }

        public SliderRadarRange()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("SliderRadarRange");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(MIN_SCALE, MAX_SCALE);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute(MOD_PREFIX + "Magnification");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock block, StringBuilder text)
        {
            float scale = Getter(block);
            text.Append(scale.ToString("0.#"));
            text.Append("x (");
            text.Append(FormatingHelper.DistanceToString(GetRangeMeters(scale)));
            text.Append(')');
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigRadar;
            if (config == null)
                return;

            config.RangeScale = ClampRangeScale(value);
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigRadar;
            if (config == null)
                return DEFAULT_SCALE;

            return ClampRangeScale(config.RangeScale);
        }

        public static float ClampRangeScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale))
                return DEFAULT_SCALE;

            return MathHelper.Clamp(scale <= 0f ? DEFAULT_SCALE : scale, MIN_SCALE, MAX_SCALE);
        }

        public static float GetRangeMeters(float scale)
        {
            float magnification = ClampRangeScale(scale);
            return BASE_RANGE_METERS / magnification;
        }

        public static float ApplyScrollStep(float scale, int delta)
        {
            float step = delta > 0 ? 1.1f : 1f / 1.1f;
            return ClampRangeScale(scale * step);
        }
    }
}
