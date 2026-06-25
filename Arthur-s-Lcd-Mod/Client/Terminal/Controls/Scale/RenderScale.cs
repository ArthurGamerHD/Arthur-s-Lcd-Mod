using System.Text;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Scale
{
    public sealed partial class SliderRenderScale : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }
        protected override bool RequiresAdvancedTweakables => true;

        public SliderRenderScale()
        {
            var slider = CreateControl<IMyTerminalControlSlider>("RenderScaleSlider");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.SetLimits(.1f, 1);
            slider.Writer = Writer;
            slider.Title = MyStringId.GetOrCompute(MOD_PREFIX + "RenderScale");

            TerminalControl = slider;
        }

        void Writer(IMyTerminalBlock b, StringBuilder arg2)
        {
            if (Getter(b) >= .99f)
            {
                arg2.Append("I paid 100% of the CPU");
                return;
            }
            arg2.Append(Getter(b).ToString("P"));
        }

        void Setter(IMyTerminalBlock block, float value)
        {
            ConfigManager.ModifyComponentForTerminalApp<RaycastConfigComponent>(
                block,
                config => config.RenderScale = value);
        }

        float Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetComponentForTerminalApp<RaycastConfigComponent>(block);
            if (config == null)
                return 1;

            return config.RenderScale;
        }
    }
}
