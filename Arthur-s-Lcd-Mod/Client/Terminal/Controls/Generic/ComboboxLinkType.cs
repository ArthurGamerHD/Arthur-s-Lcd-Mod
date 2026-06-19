using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Interfaces;
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
            var cfg = ConfigManager.GetConfigForCurrentScreen(block) as IGridGroupReference;
            return ToId((GridLinkTypeEnum)(cfg?.GridLinkTypeInternal ?? 3));
        }

        void Setter(IMyTerminalBlock block, long value)
        {
            var cfg = ConfigManager.GetConfigForCurrentScreen(block) as IGridGroupReference;
            if (cfg == null) return;
            cfg.GridLinkTypeInternal = FromId(value);
            ConfigManager.Sync(block);
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
    }
}