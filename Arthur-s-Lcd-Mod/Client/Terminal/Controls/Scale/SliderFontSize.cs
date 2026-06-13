using System.Text;
using LcdMod.Common.Config.Models;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;


namespace LcdMod.Client.Terminal.Controls.Scale
{
    public sealed partial class SliderFontSize : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderFontSize()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("SliderFontSize");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(ScreenConfigGeneral.MIN_SCALE, ScreenConfigGeneral.MAX_SCALE);
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
