using System.Text;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.TerminalControls.Generic
{
    public sealed partial class SliderPadding : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderPadding()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("LcdMod_PaddingSlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(0, 50f);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("BlockPropertyTitle_LCDScreenTextPadding");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            arg2.Append(FormatingHelper.PercentageToString(Getter(b)/100));
        }

        void Setter(IMyTerminalBlock block, float value) => GetThisSurface(block).TextPadding = value;

        float Getter(IMyTerminalBlock block) => GetThisSurface(block).TextPadding;
    }
}
