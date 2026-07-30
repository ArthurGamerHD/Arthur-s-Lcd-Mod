using System;
using System.Collections.Generic;
using System.Globalization;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.Terminal.Controls.Gps
{
    internal static class GpsDisplayConfigHelper
    {
        public static IGpsDisplayConfig GetConfig(IMyTerminalBlock block)
        {
            var surface = ConfigManager.GetSurfaceConfigForCurrentScreen(block);
            return GetConfig(surface);
        }

        public static bool IsStaticMode(IMyTerminalBlock block)
        {
            var surface = ConfigManager.GetSurfaceConfigForCurrentScreen(block);
            if (surface == null)
                return false;

            var starMap = surface.TryGet<StarMapConfigComponent>(Constants.APP);
            if (starMap != null)
            {
                var general = surface.TryGet<GeneralConfigComponent>(Constants.GENERAL);
                return general != null && general.DisplayMode == (int)DisplayMode.Legacy;
            }

            var planetaryMap = surface.TryGet<PlanetaryMapConfigComponent>(Constants.APP);
            return planetaryMap != null;
        }

        public static bool Modify(IMyTerminalBlock block, Action<IGpsDisplayConfig> action)
        {
            if (block == null || action == null)
                return false;

            var provider = ConfigManager.GetConfigForBlock(block);
            if (provider == null || !provider.CanWrite)
                return false;

            var surface = provider.GetSurfaceConfig(ConfigManager.GetThisSurfaceIndex(block));
            if (!provider.CanWriteConfig(surface))
                return false;

            var config = GetConfig(surface);
            if (config == null)
                return false;

            action(config);
            ConfigManager.Sync(block, provider);
            return true;
        }

        public static void GetLocalGps(List<IMyGps> gpsEntries)
        {
            if (gpsEntries == null)
                return;

            gpsEntries.Clear();
            var session = MyAPIGateway.Session;
            var player = session == null ? null : session.Player;
            if (session == null || session.GPS == null || player == null)
                return;

            session.GPS.GetGpsList(player.IdentityId, gpsEntries);
        }

        public static GpsDisplayWaypoint CreateWaypoint(IMyGps gps)
        {
            if (gps == null)
                return null;

            Vector3D position = gps.Coords;
            return new GpsDisplayWaypoint
            {
                SourceHash = gps.Hash,
                Name = gps.Name ?? string.Empty,
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                Color = gps.GPSColor
            };
        }

        public static bool ContainsAlwaysDisplayedGps(IGpsDisplayConfig config, int gpsHash)
        {
            if (config == null)
                return false;

            if (ContainsWaypointSourceHash(config.AlwaysDisplayedGpsWaypoints, gpsHash))
                return true;

            return ContainsGpsHash(config.AlwaysDisplayedGpsHashes, gpsHash);
        }

        public static string GetWaypointDisplayName(GpsDisplayWaypoint waypoint)
        {
            return waypoint == null || string.IsNullOrWhiteSpace(waypoint.Name)
                ? "GPS"
                : waypoint.Name;
        }

        public static string GetWaypointTooltip(GpsDisplayWaypoint waypoint)
        {
            if (waypoint == null)
                return string.Empty;

            return string.Format(
                CultureInfo.InvariantCulture,
                "GPS:{0}:{1}:{2}:{3}:{4}:",
                GetWaypointDisplayName(waypoint),
                waypoint.X,
                waypoint.Y,
                waypoint.Z,
                waypoint.Color.ToAHex());
        }

        public static string GetWaypointKey(GpsDisplayWaypoint waypoint)
        {
            if (waypoint == null)
                return string.Empty;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2:R}|{3:R}|{4:R}|{5:X8}",
                waypoint.SourceHash,
                waypoint.Name ?? string.Empty,
                waypoint.X,
                waypoint.Y,
                waypoint.Z,
                waypoint.Color.PackedValue);
        }

        public static void AddAlwaysDisplayedWaypoints(
            IGpsDisplayConfig config,
            IEnumerable<GpsDisplayWaypoint> additions)
        {
            if (config == null || additions == null)
                return;

            var waypoints = new List<GpsDisplayWaypoint>();
            var existing = config.AlwaysDisplayedGpsWaypoints ?? Array.Empty<GpsDisplayWaypoint>();
            foreach (var waypoint in existing)
                AddUniqueWaypoint(waypoints, waypoint);

            foreach (var waypoint in additions)
                AddUniqueWaypoint(waypoints, waypoint);

            config.AlwaysDisplayedGpsWaypoints = waypoints.ToArray();

            var legacyHashes = config.AlwaysDisplayedGpsHashes ?? Array.Empty<int>();
            if (legacyHashes.Length == 0)
                return;

            var retainedLegacyHashes = new List<int>(legacyHashes.Length);
            for (var i = 0; i < legacyHashes.Length; i++)
            {
                var hash = legacyHashes[i];
                if (!ContainsWaypointSourceHash(config.AlwaysDisplayedGpsWaypoints, hash) &&
                    !ContainsGpsHash(retainedLegacyHashes, hash))
                {
                    retainedLegacyHashes.Add(hash);
                }
            }

            config.AlwaysDisplayedGpsHashes = retainedLegacyHashes.ToArray();
        }

        public static void RemoveAlwaysDisplayedGps(
            IGpsDisplayConfig config,
            HashSet<string> waypointKeys,
            HashSet<int> gpsHashes)
        {
            if (config == null)
                return;

            var waypoints = config.AlwaysDisplayedGpsWaypoints ?? Array.Empty<GpsDisplayWaypoint>();
            var retainedWaypoints = new List<GpsDisplayWaypoint>(waypoints.Length);
            for (var i = 0; i < waypoints.Length; i++)
            {
                var waypoint = waypoints[i];
                if (waypoint == null)
                    continue;

                var removeByKey = waypointKeys != null && waypointKeys.Contains(GetWaypointKey(waypoint));
                var removeByHash = gpsHashes != null &&
                                   waypoint.SourceHash != 0 &&
                                   gpsHashes.Contains(waypoint.SourceHash);
                if (!removeByKey && !removeByHash)
                    retainedWaypoints.Add(waypoint);
            }

            config.AlwaysDisplayedGpsWaypoints = retainedWaypoints.ToArray();

            var hashes = config.AlwaysDisplayedGpsHashes ?? Array.Empty<int>();
            var retainedHashes = new List<int>(hashes.Length);
            for (var i = 0; i < hashes.Length; i++)
            {
                var hash = hashes[i];
                if ((gpsHashes == null || !gpsHashes.Contains(hash)) &&
                    !ContainsGpsHash(retainedHashes, hash))
                {
                    retainedHashes.Add(hash);
                }
            }

            config.AlwaysDisplayedGpsHashes = retainedHashes.ToArray();
        }

        static void AddUniqueWaypoint(List<GpsDisplayWaypoint> waypoints, GpsDisplayWaypoint waypoint)
        {
            if (waypoints == null || waypoint == null)
                return;

            var key = GetWaypointKey(waypoint);
            for (var i = 0; i < waypoints.Count; i++)
            {
                if (GetWaypointKey(waypoints[i]) == key)
                    return;
            }

            waypoints.Add(waypoint.Clone());
        }

        public static bool ContainsWaypointSourceHash(GpsDisplayWaypoint[] waypoints, int sourceHash)
        {
            if (waypoints == null || sourceHash == 0)
                return false;

            for (var i = 0; i < waypoints.Length; i++)
            {
                var waypoint = waypoints[i];
                if (waypoint != null && waypoint.SourceHash == sourceHash)
                    return true;
            }

            return false;
        }

        static bool ContainsGpsHash(IEnumerable<int> hashes, int hash)
        {
            if (hashes == null)
                return false;

            foreach (var candidate in hashes)
            {
                if (candidate == hash)
                    return true;
            }

            return false;
        }

        static IGpsDisplayConfig GetConfig(SurfaceConfig surface)
        {
            if (surface == null)
                return null;

            var starMap = surface.TryGet<StarMapConfigComponent>(Constants.APP);
            if (starMap != null)
                return starMap;

            return surface.TryGet<PlanetaryMapConfigComponent>(Constants.APP);
        }
    }
}
