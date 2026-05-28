using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Interfaces;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

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
            combo.Title = MyStringId.GetOrCompute("LcdMod_LinkType");
            combo.Tooltip = MyStringId.GetOrCompute(string.Format(
                LocHelper.GetLoc("LcdMod_LinkTypeDescription"),
                LocHelper.GetLoc("LcdMod_LocalGrid"),
                LocHelper.GetLoc("LcdMod_MechanicalConnection"),
                LocHelper.GetLoc("LcdMod_PhysicalConnection")
            ));
            TerminalControl = combo;
        }

        static void Content(List<MyTerminalControlComboBoxItem> list)
        {
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = 0, Value = MyStringId.GetOrCompute("LcdMod_LocalGrid")
            });
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = ToId(GridLinkTypeEnum.Mechanical),
                Value = MyStringId.GetOrCompute("LcdMod_MechanicalConnection")
            });
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = ToId(GridLinkTypeEnum.Physical),
                Value = MyStringId.GetOrCompute("LcdMod_PhysicalConnection")
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