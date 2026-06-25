using System;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

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
            slider.Title = MyStringId.GetOrCompute(MOD_PREFIX + "ProxyOffsetY");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            arg2.Append(Getter(b).ToString("0"));
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            ConfigManager.ModifyComponentForTerminalApp<RenderProxyConfigComponent>(
                block,
                config => config.YAxisOffset = (sbyte)Math.Round(value));
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetComponentForTerminalApp<RenderProxyConfigComponent>(block);
            if (config == null)
                return 1;

            return config.YAxisOffset;
        }
    }
}
