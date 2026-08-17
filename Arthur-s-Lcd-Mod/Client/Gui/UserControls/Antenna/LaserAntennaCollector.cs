using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using IMyLaserAntenna = Sandbox.ModAPI.IMyLaserAntenna;

namespace LcdMod.Client.Gui.UserControls.Antenna
{
    internal sealed class LaserAntennaCollector : AntennaCollector
    {
        readonly LinkedTypedBlockSourceSet<IMyLaserAntenna> _lasers =
            new LinkedTypedBlockSourceSet<IMyLaserAntenna>(delegate(TypedBlockCollection blocks)
            {
                return blocks.LaserAntennas;
            });
        long _statusAnimTick;

        public LaserAntennaCollector(
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
            _lasers.Bind(grid, GridLinkType);
            var sources = _lasers.Sources;
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var lasers = sources[sourceIndex];
                for (int i = 0; i < lasers.Count; i++)
                {
                    var laser = lasers[i];
                    if(!IsValid(laser))
                        continue;

                    var entry = GetOrCreateEntry(laser.EntityId, entries, models, activeEntryIds);
                    entry.Update(
                        GetName(laser),
                        GetStatusIcon(laser),
                        GetStatusText(laser),
                        GetStatusColor(laser),
                        laser.IsFunctional,
                        true);
                }
            }
        }

        public override void Dispose()
        {
            _lasers.Dispose();
        }

        string GetName(IMyLaserAntenna laser)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(laser.CustomName) ? laser.CustomName : laser.DisplayNameText;
            }
            catch
            {
                return "Laser Antenna";
            }
        }

        string GetStatusIcon(IMyLaserAntenna laserAntenna)
        {
            if (laserAntenna == null || !laserAntenna.Enabled || (AntennaConfig.SelectedBlocks.Any() && !AntennaConfig.SelectedBlocks.Contains(laserAntenna.EntityId)))
                return "GridPower";

            if (!laserAntenna.IsFunctional)
                return "Warning";

            switch (laserAntenna.Status)
            {
                case MyLaserAntennaStatus.RotatingToTarget:
                    return "RotationPlane";
                case MyLaserAntennaStatus.SearchingTargetForAntenna:
                    return "Search";
                case MyLaserAntennaStatus.Connecting:
                {
                    _statusAnimTick++;
                    if (_statusAnimTick >= 7)
                        _statusAnimTick = 0;
                    return _statusAnimTick >= 4 ? "BroadcastingOff" : "BroadcastingOn";
                }
                case MyLaserAntennaStatus.Connected:
                    return "BroadcastingOn";
                case MyLaserAntennaStatus.OutOfRange:
                    return "Disconnected";
            }

            return "BroadcastingOff";
        }

        string GetStatusText(IMyLaserAntenna laserAntenna)
        {
            if (laserAntenna == null || !laserAntenna.Enabled)
                return GetLocCached("AssemblerState_Disabled");

            if (!laserAntenna.IsFunctional)
                return GetLocCached("Damaged");

            switch (laserAntenna.Status)
            {
                case MyLaserAntennaStatus.RotatingToTarget:
                    return GetLocCached("LaserAntennaModeRotRec").TrimEnd();
                case MyLaserAntennaStatus.OutOfRange:
                case MyLaserAntennaStatus.SearchingTargetForAntenna:
                    return GetLocCached("LaserAntennaModeSearchGPS").TrimEnd();
                case MyLaserAntennaStatus.Connecting:
                    return GetLocCached("LaserAntennaModeContactRec") + GetOtherName(laserAntenna);
                case MyLaserAntennaStatus.Connected:
                {
                    var sb = new StringBuilder();
                    var other = laserAntenna.Other;
                    sb.AppendLine(GetLocCached("LaserAntennaModeConnectedTo") + GetOtherName(laserAntenna));

                    if (other == null)
                        return sb.ToString();

                    var distance = Vector3.Distance(other.GetPosition(), laserAntenna.GetPosition());
                    sb.AppendLine(FormatLabelWithColon(GetLocCached("TerminalDistance")) + " " + FormatingHelper.DistanceToString(distance));
                    sb.AppendLine(other.CubeGrid.CustomName);

                    return sb.ToString();
                }
            }

            return GetLocCached("LaserAntennaModeIdle");
        }

        Color GetStatusColor(IMyLaserAntenna laserAntenna)
        {
            if (!laserAntenna.IsFunctional)
                return WarningColor;

            if (!laserAntenna.Enabled)
                return ForegroundColor;

            switch (laserAntenna.Status)
            {
                case MyLaserAntennaStatus.Connected:
                case MyLaserAntennaStatus.Idle:
                    return ForegroundColor;
                default:
                    return WarningColor;
            }
        }

        string GetOtherName(IMyLaserAntenna laserAntenna)
        {
            var other = laserAntenna?.Other;
            if (other == null)
                return "Unknown";

            return !string.IsNullOrWhiteSpace(other.CustomName) ? other.CustomName : other.DisplayNameText;
        }
    }
}
