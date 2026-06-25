using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public partial class SwitchToggleLines : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SwitchToggleLines()
        {
            var slider = CreateControl<IMyTerminalControlOnOffSwitch>("LinesSwitch");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.Title = MyStringId.GetOrCompute("SafeZone_Texture_Lines");
            slider.OnText = MyStringId.GetOrCompute("HudInfoOn");
            slider.OffText = MyStringId.GetOrCompute("HudInfoOff");

            TerminalControl = slider;
        }

        void Setter(IMyTerminalBlock block, bool value)
        {
            ConfigManager.ModifyComponentForCurrentSurface<GeneralConfigComponent>(
                block,
                Constants.GENERAL,
                config => config.DrawLines = value);
        }

        bool Getter(IMyTerminalBlock myTerminalBlock)
        {
            var config = ConfigManager.GetComponentForCurrentSurface<GeneralConfigComponent>(
                myTerminalBlock,
                Constants.GENERAL);
            return config != null && config.DrawLines;
        }
    }
}
