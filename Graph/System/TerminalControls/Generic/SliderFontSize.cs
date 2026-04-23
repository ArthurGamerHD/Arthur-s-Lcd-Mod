using System.Text;
using Graph.System.Config;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace Graph.System.TerminalControls.Generic
{
    public sealed partial class SliderFontSize : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderFontSize()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("LcdMod_SliderFontSize");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(ScreenConfig.MIN_SCALE, ScreenConfig.MAX_SCALE);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("BlockPropertyTitle_LCDScreenTextSize");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            arg2.Append(Getter(b).ToString("0.000"));
        }

        void Setter(IMyTerminalBlock block, float value) => GetThisSurface(block).FontSize = value;

        float Getter(IMyTerminalBlock block) => GetThisSurface(block).FontSize;
    }
}
