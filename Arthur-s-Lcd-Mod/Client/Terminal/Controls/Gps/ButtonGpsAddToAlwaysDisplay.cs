using System.Collections.Generic;
using LcdMod.Common.Config.Components;
using LcdMod.Client.Terminal.Controls.Filter.Buttons;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Terminal.Controls.Gps
{
    public sealed partial class ButtonGpsAddToAlwaysDisplay : TerminalControlFilterButton
    {
        readonly List<IMyGps> _gpsEntries = new List<IMyGps>();

        public ButtonGpsAddToAlwaysDisplay(
            TerminalControlsListbox sourceList,
            TerminalControlsListbox targetList)
            : base(sourceList, targetList)
        {
            CreateButton("GpsAddToAlwaysDisplay", "Add GPS");
        }

        protected override bool IsAvailableForCurrentConfig(IMyTerminalBlock block)
        {
            return GpsDisplayConfigHelper.GetConfig(block) != null &&
                   GpsDisplayConfigHelper.IsStaticMode(block);
        }

        protected override void Action(IMyTerminalBlock block)
        {
            if (SourceList.Selection == null || SourceList.Selection.Count == 0)
                return;

            var additions = new HashSet<int>();
            foreach (var item in SourceList.Selection)
            {
                if (item != null && item.UserData is int)
                    additions.Add((int)item.UserData);
            }

            if (additions.Count == 0)
                return;

            GpsDisplayConfigHelper.GetLocalGps(_gpsEntries);
            var waypoints = new List<GpsDisplayWaypoint>(additions.Count);
            foreach (var gps in _gpsEntries)
            {
                if (gps == null || !additions.Contains(gps.Hash))
                    continue;

                var waypoint = GpsDisplayConfigHelper.CreateWaypoint(gps);
                if (waypoint != null)
                    waypoints.Add(waypoint);
            }

            if (waypoints.Count == 0)
                return;

            if (!GpsDisplayConfigHelper.Modify(block, config =>
            {
                GpsDisplayConfigHelper.AddAlwaysDisplayedWaypoints(config, waypoints);
            }))
            {
                return;
            }

            SourceList.TerminalControl.UpdateVisual();
            TargetList.TerminalControl.UpdateVisual();
        }
    }
}
