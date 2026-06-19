using LcdMod.Client.Config;
using LcdMod.Common.Config.Interfaces;
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
    /// <c>ScreenConfigWithBlocks</c> default. Writes the same <c>GridLinkTypeInternal</c> field as
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
            var cfg = ConfigManager.GetConfigForCurrentScreen(block) as IGridGroupReference;
            if (cfg == null)
                return;

            cfg.GridLinkTypeInternal = (int)(value ? GridLinkTypeEnum.Physical : GridLinkTypeEnum.Mechanical);
            ConfigManager.Sync(block);
        }

        bool Getter(IMyTerminalBlock block)
        {
            var cfg = ConfigManager.GetConfigForCurrentScreen(block) as IGridGroupReference;
            if (cfg == null)
                return true; // default ON = Physical

            return cfg.GridLinkTypeInternal == (int)GridLinkTypeEnum.Physical;
        }
    }
}
