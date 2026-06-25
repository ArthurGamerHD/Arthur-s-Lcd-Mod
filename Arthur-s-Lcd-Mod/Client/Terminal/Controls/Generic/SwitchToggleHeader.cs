using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public partial class SwitchToggleHeader : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SwitchToggleHeader()
        {
            var slider = CreateControl<IMyTerminalControlOnOffSwitch>("TitleSwitch");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.Title = MyStringId.GetOrCompute(
                    $"{MyTexts.Get(MyStringId.GetOrCompute("BlockPropertyTitle_TextPanelPublicTitle"))} " +
                    $"{MyTexts.Get(MyStringId.GetOrCompute("RadialMenuAction_Hud_Visible"))}");

            slider.OnText = MyStringId.GetOrCompute("HudInfoOn");
            slider.OffText = MyStringId.GetOrCompute("HudInfoOff");

            TerminalControl = slider;
        }

        void Setter(IMyTerminalBlock block, bool value)
        {
            ConfigManager.ModifyComponentForCurrentSurface<GeneralConfigComponent>(
                block,
                Constants.GENERAL,
                config => config.TitleVisible = value);
        }

        bool Getter(IMyTerminalBlock myTerminalBlock)
        {
            var config = ConfigManager.GetComponentForCurrentSurface<GeneralConfigComponent>(
                myTerminalBlock,
                Constants.GENERAL);
            return config != null && config.TitleVisible;
        }
    }
}
