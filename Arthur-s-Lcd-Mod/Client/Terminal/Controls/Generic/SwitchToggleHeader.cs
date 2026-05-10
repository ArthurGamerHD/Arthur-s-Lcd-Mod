using LcdMod.Client.Config;
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
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if (config == null)
                return;

            config.TitleVisible = value;
            ConfigManager.Sync(block);
        }

        bool Getter(IMyTerminalBlock myTerminalBlock)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(myTerminalBlock);
            return config != null && config.TitleVisible;
        }
    }
}
