using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    /// <summary>
    /// Quick toggle for the grid-link scope: ON = Physical (also picks docked/subgrid containers),
    /// OFF = Mechanical (rotor/piston/hinge subgrids only). Defaults to ON (Physical), matching the
    /// block-selection default. Writes the same <c>GridLinkTypeInternal</c> field as
    /// <see cref="ComboboxLinkType"/>, so the two stay in sync.
    /// </summary>
    public partial class SwitchSubGrid : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public SwitchSubGrid()
        {
            var toggle = CreateControl<IMyTerminalControlOnOffSwitch>("SubGridSwitch");
            toggle.Getter = Getter;
            toggle.Setter = Setter;
            toggle.Visible = Visible;
            toggle.Title = MyStringId.GetOrCompute(MOD_PREFIX + "SubGrid");
            toggle.OnText = MyStringId.GetOrCompute(MOD_PREFIX + "PhysicalConnection");
            toggle.OffText = MyStringId.GetOrCompute(MOD_PREFIX + "MechanicalConnection");

            TerminalControl = toggle;
        }

        void Setter(IMyTerminalBlock block, bool value)
        {
            var gridLinkType = (int)(value ? GridLinkTypeEnum.Physical : GridLinkTypeEnum.Mechanical);
            if (ConfigManager.ModifyComponentForCurrentSurface<BlockSelectionConfigComponent>(
                    block,
                    BLOCKS,
                    config => config.GridLinkTypeInternal = gridLinkType))
                return;

            if (ConfigManager.ModifyComponentForTerminalApp<PowerConfigComponent>(
                    block,
                    config => config.GridLinkTypeInternal = gridLinkType))
                return;

            ConfigManager.ModifyComponentForTerminalApp<CargoActionsConfigComponent>(
                block,
                config => config.GridLinkTypeInternal = gridLinkType);
        }

        bool Getter(IMyTerminalBlock block)
        {
            var blocks = ConfigManager.GetComponentForCurrentSurface<BlockSelectionConfigComponent>(
                block,
                BLOCKS);
            if (blocks != null)
                return blocks.GridLinkTypeInternal == (int)GridLinkTypeEnum.Physical;

            var power = ConfigManager.GetComponentForTerminalApp<PowerConfigComponent>(block);
            if (power != null)
                return power.GridLinkTypeInternal == (int)GridLinkTypeEnum.Physical;

            var cargo = ConfigManager.GetComponentForTerminalApp<CargoActionsConfigComponent>(block);
            if (cargo == null)
                return true;

            return cargo.GridLinkTypeInternal == (int)GridLinkTypeEnum.Physical;
        }
    }
}
