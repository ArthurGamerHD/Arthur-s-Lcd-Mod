using System.Text;
using LcdMod.Client.Config;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using ScreenConfigGeneral = LcdMod.Common.Config.Models.ScreenConfigGeneral;
using ScreenConfigInteractive = LcdMod.Common.Config.Models.ScreenConfigInteractive;

namespace LcdMod.Client.TerminalControls.Interactive
{
    public sealed partial class SliderCursorScale : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderCursorScale()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("LcdMod_CursorScaleSlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(0, ScreenConfigGeneral.MAX_SCALE);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("LcdMod_CursorScale");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            arg2.Append(Getter(b).ToString("0.000"));
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigInteractive;
            if (config == null)
                return;

            config.CursorScale = value;
            ConfigManager.Sync(block);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigInteractive;
            if (config == null)
                return 1;

            return config.CursorScale;
        }
    }
}