using LcdMod.Client.Config;
using LcdMod.Common.Config.Interfaces;
using LcdMod.Common.Config.Models.Apps;
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

            return (ConfigManager.GetConfigForCurrentScreen(block) as IHideEmpty) != null;
        }

        void Setter(IMyTerminalBlock block, bool value)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as IHideEmpty;
            if (config == null)
                return;

            config.HideEmpty = value;
            ConfigManager.Sync(block);
        }

        bool Getter(IMyTerminalBlock myTerminalBlock)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(myTerminalBlock) as IHideEmpty;
            return config != null && config.HideEmpty;
        }
    }
}
