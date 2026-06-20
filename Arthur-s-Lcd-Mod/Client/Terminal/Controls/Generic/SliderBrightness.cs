using System;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Client.ScreenAreas;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class SliderBrightness : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderBrightness()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("SliderBrightness");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(byte.MinValue, byte.MaxValue);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("Brightness");

            TerminalControl = slider;
        }

        public override bool Visible(IMyTerminalBlock block)
        {
            return base.Visible(block) && IsBrightnessSupported(block);
        }

        void Writer(IMyTerminalBlock block, StringBuilder builder)
        {
            builder.Append((Getter(block) / byte.MaxValue).ToString("P"));
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            if (!IsBrightnessSupported(block))
                return;

            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            value = Mirror((byte)value);
            var @default = GetThisSurface(block).BackgroundAlpha;

            if (((byte)value).Equals(@default))
                config.BackgroundAlpha.Clear();
            else
                config.BackgroundAlpha.Set((byte)value);

            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config != null && config.BackgroundAlpha.HasValue)
                return Mirror(config.BackgroundAlpha.Value);

            return Mirror(GetThisSurface(block).BackgroundAlpha);
        }

        float Mirror(byte x) => (byte)~x;

        bool IsBrightnessSupported(IMyTerminalBlock block)
        {
            return !ScreenAreaGeometry.IsTransparentScreenArea(block, GetThisSurfaceIndex(block));
        }
    }
}
