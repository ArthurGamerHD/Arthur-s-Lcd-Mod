using System.Text;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;


namespace LcdMod.Client.Terminal.Controls.Interactive
{
    public sealed partial class SliderCursorScale : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderCursorScale()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("CursorScaleSlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(0, GeneralConfigComponentExtensions.MAX_SCALE);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute(MOD_PREFIX + "CursorScale");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            arg2.Append(Getter(b).ToString("0.000"));
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            ConfigManager.ModifyComponentForCurrentSurface<InteractiveConfigComponent>(
                block,
                Constants.INTERACTION,
                config => config.CursorScale = value);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetComponentForCurrentSurface<InteractiveConfigComponent>(
                block,
                Constants.INTERACTION);
            if (config == null)
                return 1;

            return config.CursorScale;
        }
    }
}
