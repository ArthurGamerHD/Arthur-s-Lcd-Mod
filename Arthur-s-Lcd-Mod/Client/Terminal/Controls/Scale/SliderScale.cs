using System.Text;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Models;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;


namespace LcdMod.Client.Terminal.Controls.Scale
{
    public sealed partial class SliderScale : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderScale()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("ScaleSlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(ScreenConfigGeneral.MIN_SCALE, ScreenConfigGeneral.MAX_SCALE);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("BlockPropertyTitle_Scale");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            arg2.Append(Getter(b).ToString("0.000"));
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            config.Scale = value;
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return 1;

            return config.Scale;
        }
    }
}
