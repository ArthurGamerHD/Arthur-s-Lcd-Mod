using System.Collections.Generic;
using System.Text;
using Graph.Apps.Antenna;
using Graph.Helpers;
using VRageMath;
using IMyBeacon = Sandbox.ModAPI.IMyBeacon;

namespace Graph.System.Antenna
{
    internal sealed class BeaconCollector : AntennaCollector
    {
        public BeaconCollector(AntennaSurfaceScript antennaSurfaceScript) : base(antennaSurfaceScript)
        {
            
        }

        public override void Collect(GridLogic grid, List<AntennaEntry> entries)
        {
            var beacons = grid.GetBeacons();

            for (int i = 0; i < beacons.Count; i++)
            {
                var beacon = beacons[i];

                if(!IsValid(beacon))
                    continue;
                
                entries.Add(new AntennaEntry
                {
                    Name = GetName(beacon),
                    StatusIcon = GetStatusIcon(beacon),
                    StatusText = GetStatusText(beacon),
                    StatusColor = GetStatusColor(beacon),
                    IsFunctional = beacon.IsFunctional,
                    UseLaserIconCompensation = false
                });
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
            sb.Append(GetLocCached("BlockPropertyDescription_BroadcastRadius") + ": " +
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
