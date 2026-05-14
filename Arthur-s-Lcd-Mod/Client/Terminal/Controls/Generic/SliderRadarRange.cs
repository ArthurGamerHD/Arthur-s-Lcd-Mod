using System;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using VRageMath;
using ScreenConfigRadar = LcdMod.Common.Config.Models.Apps.ScreenConfigRadar;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class SliderRadarRange : TerminalControlsWrapper
    {
        public const float MinScale = 0.1f;
        public const float MaxScale = 10f;
        public const float DefaultScale = 1f;
        public const float BaseRangeMeters = 3000f;

        public override IMyTerminalControl TerminalControl { get; }

        public SliderRadarRange()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("LcdMod_SliderRadarRange");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(MinScale, MaxScale);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("LcdMod_Magnification");

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
                return DefaultScale;

            return ClampRangeScale(config.RangeScale);
        }

        public static float ClampRangeScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale))
                return DefaultScale;

            return MathHelper.Clamp(scale <= 0f ? DefaultScale : scale, MinScale, MaxScale);
        }

        public static float GetRangeMeters(float scale)
        {
            float magnification = ClampRangeScale(scale);
            return BaseRangeMeters / magnification;
        }

        public static float ApplyScrollStep(float scale, int delta)
        {
            float step = delta > 0 ? 1.1f : 1f / 1.1f;
            return ClampRangeScale(scale * step);
        }
    }
}
