using System;
using System.Collections.Generic;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls.Filter.Listbox;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;

using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Gps
{
    public sealed partial class ListboxGpsCandidates : TerminalControlsListbox
    {
        readonly List<IMyGps> _gpsEntries = new List<IMyGps>();

        public ListboxGpsCandidates()
        {
            CreateListbox("GpsCandidates", MOD_PREFIX + "Gps_Available_Title");
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
            _gpsEntries.Sort((left, right) => string.Compare(
                left == null ? string.Empty : left.Name,
                right == null ? string.Empty : right.Name,
                StringComparison.CurrentCultureIgnoreCase));

            foreach (var gps in _gpsEntries)
            {
                if (gps == null || GpsDisplayConfigHelper.ContainsAlwaysDisplayedGps(config, gps.Hash))
                    continue;

                itemList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                    string.IsNullOrWhiteSpace(gps.Name)
                        ? LocHelper.GetLoc(MOD_PREFIX + "Gps_Unnamed")
                        : gps.Name,
                    gps.ToString(),
                    gps.Hash));
            }

            base.Getter(block, itemList, selected);
        }
    }
}
