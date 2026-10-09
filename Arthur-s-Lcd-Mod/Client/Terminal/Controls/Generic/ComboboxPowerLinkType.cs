using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Client.Modules.Power;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    /// <summary>
    /// Power network scope for the power apps: this grid only, the mechanical group, or the whole electrical network
    /// vanilla shares power over (mechanical links plus connectors that transfer power).
    /// </summary>
    public sealed partial class ComboboxPowerLinkType : TerminalControlsWrapper
    {
        const long LOCAL = 0;
        const long MECHANICAL = 1;
        const long ELECTRICAL = 2;

        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxPowerLinkType()
        {
            var combo = CreateControl<IMyTerminalControlCombobox>("PowerLinkTypeCombobox");
            combo.Getter = Getter;
            combo.Setter = Setter;
            combo.ComboBoxContent = Content;
            combo.Visible = Visible;
            combo.Title = MyStringId.GetOrCompute(MOD_PREFIX + "LinkType");
            TerminalControl = combo;
        }

        static void Content(List<MyTerminalControlComboBoxItem> list)
        {
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = LOCAL, Value = MyStringId.GetOrCompute(MOD_PREFIX + "LocalGrid")
            });
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = MECHANICAL, Value = MyStringId.GetOrCompute(MOD_PREFIX + "MechanicalConnection")
            });
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = ELECTRICAL, Value = MyStringId.GetOrCompute(MOD_PREFIX + "ElectricalConnection")
            });
        }

        long Getter(IMyTerminalBlock block)
        {
            var cfg = ConfigManager.GetComponentForTerminalApp<PowerConfigComponent>(block);
            if (cfg == null)
                return MECHANICAL;

            var linkType = PowerScopeResolver.NormalizeLinkType(cfg.GridLinkTypeInternal);
            if (linkType < 0)
                return LOCAL;
            return linkType == GridLinkTypeEnum.Electrical ? ELECTRICAL : MECHANICAL;
        }

        void Setter(IMyTerminalBlock block, long value)
        {
            int linkType = value == LOCAL
                ? -1
                : (int)(value == ELECTRICAL ? GridLinkTypeEnum.Electrical : GridLinkTypeEnum.Mechanical);
            ConfigManager.ModifyComponentForTerminalApp<PowerConfigComponent>(
                block,
                cfg => cfg.GridLinkTypeInternal = linkType);
        }
    }
}
