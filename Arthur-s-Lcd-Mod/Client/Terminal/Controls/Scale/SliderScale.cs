using System.Text;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
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
            slider.SetLimits(GeneralConfigComponentExtensions.MIN_SCALE, GeneralConfigComponentExtensions.MAX_SCALE);
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
            ConfigManager.ModifyComponentForCurrentSurface<GeneralConfigComponent>(
                block,
                Constants.GENERAL,
                config => config.SetScale(value));
        }

        float Getter(IMyTerminalBlock block)
        {
            return ConfigManager
                .GetComponentForCurrentSurface<GeneralConfigComponent>(block, Constants.GENERAL)
                .GetScale();
        }
    }
}
