using LcdMod.Client.Config;

using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using ScreenConfigPower = LcdMod.Common.Config.Models.Apps.ScreenConfigPower;
using ScreenConfigWithFilters = LcdMod.Common.Config.Models.Apps.ScreenConfigWithFilters;

namespace LcdMod.Client.TerminalControls.Generic
{
    public partial class CheckboxHideEmpty : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public CheckboxHideEmpty()
        {
            var slider = CreateControl<IMyTerminalControlCheckbox>("LinesSwitch");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.Title = MyStringId.GetOrCompute("HideEmpty");
            slider.OnText = MyStringId.GetOrCompute("HudInfoOn");
            slider.OffText = MyStringId.GetOrCompute("HudInfoOff");
            
            TerminalControl = slider;
        }

        void Setter(IMyTerminalBlock block, bool value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            var withFilters = config as ScreenConfigWithFilters;
            var power = config as ScreenConfigPower;
            if (withFilters == null && power == null)
                return;

            if (withFilters != null)
                withFilters.HideEmpty = value;
            if (power != null)
                power.HideEmpty = value;
            ConfigManager.Sync(block);
        }

        bool Getter(IMyTerminalBlock myTerminalBlock)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(myTerminalBlock);
            var withFilters = config as ScreenConfigWithFilters;
            if (withFilters != null)
                return withFilters.HideEmpty;

            var power = config as ScreenConfigPower;
            return power != null && power.HideEmpty;
        }
    }
}
