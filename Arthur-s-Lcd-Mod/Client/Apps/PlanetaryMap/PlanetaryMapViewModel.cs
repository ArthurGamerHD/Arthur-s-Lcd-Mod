using Acrux.Core.Input;
using Acrux.Styling;
using Adk.Mvvm;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Maps;
using LcdMod.Client.Helpers;
using Acrux.Cartography;
using Acrux.Cartography.Maps;
using LcdMod.Common.Config.Components;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps.PlanetaryMap
{
    internal interface IPlanetaryMapHost : IMapHost
    {
        PlanetaryMapConfigComponent Config { get; }
        IMyCubeBlock Block { get; }
        Color HeaderColor { get; }
        Color WarningColor { get; }
        Color ErrorColor { get; }
        Color ForegroundColor { get; }
        void SyncConfig();
    }

    [PreviewWith("IsStarMap", false)]
    [PreviewWith("IsPlanetary", true)]
    [PreviewWith("RoadsToggleVisible", true)]
    [PreviewWith("WaterVisible", true)]
    internal sealed class PlanetaryMapViewModel : CartographyMapViewModel
    {
        const double STREET_CAMERA_HEIGHT_METERS = 2.5d;
        const double GPS_SURFACE_TARGET_TOLERANCE_METERS = 10000d;
        const double GPS_SURFACE_TERRAIN_MARGIN_METERS = 5000d;
        const double GPS_SURFACE_MAXIMUM_RADIUS_RATIO = .25d;
        const double GPS_SURFACE_MINIMUM_ALLOWED_TOLERANCE_METERS = 1000d;
        const float GPS_CLUSTER_DISTANCE = 30f;
        const float RADIO_CLUSTER_DISTANCE = 30f;
        const int RADIO_REFRESH_UPDATES = 6;
        const int GPS_CREATED_STATUS_UPDATES = 18;
        const float ORBIT_EPSILON = .000001f;
        const float ZOOM_EPSILON = .0001f;
        // Opening tilt of the 3D view, from the horizon: a top-down camera would hide the relief.
        const float VIEW_3D_PITCH = MathHelper.PiOver4;

        readonly IPlanetaryMapHost _host;
        readonly Random _random = new Random();
        readonly List<IMyGps> _gpsEntries = new List<IMyGps>();
        readonly List<GpsMarkerProjection> _gpsProjections = new List<GpsMarkerProjection>();
        readonly List<GpsMarkerCluster> _gpsClusters = new List<GpsMarkerCluster>();
        readonly List<byte> _gpsConsumed = new List<byte>();
        readonly RadioSignalMarkerCollector _radioCollector = new RadioSignalMarkerCollector();
        readonly List<RadioSignalMarker> _radioSignals = new List<RadioSignalMarker>();
        readonly List<RadioSignalMarkerProjection> _radioProjections = new List<RadioSignalMarkerProjection>();
        readonly List<RadioSignalMarkerCluster> _radioClusters = new List<RadioSignalMarkerCluster>();
        readonly List<byte> _radioConsumed = new List<byte>();
        int _updates;
        bool _streetViewAvailable;
        bool _is3D;
        bool _known3D;
        bool _hasLastCreatedGps;
        int _lastCreatedGpsHash;
        string _gpsCreatedStatus;
        int _gpsCreatedStatusUntil;
        bool _applyingConfig;
        bool _knownNorthUp = true;
        bool _knownRenderSkybox = true;
        bool _knownRenderAtmosphere = true;
        bool _knownFollow = true;
        int _knownLayer;
        float _knownYaw;
        float _knownPitch;
        float[] _knownBasis;
        float _knownZoom = PLANET_MAXIMUM_ZOOM;
        bool _knownHasStatic;
        Vector3D _knownStatic;

        public PlanetaryMapViewModel(IPlanetaryMapHost host) : base(host, MapMode.PlanetaryMap)
        {
            _host = host;
            ProjectionCommand = new DelegateCommand(_ => Toggle3D());
        }

        public event Action SurfaceGpsCreated;
        public event Action CameraOrbitChanged;

        public bool StreetViewAvailable => _streetViewAvailable;
        public ICommand ProjectionCommand { get; }
        public bool Is3D => _is3D;
        [PreviewWith("Map2D Uniform")]
        public BackgroundBrush ProjectionIcon => _is3D ? Map3DIcon : Map2DIcon;
        [PreviewWith("2D map\nShow the planet as a flat projected globe.")]
        public string ProjectionTip => Tooltip(_is3D ? "View3D" : "View2D");

        static readonly BackgroundBrush Map2DIcon = new ImageBrush("Map2D") { Stretch = Stretch.Uniform };
        static readonly BackgroundBrush Map3DIcon = new ImageBrush("Map3D") { Stretch = Stretch.Uniform };
        
        [PreviewWith("Street View\nDo you really expects me to to program the entire 3D world of space engineers just to preview the street view on the editor?.")]
        public string StreetViewTip => LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_StreetView_Title") + "\n" +
                                       LocHelper.GetLoc(MOD_PREFIX + (_streetViewAvailable
                                           ? "PlanetaryMap_StreetView_Available_Tooltip"
                                           : "PlanetaryMap_StreetView_Unavailable_Tooltip"));
        public bool CanCreateSurfaceGps => ResolveFocusedPlanet() != null;

        public void Tick()
        {
            _updates++;
            ApplyConfig(false);
            Update();
            // The terrain heights load after the planet; until then the 3D mode shows the flat globe.
            if (_is3D && !State.SurfaceViewActive && Renderer != null && Renderer.EnterPlanetSurfaceView(Data, State))
                ViewChanged();
            if (RaiseAndSetIfChanged(ref _streetViewAvailable, CanUseStreetView(), nameof(StreetViewAvailable)))
                RaisePropertyChanged<string>(nameof(StreetViewTip));
        }

        public void ApplyConfig(bool force)
        {
            PlanetaryMapConfigComponent config = _host.Config;
            if (config == null)
                return;
            var staticPosition = new Vector3D(config.StaticCameraPositionX, config.StaticCameraPositionY,
                config.StaticCameraPositionZ);
            bool changed = false;
            _applyingConfig = true;
            try
            {
                if (force || config.RenderSkybox != _knownRenderSkybox)
                {
                    State.RenderSkybox = config.RenderSkybox;
                    changed = true;
                }
                if (force || config.RenderAtmosphere != _knownRenderAtmosphere)
                {
                    State.AtmosphereVisible = config.RenderAtmosphere;
                    changed = true;
                }
                if (force || config.View3D != _known3D)
                {
                    Set3D(config.View3D);
                    changed = true;
                }
                if (force || config.NorthUp != _knownNorthUp)
                {
                    State.PlanetOrientation = config.NorthUp
                        ? PlanetOrientationMode.NorthLocked
                        : PlanetOrientationMode.HeadingLocked;
                    State.HasPlanetFreeReferenceUpWorld = false;
                    changed = true;
                }
                if (force || config.MapLayer != _knownLayer)
                {
                    State.Layer = NormalizeLayer(config.MapLayer);
                    changed = true;
                }
                if (force || !SameBasis(config.OrbitBasis, _knownBasis) ||
                    !Near(config.OrbitYawRadians, _knownYaw, ORBIT_EPSILON) ||
                    !Near(config.OrbitPitchRadians, _knownPitch, ORBIT_EPSILON))
                {
                    if (!TrySetOrbitBasis(config.OrbitBasis, false))
                        SetPlanetOrbitAngles(config.OrbitYawRadians, config.OrbitPitchRadians);
                    changed = true;
                }
                if (force || !Near(config.Zoom, _knownZoom, ZOOM_EPSILON))
                {
                    State.Zoom = ClampZoom(config.Zoom);
                    changed = true;
                }
                bool staticChanged = config.HasStaticCameraPosition != _knownHasStatic ||
                                     config.HasStaticCameraPosition && Vector3D.DistanceSquared(staticPosition, _knownStatic) > 1e-6;
                if (force || config.FollowCamera != _knownFollow || staticChanged)
                {
                    State.FollowPlayer = config.FollowCamera;
                    State.HasStaticCameraPosition = !config.FollowCamera;
                    if (!config.FollowCamera)
                    {
                        if (!config.HasStaticCameraPosition)
                            Data.UpdatePlayerPosition();
                        State.StaticCameraPosition = config.HasStaticCameraPosition ? staticPosition : Data.PlayerPosition;
                    }
                    changed = true;
                }
            }
            finally
            {
                _applyingConfig = false;
            }
            Capture(config);
            if (changed)
                ViewChanged();
        }

        void Capture(PlanetaryMapConfigComponent config)
        {
            _knownNorthUp = config.NorthUp;
            _known3D = config.View3D;
            _knownRenderSkybox = config.RenderSkybox;
            _knownRenderAtmosphere = config.RenderAtmosphere;
            _knownFollow = config.FollowCamera;
            _knownLayer = config.MapLayer;
            _knownYaw = config.OrbitYawRadians;
            _knownPitch = config.OrbitPitchRadians;
            _knownBasis = config.OrbitBasis;
            _knownZoom = config.Zoom;
            _knownHasStatic = config.HasStaticCameraPosition;
            _knownStatic = new Vector3D(config.StaticCameraPositionX, config.StaticCameraPositionY,
                config.StaticCameraPositionZ);
        }

        protected override void OnViewChanged()
        {
            if (_applyingConfig)
                return;
            PlanetaryMapConfigComponent config = _host.Config;
            if (config == null)
                return;
            float yaw;
            float pitch;
            GetPlanetOrbitAngles(out yaw, out pitch);
            float[] basis = GetOrbitBasis(false);
            bool northUp = State.PlanetOrientation != PlanetOrientationMode.HeadingLocked;
            bool changed = config.View3D != _is3D || config.NorthUp != northUp || config.FollowCamera != State.FollowPlayer ||
                           config.MapLayer != (int)State.Layer || !SameBasis(config.OrbitBasis, basis) ||
                           !Near(config.Zoom, State.Zoom, ZOOM_EPSILON) ||
                           config.HasStaticCameraPosition != State.HasStaticCameraPosition;
            config.View3D = _is3D;
            config.NorthUp = northUp;
            config.FollowCamera = State.FollowPlayer;
            config.MapLayer = (int)State.Layer;
            config.OrbitBasis = basis;
            config.OrbitYawRadians = yaw;
            config.OrbitPitchRadians = pitch;
            config.Zoom = State.Zoom;
            config.HasStaticCameraPosition = State.HasStaticCameraPosition;
            if (State.HasStaticCameraPosition)
            {
                changed |= Vector3D.DistanceSquared(State.StaticCameraPosition,
                    new Vector3D(config.StaticCameraPositionX, config.StaticCameraPositionY, config.StaticCameraPositionZ)) > 1e-6;
                config.StaticCameraPositionX = State.StaticCameraPosition.X;
                config.StaticCameraPositionY = State.StaticCameraPosition.Y;
                config.StaticCameraPositionZ = State.StaticCameraPosition.Z;
            }
            Capture(config);
            if (changed)
                _host.SyncConfig();
            CameraOrbitChanged?.Invoke();
        }

        // The user switched the map between 2D and 3D, or dropped into Street View (FTUE tips complete on them).
        public event Action ProjectionToggled;
        public event Action StreetViewEntered;

        void Toggle3D()
        {
            Set3D(!_is3D);
            ViewChanged();
            _host.PlayClick();
            ProjectionToggled?.Invoke();
        }

        void Set3D(bool value)
        {
            if (!RaiseAndSetIfChanged(ref _is3D, value, nameof(Is3D)))
                return;
            State.SurfaceViewOnly = value;
            if (value)
            {
                State.SurfaceViewYaw = 0f;
                State.SurfaceViewPitch = VIEW_3D_PITCH;
            }
            else if (Renderer != null)
                Renderer.ExitPlanetSurfaceView(State);
            else
                State.SurfaceViewActive = false;
            RaisePropertyChanged<BackgroundBrush>(nameof(ProjectionIcon));
            RaisePropertyChanged<string>(nameof(ProjectionTip));
        }

        protected override bool TryGetStatus(out string status, out Color color)
        {
            color = _host.ForegroundColor;
            status = null;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null)
            {
                status = LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_NoPlanet");
                return true;
            }
            if (!string.IsNullOrWhiteSpace(RendererError))
            {
                status = LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_CartographyFailed");
                color = _host.ErrorColor;
                return true;
            }
            if (!string.IsNullOrEmpty(_gpsCreatedStatus) && _updates <= _gpsCreatedStatusUntil)
            {
                status = _gpsCreatedStatus;
                return true;
            }
            return false;
        }

        protected override void OnClick(Vector2 pixel)
        {
            Vector3D surface;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null || planet.Planet == null || planet.Planet.MarkedForClose || !TryGetSurfacePosition(pixel, out surface))
                return;
            var gpsCollection = MyAPIGateway.Session.GPS;
            if (gpsCollection == null)
                return;
            IMyGps gps = gpsCollection.Create(FormatSurfaceGpsName(LocalDirection(planet.Planet, surface - planet.Position)),
                string.Empty, surface, false, true);
            if (gps == null)
                return;
            gpsCollection.AddLocalGps(gps);
            _lastCreatedGpsHash = gps.Hash;
            _hasLastCreatedGps = true;
            ShowGpsCreated(gps.Name);
            _host.PlayGps();
            SurfaceGpsCreated?.Invoke();
        }

        protected override void OnMiddleClick(Vector2 pixel)
        {
            Vector3D surface;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null || planet.Planet == null || !TryGetSurfacePosition(pixel, out surface))
                return;
            Vector3D direction = surface - planet.Position;
            if (direction.Normalize() <= 1e-9)
                return;
            Data.UpdatePlayerPosition();
            double distance = Math.Max(Vector3D.Distance(Data.PlayerPosition, planet.Position),
                Math.Max(planet.Planet.AverageRadius, planet.Planet.MaximumRadius));
            State.StaticCameraPosition = planet.Position + direction * distance;
            State.HasStaticCameraPosition = true;
            State.FollowPlayer = false;
            ResetPlanetOrbit();
            _host.PlayClick();
            ViewChanged();
        }

        protected override void OnMarkerClicked(MapMarker marker)
        {
            var gpsCollection = MyAPIGateway.Session.GPS;
            if (gpsCollection == null || marker.Kind != MapMarkerKind.Radio)
                return;
            IMyGps gps = gpsCollection.Create(LocalGpsName(marker.Name), string.Empty, marker.WorldPosition, false, true);
            if (gps == null)
                return;
            gps.GPSColor = marker.Color;
            gpsCollection.AddLocalGps(gps);
            ShowGpsCreated(gps.Name);
            _host.PlayGps();
        }

        protected override void OnStreetView(Vector2 pixel)
        {
            Vector3D surface;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (!CanUseStreetView() || planet == null || !TryGetSurfacePosition(pixel, out surface))
                return;
            Vector3D up = surface - planet.Position;
            if (up.Normalize() <= 1e-9)
                return;
            Vector3D forward = RandomHorizonDirection(up);
            if (forward.Normalize() <= 1e-9)
                return;
            Vector3D cameraPosition = surface + up * STREET_CAMERA_HEIGHT_METERS;
            MatrixD view = MatrixD.Invert(MatrixD.CreateWorld(cameraPosition, forward, up));
            var session = MyAPIGateway.Session;
            session.SetCameraController(MyCameraControllerEnum.Spectator, null, cameraPosition);
            var spectator = session.CameraController as MySpectatorCameraController;
            if (spectator == null)
                return;
            spectator.SpectatorCameraMovement = MySpectatorCameraMovementEnum.UserControlled;
            spectator.SetViewMatrix((Matrix)view);
            ShowStreetViewNotification();
            StreetViewEntered?.Invoke();
        }

        static void ShowStreetViewNotification()
        {
            var utilities = MyAPIGateway.Utilities;
            IMyHudNotification notification = utilities.CreateNotification(
                LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_StreetView_ActiveNotification"), 500);
            notification.Show();
            Action caller = null;
            caller = delegate
            {
                if (!(MyAPIGateway.Session.CameraController is MySpectatorCameraController))
                {
                    notification.Hide();
                    return;
                }
                notification.AliveTime = 64;
                notification.ResetAliveTime();
                notification.Show();
                LcdModClientComponent.RunNextFrame.Add(caller);
            };
            LcdModClientComponent.RunNextFrame.Add(caller);
        }

        Vector3D RandomHorizonDirection(Vector3D up)
        {
            Vector3D reference = Math.Abs(Vector3D.Dot(up, Vector3D.Up)) < .95d ? Vector3D.Up : Vector3D.Right;
            Vector3D tangent = Vector3D.Cross(reference, up);
            if (tangent.Normalize() <= 1e-9)
                return Vector3D.Zero;
            Vector3D bitangent = Vector3D.Cross(up, tangent);
            if (bitangent.Normalize() <= 1e-9)
                return Vector3D.Zero;
            double angle = _random.NextDouble() * MathHelper.TwoPi;
            return tangent * Math.Cos(angle) + bitangent * Math.Sin(angle);
        }

        bool CanUseStreetView()
        {
            if (ResolveFocusedPlanet() == null)
                return false;
            var session = MyAPIGateway.Session;
            if (session.SessionSettings != null && session.SessionSettings.EnableSpectator)
                return true;
            var player = session.Player;
            ulong steamUserId = player == null ? 0UL : player.SteamUserId;
            return steamUserId != 0UL && session.IsUserAdmin(steamUserId) && (session.CreativeMode || session.HasCreativeRights);
        }

        protected override void CollectMarkers(List<MapMarker> markers)
        {
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null || planet.Planet == null || planet.Planet.MarkedForClose)
                return;
            CollectRadioSignals(markers);
            CollectGps(markers, planet.Planet);
            Vector2 pixel;
            float rotation;
            if (TryGetPlayerOverlay(out pixel, out rotation))
                markers.Add(new MapMarker
                {
                    Kind = MapMarkerKind.Player,
                    Position = pixel,
                    Rotation = rotation,
                    Icon = "GpsArrow",
                    Color = Color.White
                });
        }

        void CollectGps(List<MapMarker> markers, MyPlanet planet)
        {
            PlanetaryMapConfigComponent config = _host.Config;
            if (config == null)
                return;
            GpsDisplayWaypoint[] always = config.AlwaysDisplayedGpsWaypoints ?? Array.Empty<GpsDisplayWaypoint>();
            int[] legacy = config.AlwaysDisplayedGpsHashes ?? Array.Empty<int>();
            bool needsLive = config.DisplayMyGps || legacy.Length != 0 || _hasLastCreatedGps;
            if (!needsLive && always.Length == 0)
                return;

            _gpsProjections.Clear();
            var session = MyAPIGateway.Session;
            if (needsLive && session.GPS != null && session.Player != null)
            {
                _gpsEntries.Clear();
                session.GPS.GetGpsList(session.Player.IdentityId, _gpsEntries);
                for (int i = 0; i < _gpsEntries.Count; i++)
                {
                    IMyGps gps = _gpsEntries[i];
                    bool lastCreated = _hasLastCreatedGps && gps != null && gps.Hash == _lastCreatedGpsHash &&
                                       !GpsMarkerLayout.ContainsWaypointSourceHash(always, gps.Hash);
                    if (!lastCreated && !GpsMarkerLayout.ShouldRenderLiveGps(gps, config.DisplayMyGps, always, legacy))
                        continue;
                    AddGpsProjection(GpsMarkerLayout.FromGps(gps), planet);
                }
            }
            for (int i = 0; i < always.Length; i++)
            {
                GpsMarker marker;
                if (GpsMarkerLayout.TryCreateMarker(always[i], out marker))
                    AddGpsProjection(marker, planet);
            }

            GpsMarkerLayout.Cluster(_gpsProjections, GPS_CLUSTER_DISTANCE, _gpsClusters, _gpsConsumed);
            for (int i = 0; i < _gpsClusters.Count; i++)
            {
                GpsMarkerCluster cluster = _gpsClusters[i];
                GpsMarker marker = cluster.RepresentativeMarker;
                Color color = marker.Color;
                color.A = byte.MaxValue;
                bool isCluster = cluster.Count > 1;
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
                        : string.IsNullOrWhiteSpace(marker.Name) ? LocHelper.GetLoc(MOD_PREFIX + "Gps_Unnamed") : marker.Name,
                    Name = marker.Name,
                    WorldPosition = marker.WorldPosition
                });
            }
        }

        void AddGpsProjection(GpsMarker marker, MyPlanet planet)
        {
            Vector2 pixel;
            if (!IsNearSurface(planet, marker.WorldPosition) || !TryProjectSurface(marker.WorldPosition, out pixel) ||
                !IsInsideCanvas(pixel, 32f))
                return;
            _gpsProjections.Add(new GpsMarkerProjection { Marker = marker, ScreenPosition = pixel });
        }

        void CollectRadioSignals(List<MapMarker> markers)
        {
            PlanetaryMapConfigComponent config = _host.Config;
            if (config == null || !config.IncludeRadioSignals)
                return;
            if (_updates % RADIO_REFRESH_UPDATES == 1 || _radioSignals.Count == 0)
                _radioCollector.Collect(_host.Block, _radioSignals);

            _radioProjections.Clear();
            for (int i = 0; i < _radioSignals.Count; i++)
            {
                Vector2 pixel;
                if (TryProjectSurface(_radioSignals[i].WorldPosition, out pixel) && IsInsideCanvas(pixel, 32f))
                    _radioProjections.Add(new RadioSignalMarkerProjection { Marker = _radioSignals[i], ScreenPosition = pixel });
            }
            RadioSignalMarkerLayout.Cluster(_radioProjections, RADIO_CLUSTER_DISTANCE, _radioClusters, _radioConsumed);
            for (int i = 0; i < _radioClusters.Count; i++)
            {
                RadioSignalMarkerCluster cluster = _radioClusters[i];
                RadioSignalMarker marker = cluster.RepresentativeMarker;
                bool isCluster = cluster.Count > 1;
                Color color = RadioColor(marker.Relationship);
                color.A = byte.MaxValue;
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
            switch (relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return _host.ErrorColor;
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    return _host.HeaderColor;
                default:
                    return _host.WarningColor;
            }
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

        static bool IsNearSurface(MyPlanet planet, Vector3D position)
        {
            double average = planet.AverageRadius > 0d ? planet.AverageRadius : planet.MaximumRadius;
            if (average <= 0d)
                return false;
            double maximum = planet.MaximumRadius > 0d ? planet.MaximumRadius : average;
            double desired = Math.Max(GPS_SURFACE_TARGET_TOLERANCE_METERS, Math.Abs(maximum - average) + GPS_SURFACE_TERRAIN_MARGIN_METERS);
            double tolerance = Math.Max(GPS_SURFACE_MINIMUM_ALLOWED_TOLERANCE_METERS,
                Math.Min(desired, average * GPS_SURFACE_MAXIMUM_RADIUS_RATIO));
            return Math.Abs(Vector3D.Distance(position, planet.WorldMatrix.Translation) - average) <= tolerance;
        }

        void ShowGpsCreated(string name)
        {
            _gpsCreatedStatus = string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "Gps_CreatedFormat"),
                LocalGpsName(name));
            _gpsCreatedStatusUntil = _updates + GPS_CREATED_STATUS_UPDATES;
            ViewChanged();
        }

        static string LocalGpsName(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? LocHelper.GetLoc(MOD_PREFIX + "Gps_Unknown_Name") : name;
        }

        static Vector3 LocalDirection(MyPlanet planet, Vector3D worldDirection)
        {
            return new Vector3(
                (float)Vector3D.Dot(worldDirection, planet.WorldMatrix.Right),
                (float)Vector3D.Dot(worldDirection, planet.WorldMatrix.Up),
                (float)Vector3D.Dot(worldDirection, planet.WorldMatrix.Backward));
        }

        static string FormatSurfaceGpsName(Vector3 localDirection)
        {
            double latitude = 0d;
            double longitude = 0d;
            if (localDirection.Normalize() > 1e-6f)
            {
                // North is planet-local -Y (CartographyMapRenderer.PlanetLocalNorth); with north there, east at the
                // +Z meridian is -X, so longitude grows towards -X.
                latitude = Math.Asin(MathHelper.Clamp(-localDirection.Y, -1f, 1f)) * 180d / Math.PI;
                longitude = Math.Atan2(-localDirection.X, localDirection.Z) * 180d / Math.PI;
            }
            return string.Format(
                FormatingHelper.Culture,
                LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_CoordinateFormat"),
                LocHelper.GetLoc(MOD_PREFIX + (latitude < 0d ? "PlanetaryMap_CardinalSouthShort" : "PlanetaryMap_CardinalNorthShort")),
                Math.Abs(latitude),
                LocHelper.GetLoc(MOD_PREFIX + (longitude < 0d ? "PlanetaryMap_CardinalWestShort" : "PlanetaryMap_CardinalEastShort")),
                Math.Abs(longitude));
        }

        static CartographyLayer NormalizeLayer(int value)
        {
            return value >= (int)CartographyLayer.Satellite && value <= (int)CartographyLayer.Biomes
                ? (CartographyLayer)value
                : CartographyLayer.Satellite;
        }

        static float ClampZoom(float zoom)
        {
            return float.IsNaN(zoom) || float.IsInfinity(zoom)
                ? PLANET_MAXIMUM_ZOOM
                : MathHelper.Clamp(zoom, PLANET_MINIMUM_ZOOM, PLANET_MAXIMUM_ZOOM);
        }

        static bool Near(float left, float right, float epsilon)
        {
            return Math.Abs(left - right) <= epsilon;
        }
    }
}
