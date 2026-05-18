using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Grid;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using AntennaSurfaceScript = LcdMod.Client.SurfaceScripts.AntennaSurfaceScript;
using IMyLaserAntenna = Sandbox.ModAPI.IMyLaserAntenna;

namespace LcdMod.Client.Gui.Models.Antenna
{
    internal sealed class LaserAntennaCollector : AntennaCollector
    {
        long _statusAnimTick;

        public LaserAntennaCollector(SurfaceScriptBase antennaSurfaceScript) : base(antennaSurfaceScript)
        {
            
        }

        public override void Collect(GridLogic grid, List<AntennaEntry> entries)
        {
            var lasers = grid.GetLaserAntennae();

            for (int i = 0; i < lasers.Count; i++)
            {
                var laser = lasers[i];
                if(!IsValid(laser))
                    continue;

                entries.Add(new AntennaEntry
                {
                    Name = GetName(laser),
                    StatusIcon = GetStatusIcon(laser),
                    StatusText = GetStatusText(laser),
                    StatusColor = GetStatusColor(laser),
                    IsFunctional = laser.IsFunctional,
                    UseLaserIconCompensation = true
                });
            }
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
            if (laserAntenna == null || !laserAntenna.Enabled || (ScreenConfigGeneral.SelectedBlocks.Any() && !ScreenConfigGeneral.SelectedBlocks.Contains(laserAntenna.EntityId)))
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
                    sb.AppendLine(GetLocCached("TerminalDistance") + ": " + FormatingHelper.DistanceToString(distance));
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
