using System.Text;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class SliderRotation : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SliderRotation()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("RotationSlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(0, 359);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute("BlockPropertyTitle_ProjectionRotationY");
            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            arg2.Append(Getter(b).ToString("0")+"º");
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            ConfigManager.ModifyComponentForTerminalApp<DiagnosticConfigComponent>(
                block,
                config => config.Rotation = (int)(value/5) * 5);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetComponentForTerminalApp<DiagnosticConfigComponent>(block);
            if (config == null)
                return 1;

            return config.Rotation;
        }
    }
}
