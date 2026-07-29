using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;


namespace LcdMod.Client.Terminal.Controls.Interactive
{
    public partial class SwitchToggleAlt : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }
        
        public SwitchToggleAlt()
        {
            var slider = CreateControl<IMyTerminalControlOnOffSwitch>("SwitchToggleRequiresAlt");
            slider.Getter = Getter;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.Title = MyStringId.GetOrCompute(MOD_PREFIX + "AlwaysActive");
            slider.Tooltip = MyStringId.GetOrCompute(MOD_PREFIX + "AlwaysActive_Tooltip");

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
            ConfigManager.ModifyComponentForCurrentSurface<InteractiveConfigComponent>(
                block,
                INTERACTION,
                config => config.RequiresAlt = !value);
        }

        bool Getter(IMyTerminalBlock myTerminalBlock)
        {
            var config = ConfigManager.GetComponentForCurrentSurface<InteractiveConfigComponent>(
                myTerminalBlock,
                INTERACTION);
            return config != null && !config.RequiresAlt;
        }
    }
}
