using System;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using ScreenConfigGeneral = LcdMod.Common.Config.Models.ScreenConfigGeneral;

namespace LcdMod.Client.Terminal.Controls.Proxy
{
    public sealed partial class SliderProxyY : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderProxyY()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("SliderProxyY");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(-16, 16);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("LcdMod_ProxyOffsetY");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            arg2.Append(Getter(b).ToString("0"));
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigRenderProxy;
            if (config == null)
                return;

            config.YAxisOffset = (sbyte)Math.Round(value);
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigRenderProxy;
            if (config == null)
                return 1;

            return config.YAxisOffset;
        }
    }
}
