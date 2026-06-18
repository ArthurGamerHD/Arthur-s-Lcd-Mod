using System.Text;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Scale
{
    public sealed partial class SliderRaysPerTick : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }
        protected override bool RequiresAdvancedTweakables => true;

        public SliderRaysPerTick()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("RaysPerTickSlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(2, 256);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute(MOD_PREFIX + "RaysPerTick");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            arg2.Append(((int)Getter(b)).ToString("0"));
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigRaycast;
            if (config == null)
                return;

            config.RaysPerTick = (int)value;
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigRaycast;
            if (config == null)
                return 32;

            return config.RaysPerTick;
        }
    }
}
