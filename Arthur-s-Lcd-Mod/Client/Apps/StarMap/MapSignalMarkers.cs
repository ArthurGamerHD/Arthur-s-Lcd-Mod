using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Maps;
using LcdMod.Client.Helpers;
using Acrux.Cartography.Maps;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps.StarMap
{
    internal interface IMarkerColors
    {
        Color HeaderColor { get; }
        Color WarningColor { get; }
        Color ErrorColor { get; }
    }

    // The GPS waypoints and radio signals a star map camera draws, shared by the Star Map and External Sensors.
    internal sealed class MapSignalMarkers
    {
        const int RADIO_REFRESH_UPDATES = 6;
        const float CLUSTER_DISTANCE = 30f;

        // A world position on the screen, inside the canvas with room for the icon.
        public delegate bool Projector(Vector3D world, out Vector2 pixel);

        readonly IMarkerColors _colors;
        readonly List<IMyGps> _gpsEntries = new List<IMyGps>();
        readonly List<GpsMarkerProjection> _gpsProjections = new List<GpsMarkerProjection>();
        readonly List<GpsMarkerCluster> _gpsClusters = new List<GpsMarkerCluster>();
        readonly List<byte> _gpsConsumed = new List<byte>();
        readonly RadioSignalMarkerCollector _radioCollector = new RadioSignalMarkerCollector();
        readonly List<RadioSignalMarker> _radioSignals = new List<RadioSignalMarker>();
        readonly List<RadioSignalMarkerProjection> _radioProjections = new List<RadioSignalMarkerProjection>();
        readonly List<RadioSignalMarkerCluster> _radioClusters = new List<RadioSignalMarkerCluster>();
        readonly List<byte> _radioConsumed = new List<byte>();
        long _radioRuns;

        public MapSignalMarkers(IMarkerColors colors)
        {
            _colors = colors;
        }

        public void CollectGps(List<MapMarker> markers, StarMapConfigComponent config, Projector project)
        {
            var session = MyAPIGateway.Session;
            if (config == null)
                return;
            GpsDisplayWaypoint[] always = config.AlwaysDisplayedGpsWaypoints ?? Array.Empty<GpsDisplayWaypoint>();
            int[] legacy = config.AlwaysDisplayedGpsHashes ?? Array.Empty<int>();
            _gpsProjections.Clear();
            if ((config.DisplayMyGps || legacy.Length != 0) && session.GPS != null && session.Player != null)
            {
                _gpsEntries.Clear();
                session.GPS.GetGpsList(session.Player.IdentityId, _gpsEntries);
                for (int i = 0; i < _gpsEntries.Count; i++)
                    if (GpsMarkerLayout.ShouldRenderLiveGps(_gpsEntries[i], config.DisplayMyGps, always, legacy))
                        AddGps(GpsMarkerLayout.FromGps(_gpsEntries[i]), project);
            }
            for (int i = 0; i < always.Length; i++)
            {
                GpsMarker marker;
                if (GpsMarkerLayout.TryCreateMarker(always[i], out marker))
                    AddGps(marker, project);
            }
            GpsMarkerLayout.Cluster(_gpsProjections, CLUSTER_DISTANCE, _gpsClusters, _gpsConsumed);
            for (int i = 0; i < _gpsClusters.Count; i++)
            {
                GpsMarkerCluster cluster = _gpsClusters[i];
                bool isCluster = cluster.Count > 1;
                Color color = cluster.RepresentativeMarker.Color;
                color.A = byte.MaxValue;
                markers.Add(new MapMarker
                {
                    Kind = MapMarkerKind.Gps,
                    Position = cluster.ScreenPosition,
                    Icon = "WaypointIcon",
                    Color = color,
                    Size = isCluster ? 1.35f : 1.4f,
                    CountText = isCluster ? cluster.Count.ToString() : null,
                    Label = isCluster
                        ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "Gps_ClusterFormat"), cluster.Count)
                        : string.IsNullOrWhiteSpace(cluster.RepresentativeMarker.Name)
                            ? LocHelper.GetLoc(MOD_PREFIX + "Gps_Unnamed")
                            : cluster.RepresentativeMarker.Name
                });
            }
        }

        void AddGps(GpsMarker marker, Projector project)
        {
            Vector2 pixel;
            if (project(marker.WorldPosition, out pixel))
                _gpsProjections.Add(new GpsMarkerProjection { Marker = marker, ScreenPosition = pixel });
        }

        public void CollectRadio(List<MapMarker> markers, StarMapConfigComponent config, IMyCubeBlock block,
            Projector project)
        {
            if (config == null || !config.IncludeRadioSignals)
                return;
            if (++_radioRuns % RADIO_REFRESH_UPDATES == 1 || _radioSignals.Count == 0)
                _radioCollector.Collect(block, _radioSignals);
            _radioProjections.Clear();
            for (int i = 0; i < _radioSignals.Count; i++)
            {
                Vector2 pixel;
                if (project(_radioSignals[i].WorldPosition, out pixel))
                    _radioProjections.Add(new RadioSignalMarkerProjection { Marker = _radioSignals[i], ScreenPosition = pixel });
            }
            RadioSignalMarkerLayout.Cluster(_radioProjections, CLUSTER_DISTANCE, _radioClusters, _radioConsumed);
            for (int i = 0; i < _radioClusters.Count; i++)
            {
                RadioSignalMarkerCluster cluster = _radioClusters[i];
                RadioSignalMarker marker = cluster.RepresentativeMarker;
                bool isCluster = cluster.Count > 1;
                Color color = RadioColor(marker.Relationship);
                markers.Add(new MapMarker
                {
                    Kind = MapMarkerKind.Radio,
                    Position = cluster.ScreenPosition,
                    Icon = isCluster ? "CircleHollow" : RadioTexture(marker.Relationship),
                    Color = color,
                    Size = isCluster ? 1.35f : 1f,
                    CountText = isCluster ? cluster.Count.ToString() : null,
                    Label = isCluster
                        ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "RadioSignal_ClusterFormat"), cluster.Count)
                        : string.IsNullOrWhiteSpace(marker.Name) ? LocHelper.GetLoc(MOD_PREFIX + "RadioSignal_Unnamed") : marker.Name,
                    Name = marker.Name,
                    WorldPosition = marker.WorldPosition,
                    Clickable = !isCluster
                });
            }
        }

        Color RadioColor(MyRelationsBetweenPlayerAndBlock relationship)
        {
            Color color;
            switch (relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    color = _colors.ErrorColor;
                    break;
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    color = _colors.HeaderColor;
                    break;
                default:
                    color = _colors.WarningColor;
                    break;
            }
            color.A = byte.MaxValue;
            return color;
        }

        static string RadioTexture(MyRelationsBetweenPlayerAndBlock relationship)
        {
            switch (relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return "Circle";
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    return "SquareSimple";
                default:
                    return "Triangle";
            }
        }
    }
}
