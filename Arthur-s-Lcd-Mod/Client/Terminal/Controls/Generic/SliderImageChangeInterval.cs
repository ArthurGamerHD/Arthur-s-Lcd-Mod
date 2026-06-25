using System;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class SliderImageChangeInterval : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderImageChangeInterval()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("ImageChangeIntervalSlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(0f, 30f);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("BlockPropertyTitle_LCDScreenRefreshInterval");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock block, StringBuilder builder)
        {
            builder.Append(Getter(block).ToString("0.000")).Append(" s");
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            ConfigManager.ModifyComponentForTerminalApp<DigitalPictureFramesConfigComponent>(
                block,
                config => config.ImageChangeInterval = Math.Max(0f, value));
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetComponentForTerminalApp<DigitalPictureFramesConfigComponent>(block);
            return config?.ImageChangeInterval ?? 0f;
        }
    }
}
