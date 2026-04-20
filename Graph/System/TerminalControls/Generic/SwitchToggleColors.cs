using Graph.System.Config;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage;
using VRage.Utils;

namespace Graph.System.TerminalControls.Generic
{
    public class SwitchToggleColors : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SwitchToggleColors()
        {
            var slider = CreateControl<IMyTerminalControlOnOffSwitch>("HelpScreen_ControllerColorTool");
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
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            config.CustomizedColors = value;
            ConfigManager.Sync(block);
        }

        bool Getter(IMyTerminalBlock myTerminalBlock)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(myTerminalBlock);
            return config != null && config.CustomizedColors;
        }
    }
}