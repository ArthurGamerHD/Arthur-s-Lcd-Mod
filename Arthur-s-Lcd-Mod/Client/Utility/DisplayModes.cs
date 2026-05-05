using System.Collections.Generic;
using VRage.ModAPI;
using VRage.Utils;

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
                    Value = MyStringId.GetOrCompute("Lcd_Grid")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = 1,
                    Value = MyStringId.GetOrCompute("StoryTitle_MinerStories12")
                }
            };
    }
}
