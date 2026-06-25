using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
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

        public override bool Visible(IMyTerminalBlock block)
        {
            if (!base.Visible(block))
                return false;

            return GetButtonPanel(block) != null
                   || GetPower(block) != null
                   || GetFilters(block) != null;
        }

        void Setter(IMyTerminalBlock block, bool value)
        {
            if (ConfigManager.ModifyComponentForTerminalApp<ButtonPanelConfigComponent>(
                    block,
                    config => config.HideEmpty = value))
                return;

            if (ConfigManager.ModifyComponentForTerminalApp<PowerConfigComponent>(
                    block,
                    config => config.HideEmpty = value))
                return;

            ConfigManager.ModifyComponentForCurrentSurface<FilterConfigComponent>(
                block,
                Constants.FILTERS,
                config => config.HideEmpty = value);
        }

        bool Getter(IMyTerminalBlock myTerminalBlock)
        {
            var buttonPanel = GetButtonPanel(myTerminalBlock);
            if (buttonPanel != null)
                return buttonPanel.HideEmpty;

            var power = GetPower(myTerminalBlock);
            if (power != null)
                return power.HideEmpty;

            var filters = GetFilters(myTerminalBlock);
            return filters != null && filters.HideEmpty;
        }

        static ButtonPanelConfigComponent GetButtonPanel(IMyTerminalBlock block)
        {
            return ConfigManager.GetComponentForTerminalApp<ButtonPanelConfigComponent>(block);
        }

        static PowerConfigComponent GetPower(IMyTerminalBlock block)
        {
            return ConfigManager.GetComponentForTerminalApp<PowerConfigComponent>(block);
        }

        static FilterConfigComponent GetFilters(IMyTerminalBlock block)
        {
            return ConfigManager.GetComponentForCurrentSurface<FilterConfigComponent>(block, Constants.FILTERS);
        }
    }
}
