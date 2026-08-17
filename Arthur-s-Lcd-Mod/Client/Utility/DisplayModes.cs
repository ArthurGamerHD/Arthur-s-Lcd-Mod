using System.Collections.Generic;
using LcdMod.Common.Config.Components;
using VRage.ModAPI;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Utility
{
    public static class DisplayModes
    {
        public static readonly List<MyTerminalControlComboBoxItem> GridAndLegacy =
            new List<MyTerminalControlComboBoxItem>
            {
                new MyTerminalControlComboBoxItem
                {
                    Key = 0,
                    Value = MyStringId.GetOrCompute(MOD_PREFIX + "Grid")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = 1,
                    Value = MyStringId.GetOrCompute(MOD_PREFIX + "List")
                }
            };

        public static readonly List<MyTerminalControlComboBoxItem> Items =
            new List<MyTerminalControlComboBoxItem>
            {
                CreateItemMode(ItemDisplayMode.Card, MOD_PREFIX + "Card"),
                CreateItemMode(ItemDisplayMode.List, MOD_PREFIX + "List"),
                CreateItemMode(ItemDisplayMode.Table, MOD_PREFIX + "Table"),
                CreateItemMode(ItemDisplayMode.Grid, MOD_PREFIX + "Grid")
            };

        static MyTerminalControlComboBoxItem CreateItemMode(ItemDisplayMode mode, string name)
        {
            return new MyTerminalControlComboBoxItem
            {
                Key = (long)mode,
                Value = MyStringId.GetOrCompute(name)
            };
        }
    }
}
