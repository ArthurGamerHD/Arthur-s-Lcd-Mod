using System;
using System.Text;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Models;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class SliderAutoScrollStep : TerminalControlsWrapper
    {
        public const float MIN_STEP = 0f;
        public const float MAX_STEP = 10f;
        public const float STEP_QUANTUM = 0.1f;
        
        public override IMyTerminalControl TerminalControl { get; }

        public SliderAutoScrollStep()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("AutoScrollStepSlider");

            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(MIN_STEP, MAX_STEP);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute(MOD_PREFIX + "AutoScroll");
            slider.Tooltip = MyStringId.GetOrCompute(MOD_PREFIX + "AutoScroll_Tooltip");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock block, StringBuilder text)
        {
            float step = Getter(block);
            if (step <= 0f)
            {
                text.Append(LocHelper.Disabled);
                return;
            }

            text.Append(step.ToString("0.##")).Append(" s");
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            config.AutoScrollStep = Normalize(value);
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            ScreenConfigInteractive config = ConfigManager.GetConfigForCurrentScreen(block);
            return config.AutoScrollStep;
        }
        
        public static float Normalize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;

            value = Math.Max(MIN_STEP, Math.Min(MAX_STEP, value));
            value = (float)Math.Round(value / STEP_QUANTUM) * STEP_QUANTUM;
            return Math.Max(MIN_STEP, Math.Min(MAX_STEP, value));
        }
    }
}
