using System;
using System.Collections.Generic;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace LcdMod.Client.Terminal.Controls.Gps
{
    public sealed partial class ListboxGpsAlwaysDisplayed : TerminalControlsListbox
    {
        readonly List<IMyGps> _gpsEntries = new List<IMyGps>();
        readonly Dictionary<int, IMyGps> _gpsByHash = new Dictionary<int, IMyGps>();

        public ListboxGpsAlwaysDisplayed()
        {
            CreateListbox("GpsAlwaysDisplayed", "Always Display GPS");
        }

        protected override bool IsAvailableForCurrentConfig(IMyTerminalBlock block)
        {
            return GpsDisplayConfigHelper.GetConfig(block) != null &&
                   GpsDisplayConfigHelper.IsStaticMode(block);
        }

        protected override void Getter(
            IMyTerminalBlock block,
            List<MyTerminalControlListBoxItem> itemList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var config = GpsDisplayConfigHelper.GetConfig(block);
            if (config == null)
                return;

            GpsDisplayConfigHelper.GetLocalGps(_gpsEntries);
            _gpsByHash.Clear();
            foreach (var gps in _gpsEntries)
            {
                if (gps != null)
                    _gpsByHash[gps.Hash] = gps;
            }

            var waypoints = config.AlwaysDisplayedGpsWaypoints ?? Array.Empty<GpsDisplayWaypoint>();
            foreach (var waypoint in waypoints)
            {
                if (waypoint == null)
                    continue;

                itemList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                    GpsDisplayConfigHelper.GetWaypointDisplayName(waypoint),
                    GpsDisplayConfigHelper.GetWaypointTooltip(waypoint),
                    GpsDisplayConfigHelper.GetWaypointKey(waypoint)));
            }

            var hashes = config.AlwaysDisplayedGpsHashes ?? Array.Empty<int>();
            foreach (var hash in hashes)
            {
                if (GpsDisplayConfigHelper.ContainsWaypointSourceHash(waypoints, hash))
                    continue;

                IMyGps gps;
                if (_gpsByHash.TryGetValue(hash, out gps))
                {
                    itemList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                        string.IsNullOrWhiteSpace(gps.Name) ? "GPS" : gps.Name,
                        gps.ToString(),
                        hash));
                }
                else
                {
                    itemList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                        "Unavailable GPS",
                        "The GPS entry no longer exists. Remove it from this list to clear the saved reference.",
                        hash));
                }
            }

            base.Getter(block, itemList, selected);
        }
    }
}
