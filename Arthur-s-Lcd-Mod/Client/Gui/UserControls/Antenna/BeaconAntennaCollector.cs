using LcdMod.Common.Config.Components;
using System;
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
        readonly LinkedTypedBlockSourceSet<IMyBeacon> _beacons =
            new LinkedTypedBlockSourceSet<IMyBeacon>(delegate(TypedBlockCollection blocks)
            {
                return blocks.Beacons;
            });

        public BeaconCollector(
            IAppHost antennaSurfaceScript,
            Func<BlockSelectionConfigComponent> getConfig,
            Func<ColorConfigComponent> getColors)
            : base(antennaSurfaceScript, getConfig, getColors)
        {
            
        }

        public override void Collect(
            GridLogic grid,
            List<AntennaEntry> entries,
            Dictionary<long, AntennaEntry> models,
            HashSet<long> activeEntryIds)
        {
            _beacons.Bind(grid, GridLinkType);
            var sources = _beacons.Sources;
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var beacons = sources[sourceIndex];
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
        }

        public override void Dispose()
        {
            _beacons.Dispose();
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
