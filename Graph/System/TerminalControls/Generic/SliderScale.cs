using System.Text;
using Graph.System.Config;
using Graph.System.Config.Models.Apps;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using ScreenConfigGeneral = Graph.System.Config.Models.ScreenConfigGeneral;

namespace Graph.System.TerminalControls.Generic
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
