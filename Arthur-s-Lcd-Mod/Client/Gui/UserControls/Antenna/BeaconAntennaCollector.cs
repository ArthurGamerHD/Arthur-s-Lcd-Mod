using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using VRageMath;
using IMyBeacon = Sandbox.ModAPI.IMyBeacon;

namespace LcdMod.Client.Gui.UserControls.Antenna
{
    internal sealed class BeaconCollector : AntennaCollector
    {
        public BeaconCollector(IAppHost antennaSurfaceScript) : base(antennaSurfaceScript)
        {
            
        }

        public override void Collect(
            GridLogic grid,
            List<AntennaEntry> entries,
            Dictionary<long, AntennaEntry> models,
            HashSet<long> activeEntryIds)
        {
            var beacons = grid.GetTerminalBlocks<IMyBeacon>(ScreenConfigGeneral.GridLinkType);

            for (int i = 0; i < beacons.Count; i++)
            {
                var beacon = beacons[i];

                if(!IsValid(beacon))
                    continue;
                
                var entry = GetOrCreateEntry(beacon.EntityId, entries, models, activeEntryIds);
                entry.Update(
                    GetName(beacon),
                    GetStatusIcon(beacon),
                    GetStatusText(beacon),
                    GetStatusColor(beacon),
                    beacon.IsFunctional,
                    false);
            }
        }

        string GetName(IMyBeacon beacon)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(beacon.CustomName) ? beacon.CustomName : beacon.DisplayNameText;
            }
            catch
            {
                return "Beacon";
            }
        }

        string GetStatusIcon(IMyBeacon beacon)
        {
            if (beacon == null || !beacon.Enabled)
                return "GridPower";

            if (!beacon.IsFunctional)
                return "Warning";

            return "BeaconBroadcast";
        }

        string GetStatusText(IMyBeacon beacon)
        {
            if (beacon == null || !beacon.Enabled)
                return GetLocCached("AssemblerState_Disabled");

            if (!beacon.IsFunctional)
                return GetLocCached("Module_Damaged");

            var sb = new StringBuilder();
            sb.AppendLine(string.IsNullOrWhiteSpace(beacon.HudText) ? beacon.CustomName : beacon.HudText);
            sb.Append(FormatLabelWithColon(GetLocCached("BlockPropertyDescription_BroadcastRadius")) + " " +
                      FormatingHelper.DistanceToString(beacon.Radius));
            return sb.ToString();
        }

        Color GetStatusColor(IMyBeacon beacon)
        {
            if (!beacon.IsFunctional)
                return WarningColor;

            return ForegroundColor;
        }
    }
}
