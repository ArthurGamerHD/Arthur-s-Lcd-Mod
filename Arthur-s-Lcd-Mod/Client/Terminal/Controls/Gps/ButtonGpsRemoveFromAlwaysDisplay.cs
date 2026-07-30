using System.Collections.Generic;
using LcdMod.Client.Terminal.Controls.Filter.Buttons;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using Sandbox.ModAPI;

using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Gps
{
    public sealed partial class ButtonGpsRemoveFromAlwaysDisplay : TerminalControlFilterButton
    {
        public ButtonGpsRemoveFromAlwaysDisplay(
            TerminalControlsListbox sourceList,
            TerminalControlsListbox targetList)
            : base(sourceList, targetList)
        {
            CreateButton("GpsRemoveFromAlwaysDisplay", MOD_PREFIX + "Gps_Remove_Title");
        }

        protected override bool IsAvailableForCurrentConfig(IMyTerminalBlock block)
        {
            return GpsDisplayConfigHelper.GetConfig(block) != null &&
                   GpsDisplayConfigHelper.IsStaticMode(block);
        }

        protected override void Action(IMyTerminalBlock block)
        {
            if (TargetList.Selection == null || TargetList.Selection.Count == 0)
                return;

            var waypointKeys = new HashSet<string>();
            var removals = new HashSet<int>();
            foreach (var item in TargetList.Selection)
            {
                if (item == null)
                    continue;

                if (item.UserData is int)
                    removals.Add((int)item.UserData);
                else if (item.UserData is string)
                    waypointKeys.Add((string)item.UserData);
            }

            if (removals.Count == 0 && waypointKeys.Count == 0)
                return;

            if (!GpsDisplayConfigHelper.Modify(block, config =>
            {
                GpsDisplayConfigHelper.RemoveAlwaysDisplayedGps(config, waypointKeys, removals);
            }))
            {
                return;
            }

            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
        }
    }
}
