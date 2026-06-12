using System;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class SliderNpcMarketPageSwitchDelay : TerminalControlsWrapper
    {
        public const float MAX_SECONDS = 30f;

        public override IMyTerminalControl TerminalControl { get; }

        public SliderNpcMarketPageSwitchDelay()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("NpcMarketPageSwitchDelaySlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(0f, MAX_SECONDS);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("Page switch delay");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock block, StringBuilder text)
        {
            var seconds = Getter(block);
            if (seconds <= 0f)
            {
                text.Append("Disabled");
                return;
            }

            text.Append(seconds.ToString("0.###")).Append(" s");
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigNpcMarket;
            if (config == null)
                return;

            config.PageSwitchSeconds = ClampSeconds(value);
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigNpcMarket;
            return config != null
                ? ClampSeconds(config.PageSwitchSeconds)
                : ScreenConfigNpcMarket.DEFAULT_PAGE_SWITCH_SECONDS;
        }

        public static float ClampSeconds(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds))
                return ScreenConfigNpcMarket.DEFAULT_PAGE_SWITCH_SECONDS;

            return Math.Max(0f, Math.Min(MAX_SECONDS, seconds));
        }
    }
}
