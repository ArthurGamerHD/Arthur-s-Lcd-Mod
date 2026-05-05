using LcdMod.Client.Config;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using ScreenConfigInteractive = LcdMod.Common.Config.Models.ScreenConfigInteractive;

namespace LcdMod.Client.TerminalControls.Interactive
{
    public partial class SwitchToggleAlt : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }
        
        public SwitchToggleAlt()
        {
            var slider = CreateControl<IMyTerminalControlOnOffSwitch>("LcdMod_SwitchToggleRequiresAlt");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.Title = MyStringId.GetOrCompute("LcdMod_AlwaysActive");
            slider.Tooltip = MyStringId.GetOrCompute("LcdMod_AlwaysActive_Tooltip");

            slider.OnText = MyStringId.GetOrCompute("HudInfoOn");
            slider.OffText = MyStringId.GetOrCompute("HudInfoOff");

            TerminalControl = slider;
        }

        public override bool Visible(IMyTerminalBlock block)
        {
            var definition = ((block as IMyCockpit) as MyCubeBlock)?.BlockDefinition as MyCockpitDefinition;
            if (definition?.EnableShipControl ?? false)
                return false;
            
            return base.Visible(block);
        }


        void Setter(IMyTerminalBlock block, bool value)
        {
            var screen = GetThisSurfaceIndex(block);
            var config = ConfigManager.GetConfigForScreen(block, screen) as ScreenConfigInteractive;

            if (config == null)
                return;

            config.RequiresAlt = !value;

            ConfigManager.Sync(block);
        }

        bool Getter(IMyTerminalBlock myTerminalBlock)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(myTerminalBlock) as ScreenConfigInteractive;
            return config != null && !config.RequiresAlt;
        }
    }
}
