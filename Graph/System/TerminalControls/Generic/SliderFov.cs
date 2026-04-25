using System.Text;
using Graph.System.Config;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace Graph.System.TerminalControls.Generic
{
    public sealed partial class SliderFov : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderFov()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("LcdMod_SliderFov");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(1, 120);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("FieldOfView");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            arg2.Append(Getter(b).ToString("0.0")+"ª");
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            config.FoV = value;
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return 1;

            return config.FoV;
        }
    }
}
