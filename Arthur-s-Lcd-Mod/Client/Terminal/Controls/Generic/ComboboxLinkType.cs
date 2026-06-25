using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public partial class ComboboxLinkType : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxLinkType()
        {
            var combo = CreateControl<IMyTerminalControlCombobox>("LinkTypeCombobox");
            combo.Getter = Getter;
            combo.Setter = Setter;
            combo.ComboBoxContent = Content;
            combo.Visible = Visible;
            combo.Title = MyStringId.GetOrCompute(MOD_PREFIX + "LinkType");
            combo.Tooltip = MyStringId.GetOrCompute(string.Format(
                LocHelper.GetLoc(MOD_PREFIX + "LinkTypeDescription"),
                LocHelper.GetLoc(MOD_PREFIX + "LocalGrid"),
                LocHelper.GetLoc(MOD_PREFIX + "MechanicalConnection"),
                LocHelper.GetLoc(MOD_PREFIX + "PhysicalConnection")
            ));
            TerminalControl = combo;
        }

        static void Content(List<MyTerminalControlComboBoxItem> list)
        {
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = 0, Value = MyStringId.GetOrCompute(MOD_PREFIX + "LocalGrid")
            });
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = ToId(GridLinkTypeEnum.Mechanical),
                Value = MyStringId.GetOrCompute(MOD_PREFIX + "MechanicalConnection")
            });
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = ToId(GridLinkTypeEnum.Physical),
                Value = MyStringId.GetOrCompute(MOD_PREFIX + "PhysicalConnection")
            });
        }

        long Getter(IMyTerminalBlock block)
        {
            return ToId((GridLinkTypeEnum)GetGridLinkTypeInternal(block, 3));
        }

        void Setter(IMyTerminalBlock block, long value)
        {
            var gridLinkType = FromId(value);
            if (ConfigManager.ModifyComponentForCurrentSurface<BlockSelectionConfigComponent>(
                    block,
                    Constants.BLOCKS,
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

        static long ToId(GridLinkTypeEnum enumValue)
        {
            switch (enumValue)
            {
                case GridLinkTypeEnum.Mechanical:
                    return 1;
                case GridLinkTypeEnum.Physical:
                    return 2;
            }

            return 0;
        }

        int FromId(long value)
        {
            switch (value)
            {
                case 1:
                    return (int)GridLinkTypeEnum.Mechanical;
                case 2:
                    return (int)GridLinkTypeEnum.Physical;
            }

            return -1;
        }

        static int GetGridLinkTypeInternal(IMyTerminalBlock block, int defaultValue)
        {
            var blocks = ConfigManager.GetComponentForCurrentSurface<BlockSelectionConfigComponent>(
                block,
                Constants.BLOCKS);
            if (blocks != null)
                return blocks.GridLinkTypeInternal;

            var power = ConfigManager.GetComponentForTerminalApp<PowerConfigComponent>(block);
            if (power != null)
                return power.GridLinkTypeInternal;

            var cargo = ConfigManager.GetComponentForTerminalApp<CargoActionsConfigComponent>(block);
            return cargo != null ? cargo.GridLinkTypeInternal : defaultValue;
        }
    }
}
