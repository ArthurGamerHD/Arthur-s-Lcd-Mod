using LcdMod.Client.Config;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Cargo
{
    /// <summary>Per-screen flag in the LCD terminal that shows or hides the Settings button of the
    /// Cargo Actions screen. Per screen on purpose: hiding the button on a wall panel must not hide
    /// it on the cockpit screen next to it.</summary>
    public sealed partial class SwitchShowConfigButton : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SwitchShowConfigButton()
        {
            var slider = CreateControl<IMyTerminalControlOnOffSwitch>("CargoActionsShowConfigButton");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.Title = MyStringId.GetOrCompute(MOD_PREFIX + "CargoActions_ShowConfigButton");
            slider.Tooltip = MyStringId.GetOrCompute(MOD_PREFIX + "CargoActions_ShowConfigButton_Tooltip");
            slider.OnText = MyStringId.GetOrCompute("HudInfoOn");
            slider.OffText = MyStringId.GetOrCompute("HudInfoOff");

            TerminalControl = slider;
        }

        void Setter(IMyTerminalBlock block, bool value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigCargoActions;
            if (config == null)
                return;

            config.ShowConfigButton = value;
            ConfigManager.Sync(block);
        }

        bool Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigCargoActions;
            return config != null && config.ShowConfigButton;
        }
    }
}
