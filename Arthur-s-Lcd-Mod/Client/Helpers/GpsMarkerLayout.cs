using System.Collections.Generic;
using LcdMod.Common.Config.Components;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.Helpers
{
    internal struct GpsMarker
    {
        public int SourceHash;
        public string Name;
        public Vector3D WorldPosition;
        public Color Color;
    }

    internal struct GpsMarkerProjection
    {
        public GpsMarker Marker;
        public Vector2 ScreenPosition;
    }

    internal struct GpsMarkerCluster
    {
        public GpsMarker RepresentativeMarker;
        public Vector2 ScreenPosition;
        public int Count;
    }

    internal static class GpsMarkerLayout
    {
        public static GpsMarker FromGps(IMyGps gps)
        {
            return new GpsMarker
            {
                SourceHash = gps == null ? 0 : gps.Hash,
                Name = gps == null ? string.Empty : gps.Name,
                WorldPosition = gps == null ? Vector3D.Zero : gps.Coords,
                Color = gps == null ? new Color(117, 201, 241) : gps.GPSColor
            };
        }

        public static bool TryCreateMarker(GpsDisplayWaypoint waypoint, out GpsMarker marker)
        {
            marker = default(GpsMarker);
            if (waypoint == null)
                return false;

            marker = new GpsMarker
            {
                SourceHash = waypoint.SourceHash,
                Name = waypoint.Name,
                WorldPosition = new Vector3D(waypoint.X, waypoint.Y, waypoint.Z),
                Color = waypoint.Color
            };
            return true;
        }

        public static bool ShouldRenderLiveGps(
            IMyGps gps,
            bool displayMyGps,
            GpsDisplayWaypoint[] alwaysDisplayedGpsWaypoints,
            int[] legacyAlwaysDisplayedGpsHashes)
        {
            if (gps == null)
                return false;

            if (ContainsWaypointSourceHash(alwaysDisplayedGpsWaypoints, gps.Hash))
                return false;

            bool isLegacyForced = ContainsGpsHash(legacyAlwaysDisplayedGpsHashes, gps.Hash);
            return isLegacyForced || (displayMyGps && gps.ShowOnHud);
        }

        public static void Cluster(
            IList<GpsMarkerProjection> projections,
            float maximumDistance,
            List<GpsMarkerCluster> clusters,
            List<byte> consumed)
        {
            clusters.Clear();
            consumed.Clear();

            int count = projections == null ? 0 : projections.Count;
            for (int i = 0; i < count; i++)
                consumed.Add(0);

            float maximumDistanceSquared = maximumDistance * maximumDistance;
            for (int i = 0; i < count; i++)
            {
                if (consumed[i] != 0)
                    continue;

                if (projections != null)
                {
                    GpsMarkerProjection anchor = projections[i];
                    consumed[i] = 1;
                    Vector2 positionSum = anchor.ScreenPosition;
                    int clusterCount = 1;

                    for (int j = i + 1; j < count; j++)
                    {
                        if (consumed[j] != 0)
                            continue;

                        Vector2 offset = projections[j].ScreenPosition - anchor.ScreenPosition;
                        if (offset.LengthSquared() > maximumDistanceSquared)
                            continue;

                        consumed[j] = 1;
                        positionSum += projections[j].ScreenPosition;
                        clusterCount++;
                    }

                    clusters.Add(new GpsMarkerCluster
                    {
                        RepresentativeMarker = anchor.Marker,
                        ScreenPosition = positionSum / clusterCount,
                        Count = clusterCount
                    });
                }
            }
        }

        public static bool ContainsWaypointSourceHash(GpsDisplayWaypoint[] waypoints, int sourceHash)
        {
            if (waypoints == null || sourceHash == 0)
                return false;

            for (int i = 0; i < waypoints.Length; i++)
            {
                var waypoint = waypoints[i];
                if (waypoint != null && waypoint.SourceHash == sourceHash)
                    return true;
            }

            return false;
        }

        static bool ContainsGpsHash(int[] hashes, int hash)
        {
            if (hashes == null)
                return false;

            for (int i = 0; i < hashes.Length; i++)
            {
                if (hashes[i] == hash)
                    return true;
            }

            return false;
        }
    }
}
