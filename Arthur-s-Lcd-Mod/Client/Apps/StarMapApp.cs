using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Globalization;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Custom.Camera;
using LcdMod.Client.Gui.ControlsTemplates.Custom.Planet;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Cartography;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using SliderFov = LcdMod.Client.Terminal.Controls.Generic.SliderFov;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;

namespace LcdMod.Client.Apps
{
    [LcdApp(13)]
    [ConfigComponent(APP, typeof(StarMapConfigComponent), PropertyName = "StarMapComponent")]
    public partial class StarMapApp : App, IApp
    {
        readonly IAppHost _host;
        IMyCubeBlock Block => _host.Block;
        Sandbox.ModAPI.Ingame.IMyTextSurface Surface => _host.Surface;
        RectangleF ViewBox => _host.ViewBox;
        float Scale => _host.ConfiguredScale;
        float FontScale => _host.Surface.FontSize;
        Color ForegroundColor => _host.ForegroundColor;
        Color BackgroundColor => _host.BackgroundColor;
        public override IReadOnlyList<Control> VisualChildren => _children;
        public CursorType RequestedCursorType { get; private set; } = CursorType.Default;

        float _fov;
        double _halfFovY;
        float _lastKnownConfigFov = float.NaN;
        long _lastFovChangedFrame = long.MinValue;
        bool _syncConfigNextRun;
        float _lastKnownConfigStaticOrbitYawRadians;
        float _lastKnownConfigStaticOrbitPitchRadians;
        long _lastKnownConfigStaticFocusPlanetId;
        Vector3D _lastKnownConfigStaticCameraTargetOffsetWorld;
        IMyGravityProviderSystem _gravityProvider;
        readonly EyeTrackingFrameState _eyeTracking = new EyeTrackingFrameState();

        long _jumpPointRunCounter;

        readonly List<MySprite> _baseSprites = new List<MySprite>();
        readonly List<MySprite> _groundSprites = new List<MySprite>();
        readonly List<MySprite> _groundOcclusionSprites = new List<MySprite>();
        readonly List<MySprite> _ringSprites = new List<MySprite>();
        readonly List<MySprite> _frontRingSprites = new List<MySprite>();
        readonly List<MySprite> _overlaySprites = new List<MySprite>();
        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<IMyGps> _gpsEntries = new List<IMyGps>();
        readonly List<GpsMarkerProjection> _gpsMarkerProjections =
            new List<GpsMarkerProjection>();
        readonly List<GpsMarkerCluster> _gpsMarkerClusters =
            new List<GpsMarkerCluster>();
        readonly List<byte> _gpsMarkerClusterConsumed = new List<byte>();
        readonly RadioSignalMarkerCollector _radioSignalCollector = new RadioSignalMarkerCollector();
        readonly List<RadioSignalMarker> _radioSignals = new List<RadioSignalMarker>();
        readonly List<RadioSignalMarkerProjection> _radioSignalMarkerProjections =
            new List<RadioSignalMarkerProjection>();
        readonly List<RadioSignalMarkerCluster> _radioSignalMarkerClusters =
            new List<RadioSignalMarkerCluster>();
        readonly List<byte> _radioSignalMarkerClusterConsumed = new List<byte>();
        readonly List<Control> _children = new List<Control>();
        readonly OrbitCameraControl _staticOrbitControl;
        sealed class PlanetCubemapState
        {
            public PlanetColorCubemap Cubemap;
            public CartographyTicket Ticket;
            public int RequestedFaceSide = -1;
            public int RequestVersion;
            public int RetryFaceSide = int.MinValue;
            public long RetryFrame;
        }

        sealed class PlanetInteractiveState
        {
            public PlanetProjection Projection;
            public PlanetGlobeControl Entry;
            public InteractiveTooltip StaticTooltip;
            public bool UsedThisFrame;
        }

        sealed class StaticMarkerInteractiveState
        {
            public RectangleControl Entry;
            public string Name;
            public Vector3D Position;
            public Color Color;
            public bool UsedThisFrame;
        }

        readonly Dictionary<long, PlanetCubemapState> _planetCubemapStates =
            new Dictionary<long, PlanetCubemapState>();
        readonly Dictionary<long, PlanetGlobeControl> _planetGlobeControls =
            new Dictionary<long, PlanetGlobeControl>();
        CartographyModule _cartographyModule;
        bool _closed;

        // Static orbit rings are cached between renders and rebuilt whenever the
        // static camera, layout, or map data invalidates them.
        bool _planetariumOrbitCacheValid;
        readonly List<MySprite> _cachedStaticBaseSprites = new List<MySprite>();
        readonly List<MySprite> _cachedStaticTitleSprites = new List<MySprite>();
        readonly List<MySprite> _cachedStaticRingSprites = new List<MySprite>();
        readonly List<MySprite> _cachedStaticFrontRingSprites = new List<MySprite>();
        readonly List<Control> _cachedInteractiveEntries = new List<Control>();
        readonly Dictionary<long, PlanetInteractiveState> _planetInteractiveStates =
            new Dictionary<long, PlanetInteractiveState>();
        readonly List<long> _removedPlanetInteractiveIds = new List<long>();
        readonly Dictionary<long, StaticMarkerInteractiveState> _staticRadioMarkerInteractiveStates =
            new Dictionary<long, StaticMarkerInteractiveState>();

        bool _dynamicMapCacheValid;
        MatrixD _cachedDynamicWorldMatrix;
        RectangleF _cachedDynamicViewBox;
        Vector2 _cachedDynamicCursorPosition;
        Vector3D _cachedDynamicLinearVelocity;
        bool _cachedDynamicHasRecentVisualContact;
        bool _cachedDynamicSuppressOverlays;
        int _cachedDynamicPlanetCount;
        readonly List<MySprite> _cachedDynamicGroundSprites = new List<MySprite>();
        readonly List<MySprite> _cachedDynamicGroundOcclusionSprites = new List<MySprite>();
        readonly List<MySprite> _cachedDynamicRingSprites = new List<MySprite>();
        readonly List<MySprite> _cachedOverlaySprites = new List<MySprite>();

        const double JUMP_POINT_RUNS_PER_SECOND = 6d; // ScriptUpdate.Update10 at 60 FPS
        const double JUMP_POINT_DISTANCE_PER_SECOND = 1000000d; // Distance jump drive "calculates" per second

        struct JumpPointThrottleState
        {
            public long StartRun;
            public long DurationRuns;
            public long LastRequestRun;
        }

        readonly Dictionary<long, JumpPointThrottleState> _jumpPointThrottleByPlanet =
            new Dictionary<long, JumpPointThrottleState>();
        readonly Dictionary<string, string> _propertyLabelCache = new Dictionary<string, string>();
        readonly List<RectangleF> _selectedInfoKeepAliveBounds = new List<RectangleF>();
        readonly List<RectangleF> _selectedInfoBoundsThisFrame = new List<RectangleF>();

        bool _busy = true;
        long _selectedInfoPlanetId;
        long _staticFocusPlanetId;
        Vector3D _staticCameraTargetOffsetWorld;
        Vector3D _staticPanScreenRightWorld;
        Vector3D _staticPanScreenUpWorld;
        double _staticPanWorldUnitsPerPixel;
        bool _staticPanProjectionValid;
        bool _suppressDynamicOverlays;
        int _artificialHorizonLastRadarAlt;
        int _artificialHorizonVerticalSpeed;
        long _artificialHorizonLastRadarAltFrame = long.MinValue;
        long _artificialHorizonLastRadarAltPlanetId;
        bool _artificialHorizonShowAltWarning;
        long _artificialHorizonAltWarningShownAt;
        Vector2 _cursorPosition = new Vector2(float.NaN, float.NaN);
        long _lastCursorVisualContactFrame = long.MinValue;
        long _lastRadioSignalRefreshFrame = long.MinValue;
        string _gpsCreatedStatus;
        long _gpsCreatedStatusUntilFrame = long.MinValue;

        public event Action StaticCameraOrbitChanged;

        public bool PlanetariumMode => GeneralComponent.DisplayMode == (int)StarMapDisplayMode.Planetarium;

        struct PlanetProjection
        {
            public long PlanetId;
            public string Name;
            public Color GpsColor;
            public Vector3D WorldPosition;
            public Vector3D Direction;
            public Vector3 ViewDirectionLocal;
            public Vector3 ScreenRightLocal;
            public Vector3 ScreenUpLocal;
            public double Distance;
            public double CameraDepth;
            public float Visibility;
            public double AngularRadius;
            public Vector2 ScreenPos;
            public float MarkerRadius;
            public bool ShouldDisplayInfo;
            public float Radius;
            public float SurfaceGravityG;
            public float GravityRange;
            public float AtmosphereDensity;
            public float OxygenDensity;
            public MyTemperatureLevel? AverageTemperature;
            public float MaxWindSpeed;
            public List<ITooltipLine> CachedInfoLines;
            public List<ITooltipLine> CachedCompactInfoLines;
        }

        struct StaticRingProjection
        {
            public long OwnerPlanetId;
            public Vector3D CenterWorld;
            public Vector3D AxisXWorld;
            public Vector3D AxisYWorld;
            public double RadiusWorld;
            public bool IsMoonRing;
            public double CameraDepth;
        }

        public const string ID = MOD_PREFIX + "StarMapSurface";
        public const string TITLE = MOD_PREFIX + "StarMapSurface";
        const float POLAR_CAP_RATIO = 0.06f; // top/bottom % of diameter
        const float EQUATOR_BAND_RATIO = 0.18f; // % of diameter
        const float SURFACE_GROUND_COLOR_TRANSITION_DEG = 2f; // soft transition between base/equator/polar surface colors
        const float MAP_VERTICAL_FOV_DEFAULT_DEG = 70f;
        const long MAGNIFICATION_HUD_VISIBLE_FRAMES = 300L;
        const float MAP_NEAR_CLIP_METERS = 10f;
        const long PLANET_CUBEMAP_RETRY_FRAMES = 600L;
        const float ARTIFICIAL_HORIZON_LINE_WIDTH_PX = 5f;
        const float ARTIFICIAL_HORIZON_ANGLE_STEP_RAD = 0.087266445f; // 5 degrees
        const float ARTIFICIAL_HORIZON_LADDER_TEXT_SCALE_MULTIPLIER = 0.7f;
        const int ARTIFICIAL_HORIZON_ALTITUDE_WARNING_RUN_THRESHOLD = 24;
        const long ARTIFICIAL_HORIZON_ALTITUDE_DELTA_SAMPLE_FRAMES = 60L;
        const float ARTIFICIAL_HORIZON_VELOCITY_DOT_THRESHOLD = -0.1f;
        const float ARTIFICIAL_HORIZON_HUD_SCALING = 1200f;
        const float SURFACE_GROUND_SCALE_BOOST_START_RATIO = 0.8f; // normalized current gravity / planet surface gravity
        const float SURFACE_GROUND_SPACE_PLANET_FADE_START_RATIO = 0.5f; // start fading normal planet marker before terrain expansion begins
        const float SURFACE_GROUND_SPACE_PLANET_FADE_END_RATIO = 0.8f; // finish fading the normal marker before scale boost starts
        const float SURFACE_GROUND_GEOMETRY_TRANSITION_START_RATIO = 0.5f; // start easing projected ground disk toward surface placement
        const float SURFACE_GROUND_GEOMETRY_TRANSITION_END_RATIO = 0.9f; // finish settling terrain before rectangle blending begins
        const float SURFACE_GROUND_RECTANGLE_TRANSITION_START_RATIO = 0.9f; // start blending the surface circle toward rectangle terrain
        const float SURFACE_GROUND_RECTANGLE_TRANSITION_END_RATIO = 1f; // finish closing the circle-to-horizon gap at full surface gravity
        const float SURFACE_GROUND_MAX_SCALE_BOOST = 10f;
        const float SIDE_INFO_TEXT_SCALE = 0.53f;
        const float SIDE_INFO_MARGIN_PX = 14f;
        const float SIDE_INFO_Y_OFFSET_PX = 6f;
        const float STATIC_PLANET_SCALE = 4f;
        const float STATIC_REAL_SCALE_BLEND_START_MAGNIFICATION = 5f;
        const float STATIC_MAX_MAGNIFICATION = 240f;
        const float STATIC_PLANET_BODY_RADIUS_PX = 10f;
        const float STATIC_MOON_BODY_RADIUS_PX = 5f;
        public static float StaticOrbitLineThicknessPx = 2f;
        const float STATIC_ORBIT_CONFIG_EPSILON = 0.000001f;
        const double STATIC_CAMERA_TARGET_CONFIG_EPSILON_METERS = 0.001d;
        const double STATIC_CAMERA_NEAR_CLIP_DEPTH = 0d;
        const double STATIC_ORBIT_MIN_RING_METERS = 100000d;
        const double STATIC_PARENT_ORBIT_MAX_METERS = 300000d;
        const double STATIC_GPS_MOON_LOCAL_RANGE_METERS = 100000d;
        const float STATIC_GPS_MARKER_SIZE_PX = 12f;
        const float STATIC_GPS_LABEL_SCALE = 0.5f;
        const float STATIC_GPS_LABEL_GAP_PX = 5f;
        const float STATIC_GPS_CLUSTER_DISTANCE_PX = 30f;
        const float STATIC_MARKER_HITBOX_MIN_PX = 18f;
        const long STATIC_RADIO_SIGNAL_REFRESH_FRAMES = 60L;
        const float STATIC_RADIO_SIGNAL_MARKER_SIZE_PX = 14f;
        const float STATIC_RADIO_SIGNAL_LABEL_SCALE = 0.5f;
        const float STATIC_RADIO_SIGNAL_LABEL_GAP_PX = 5f;
        const float STATIC_RADIO_SIGNAL_CLUSTER_DISTANCE_PX = 30f;
        const long GPS_CREATED_STATUS_FRAMES = 180L;

        public static readonly List<MyTerminalControlComboBoxItem> StarMapDisplayModes =
            new List<MyTerminalControlComboBoxItem>
            {
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)StarMapDisplayMode.Planetarium,
                    Value = VRage.Utils.MyStringId.GetOrCompute("LcdMod_Planetarium")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)StarMapDisplayMode.Hud,
                    Value = VRage.Utils.MyStringId.GetOrCompute("LcdMod_Hud")
                }
            };

        public StarMapApp(IAppHost host)
            : base(host)
        {
            _host = host;
            _staticOrbitControl = AddLogicalChild(
                new OrbitCameraControl(default(RectangleF)));
            _staticOrbitControl.CameraChanged = OnStaticOrbitCameraChanged;
            _staticOrbitControl.PrimaryDrag = OnStaticCameraMoved;
            _staticOrbitControl.ZoomStep = 1.1f;
            _staticOrbitControl.ZoomValueProvider = GetStarMapMagnification;
            _staticOrbitControl.NormalizeZoomValue = NormalizeStarMapMagnification;
            _staticOrbitControl.ZoomChanged = OnStaticZoomChanged;
            _staticOrbitControl.SetDraggable();
            _staticOrbitControl.PreservePrimaryClickUntilDragged = true;

            LocalConfigManager.TextureQualityChanged += OnTextureQualityChanged;
            EnsureCartographyEventSubscription();
            ApplyTextureQuality(LocalConfigManager.TextureQuality, false);
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes() => StarMapDisplayModes;

        public override void LayoutChanged()
        {
            _fov = GetEffectiveVerticalFovDeg();
            _halfFovY = MathHelper.ToRadians(_fov) * 0.5;
            _lastKnownConfigFov = StarMapComponent.FoV;
            InvalidateStaticOrbitCache();
            InvalidateDynamicMapCache();
            RebuildPropertyLabelCache();

            _staticOrbitControl.SetRect(ViewBox);

            RequestedCursorType = GetDefaultCursorType();
        }

        void RebuildPropertyLabelCache()
        {
            _propertyLabelCache.Clear();
            CachePropertyLabel("Radius");
            CachePropertyLabel("Gravity");
            CachePropertyLabel("Range");
            CachePropertyLabel("Atmosphere");
            CachePropertyLabel("O2");
            CachePropertyLabel("Temperature");
            CachePropertyLabel("Wind");
            CachePropertyLabel("Position");
            CachePropertyLabel("Jump");
        }

        void CachePropertyLabel(string name)
        {
            _propertyLabelCache[name] = LocHelper.GetLoc(BuildPropertyLocKey(name));
        }

        string BuildPropertyLocKey(string name) => MOD_PREFIX + "" + name + (!PlanetariumMode ? "_Short" : string.Empty);

        string FormatPropertyLine(string name, object value)
        {
            string format;
            if (!_propertyLabelCache.TryGetValue(name, out format))
            {
                format = LocHelper.GetLoc(BuildPropertyLocKey(name));
                _propertyLabelCache[name] = format;
            }

            return string.Format(FormatingHelper.Culture, format, value);
        }

        CursorType GetDefaultCursorType() => PlanetariumMode ? CursorType.Default : CursorType.None;


        void ClearCachedInteractiveEntries()
        {
            _cachedInteractiveEntries.Clear();
        }

        void InvalidateStaticOrbitCache()
        {
            _planetariumOrbitCacheValid = false;
            _cachedStaticBaseSprites.Clear();
            _cachedStaticTitleSprites.Clear();
            _cachedStaticFrontRingSprites.Clear();
        }

        void BeginPlanetInteractiveFrame()
        {
            foreach (PlanetInteractiveState state in _planetInteractiveStates.Values)
                state.UsedThisFrame = false;
        }

        void BeginStaticMarkerInteractiveFrame()
        {
            foreach (StaticMarkerInteractiveState state in _staticRadioMarkerInteractiveStates.Values)
                state.UsedThisFrame = false;
        }

        void FinalizePlanetInteractiveFrame()
        {
            Dictionary<long, MyPlanet> planets = PlanetHelper.PlanetsById;
            _removedPlanetInteractiveIds.Clear();
            foreach (KeyValuePair<long, PlanetInteractiveState> pair in _planetInteractiveStates)
            {
                PlanetInteractiveState state = pair.Value;
                if (!state.UsedThisFrame)
                    state.Entry.SetVisible(false);

                if (planets == null)
                    continue;

                MyPlanet planet;
                if (!planets.TryGetValue(pair.Key, out planet) ||
                    planet == null ||
                    planet.MarkedForClose)
                {
                    _removedPlanetInteractiveIds.Add(pair.Key);
                }
            }

            bool invalidatedDynamicCache = false;
            for (int i = 0; i < _removedPlanetInteractiveIds.Count; i++)
            {
                long planetId = _removedPlanetInteractiveIds[i];
                PlanetInteractiveState state;
                if (!_planetInteractiveStates.TryGetValue(planetId, out state))
                    continue;

                if (_cachedInteractiveEntries.Remove(state.Entry))
                    invalidatedDynamicCache = true;

                RemoveLogicalChild(state.Entry);
                _planetGlobeControls.Remove(planetId);
                _planetInteractiveStates.Remove(planetId);
            }

            if (invalidatedDynamicCache)
                InvalidateDynamicMapCache();

            _removedPlanetInteractiveIds.Clear();
        }

        void FinalizeStaticMarkerInteractiveFrame()
        {
            HideUnusedStaticMarkerEntries(_staticRadioMarkerInteractiveStates);
        }

        static void HideUnusedStaticMarkerEntries(
            Dictionary<long, StaticMarkerInteractiveState> states)
        {
            foreach (StaticMarkerInteractiveState state in states.Values)
            {
                if (!state.UsedThisFrame && state.Entry != null)
                    state.Entry.SetVisible(false);
            }
        }

        void InvalidateDynamicMapCache()
        {
            _dynamicMapCacheValid = false;
            _cachedDynamicGroundSprites.Clear();
            _cachedDynamicGroundOcclusionSprites.Clear();
            _cachedDynamicRingSprites.Clear();
            _cachedOverlaySprites.Clear();
            ClearCachedInteractiveEntries();
        }
        
        public override void Update()
        {
            EnsureCartographyEventSubscription();
            _jumpPointRunCounter++;

            ApplyStarMapConfig();
            SyncConfigIfNeeded();

            bool hadKnownFov = !float.IsNaN(_lastKnownConfigFov);
            if (!hadKnownFov || Math.Abs(_lastKnownConfigFov - StarMapComponent.FoV) > 0.001f)
            {
                if (hadKnownFov)
                    _lastFovChangedFrame = GetCurrentGameFrame();

                LayoutChanged();
            }
        }

        void ApplyStarMapConfig()
        {
            StarMapConfigComponent config = StarMapComponent;
            bool cameraChanged = false;

            if (!NearlyEqual(config.StaticOrbitYawRadians, _lastKnownConfigStaticOrbitYawRadians) ||
                !NearlyEqual(config.StaticOrbitPitchRadians, _lastKnownConfigStaticOrbitPitchRadians))
            {
                _staticOrbitControl.SetOrbit(
                    config.StaticOrbitYawRadians,
                    config.StaticOrbitPitchRadians,
                    false);
                cameraChanged = true;
            }

            Vector3D targetOffset = GetConfigStaticCameraTargetOffset(config);
            if (config.StaticFocusPlanetId != _lastKnownConfigStaticFocusPlanetId ||
                !NearlyEqual(targetOffset, _lastKnownConfigStaticCameraTargetOffsetWorld))
            {
                _staticFocusPlanetId = config.StaticFocusPlanetId;
                _staticCameraTargetOffsetWorld = targetOffset;
                cameraChanged = true;
            }

            CaptureStaticCameraConfigSnapshot(config);

            if (cameraChanged)
                InvalidateStaticOrbitCache();
        }

        void PersistStaticCameraConfig()
        {
            StarMapConfigComponent config = StarMapComponent;
            bool changed = false;
            float orbitYawRadians = _staticOrbitControl.YawRadians;
            float orbitPitchRadians = _staticOrbitControl.PitchRadians;

            if (!NearlyEqual(config.StaticOrbitYawRadians, orbitYawRadians))
            {
                config.StaticOrbitYawRadians = orbitYawRadians;
                changed = true;
            }

            if (!NearlyEqual(config.StaticOrbitPitchRadians, orbitPitchRadians))
            {
                config.StaticOrbitPitchRadians = orbitPitchRadians;
                changed = true;
            }

            if (config.StaticFocusPlanetId != _staticFocusPlanetId)
            {
                config.StaticFocusPlanetId = _staticFocusPlanetId;
                changed = true;
            }

            if (!NearlyEqual(GetConfigStaticCameraTargetOffset(config), _staticCameraTargetOffsetWorld))
            {
                config.StaticCameraTargetOffsetX = _staticCameraTargetOffsetWorld.X;
                config.StaticCameraTargetOffsetY = _staticCameraTargetOffsetWorld.Y;
                config.StaticCameraTargetOffsetZ = _staticCameraTargetOffsetWorld.Z;
                changed = true;
            }

            CaptureStaticCameraConfigSnapshot(config);

            if (changed)
                _syncConfigNextRun = true;
        }

        void SyncConfigIfNeeded()
        {
            if (!_syncConfigNextRun)
                return;

            _syncConfigNextRun = false;
            if (Block != null && _host.ProviderConfig != null)
                ConfigManager.Sync(Block, _host.ProviderConfig);
        }

        void CaptureStaticCameraConfigSnapshot(StarMapConfigComponent config)
        {
            _lastKnownConfigStaticOrbitYawRadians = config.StaticOrbitYawRadians;
            _lastKnownConfigStaticOrbitPitchRadians = config.StaticOrbitPitchRadians;
            _lastKnownConfigStaticFocusPlanetId = config.StaticFocusPlanetId;
            _lastKnownConfigStaticCameraTargetOffsetWorld = GetConfigStaticCameraTargetOffset(config);
        }

        static Vector3D GetConfigStaticCameraTargetOffset(StarMapConfigComponent config)
        {
            return new Vector3D(
                config.StaticCameraTargetOffsetX,
                config.StaticCameraTargetOffsetY,
                config.StaticCameraTargetOffsetZ);
        }

        static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) <= STATIC_ORBIT_CONFIG_EPSILON;
        }

        static bool NearlyEqual(Vector3D left, Vector3D right)
        {
            return Vector3D.DistanceSquared(left, right) <=
                   STATIC_CAMERA_TARGET_CONFIG_EPSILON_METERS *
                   STATIC_CAMERA_TARGET_CONFIG_EPSILON_METERS;
        }

        public override void Close()
        {
            _closed = true;
            ClearCartographyEventSubscription();
            LocalConfigManager.TextureQualityChanged -= OnTextureQualityChanged;
            _staticOrbitControl.StopCameraInertia();

            foreach (PlanetCubemapState state in _planetCubemapStates.Values)
            {
                if (state != null && state.Ticket != null)
                    state.Ticket.Cancel();
            }

            _planetCubemapStates.Clear();
            _planetGlobeControls.Clear();
            _planetInteractiveStates.Clear();
            _removedPlanetInteractiveIds.Clear();
            _staticRadioMarkerInteractiveStates.Clear();
            _gpsEntries.Clear();
            _radioSignals.Clear();
            ClearLogicalChildren();
            base.Close();
        }

        public override bool HasVisibleItems()
        {
            return true;
        }

        public override List<MySprite> GetSprites()
        {
            _baseSprites.Clear();
            _groundSprites.Clear();
            _groundOcclusionSprites.Clear();
            _ringSprites.Clear();
            _frontRingSprites.Clear();
            _overlaySprites.Clear();
            _children.Clear();
            BeginPlanetInteractiveFrame();
            BeginStaticMarkerInteractiveFrame();
            RequestedCursorType = GetDefaultCursorType();
            _suppressDynamicOverlays = false;

            bool hasPlanets;

            if (PlanetariumMode && _planetariumOrbitCacheValid)
            {
                _baseSprites.AddRange(_cachedStaticBaseSprites);
                _overlaySprites.AddRange(_cachedStaticTitleSprites);

                hasPlanets = DrawPlanetMap(_groundSprites, _groundOcclusionSprites, _ringSprites, _frontRingSprites, _overlaySprites);
            }
            else if (PlanetariumMode)
            {
                _cachedStaticBaseSprites.Clear();
                _cachedStaticBaseSprites.AddRange(_baseSprites);
                _cachedStaticTitleSprites.Clear();
                _cachedStaticTitleSprites.AddRange(_overlaySprites);

                hasPlanets = DrawPlanetMap(_groundSprites, _groundOcclusionSprites, _ringSprites, _frontRingSprites, _overlaySprites);
            }
            else
            {
                hasPlanets = DrawPlanetMap(_groundSprites, _groundOcclusionSprites, _ringSprites, _frontRingSprites, _overlaySprites);
                
                _baseSprites.AddRange(_groundSprites);
            }

            if (hasPlanets)
            {
                if (!PlanetariumMode && ShouldDrawFovHud())
                    DrawFovHud(_overlaySprites, _fov);
            }
            else
            {
                DrawMessage(_overlaySprites, LocHelper.Empty, "Warning", ColorComponent.ResolveWarningColor(), GeneralComponent.GetScale());
            }

            if (PlanetariumMode)
            {
                _staticOrbitControl.SetRect(ViewBox);
                _children.Insert(0, _staticOrbitControl);
            }

            FinalizePlanetInteractiveFrame();
            FinalizeStaticMarkerInteractiveFrame();

            _sprites.Clear();
            _sprites.AddRange(_baseSprites);
            _sprites.AddRange(_ringSprites);
            return _sprites;
        }

        public void RenderPostInteractiveSprites(List<MySprite> sprites)
        {
            if (sprites == null)
                return;

            sprites.AddRange(_groundOcclusionSprites);
            sprites.AddRange(_frontRingSprites);
            sprites.AddRange(_overlaySprites);
            AddGpsCreatedStatus(sprites);
        }

        void OnTextureQualityChanged(PlanetTextureQuality quality)
        {
            if (_closed)
                return;

            ApplyTextureQuality(quality, true);
        }

        void ApplyTextureQuality(PlanetTextureQuality quality, bool redraw)
        {
            quality = PlanetTextureQualitySettings.Normalize(quality);
            int maximumFaceSide = PlanetTextureQualitySettings.GetMaximumFaceSide(quality);
            float textCellPixels = PlanetTextureQualitySettings.GetTextCellSizePixels(quality);

            foreach (PlanetGlobeControl globe in _planetGlobeControls.Values)
                globe.SetRenderQuality(maximumFaceSide, textCellPixels);

            foreach (PlanetCubemapState state in _planetCubemapStates.Values)
            {
                if (state == null)
                    continue;

                CancelPlanetCubemapRequest(state);
                state.RetryFaceSide = int.MinValue;
                state.RetryFrame = 0L;
            }

            InvalidateStaticOrbitCache();
            InvalidateDynamicMapCache();
            if (redraw)
                Host.RenderSprites();
        }

        IMyGravityProviderSystem GetGravityProvider()
        {
            if (_gravityProvider == null && MyAPIGateway.Session != null)
                _gravityProvider = (IMyGravityProviderSystem)MyAPIGateway.Session
                    .GetComponentByInterfaceType<IMyGravityProviderSystem>();
            return _gravityProvider;
        }

        bool ShouldDrawFovHud()
        {
            long frame = GetCurrentGameFrame();
            return _lastFovChangedFrame != long.MinValue &&
                   frame >= _lastFovChangedFrame &&
                   frame - _lastFovChangedFrame <= MAGNIFICATION_HUD_VISIBLE_FRAMES;
        }

        static long GetCurrentGameFrame()
        {
            return MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
        }

        void QueueArtificialHorizonRenderNextFrame() => LcdModClientComponent.RunNextFrame.Add(_host.RenderSprites);

        void DrawFovHud(List<MySprite> sprites, float fovDeg)
        {
            const float textScale = 0.55f;
            double baseHalfFov = MathHelper.ToRadians(MAP_VERTICAL_FOV_DEFAULT_DEG) * 0.5;
            double currentHalfFov = MathHelper.ToRadians(Math.Max(0.1f, fovDeg)) * 0.5;
            double magnification = Math.Tan(baseHalfFov) / Math.Tan(currentHalfFov);
            string text = string.Format(
                FormatingHelper.Culture,
                LocHelper.GetLoc(MOD_PREFIX + "StarMap_MagnificationFormat"),
                magnification);
            var textSize = FormatingHelper.GetSizeInPixel(text, TextFont, textScale, Surface);
            const float margin = 8f;
            var pos = new Vector2(
                MathHelper.Clamp(ViewBox.Right - margin - textSize.X * 0.5f, ViewBox.X + textSize.X * 0.5f,
                    ViewBox.Right - textSize.X * 0.5f),
                MathHelper.Clamp(ViewBox.Bottom - margin - textSize.Y * 0.5f, ViewBox.Y + textSize.Y * 0.5f,
                    ViewBox.Bottom - textSize.Y * 0.5f));

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = pos,
                Color = ForegroundColor,
                FontId = TextFont,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });
        }

        bool DrawPlanetMap(
            List<MySprite> groundSprites,
            List<MySprite> groundOcclusionSprites,
            List<MySprite> ringSprites,
            List<MySprite> frontRingSprites,
            List<MySprite> overlaySprites)
        {
            var planets = PlanetHelper.PlanetsById;
            if (planets == null || planets.Count == 0)
                return false;
            bool hasDetectedPlanets = false;

            if (PlanetariumMode)
                return DrawSolar_SystemOrbitMap(ringSprites, frontRingSprites, planets);

            if (Block == null)
                return false;

            MatrixD referenceMatrix = GetReferenceMatrix();
            int groundStartIndex = groundSprites.Count;
            int groundOcclusionStartIndex = groundOcclusionSprites.Count;
            int ringStartIndex = ringSprites.Count;
            int overlayStartIndex = overlaySprites.Count;
            if (TryUseDynamicMapCache(groundSprites, groundOcclusionSprites, ringSprites, overlaySprites, planets.Count, referenceMatrix))
                return true;

            var camPos = referenceMatrix.Translation;
            var camRight = referenceMatrix.Right;
            var camUp = referenceMatrix.Up;
            var camForward = referenceMatrix.Forward;

            long gravityPlanetId = GetCurrentGravityPlanetId(camPos, planets);
            float naturalGravityMultiplier = GetNaturalGravityMultiplier(camPos);
            float surfaceGravityRatio = GetSurfaceGravityRatio(
                gravityPlanetId,
                planets,
                naturalGravityMultiplier);
            float spacePlanetFade = GetSurfaceGroundSpacePlanetFade(surfaceGravityRatio);
            if (naturalGravityMultiplier > 0.005f)
                QueueArtificialHorizonRenderNextFrame();

            if (_halfFovY < 1e-6)
                return false;

            double aspect = ViewBox.Width / Math.Max(1f, ViewBox.Height);
            double halfFovX = Math.Atan(Math.Tan(_halfFovY) * aspect);
            bool gravityPlanetRenderedAsGround = DrawDynamicArtificialHorizon(
                groundSprites,
                groundOcclusionSprites,
                overlaySprites,
                referenceMatrix,
                camPos,
                halfFovX,
                gravityPlanetId,
                naturalGravityMultiplier,
                planets);
            if (gravityPlanetRenderedAsGround)
                hasDetectedPlanets = true;
            var projectedPlanets = new List<PlanetProjection>(planets.Count);

            foreach (var kv in planets)
            {
                var planet = kv.Value;
                if (planet == null || planet.MarkedForClose)
                    continue;

                // Fade the current gravity planet marker out before the terrain disk starts
                // visibly expanding. The terrain disk itself is drawn as background at full opacity.
                if (gravityPlanetRenderedAsGround &&
                    planet.EntityId == gravityPlanetId &&
                    spacePlanetFade >= 0.999f)
                {
                    continue;
                }

                Vector3D delta = planet.WorldMatrix.Translation - camPos;
                double depth = Vector3D.Dot(delta, camForward);
                if (depth <= MAP_NEAR_CLIP_METERS)
                    continue;
                double distance = delta.Length();
                if (distance <= 1e-3)
                    continue;

                double localX = Vector3D.Dot(delta, camRight);
                double localY = Vector3D.Dot(delta, camUp);
                double azimuth = Math.Atan2(localX, depth);
                double elevation = Math.Atan2(localY, depth);
                double ndcX = azimuth / halfFovX;
                double ndcY = elevation / _halfFovY;

                var screenPos = new Vector2(
                    ViewBox.Center.X + (float)(ndcX * (ViewBox.Width * 0.5f)),
                    ViewBox.Center.Y - (float)(ndcY * (ViewBox.Height * 0.5f)));

                double planetRadiusMeters = planet.AverageRadius;
                if (planetRadiusMeters <= 0d)
                    continue;
                hasDetectedPlanets = true;

                double angularRadius = Math.Asin(Math.Min(1d, planetRadiusMeters / distance));
                float markerRadius = (float)(angularRadius / _halfFovY * (ViewBox.Height * 0.5f));
                float visibility = planet.EntityId == gravityPlanetId
                    ? 1f - spacePlanetFade
                    : 1f;
                if (visibility <= 0.001f)
                    continue;

                // Keep drawing while any part of the planet disk overlaps the LCD texture.
                // Terrain occlusion is generated across the whole texture, so culling by
                // ViewBox makes edge planets pop while the occlusion layer still reaches them.
                RectangleF textureBounds = GetTextureBounds();
                if (screenPos.X + markerRadius < textureBounds.X ||
                    screenPos.X - markerRadius > textureBounds.Right ||
                    screenPos.Y + markerRadius < textureBounds.Y ||
                    screenPos.Y - markerRadius > textureBounds.Bottom)
                    continue;

                string name;
                if (!PlanetHelper.PlanetNamesById.TryGetValue(planet.EntityId, out name))
                    name = planet.Name;
                var generator = planet.Generator;
                var atmosphere = generator?.Atmosphere;
                MyTemperatureLevel? averageTemperature = generator?.DefaultSurfaceTemperature;
                double surfaceGravity = planet.GetInitArguments.SurfaceGravity;
                double gravityFalloff =  planet.GetInitArguments.GravityFalloff;
                double gravityLimitRadius = 0d;
                if (planet.MaximumRadius > 0d && surfaceGravity > 0d && gravityFalloff > 0d)
                    gravityLimitRadius = planet.MaximumRadius * Math.Pow(surfaceGravity / 0.05d, 1d / gravityFalloff);
                Vector3 viewDirectionLocal;
                Vector3 screenRightLocal;
                Vector3 screenUpLocal;
                BuildPlanetLocalProjection(
                    planet,
                    camPos - planet.WorldMatrix.Translation,
                    camRight,
                    camUp,
                    out viewDirectionLocal,
                    out screenRightLocal,
                    out screenUpLocal);
                var projection = new PlanetProjection
                {
                    PlanetId = planet.EntityId,
                    Name = string.IsNullOrWhiteSpace(name)
                        ? LocHelper.GetLoc(MOD_PREFIX + "ClockDashboard_UnknownPlanet")
                        : name,
                    GpsColor = ResolvePlanetTexture(planet).BaseColor,
                    WorldPosition = planet.WorldMatrix.Translation,
                    Direction = delta / distance,
                    ViewDirectionLocal = viewDirectionLocal,
                    ScreenRightLocal = screenRightLocal,
                    ScreenUpLocal = screenUpLocal,
                    Distance = distance,
                    Visibility = visibility,
                    AngularRadius = angularRadius,
                    ScreenPos = screenPos,
                    MarkerRadius = markerRadius,
                    ShouldDisplayInfo = false,
                    Radius = planet.AverageRadius,
                    SurfaceGravityG = (float)surfaceGravity,
                    GravityRange = (float)(Math.Max(0d, gravityLimitRadius - planet.AverageRadius)),
                    AtmosphereDensity = planet.HasAtmosphere && atmosphere != null ? atmosphere.Density : 0f,
                    OxygenDensity = planet.HasAtmosphere && atmosphere != null ? atmosphere.OxygenDensity : 0f,
                    AverageTemperature = averageTemperature,
                    MaxWindSpeed = atmosphere?.MaxWindSpeed ?? 0f
                };
                CachePlanetInfoLines(ref projection);
                projectedPlanets.Add(projection);
            }

            projectedPlanets.Sort((a, b) => a.Distance.CompareTo(b.Distance)); // near -> far
            var visiblePlanets = new List<PlanetProjection>(projectedPlanets.Count);

            foreach (var candidate in projectedPlanets)
            {
                bool occluded = false;

                foreach (var planet in visiblePlanets)
                {
                    if (IsFullyOccludedBy(planet, candidate))
                    {
                        occluded = true;
                        break;
                    }
                }

                if (!occluded)
                    visiblePlanets.Add(candidate);
            }

            _suppressDynamicOverlays = SelectDynamicPlanetForInfo(visiblePlanets);

            if (naturalGravityMultiplier > 0.005f)
            {
                Vector3D artificialHorizonGravity;
                if (TryGetArtificialHorizonGravityDirection(camPos, out artificialHorizonGravity))
                    DrawArtificialHorizonPlanetOverlay(
                        overlaySprites,
                        artificialHorizonGravity,
                        referenceMatrix,
                        gravityPlanetId,
                        planets,
                        _suppressDynamicOverlays);
            }
            else
            {
                DrawArtificialHorizonSpaceOverlay(overlaySprites, referenceMatrix, _suppressDynamicOverlays);
            }

            for (int i = visiblePlanets.Count - 1; i >= 0; i--) // far -> near draw order
            {
                var planet = visiblePlanets[i];
                DrawPlanet(planet);
                DrawPlanetLabels(overlaySprites, planet);
            }

            CacheDynamicMap(
                groundSprites,
                groundStartIndex,
                groundOcclusionSprites,
                groundOcclusionStartIndex,
                ringSprites,
                ringStartIndex,
                overlaySprites,
                overlayStartIndex,
                planets.Count,
                referenceMatrix);
            return hasDetectedPlanets;
        }

        bool TryUseDynamicMapCache(
            List<MySprite> groundSprites,
            List<MySprite> groundOcclusionSprites,
            List<MySprite> ringSprites,
            List<MySprite> overlaySprites,
            int planetCount,
            MatrixD world)
        {
            if (!_dynamicMapCacheValid)
                return false;

            if (_cachedDynamicPlanetCount != planetCount ||
                !MatrixNearlyEquals(_cachedDynamicWorldMatrix, world) ||
                !RectangleNearlyEquals(_cachedDynamicViewBox, ViewBox) ||
                !VectorNearlyEquals(_cachedDynamicCursorPosition, CursorPosition) ||
                !VectorNearlyEquals(_cachedDynamicLinearVelocity, GetBlockLinearVelocity()) ||
                _cachedDynamicHasRecentVisualContact != HasRecentVisualContact ||
                GetNaturalGravityMultiplier(world.Translation) > 0.005f)
            {
                return false;
            }

            _suppressDynamicOverlays = _cachedDynamicSuppressOverlays;
            
            groundSprites.AddRange(_cachedDynamicGroundSprites);
            groundOcclusionSprites.AddRange(_cachedDynamicGroundOcclusionSprites);
            ringSprites.AddRange(_cachedDynamicRingSprites);
            overlaySprites.AddRange(_cachedOverlaySprites);
            for (int i = 0; i < _cachedInteractiveEntries.Count; i++)
            {
                PlanetGlobeControl entry = _cachedInteractiveEntries[i] as PlanetGlobeControl;
                if (entry == null)
                    continue;

                long planetId = entry.DataContext is long ? (long)entry.DataContext : 0L;
                PlanetInteractiveState state;
                if (planetId == 0L ||
                    !_planetInteractiveStates.TryGetValue(planetId, out state) ||
                    !ReferenceEquals(state.Entry, entry))
                {
                    continue;
                }

                UpdatePlanetGlobe(entry, state.Projection);
                state.UsedThisFrame = true;
                entry.SetVisible(true);
                _children.Add(entry);
            }
            return true;
        }

        void CacheDynamicMap(
            List<MySprite> groundSprites,
            int groundStartIndex,
            List<MySprite> groundOcclusionSprites,
            int groundOcclusionStartIndex,
            List<MySprite> ringSprites,
            int ringStartIndex,
            List<MySprite> overlaySprites,
            int overlayStartIndex,
            int planetCount,
            MatrixD world)
        {
            _planetariumOrbitCacheValid = false;
            _cachedDynamicWorldMatrix = world;
            _cachedDynamicViewBox = ViewBox;
            _cachedDynamicCursorPosition = CursorPosition;
            _cachedDynamicLinearVelocity = GetBlockLinearVelocity();
            _cachedDynamicHasRecentVisualContact = HasRecentVisualContact;
            _cachedDynamicSuppressOverlays = _suppressDynamicOverlays;
            _cachedDynamicPlanetCount = planetCount;
            
            _cachedDynamicGroundSprites.Clear();
            for (int i = groundStartIndex; i < groundSprites.Count; i++)
                _cachedDynamicGroundSprites.Add(groundSprites[i]);

            _cachedDynamicGroundOcclusionSprites.Clear();
            for (int i = groundOcclusionStartIndex; i < groundOcclusionSprites.Count; i++)
                _cachedDynamicGroundOcclusionSprites.Add(groundOcclusionSprites[i]);

            _cachedDynamicRingSprites.Clear();
            for (int i = ringStartIndex; i < ringSprites.Count; i++)
                _cachedDynamicRingSprites.Add(ringSprites[i]);

            _cachedOverlaySprites.Clear();
            for (int i = overlayStartIndex; i < overlaySprites.Count; i++)
                _cachedOverlaySprites.Add(overlaySprites[i]);
            ClearCachedInteractiveEntries();
            _cachedInteractiveEntries.AddRange(VisualChildren);
            _dynamicMapCacheValid = true;
        }

        bool SelectDynamicPlanetForInfo(List<PlanetProjection> visiblePlanets)
        {
            if (visiblePlanets == null || visiblePlanets.Count == 0)
            {
                _selectedInfoPlanetId = 0;
                _selectedInfoKeepAliveBounds.Clear();
                return false;
            }

            var target = GetDynamicInfoTargetPosition();
            if (float.IsNaN(target.X) || float.IsNaN(target.Y))
                return false;

            int selectedIndex = -1;
            for (int i = 0; i < visiblePlanets.Count; i++) // near -> far, matching top draw priority
            {
                var planet = visiblePlanets[i];
                float radius = Math.Max(planet.MarkerRadius, 2f * Scale);
                if (Vector2.DistanceSquared(planet.ScreenPos, target) > radius * radius)
                    continue;

                selectedIndex = i;
                break;
            }

            if (selectedIndex < 0 && UsesCursorDynamicInfoTarget() && _selectedInfoPlanetId != 0 &&
                IsInsideSelectedInfoKeepAliveBounds(target))
            {
                for (int i = 0; i < visiblePlanets.Count; i++)
                {
                    if (visiblePlanets[i].PlanetId == _selectedInfoPlanetId)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            if (selectedIndex < 0)
            {
                _selectedInfoPlanetId = 0;
                _selectedInfoKeepAliveBounds.Clear();
                return false;
            }

            var selected = visiblePlanets[selectedIndex];
            selected.ShouldDisplayInfo = true;
            _selectedInfoPlanetId = selected.PlanetId;
            _selectedInfoBoundsThisFrame.Clear();
            _selectedInfoKeepAliveBounds.Clear();
            visiblePlanets[selectedIndex] = selected;
            return true;
        }

        bool IsInsideSelectedInfoKeepAliveBounds(Vector2 target)
        {
            for (int i = 0; i < _selectedInfoKeepAliveBounds.Count; i++)
            {
                if (_selectedInfoKeepAliveBounds[i].Contains(target))
                    return true;
            }

            return false;
        }

        Vector2 GetDynamicInfoTargetPosition()
        {
            if (UsesCursorDynamicInfoTarget())
            {
                return CursorPosition;
            }

            return ViewBox.Center;
        }

        bool UsesCursorDynamicInfoTarget()
        {
            return HasRecentVisualContact &&
                !float.IsNaN(CursorPosition.X) &&
                !float.IsNaN(CursorPosition.Y);
        }

        Vector2 CursorPosition => _cursorPosition;

        bool HasRecentVisualContact =>
            _lastCursorVisualContactFrame != long.MinValue &&
            MyAPIGateway.Session != null &&
            MyAPIGateway.Session.GameplayFrameCounter - _lastCursorVisualContactFrame <= 30;

        static bool MatrixNearlyEquals(MatrixD a, MatrixD b)
        {
            const double positionTolerance = 0.001d;
            const double axisTolerance = 0.000001d;

            return Vector3D.DistanceSquared(a.Translation, b.Translation) <= positionTolerance * positionTolerance &&
                   Vector3D.DistanceSquared(a.Right, b.Right) <= axisTolerance * axisTolerance &&
                   Vector3D.DistanceSquared(a.Up, b.Up) <= axisTolerance * axisTolerance &&
                   Vector3D.DistanceSquared(a.Forward, b.Forward) <= axisTolerance * axisTolerance;
        }

        static bool RectangleNearlyEquals(RectangleF a, RectangleF b)
        {
            return NearlyEquals(a.X, b.X) &&
                   NearlyEquals(a.Y, b.Y) &&
                   NearlyEquals(a.Width, b.Width) &&
                   NearlyEquals(a.Height, b.Height);
        }

        static bool VectorNearlyEquals(Vector2 a, Vector2 b)
        {
            return NearlyEquals(a.X, b.X) && NearlyEquals(a.Y, b.Y);
        }

        static bool VectorNearlyEquals(Vector3D a, Vector3D b)
        {
            return Math.Abs(a.X - b.X) <= 0.001d &&
                   Math.Abs(a.Y - b.Y) <= 0.001d &&
                   Math.Abs(a.Z - b.Z) <= 0.001d;
        }

        static bool NearlyEquals(float a, float b)
        {
            if (float.IsNaN(a) || float.IsNaN(b))
                return float.IsNaN(a) && float.IsNaN(b);

            return Math.Abs(a - b) <= 0.001f;
        }

        Vector3D GetBlockLinearVelocity()
        {
            if (Block == null || Block.CubeGrid == null)
                return Vector3D.Zero;

            return Block.CubeGrid.LinearVelocity;
        }

        bool TryGetArtificialHorizonGravityDirection(Vector3D camPos, out Vector3D gravity)
        {
            gravity = Vector3D.Zero;

            IMyNaturalGravityComponent gravityComponent;
            if (!TryGetStrongestNaturalGravityComponent(camPos, out gravityComponent) || gravityComponent == null)
                return false;

            gravity = gravityComponent.Position - camPos;
            return gravity.Normalize() > 1e-6;
        }

        bool DrawDynamicArtificialHorizon(
            List<MySprite> groundSprites,
            List<MySprite> groundOcclusionSprites,
            List<MySprite> lineSprites,
            MatrixD world,
            Vector3D camPos,
            double halfFovX,
            long gravityPlanetId,
            float naturalGravityMultiplier,
            Dictionary<long, MyPlanet> planets)
        {
            IMyNaturalGravityComponent gravityComponent;
            if (!TryGetStrongestNaturalGravityComponent(camPos, out gravityComponent) || gravityComponent == null)
                return false;

            Vector3D gravity = gravityComponent.Position - camPos;
            if (gravity.Normalize() <= 1e-6)
                return false;

            float halfWidth = Math.Max(1f, ViewBox.Width * 0.5f);
            float halfHeight = Math.Max(1f, ViewBox.Height * 0.5f);
            float tanHalfFovX = (float)Math.Tan(halfFovX);
            float tanHalfFovY = (float)Math.Tan(_halfFovY);

            // Same source signal as the default artificial horizon: the natural gravity vector
            // transformed into the display's local frame. Here it becomes the surface-side
            // endpoint for the ground circle instead of a hard mode switch.
            float gravityRight = (float)Vector3D.Dot(gravity, world.Right);
            float gravityUp = (float)Vector3D.Dot(gravity, world.Up);
            float gravityForward = (float)Vector3D.Dot(gravity, world.Forward);

            var downNormal = new Vector2(
                gravityRight * tanHalfFovX / halfWidth,
                -gravityUp * tanHalfFovY / halfHeight);

            float normalLengthSq = downNormal.LengthSquared();
            Color planetColor = ForegroundColor;
            bool hasGroundPlanet = gravityPlanetId != 0 &&
                                   TryGetPlanetSurfaceColor(gravityPlanetId, planets, camPos, out planetColor);
            bool drawGround = hasGroundPlanet;
            var horizonColor = planetColor;
            float surfaceGravityRatio = hasGroundPlanet
                ? GetSurfaceGravityRatio(gravityPlanetId, planets, naturalGravityMultiplier)
                : 0f;
            float rectangleTransition = GetSurfaceGroundRectangleTransition(surfaceGravityRatio);
            if (normalLengthSq <= 1e-8f)
            {
                bool groundDrawn = false;
                if (gravityForward > 0f && drawGround)
                {
                    // With no stable horizon line, keep using the projected planet disk.
                    // Do not force a viewport rectangle at or above surface gravity.
                    groundDrawn = TryDrawProjectedGravityPlanetGroundCircle(
                        groundSprites,
                        world,
                        camPos,
                        halfFovX,
                        gravityPlanetId,
                        planets,
                        horizonColor);
                    if (groundDrawn)
                        TryDrawProjectedGravityPlanetGroundCircle(
                            groundOcclusionSprites,
                            world,
                            camPos,
                            halfFovX,
                            gravityPlanetId,
                            planets,
                            horizonColor);
                }
                return groundDrawn;
            }

            Func<Vector2, float> score = point =>
                downNormal.X * (point.X - ViewBox.Center.X) +
                downNormal.Y * (point.Y - ViewBox.Center.Y) +
                gravityForward;

            RectangleF textureBounds = GetTextureBounds();
            var topLeft = new Vector2(textureBounds.X, textureBounds.Y);
            var topRight = new Vector2(textureBounds.Right, textureBounds.Y);
            var bottomLeft = new Vector2(textureBounds.X, textureBounds.Bottom);
            var bottomRight = new Vector2(textureBounds.Right, textureBounds.Bottom);

            bool tlDown = score(topLeft) > 0f;
            bool trDown = score(topRight) > 0f;
            bool blDown = score(bottomLeft) > 0f;
            bool brDown = score(bottomRight) > 0f;
            bool anyCornerGroundSide = tlDown || trDown || blDown || brDown;
            bool allCornersGroundSide = tlDown && trDown && blDown && brDown;
            bool horizonVisibleInView = anyCornerGroundSide && !allCornersGroundSide;

            if (!anyCornerGroundSide)
                return false;

            var downDirection = downNormal / (float)Math.Sqrt(normalLengthSq);
            var lineCenter = ViewBox.Center - downNormal * (gravityForward / normalLengthSq);
            float diagonal = (float)Math.Sqrt(textureBounds.Width * textureBounds.Width + textureBounds.Height * textureBounds.Height);
            float rotation = (float)Math.Atan2(-downDirection.X, downDirection.Y);
            bool groundDrawnInView = false;
            if (drawGround)
            {
                bool useSurfacePlaneFill = rectangleTransition >= 0.999f;
                if (useSurfacePlaneFill)
                {
                    // At full surface gravity, the terrain is no longer drawn as a giant
                    // circle. Fill only the ground side of the artificial horizon so the
                    // top edge is the horizon line and nothing leaks into the sky side.
                    DrawGroundHalfPlaneFill(groundSprites, score, horizonColor);
                    DrawGroundHalfPlaneFill(groundOcclusionSprites, score, horizonColor, true);
                    groundDrawnInView = true;
                }
                else
                {
                    groundDrawnInView = TryDrawEasedGravityPlanetGroundCircle(
                        groundSprites,
                        world,
                        camPos,
                        halfFovX,
                        gravityPlanetId,
                        naturalGravityMultiplier,
                        planets,
                        lineCenter,
                        downDirection,
                        rectangleTransition,
                        horizonColor);

                    if (groundDrawnInView)
                        TryDrawEasedGravityPlanetGroundCircle(
                            groundOcclusionSprites,
                            world,
                            camPos,
                            halfFovX,
                            gravityPlanetId,
                            naturalGravityMultiplier,
                            planets,
                            lineCenter,
                            downDirection,
                            rectangleTransition,
                            horizonColor);

                    if (!groundDrawnInView && !horizonVisibleInView)
                    {
                        // Before full-surface mode, only use the scanline fill when the whole
                        // viewport is already ground-side. When the horizon is visible, a failed
                        // circle should leave the sky side untouched.
                        DrawGroundHalfPlaneFill(groundSprites, score, horizonColor);
                        DrawGroundHalfPlaneFill(groundOcclusionSprites, score, horizonColor, true);
                        groundDrawnInView = true;
                    }
                }
            }

            if (allCornersGroundSide)
                return groundDrawnInView;

            DrawClippedRectangle(
                lineSprites,
                lineCenter,
                new Vector2(diagonal * 4f, Math.Max(1f, ARTIFICIAL_HORIZON_LINE_WIDTH_PX * Scale)),
                "SquareTapered",
                ForegroundColor,
                rotation);
            return groundDrawnInView;
        }

        void DrawArtificialHorizonPlanetOverlay(
            List<MySprite> sprites,
            Vector3D gravityDirection,
            MatrixD world,
            long gravityPlanetId,
            Dictionary<long, MyPlanet> planets,
            bool essentialOnly)
        {
            if (sprites == null || Block == null || Block.CubeGrid == null)
                return;

            double gravityLength = gravityDirection.Normalize();
            if (gravityLength <= 1e-6)
                return;

            Vector3D linearVelocity = Block.CubeGrid.LinearVelocity;
            if (essentialOnly)
            {
                DrawArtificialHorizonVelocityVector(sprites, linearVelocity, world, Math.Max(0.1f, Scale));
                return;
            }

            Vector3D horizonForward = Vector3D.Reject(world.Forward, gravityDirection);
            if (horizonForward.Normalize() <= 1e-6)
                return;

            Vector3D gravityRoll = Vector3D.Normalize(Vector3D.Reject(gravityDirection, world.Forward));
            if (double.IsNaN(gravityRoll.X) || double.IsNaN(gravityRoll.Y) || double.IsNaN(gravityRoll.Z))
                gravityRoll = -world.Up;

            double rollAngle = -(Math.Acos(MathHelper.Clamp((float)Vector3D.Dot(gravityRoll, world.Left), -1f, 1f)) -
                                 Math.PI * 0.5d);
            if (Vector3D.Dot(gravityDirection, world.Up) >= 0d)
                rollAngle = Math.PI - rollAngle;

            double pitchAngle = Math.Acos(MathHelper.Clamp((float)Vector3D.Dot(gravityDirection, world.Forward), -1f, 1f)) -
                                Math.PI * 0.5d;
            float hudScale = Math.Max(0.1f, Scale);
            DrawArtificialHorizonLadder(sprites, gravityDirection, world, pitchAngle, horizonForward, rollAngle, hudScale);

            int radarAltitude;
            bool hasRadarAltitude = TryGetArtificialHorizonRadarAltitude(
                world.Translation,
                gravityPlanetId,
                planets,
                out radarAltitude);
            if (hasRadarAltitude)
            {
                DrawArtificialHorizonAltitudeWarning(sprites, radarAltitude);
                UpdateArtificialHorizonAltitudeSample(radarAltitude, gravityPlanetId);
                DrawArtificialHorizonAltimeter(sprites, radarAltitude, hudScale);
            }
            else
            {
                ResetArtificialHorizonAltitudeSample();
            }

            if (hasRadarAltitude)
                DrawArtificialHorizonPullUpWarning(sprites, linearVelocity, gravityDirection, radarAltitude, rollAngle, hudScale);
            DrawArtificialHorizonSpeedIndicator(sprites, linearVelocity, hudScale);
            DrawArtificialHorizonVelocityVector(sprites, linearVelocity, world, hudScale);
            DrawArtificialHorizonBoreSight(sprites, hudScale);
        }

        void DrawArtificialHorizonSpaceOverlay(List<MySprite> sprites, MatrixD world, bool essentialOnly)
        {
            if (sprites == null || Block == null || Block.CubeGrid == null)
                return;

            float hudScale = Math.Max(0.1f, Scale);
            Vector3D linearVelocity = Block.CubeGrid.LinearVelocity;
            DrawArtificialHorizonVelocityVector(sprites, linearVelocity, world, hudScale);
            if (essentialOnly)
                return;

            DrawArtificialHorizonSpeedIndicator(sprites, linearVelocity, hudScale);
            DrawArtificialHorizonBoreSight(sprites, hudScale);
        }

        void DrawArtificialHorizonLadder(
            List<MySprite> sprites,
            Vector3D gravityDirection,
            MatrixD world,
            double pitchAngle,
            Vector3D horizonForward,
            double rollAngle,
            float hudScale)
        {
            int centerStep = (int)Math.Round(pitchAngle / ARTIFICIAL_HORIZON_ANGLE_STEP_RAD);
            var ladderStepSize = GetArtificialHorizonLadderStepSize(hudScale);
            var ladderStepTextOffset = new Vector2(0f, ladderStepSize.Y * 0.5f);
            float textScale = hudScale * FontScale * ARTIFICIAL_HORIZON_LADDER_TEXT_SCALE_MULTIPLIER;
            MatrixD inverseWorld = MatrixD.Invert(world);

            for (int index = centerStep - 5; index <= centerStep + 5; index++)
            {
                if (index == 0)
                    continue;

                MatrixD pitchWorld = MatrixD.CreateRotationX(index * ARTIFICIAL_HORIZON_ANGLE_STEP_RAD) *
                                     MatrixD.CreateWorld(world.Translation, horizonForward, -gravityDirection);
                Vector3D stepLocal = Vector3D.TransformNormal(
                    Vector3D.Reject(pitchWorld.Forward, world.Forward),
                    inverseWorld);
                var stepPosition = ViewBox.Center + new Vector2((float)stepLocal.X, -(float)stepLocal.Y) *
                    ARTIFICIAL_HORIZON_HUD_SCALING * hudScale;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = index * ARTIFICIAL_HORIZON_ANGLE_STEP_RAD < 0d
                        ? "AH_GravityHudNegativeDegrees"
                        : "AH_GravityHudPositiveDegrees",
                    Position = stepPosition,
                    Size = ladderStepSize,
                    Color = ForegroundColor,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = (float)rollAngle
                });

                int degrees = Math.Abs(index * 5);
                string label = index > 18 ? (180 - index * 5).ToString(FormatingHelper.Culture) : degrees.ToString(FormatingHelper.Culture);
                Vector2 labelOffset = RotateVector(new Vector2(-ladderStepSize.X * 0.55f, 0f), (float)rollAngle);
                AddArtificialHorizonText(
                    sprites,
                    label,
                    stepPosition + labelOffset - ladderStepTextOffset,
                    textScale,
                    TextAlignment.RIGHT);

                labelOffset = RotateVector(new Vector2(ladderStepSize.X * 0.55f, 0f), (float)rollAngle);
                AddArtificialHorizonText(
                    sprites,
                    label,
                    stepPosition + labelOffset - ladderStepTextOffset,
                    textScale,
                    TextAlignment.LEFT);
            }
        }

        bool TryGetArtificialHorizonRadarAltitude(
            Vector3D position,
            long gravityPlanetId,
            Dictionary<long, MyPlanet> planets,
            out int radarAltitude)
        {
            radarAltitude = 0;

            if (gravityPlanetId == 0 || planets == null)
                return false;

            MyPlanet planet;
            if (!planets.TryGetValue(gravityPlanetId, out planet) || planet == null || planet.MarkedForClose)
                return false;

            Vector3D surfacePoint = planet.GetClosestSurfacePointGlobal(position);
            radarAltitude = Math.Max(0, (int)Math.Round(Vector3D.Distance(position, surfacePoint), 0));
            return true;
        }

        void DrawArtificialHorizonAltitudeWarning(
            List<MySprite> sprites,
            int radarAltitude)
        {
            float warningAltitude = 100f;
            var cubeGrid = Block.CubeGrid as MyCubeGrid;
            if (cubeGrid != null)
                warningAltitude += cubeGrid.PositionComp.LocalAABB.Height;

            if (_artificialHorizonLastRadarAlt >= warningAltitude && radarAltitude < warningAltitude)
            {
                _artificialHorizonShowAltWarning = true;
                _artificialHorizonAltWarningShownAt = _jumpPointRunCounter;
            }

            if (_jumpPointRunCounter - _artificialHorizonAltWarningShownAt > ARTIFICIAL_HORIZON_ALTITUDE_WARNING_RUN_THRESHOLD)
                _artificialHorizonShowAltWarning = false;

            if (!_artificialHorizonShowAltWarning)
                return;

            DrawMessage(
                sprites,
                LocHelper.GetLoc("DisplayName_TSS_ArtificialHorizon_AltitudeWarning"),
                "Warning",
                GetArtificialHorizonWarningColor(),
                GeneralComponent.GetScale());
        }

        void DrawArtificialHorizonAltimeter(List<MySprite> sprites, int radarAltitude, float hudScale)
        {
            float textScale = hudScale;
            var textBoxSize = GetArtificialHorizonTextBoxSize(textScale);
            var textOffset = GetArtificialHorizonTextOffset(textScale);
            var boxCenter = ViewBox.Center + (new Vector2(115f, 80f) * hudScale) +
                            GetArtificialHorizonTextBoxSize(hudScale) * 0.5f;
            AddArtificialHorizonTextBox(
                sprites,
                boxCenter,
                textBoxSize,
                radarAltitude.ToString(FormatingHelper.Culture),
                textScale,
                "AH_TextBox",
                textOffset.X);

            AddArtificialHorizonTextBox(
                sprites,
                boxCenter - new Vector2(0f, textBoxSize.Y),
                textBoxSize,
                _artificialHorizonVerticalSpeed.ToString(FormatingHelper.Culture),
                textScale,
                null,
                textOffset.X);
        }

        void UpdateArtificialHorizonAltitudeSample(int radarAltitude, long gravityPlanetId)
        {
            long currentFrame = GetCurrentGameFrame();
            if (_artificialHorizonLastRadarAltFrame == long.MinValue ||
                _artificialHorizonLastRadarAltPlanetId != gravityPlanetId)
            {
                _artificialHorizonLastRadarAlt = radarAltitude;
                _artificialHorizonLastRadarAltFrame = currentFrame;
                _artificialHorizonLastRadarAltPlanetId = gravityPlanetId;
                _artificialHorizonVerticalSpeed = 0;
                return;
            }

            long frameDelta = currentFrame - _artificialHorizonLastRadarAltFrame;
            if (frameDelta < 0L)
            {
                _artificialHorizonLastRadarAlt = radarAltitude;
                _artificialHorizonLastRadarAltFrame = currentFrame;
                _artificialHorizonLastRadarAltPlanetId = gravityPlanetId;
                _artificialHorizonVerticalSpeed = 0;
                return;
            }

            if (frameDelta < ARTIFICIAL_HORIZON_ALTITUDE_DELTA_SAMPLE_FRAMES)
                return;

            _artificialHorizonVerticalSpeed =
                (int)Math.Round((radarAltitude - _artificialHorizonLastRadarAlt) * 60d / frameDelta);
            _artificialHorizonLastRadarAlt = radarAltitude;
            _artificialHorizonLastRadarAltFrame = currentFrame;
            _artificialHorizonLastRadarAltPlanetId = gravityPlanetId;
        }

        void ResetArtificialHorizonAltitudeSample()
        {
            _artificialHorizonLastRadarAlt = 0;
            _artificialHorizonVerticalSpeed = 0;
            _artificialHorizonLastRadarAltFrame = long.MinValue;
            _artificialHorizonLastRadarAltPlanetId = 0;
        }

        void DrawArtificialHorizonPullUpWarning(
            List<MySprite> sprites,
            Vector3D velocity,
            Vector3D gravityDirection,
            int radarAltitude,
            double rollAngle,
            float hudScale)
        {
            double descentSpeed = Vector3D.Dot(velocity, gravityDirection);
            if (descentSpeed <= 0d)
                return;

            double warningAltitude = Math.Max(50d, descentSpeed * 3d);
            if (radarAltitude > warningAltitude || _jumpPointRunCounter % 10 <= 2)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_PullUp",
                Position = ViewBox.Center,
                Size = new Vector2(150f, 180f) * hudScale,
                Color = GetArtificialHorizonErrorColor(),
                Alignment = TextAlignment.CENTER,
                RotationOrScale = (float)rollAngle
            });
        }

        void DrawArtificialHorizonSpeedIndicator(List<MySprite> sprites, Vector3D velocity, float hudScale)
        {
            float textScale = hudScale;
            var textBoxSize = GetArtificialHorizonTextBoxSize(textScale);
            var textOffset = GetArtificialHorizonTextOffset(textScale);
            var boxCenter = ViewBox.Center + (new Vector2(-205f, 80f) * hudScale) +
                            GetArtificialHorizonTextBoxSize(hudScale) * 0.5f;
            AddArtificialHorizonTextBox(
                sprites,
                boxCenter,
                textBoxSize,
                ((int)velocity.Length()).ToString(FormatingHelper.Culture),
                textScale,
                "AH_TextBox",
                textOffset.X);
        }

        void DrawArtificialHorizonVelocityVector(
            List<MySprite> sprites,
            Vector3D velocity,
            MatrixD world,
            float hudScale)
        {
            if (Vector3D.Dot(velocity, world.Forward) < ARTIFICIAL_HORIZON_VELOCITY_DOT_THRESHOLD)
                return;

            double speedSq = velocity.LengthSquared();
            Vector3D velocityDirection = velocity;
            if (velocityDirection.Normalize() <= 1e-6)
                velocityDirection = Vector3D.Zero;

            Vector3D localVelocity = Vector3D.TransformNormal(
                Vector3D.Reject(velocityDirection, world.Forward),
                MatrixD.Invert(world));
            var positionOffset = speedSq < 9d
                ? Vector2.Zero
                : new Vector2((float)localVelocity.X, -(float)localVelocity.Y) * ARTIFICIAL_HORIZON_HUD_SCALING * hudScale;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_VelocityVector",
                Position = ViewBox.Center + positionOffset,
                Size = new Vector2(50f, 50f) * hudScale,
                Color = ForegroundColor,
                Alignment = TextAlignment.CENTER
            });
        }

        void DrawArtificialHorizonBoreSight(List<MySprite> sprites, float hudScale)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_BoreSight",
                Position = ViewBox.Center + new Vector2(0f, 19f) * hudScale,
                Size = new Vector2(50f, 50f) * hudScale,
                Color = ForegroundColor,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = -MathHelper.PiOver2
            });
        }

        void AddArtificialHorizonTextBox(
            List<MySprite> sprites,
            Vector2 position,
            Vector2 size,
            string text,
            float textScale,
            string backgroundTexture,
            float textOffset)
        {
            Vector2 rightCenter = position + new Vector2(size.X * 0.5f, 0f);
            if (!string.IsNullOrEmpty(backgroundTexture))
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = backgroundTexture,
                    Position = rightCenter,
                    Size = size,
                    Color = ForegroundColor,
                    Alignment = TextAlignment.RIGHT
                });
            }

            AddArtificialHorizonText(
                sprites,
                text,
                rightCenter + new Vector2(-textOffset, -size.Y * 0.5f),
                textScale,
                TextAlignment.RIGHT,
                size);
        }

        void AddArtificialHorizonText(
            List<MySprite> sprites,
            string text,
            Vector2 position,
            float textScale,
            TextAlignment alignment,
            Vector2? size = null,
            Color? color = null)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = position,
                Size = size,
                Color = color ?? ForegroundColor,
                FontId = TextFont,
                Alignment = alignment,
                RotationOrScale = textScale
            });
        }

        Color GetArtificialHorizonWarningColor()
        {
            return ColorComponent.ResolveWarningColor();
        }

        Color GetArtificialHorizonErrorColor()
        {
            return ColorComponent.ResolveErrorColor()
                .MulValue(2f)
                .MulSaturation(2f);
        }

        static Vector2 GetArtificialHorizonTextBoxSize(float hudScale)
        {
            return new Vector2(89f, 32f) * hudScale;
        }

        static Vector2 GetArtificialHorizonTextOffset(float hudScale)
        {
            return new Vector2(5f, 0f) * hudScale;
        }

        static Vector2 GetArtificialHorizonLadderStepSize(float hudScale)
        {
            return new Vector2(150f, 31f) * hudScale;
        }

        static Vector2 RotateVector(Vector2 vector, float angle)
        {
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
        }

        bool TryGetStrongestNaturalGravityComponent(Vector3D camPos, out IMyNaturalGravityComponent gravityComponent)
        {
            gravityComponent = null;

            var gravityProvider = GetGravityProvider();
            if (gravityProvider == null || !gravityProvider.IsPositionInNaturalGravity(camPos))
                return false;

            gravityProvider.GetStrongestNaturalGravityWell(camPos, out gravityComponent);
            return gravityComponent != null;
        }

        bool TryGetPlanetSurfaceColor(long planetId, Dictionary<long, MyPlanet> planets, Vector3D camPos, out Color color)
        {
            color = ForegroundColor;

            MyPlanet planet;
            if (planets == null || !planets.TryGetValue(planetId, out planet) || planet == null || planet.MarkedForClose)
                return false;

            var texture = ResolvePlanetTexture(planet);
            color = SamplePlanetSurfaceColor(texture, planet, camPos);
            return true;
        }

        PlanetHelper.PlanetTextureStyle ResolvePlanetTexture(MyPlanet planet)
        {
            string name;
            if (!PlanetHelper.PlanetNamesById.TryGetValue(planet.EntityId, out name))
                name = planet.Name;

            string generatorName;
            PlanetHelper.PlanetGeneratorNamesById.TryGetValue(planet.EntityId, out generatorName);
            var textureKey = string.IsNullOrWhiteSpace(generatorName) ? name : generatorName;
            return PlanetHelper.ResolvePlanetTexture(textureKey);
        }

        static Color SamplePlanetSurfaceColor(PlanetHelper.PlanetTextureStyle texture, MyPlanet planet, Vector3D camPos)
        {
            Vector3D surfaceNormal = camPos - planet.WorldMatrix.Translation;
            if (surfaceNormal.Normalize() <= 1e-6)
                return texture.BaseColor;

            Vector3D planetUp = planet.WorldMatrix.Up;
            if (planetUp.Normalize() <= 1e-6)
                return texture.BaseColor;

            // Treat the camera's center-to-surface direction as a latitude sample on
            // the same vertical axis used by the planet texture. 0 radians means the
            // equator and PI/2 means either pole. The thresholds retain the existing
            // surface-band proportions, with a small angular blend at each boundary.
            float verticalDot = MathHelper.Clamp((float)Vector3D.Dot(surfaceNormal, planetUp), -1f, 1f);
            float verticalAngle = Math.Abs((float)Math.Asin(verticalDot));
            float transitionAngle = MathHelper.ToRadians(SURFACE_GROUND_COLOR_TRANSITION_DEG);

            float equatorBandAngle = (float)Math.Asin(MathHelper.Clamp(EQUATOR_BAND_RATIO, 0f, 1f));
            float polarCapStart = MathHelper.Clamp(1f - POLAR_CAP_RATIO * 2f, 0f, 1f);
            float polarCapStartAngle = (float)Math.Asin(polarCapStart);

            Color color = texture.BaseColor;

            if (texture.EquatorColor.HasValue)
            {
                // Full equator color through the equator band, then fade back to the
                // regular/base color during the next ~5 degrees of latitude.
                float equatorWeight = 1f - Easing.EaseInRange(
                    equatorBandAngle,
                    equatorBandAngle + transitionAngle,
                    verticalAngle);
                color = BlendColor(color, texture.EquatorColor.Value, equatorWeight);
            }

            if (texture.PolarCapColor.HasValue)
            {
                // Fade from regular/base color into polar color over the ~5 degrees
                // before the cap starts, then stay fully polar toward the pole.
                float polarWeight = Easing.EaseInRange(
                    polarCapStartAngle - transitionAngle,
                    polarCapStartAngle,
                    verticalAngle);
                color = BlendColor(color, texture.PolarCapColor.Value, polarWeight);
            }

            return color;
        }

        static Color BlendColor(Color from, Color to, float amount)
        {
            amount = Easing.Clamp01(amount);
            if (amount <= 0f)
                return from;
            if (amount >= 1f)
                return to;

            return new Color(
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount),
                (int)Math.Round(from.A + (to.A - from.A) * amount));
        }


        bool TryDrawEasedGravityPlanetGroundCircle(
            List<MySprite> sprites,
            MatrixD world,
            Vector3D camPos,
            double halfFovX,
            long gravityPlanetId,
            float naturalGravityMultiplier,
            Dictionary<long, MyPlanet> planets,
            Vector2 horizonLineCenter,
            Vector2 downDirection,
            float rectangleTransition,
            Color color)
        {
            Vector2 accurateCenter;
            float accurateRadius;
            double distanceMeters;
            double radiusMeters;
            if (!TryGetProjectedGravityPlanetGroundCircle(
                    world,
                    camPos,
                    halfFovX,
                    gravityPlanetId,
                    planets,
                    out accurateCenter,
                    out accurateRadius,
                    out distanceMeters,
                    out radiusMeters))
            {
                return false;
            }

            if (accurateRadius <= 0f ||
                float.IsNaN(accurateCenter.X) ||
                float.IsNaN(accurateCenter.Y) ||
                float.IsNaN(accurateRadius))
            {
                return false;
            }

            // Drive the geometry with its own 50% -> 90% normalized-gravity transition:
            // at the start the terrain circle still matches the projected planet disk, and
            // by the end it has settled into the surface/horizon-clamped placement. The
            // terrain is background art, so opacity stays at 100%; only geometry eases.
            float surfaceGravityRatio = GetSurfaceGravityRatio(
                gravityPlanetId,
                planets,
                naturalGravityMultiplier);
            float surfaceGeometryTransition = GetSurfaceGroundGeometryTransition(surfaceGravityRatio);

            float scaleBoost = Easing.EaseInInterpolate(
                1f,
                SURFACE_GROUND_MAX_SCALE_BOOST,
                SURFACE_GROUND_SCALE_BOOST_START_RATIO,
                1f,
                surfaceGravityRatio);
            float radius = accurateRadius * scaleBoost;
            Vector2 boostedCenter = MoveCircleCenterAwayFromHorizonForRadiusBoost(
                accurateCenter,
                accurateRadius,
                radius,
                downDirection);

            Vector2 clampedCenter = ClampCircleCenterToHorizon(
                boostedCenter,
                radius,
                horizonLineCenter,
                downDirection);
            clampedCenter = CloseCircleGapToHorizon(
                clampedCenter,
                radius,
                horizonLineCenter,
                downDirection,
                rectangleTransition);
            Vector2 center = Easing.EaseInInterpolate(boostedCenter, clampedCenter, surfaceGeometryTransition);

            if (!DoesCircleOverlapTextureSurface(center, radius))
                return false;

            DrawClippedCircle(sprites, center, radius * 2f, color);
            return true;
        }

        float GetSurfaceGravityRatio(
            long gravityPlanetId,
            Dictionary<long, MyPlanet> planets,
            float naturalGravityMultiplier)
        {
            if (gravityPlanetId == 0 || planets == null || naturalGravityMultiplier <= 0f)
                return 0f;

            MyPlanet planet;
            if (!planets.TryGetValue(gravityPlanetId, out planet) || planet == null || planet.MarkedForClose)
                return 0f;

            double surfaceGravity = planet.GetInitArguments.SurfaceGravity;
            if (surfaceGravity <= 1e-6d)
                return 0f;

            return MathHelper.Clamp((float)(naturalGravityMultiplier / surfaceGravity), 0f, 1f);
        }

        static float GetSurfaceGroundSpacePlanetFade(float surfaceGravityRatio)
        {
            return Easing.EaseInRange(
                SURFACE_GROUND_SPACE_PLANET_FADE_START_RATIO,
                SURFACE_GROUND_SPACE_PLANET_FADE_END_RATIO,
                surfaceGravityRatio);
        }

        static float GetSurfaceGroundGeometryTransition(float surfaceGravityRatio)
        {
            return Easing.EaseInRange(
                SURFACE_GROUND_GEOMETRY_TRANSITION_START_RATIO,
                SURFACE_GROUND_GEOMETRY_TRANSITION_END_RATIO,
                surfaceGravityRatio);
        }

        static float GetSurfaceGroundRectangleTransition(float surfaceGravityRatio)
        {
            return Easing.EaseInRange(
                SURFACE_GROUND_RECTANGLE_TRANSITION_START_RATIO,
                SURFACE_GROUND_RECTANGLE_TRANSITION_END_RATIO,
                surfaceGravityRatio);
        }

        static Vector2 MoveCircleCenterAwayFromHorizonForRadiusBoost(
            Vector2 center,
            float originalRadius,
            float boostedRadius,
            Vector2 downDirection)
        {
            if (originalRadius <= 0f ||
                boostedRadius <= originalRadius ||
                float.IsNaN(center.X) ||
                float.IsNaN(center.Y) ||
                float.IsNaN(originalRadius) ||
                float.IsNaN(boostedRadius) ||
                float.IsNaN(downDirection.X) ||
                float.IsNaN(downDirection.Y))
            {
                return center;
            }

            float downLengthSq = downDirection.LengthSquared();
            if (downLengthSq <= 1e-8f)
                return center;

            if (Math.Abs(downLengthSq - 1f) > 0.001f)
                downDirection /= (float)Math.Sqrt(downLengthSq);

            // Scaling the circle would otherwise move the sky-facing edge toward the
            // horizon. Move the center away by the exact radius delta so only the far
            // side expands, visually flattening the surface instead of lifting it.
            return center + downDirection * (boostedRadius - originalRadius);
        }

        static Vector2 CloseCircleGapToHorizon(
            Vector2 center,
            float radius,
            Vector2 horizonLineCenter,
            Vector2 downDirection,
            float amount)
        {
            amount = Easing.Clamp01(amount);
            if (amount <= 0f ||
                radius <= 0f ||
                float.IsNaN(center.X) ||
                float.IsNaN(center.Y) ||
                float.IsNaN(radius) ||
                float.IsNaN(horizonLineCenter.X) ||
                float.IsNaN(horizonLineCenter.Y) ||
                float.IsNaN(downDirection.X) ||
                float.IsNaN(downDirection.Y))
            {
                return center;
            }

            float downLengthSq = downDirection.LengthSquared();
            if (downLengthSq <= 1e-8f)
                return center;

            if (Math.Abs(downLengthSq - 1f) > 0.001f)
                downDirection /= (float)Math.Sqrt(downLengthSq);

            // If the circle's sky-facing edge is below the horizon, there is a visible
            // gap before rectangle mode takes over. Close that gap gradually over the
            // 90% -> 100% surface-gravity transition so the final rectangle swap is seamless.
            float signedDistance = Vector2.Dot(center - horizonLineCenter, downDirection);
            float gapToHorizon = signedDistance - radius;
            if (gapToHorizon <= 0f)
                return center;

            return center - downDirection * (gapToHorizon * amount);
        }

        static Vector2 ClampCircleCenterToHorizon(
            Vector2 center,
            float radius,
            Vector2 horizonLineCenter,
            Vector2 downDirection)
        {
            if (radius <= 0f ||
                float.IsNaN(center.X) ||
                float.IsNaN(center.Y) ||
                float.IsNaN(radius) ||
                float.IsNaN(horizonLineCenter.X) ||
                float.IsNaN(horizonLineCenter.Y) ||
                float.IsNaN(downDirection.X) ||
                float.IsNaN(downDirection.Y))
            {
                return center;
            }

            float downLengthSq = downDirection.LengthSquared();
            if (downLengthSq <= 1e-8f)
                return center;

            if (Math.Abs(downLengthSq - 1f) > 0.001f)
                downDirection /= (float)Math.Sqrt(downLengthSq);

            // Positive signed distance means the center is on the ground side of the
            // horizon. If the nearest circle edge is above the horizon, shift the
            // center along the down direction just enough that the edge sits on it.
            float signedDistance = Vector2.Dot(center - horizonLineCenter, downDirection);
            float neededDistance = radius;
            if (signedDistance >= neededDistance)
                return center;

            return center + downDirection * (neededDistance - signedDistance);
        }

        bool TryDrawProjectedGravityPlanetGroundCircle(
            List<MySprite> sprites,
            MatrixD world,
            Vector3D camPos,
            double halfFovX,
            long gravityPlanetId,
            Dictionary<long, MyPlanet> planets,
            Color color)
        {
            Vector2 center;
            float radius;
            double distanceMeters;
            double radiusMeters;
            if (!TryGetProjectedGravityPlanetGroundCircle(
                    world,
                    camPos,
                    halfFovX,
                    gravityPlanetId,
                    planets,
                    out center,
                    out radius,
                    out distanceMeters,
                    out radiusMeters))
            {
                return false;
            }

            if (radius <= 0f ||
                float.IsNaN(center.X) ||
                float.IsNaN(center.Y) ||
                float.IsNaN(radius) ||
                !DoesCircleOverlapTextureSurface(center, radius))
            {
                return false;
            }

            DrawClippedCircle(sprites, center, radius * 2f, color);
            return true;
        }

        bool DoesCircleOverlapTextureSurface(Vector2 center, float radius)
        {
            if (radius <= 0f || float.IsNaN(center.X) || float.IsNaN(center.Y) || float.IsNaN(radius))
                return false;

            RectangleF bounds = GetTextureBounds();
            return center.X + radius >= bounds.X &&
                   center.X - radius <= bounds.Right &&
                   center.Y + radius >= bounds.Y &&
                   center.Y - radius <= bounds.Bottom;
        }

        bool TryGetProjectedGravityPlanetGroundCircle(
            MatrixD world,
            Vector3D camPos,
            double halfFovX,
            long gravityPlanetId,
            Dictionary<long, MyPlanet> planets,
            out Vector2 screenCenter,
            out float screenRadius,
            out double distanceMeters,
            out double radiusMeters)
        {
            screenCenter = Vector2.Zero;
            screenRadius = 0f;
            distanceMeters = 0d;
            radiusMeters = 0d;

            if (gravityPlanetId == 0 || planets == null || _halfFovY < 1e-6 || halfFovX <= 1e-6)
                return false;

            MyPlanet planet;
            if (!planets.TryGetValue(gravityPlanetId, out planet) || planet == null || planet.MarkedForClose)
                return false;

            Vector3D delta = planet.WorldMatrix.Translation - camPos;
            double distance = delta.Length();
            radiusMeters = planet.AverageRadius > 0d ? planet.AverageRadius : planet.MaximumRadius;
            distanceMeters = distance;
            if (distance <= 1e-3 || radiusMeters <= 0d)
                return false;

            double depth = Vector3D.Dot(delta, world.Forward);
            double localX = Vector3D.Dot(delta, world.Right);
            double localY = Vector3D.Dot(delta, world.Up);

            // Match the angular projection used by the dynamic planet markers, including
            // off-screen centers. That keeps the ground disk aligned with the map FOV.
            double azimuth = Math.Atan2(localX, depth);
            double elevation = Math.Atan2(localY, depth);
            double angularRadius = Math.Asin(Math.Min(1d, radiusMeters / distance));

            screenCenter = new Vector2(
                ViewBox.Center.X + (float)(azimuth / halfFovX * (ViewBox.Width * 0.5f)),
                ViewBox.Center.Y - (float)(elevation / _halfFovY * (ViewBox.Height * 0.5f)));
            screenRadius = (float)(angularRadius / _halfFovY * (ViewBox.Height * 0.5f));

            return screenRadius > 0f;
        }

        static class Easing
        {
            public static float Clamp01(float value)
            {
                return MathHelper.Clamp(value, 0f, 1f);
            }

            public static float Normalize(float edge0, float edge1, float value)
            {
                if (Math.Abs(edge1 - edge0) <= 1e-6f)
                    return value >= edge1 ? 1f : 0f;

                return Clamp01((value - edge0) / (edge1 - edge0));
            }

            public static float EaseIn(float amount)
            {
                amount = Clamp01(amount);
                return amount * amount;
            }

            public static float EaseInRange(float edge0, float edge1, float value)
            {
                return EaseIn(Normalize(edge0, edge1, value));
            }

            public static float EaseInInterpolate(float from, float to, float amount)
            {
                amount = EaseIn(amount);
                return from + (to - from) * amount;
            }

            public static float EaseInInterpolate(float from, float to, float edge0, float edge1, float value)
            {
                return EaseInInterpolate(from, to, Normalize(edge0, edge1, value));
            }

            public static Vector2 EaseInInterpolate(Vector2 from, Vector2 to, float amount)
            {
                amount = EaseIn(amount);
                return from + (to - from) * amount;
            }
        }

        void DrawClippedCircle(List<MySprite> sprites, Vector2 center, float diameter, Color color)
        {
            if (color.A == 0 || diameter <= 0f || float.IsNaN(center.X) || float.IsNaN(center.Y) || float.IsNaN(diameter))
                return;

            sprites.Add(MySprite.CreateClipRect(GetTextureClipRect()));
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = center,
                Size = new Vector2(diameter, diameter),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
            RestoreTextureClip(sprites);
        }

        void DrawGroundHalfPlaneFill(
            List<MySprite> sprites,
            Func<Vector2, float> score,
            Color color,
            bool overlayBackground = false)
        {
            RectangleF bounds = GetTextureBounds();
            if (color.A == 0 || score == null || bounds.Width <= 0f || bounds.Height <= 0f)
                return;

            float left = bounds.X;
            float right = bounds.Right;
            float top = bounds.Y;
            float bottom = bounds.Bottom;
            float width = bounds.Width;

            // Draw the terrain as small, already-in-viewport rectangles instead of one
            // oversized rotated sprite behind an SE clip rect. This avoids the LCD renderer
            // dropping one side of the background/terrain when a clipped sprite has extreme
            // coordinates or dimensions.
            float rowHeight = Math.Max(2f, 3f * Scale);

            for (float y = top; y < bottom; y += rowHeight)
            {
                float h = Math.Min(rowHeight, bottom - y);
                if (h <= 0f)
                    continue;

                float sampleY = y + h * 0.5f;
                var pLeft = new Vector2(left, sampleY);
                var pRight = new Vector2(right, sampleY);
                float sLeft = score(pLeft);
                float sRight = score(pRight);
                bool leftDown = sLeft > 0f;
                bool rightDown = sRight > 0f;

                float fillLeft;
                float fillRight;

                if (leftDown && rightDown)
                {
                    fillLeft = left;
                    fillRight = right;
                }
                else if (!leftDown && !rightDown)
                {
                    continue;
                }
                else
                {
                    float denom = sRight - sLeft;
                    if (Math.Abs(denom) <= 1e-6f)
                        continue;

                    float t = MathHelper.Clamp(-sLeft / denom, 0f, 1f);
                    float xCross = left + t * width;

                    if (leftDown)
                    {
                        fillLeft = left;
                        fillRight = xCross;
                    }
                    else
                    {
                        fillLeft = xCross;
                        fillRight = right;
                    }
                }

                float w = fillRight - fillLeft;
                if (w <= 0.5f)
                    continue;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(fillLeft + w * 0.5f, y + h * 0.5f),
                    Size = new Vector2(w, h),
                    Color = color,
                    Alignment = TextAlignment.CENTER
                });

                if (!overlayBackground)
                    continue;

                var clip = new Rectangle(
                    (int)Math.Floor(fillLeft),
                    (int)Math.Floor(y),
                    Math.Max(1, (int)Math.Ceiling(w)),
                    Math.Max(1, (int)Math.Ceiling(h)));
                sprites.Add(MySprite.CreateClipRect(clip));
                AddBackground(sprites);
                RestoreTextureClip(sprites);
            }
        }

        void DrawClippedRectangle(
            List<MySprite> sprites,
            Vector2 center,
            Vector2 size,
            string texture,
            Color color,
            float rotation)
        {
            if (color.A == 0 || size.X <= 0f || size.Y <= 0f)
                return;

            sprites.Add(MySprite.CreateClipRect(GetTextureClipRect()));
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = texture,
                Position = center,
                Size = size,
                Color = color,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = rotation
            });
            RestoreTextureClip(sprites);
        }

        Rectangle GetTextureClipRect()
        {
            RectangleF bounds = GetTextureBounds();
            return new Rectangle(
                (int)Math.Floor(bounds.X),
                (int)Math.Floor(bounds.Y),
                Math.Max(1, (int)Math.Ceiling(bounds.Width)),
                Math.Max(1, (int)Math.Ceiling(bounds.Height)));
        }

        RectangleF GetTextureBounds()
        {
            Vector2 textureSize = Surface?.TextureSize ?? Vector2.Zero;
            var textureBounds = new RectangleF(
                0f,
                0f,
                Math.Max(1f, textureSize.X),
                Math.Max(1f, textureSize.Y));
            var viewBox = ViewBox;
            if (viewBox.Width <= 0f || viewBox.Height <= 0f)
                return textureBounds;

            float left = Math.Min(textureBounds.X, viewBox.X);
            float top = Math.Min(textureBounds.Y, viewBox.Y);
            float right = Math.Max(textureBounds.Right, viewBox.Right);
            float bottom = Math.Max(textureBounds.Bottom, viewBox.Bottom);
            return new RectangleF(
                left,
                top,
                Math.Max(1f, right - left),
                Math.Max(1f, bottom - top));
        }

        void RestoreTextureClip(List<MySprite> sprites)
        {
            sprites.Add(MySprite.CreateClearClipRect());
        }

        bool DrawSolar_SystemOrbitMap(
            List<MySprite> ringSprites,
            List<MySprite> frontRingSprites,
            Dictionary<long, MyPlanet> planets)
        {
            ClearCachedInteractiveEntries();

            if (planets == null || planets.Count == 0)
                return false;

            bool hasDetectedPlanets = false;

            var positions = new List<Vector3D>(planets.Count);
            var displayPositions = new List<Vector3D>(planets.Count);
            var radii = new List<double>(planets.Count);
            var projectedPlanets = new List<PlanetProjection>(planets.Count);
            var parentIndex = new List<int>(planets.Count);
            var orbitDistances = new List<double>(planets.Count);
            var orbitPlaneNormals = new List<Vector3D>(planets.Count);
            var ringProjections = new List<StaticRingProjection>(planets.Count);

            Vector3 cameraViewDirection;
            Vector3 cameraScreenRight;
            Vector3 cameraScreenUp;
            _staticOrbitControl.BuildProjection(
                new Vector3(0f, 0.75f, 1f),
                Vector3.Up,
                out cameraViewDirection,
                out cameraScreenRight,
                out cameraScreenUp);

            var referencePos = Block != null ? Block.GetPosition() : Vector3D.Zero;

            foreach (var kv in planets)
            {
                var planet = kv.Value;
                if (planet == null || planet.MarkedForClose)
                    continue;

                double radius = planet.AverageRadius;
                if (radius <= 0d)
                    continue;
                hasDetectedPlanets = true;

                Vector3D pos = planet.WorldMatrix.Translation;
                double distanceToBlock = Block != null
                    ? Vector3D.Distance(pos, referencePos)
                    : pos.Length();
                positions.Add(pos);
                displayPositions.Add(Vector3D.Zero);
                radii.Add(radius);
                parentIndex.Add(-1);
                orbitDistances.Add(pos.Length());
                orbitPlaneNormals.Add(Vector3D.Up);

                string name;
                if (!PlanetHelper.PlanetNamesById.TryGetValue(planet.EntityId, out name))
                    name = planet.Name;
                var generator = planet.Generator;
                var atmosphere = generator != null ? generator.Atmosphere : null;
                MyTemperatureLevel? averageTemperature = generator != null
                    ? generator.DefaultSurfaceTemperature
                    : (MyTemperatureLevel?)null;
                double surfaceGravity = planet.GetInitArguments.SurfaceGravity;
                double gravityFalloff = planet.GetInitArguments.GravityFalloff;
                double gravityLimitRadius = 0d;
                if (planet.MaximumRadius > 0d && surfaceGravity > 0d && gravityFalloff > 0d)
                {
                    gravityLimitRadius = planet.MaximumRadius *
                                         Math.Pow(surfaceGravity / 0.05d, 1d / gravityFalloff);
                }

                Vector3 viewDirectionLocal;
                Vector3 screenRightLocal;
                Vector3 screenUpLocal;
                BuildPlanetLocalProjection(
                    planet,
                    new Vector3D(cameraViewDirection.X, cameraViewDirection.Y, cameraViewDirection.Z),
                    new Vector3D(cameraScreenRight.X, cameraScreenRight.Y, cameraScreenRight.Z),
                    new Vector3D(cameraScreenUp.X, cameraScreenUp.Y, cameraScreenUp.Z),
                    out viewDirectionLocal,
                    out screenRightLocal,
                    out screenUpLocal);
                var projection = new PlanetProjection
                {
                    PlanetId = planet.EntityId,
                    Name = string.IsNullOrWhiteSpace(name)
                        ? LocHelper.GetLoc(MOD_PREFIX + "ClockDashboard_UnknownPlanet")
                        : name,
                    GpsColor = ResolvePlanetTexture(planet).BaseColor,
                    WorldPosition = pos,
                    Direction = Vector3D.Zero,
                    ViewDirectionLocal = viewDirectionLocal,
                    ScreenRightLocal = screenRightLocal,
                    ScreenUpLocal = screenUpLocal,
                    Distance = distanceToBlock,
                    CameraDepth = 0d,
                    Visibility = 1f,
                    AngularRadius = 0d,
                    ScreenPos = Vector2.Zero,
                    MarkerRadius = 0f,
                    ShouldDisplayInfo = false,
                    Radius = (float)radius,
                    SurfaceGravityG = (float)surfaceGravity,
                    GravityRange = (float)Math.Max(0d, gravityLimitRadius - planet.AverageRadius),
                    AtmosphereDensity = planet.HasAtmosphere && atmosphere != null ? atmosphere.Density : 0f,
                    OxygenDensity = planet.HasAtmosphere && atmosphere != null ? atmosphere.OxygenDensity : 0f,
                    AverageTemperature = averageTemperature,
                    MaxWindSpeed = atmosphere != null ? atmosphere.MaxWindSpeed : 0f
                };
                CachePlanetInfoLines(ref projection);
                projectedPlanets.Add(projection);
            }

            if (!hasDetectedPlanets)
                return false;

            // Smaller nearby planets are treated as moons and exaggerated around
            // their parent, while the parent bodies retain their real system-space
            // positions.
            for (int i = 0; i < projectedPlanets.Count; i++)
            {
                double childRadius = radii[i];
                int bestParent = -1;
                double bestDist = double.MaxValue;
                for (int j = 0; j < projectedPlanets.Count; j++)
                {
                    if (i == j || radii[j] <= childRadius)
                        continue;

                    double distance = Vector3D.Distance(positions[i], positions[j]);
                    if (distance <= STATIC_PARENT_ORBIT_MAX_METERS && distance < bestDist)
                    {
                        bestDist = distance;
                        bestParent = j;
                    }
                }

                parentIndex[i] = bestParent;
                Vector3D orbitOffset = bestParent >= 0
                    ? positions[i] - positions[bestParent]
                    : positions[i];
                orbitDistances[i] = orbitOffset.Length();
            }

            var computeOrder = new List<int>(projectedPlanets.Count);
            for (int i = 0; i < projectedPlanets.Count; i++)
                computeOrder.Add(i);
            computeOrder.Sort(delegate(int a, int b) { return radii[b].CompareTo(radii[a]); });

            double baseHalfFov = MathHelper.ToRadians(MAP_VERTICAL_FOV_DEFAULT_DEG) * 0.5;
            double currentHalfFov = MathHelper.ToRadians(Math.Max(0.1f, _fov)) * 0.5;
            float magnification = MathHelper.Clamp(
                (float)(Math.Tan(baseHalfFov) / Math.Tan(currentHalfFov)),
                0.25f,
                STATIC_MAX_MAGNIFICATION);
            float staticPlanetScale = GetStaticPlanetScale(magnification);

            double maxDisplayDistance = 1d;
            double maxRealDisplayDistance = 1d;
            foreach (int i in computeOrder)
            {
                int parent = parentIndex[i];
                float radiusScale = parent >= 0 ? staticPlanetScale : 1f;
                Vector3D ringCenter = parent >= 0 ? displayPositions[parent] : Vector3D.Zero;
                Vector3D sourceCenter = parent >= 0 ? positions[parent] : Vector3D.Zero;
                Vector3D relative = positions[i] - sourceCenter;
                Vector3D displayPosition = ringCenter + relative * radiusScale;
                displayPositions[i] = displayPosition;

                Vector3D orbitRadial = displayPosition - ringCenter;
                Vector3D referenceNormal = parent >= 0
                    ? orbitPlaneNormals[parent]
                    : Vector3D.Up;
                orbitPlaneNormals[i] = BuildStaticOrbitPlaneNormal(
                    orbitRadial,
                    referenceNormal);

                double extent = ringCenter.Length() +
                                orbitRadial.Length() +
                                radii[i] * radiusScale;
                if (extent > maxDisplayDistance)
                    maxDisplayDistance = extent;

                Vector3D realRingCenter = parent >= 0 ? positions[parent] : Vector3D.Zero;
                Vector3D realSourceCenter = parent >= 0 ? positions[parent] : Vector3D.Zero;
                Vector3D realOrbitRadial = positions[i] - realSourceCenter;
                double realExtent = realRingCenter.Length() +
                                    realOrbitRadial.Length() +
                                    radii[i];
                if (realExtent > maxRealDisplayDistance)
                    maxRealDisplayDistance = realExtent;
            }

            float maxOrbitPx = Math.Min(ViewBox.Width, ViewBox.Height) * 0.45f;
            if (maxOrbitPx <= 1f)
                return false;

            double worldToPx = maxOrbitPx / maxDisplayDistance * magnification;
            double maxRealWorldToPx = maxOrbitPx / maxRealDisplayDistance * STATIC_MAX_MAGNIFICATION;
            var center = ViewBox.Center;
            Vector3D cameraViewWorld = new Vector3D(
                cameraViewDirection.X,
                cameraViewDirection.Y,
                cameraViewDirection.Z);
            _staticPanScreenRightWorld = new Vector3D(
                cameraScreenRight.X,
                cameraScreenRight.Y,
                cameraScreenRight.Z);
            _staticPanScreenUpWorld = new Vector3D(
                cameraScreenUp.X,
                cameraScreenUp.Y,
                cameraScreenUp.Z);
            _staticPanWorldUnitsPerPixel = worldToPx > 1e-12d
                ? 1d / worldToPx
                : 0d;
            _staticPanProjectionValid = _staticPanWorldUnitsPerPixel > 0d;
            Vector3D cameraTargetWorld = ResolveStaticCameraTarget(
                projectedPlanets,
                displayPositions);
            double cameraDistanceWorld = CalculateStaticCameraDistance(
                ViewBox,
                worldToPx,
                baseHalfFov);
            Vector3D cameraPositionWorld =
                cameraTargetWorld + cameraViewWorld * cameraDistanceWorld;

            foreach (int i in computeOrder)
            {
                int parent = parentIndex[i];
                bool isMoon = parent >= 0;
                float radiusScale = isMoon ? staticPlanetScale : 1f;
                Vector3D ringCenter = isMoon
                    ? displayPositions[parent]
                    : Vector3D.Zero;
                Vector3D displayPosition = displayPositions[i];

                var projection = projectedPlanets[i];
                projection.ScreenPos = ProjectStaticPoint(
                    displayPosition,
                    cameraTargetWorld,
                    center,
                    worldToPx,
                    cameraScreenRight,
                    cameraScreenUp);
                projection.CameraDepth = CalculateStaticCameraDepth(
                    displayPosition,
                    cameraPositionWorld,
                    cameraViewWorld);
                projection.MarkerRadius = GetStaticMarkerRadius(
                    radii[i],
                    isMoon,
                    magnification,
                    maxRealWorldToPx,
                    Scale);
                projectedPlanets[i] = projection;

                double orbitRadius = orbitDistances[i] * radiusScale;
                if (projection.CameraDepth > STATIC_CAMERA_NEAR_CLIP_DEPTH &&
                    orbitDistances[i] >= STATIC_ORBIT_MIN_RING_METERS &&
                    orbitRadius * worldToPx >= 2d)
                {
                    Vector3D axisX = displayPosition - ringCenter;
                    axisX = Vector3D.Normalize(axisX);
                    Vector3D axisY = Vector3D.Cross(orbitPlaneNormals[i], axisX);
                    axisY = Vector3D.Normalize(axisY);
                    ringProjections.Add(new StaticRingProjection
                    {
                        OwnerPlanetId = projection.PlanetId,
                        CenterWorld = ringCenter,
                        AxisXWorld = axisX,
                        AxisYWorld = axisY,
                        RadiusWorld = orbitRadius,
                        IsMoonRing = isMoon,
                        CameraDepth = CalculateStaticCameraDepth(
                            ringCenter,
                            cameraPositionWorld,
                            cameraViewWorld)
                    });
                }
            }

            ringProjections.Sort(delegate(StaticRingProjection a, StaticRingProjection b)
            {
                int depth = b.CameraDepth.CompareTo(a.CameraDepth);
                if (depth != 0)
                    return depth;
                if (a.IsMoonRing != b.IsMoonRing)
                    return a.IsMoonRing ? 1 : -1;
                return b.RadiusWorld.CompareTo(a.RadiusWorld);
            });

            if (!_planetariumOrbitCacheValid)
            {
                int ringStartIndex = ringSprites.Count;
                int frontRingStartIndex = frontRingSprites.Count;
                var ringColor = new Color(
                    ForegroundColor.R,
                    ForegroundColor.G,
                    ForegroundColor.B,
                    byte.MaxValue);
                float ringLineWidth = Math.Max(1f, StaticOrbitLineThicknessPx * Scale);
                for (int i = 0; i < ringProjections.Count; i++)
                {
                    DrawProjectedOrbitRing(
                        ringSprites,
                        frontRingSprites,
                        projectedPlanets,
                        ringProjections[i].OwnerPlanetId,
                        ringProjections[i].CenterWorld,
                        ringProjections[i].AxisXWorld,
                        ringProjections[i].AxisYWorld,
                        ringProjections[i].RadiusWorld,
                        cameraTargetWorld,
                        center,
                        worldToPx,
                        cameraScreenRight,
                        cameraScreenUp,
                        cameraViewDirection,
                        cameraPositionWorld,
                        ringLineWidth,
                        ringColor);
                }

                _cachedStaticRingSprites.Clear();
                for (int i = ringStartIndex; i < ringSprites.Count; i++)
                    _cachedStaticRingSprites.Add(ringSprites[i]);

                _cachedStaticFrontRingSprites.Clear();
                for (int i = frontRingStartIndex; i < frontRingSprites.Count; i++)
                    _cachedStaticFrontRingSprites.Add(frontRingSprites[i]);

                _planetariumOrbitCacheValid = true;
            }
            else
            {
                ringSprites.AddRange(_cachedStaticRingSprites);
                frontRingSprites.AddRange(_cachedStaticFrontRingSprites);
            }

            projectedPlanets.Sort(delegate(PlanetProjection a, PlanetProjection b)
            {
                int depth = b.CameraDepth.CompareTo(a.CameraDepth);
                return depth != 0 ? depth : a.MarkerRadius.CompareTo(b.MarkerRadius);
            });
            for (int i = 0; i < projectedPlanets.Count; i++)
            {
                var planet = projectedPlanets[i];
                if (planet.CameraDepth <= STATIC_CAMERA_NEAR_CLIP_DEPTH)
                    continue;

                DrawPlanet(planet);
                projectedPlanets[i] = planet;
            }

            DrawStaticGpsMarkers(
                positions,
                displayPositions,
                radii,
                parentIndex,
                cameraTargetWorld,
                center,
                worldToPx,
                cameraScreenRight,
                cameraScreenUp,
                cameraPositionWorld,
                cameraViewWorld,
                staticPlanetScale);
            DrawStaticRadioSignalMarkers(
                positions,
                displayPositions,
                radii,
                parentIndex,
                cameraTargetWorld,
                center,
                worldToPx,
                cameraScreenRight,
                cameraScreenUp,
                cameraPositionWorld,
                cameraViewWorld,
                staticPlanetScale);

            _dynamicMapCacheValid = false;
            _cachedDynamicRingSprites.Clear();
            _cachedOverlaySprites.Clear();
            return true;
        }

        void DrawStaticGpsMarkers(
            List<Vector3D> planetPositions,
            List<Vector3D> displayPositions,
            List<double> planetRadii,
            List<int> parentIndices,
            Vector3D cameraTargetWorld,
            Vector2 center,
            double worldToPx,
            Vector3 cameraScreenRight,
            Vector3 cameraScreenUp,
            Vector3D cameraPositionWorld,
            Vector3D cameraViewWorld,
            float staticPlanetScale)
        {
            StarMapConfigComponent config = StarMapComponent;
            GpsDisplayWaypoint[] alwaysDisplayed = config.AlwaysDisplayedGpsWaypoints ?? Array.Empty<GpsDisplayWaypoint>();
            int[] legacyAlwaysDisplayed = config.AlwaysDisplayedGpsHashes ?? Array.Empty<int>();
            bool needsLiveGps = config.DisplayMyGps || legacyAlwaysDisplayed.Length != 0;
            if (!needsLiveGps && alwaysDisplayed.Length == 0)
                return;

            float scale = Math.Max(0.5f, Scale);
            float markerSize = STATIC_GPS_MARKER_SIZE_PX * scale;
            RectangleF bounds = ViewBox;

            _gpsMarkerProjections.Clear();
            if (needsLiveGps)
            {
                var session = MyAPIGateway.Session;
                var player = session == null ? null : session.Player;
                if (session != null && session.GPS != null && player != null)
                {
                    _gpsEntries.Clear();
                    session.GPS.GetGpsList(player.IdentityId, _gpsEntries);

                    for (int i = 0; i < _gpsEntries.Count; i++)
                    {
                        IMyGps gps = _gpsEntries[i];
                        if (!GpsMarkerLayout.ShouldRenderLiveGps(
                            gps,
                            config.DisplayMyGps,
                            alwaysDisplayed,
                            legacyAlwaysDisplayed))
                        {
                            continue;
                        }

                        AddStaticGpsMarkerProjection(
                            GpsMarkerLayout.FromGps(gps),
                            planetPositions,
                            displayPositions,
                            planetRadii,
                            parentIndices,
                            cameraTargetWorld,
                            center,
                            worldToPx,
                            cameraScreenRight,
                            cameraScreenUp,
                            cameraPositionWorld,
                            cameraViewWorld,
                            staticPlanetScale,
                            bounds,
                            markerSize);
                    }
                }
            }

            for (int i = 0; i < alwaysDisplayed.Length; i++)
            {
                GpsMarker marker;
                if (!GpsMarkerLayout.TryCreateMarker(alwaysDisplayed[i], out marker))
                    continue;

                AddStaticGpsMarkerProjection(
                    marker,
                    planetPositions,
                    displayPositions,
                    planetRadii,
                    parentIndices,
                    cameraTargetWorld,
                    center,
                    worldToPx,
                    cameraScreenRight,
                    cameraScreenUp,
                    cameraPositionWorld,
                    cameraViewWorld,
                    staticPlanetScale,
                    bounds,
                    markerSize);
            }

            GpsMarkerLayout.Cluster(
                _gpsMarkerProjections,
                STATIC_GPS_CLUSTER_DISTANCE_PX * scale,
                _gpsMarkerClusters,
                _gpsMarkerClusterConsumed);

            for (int i = 0; i < _gpsMarkerClusters.Count; i++)
                DrawStaticGpsMarker(_gpsMarkerClusters[i], scale, markerSize);
        }

        void AddStaticGpsMarkerProjection(
            GpsMarker marker,
            List<Vector3D> planetPositions,
            List<Vector3D> displayPositions,
            List<double> planetRadii,
            List<int> parentIndices,
            Vector3D cameraTargetWorld,
            Vector2 center,
            double worldToPx,
            Vector3 cameraScreenRight,
            Vector3 cameraScreenUp,
            Vector3D cameraPositionWorld,
            Vector3D cameraViewWorld,
            float staticPlanetScale,
            RectangleF bounds,
            float markerSize)
        {
            Vector3D displayPosition = TransformPointForStaticMap(
                marker.WorldPosition,
                planetPositions,
                displayPositions,
                planetRadii,
                parentIndices,
                staticPlanetScale);
            double cameraDepth = CalculateStaticCameraDepth(
                displayPosition,
                cameraPositionWorld,
                cameraViewWorld);
            if (cameraDepth <= STATIC_CAMERA_NEAR_CLIP_DEPTH)
                return;

            Vector2 screenPosition = ProjectStaticPoint(
                displayPosition,
                cameraTargetWorld,
                center,
                worldToPx,
                cameraScreenRight,
                cameraScreenUp);
            if (screenPosition.X < bounds.X - markerSize ||
                screenPosition.X > bounds.Right + markerSize ||
                screenPosition.Y < bounds.Y - markerSize ||
                screenPosition.Y > bounds.Bottom + markerSize)
            {
                return;
            }

            _gpsMarkerProjections.Add(new GpsMarkerProjection
            {
                Marker = marker,
                ScreenPosition = screenPosition
            });
        }

        void DrawStaticGpsMarker(
            GpsMarkerCluster cluster,
            float scale,
            float baseMarkerSize)
        {
            GpsMarker marker = cluster.RepresentativeMarker;

            bool isCluster = cluster.Count > 1;
            float markerSize = isCluster ? baseMarkerSize * 1.35f : baseMarkerSize;
            float markerRadius = markerSize * 0.5f;
            float textScale = STATIC_GPS_LABEL_SCALE * scale;
            float lineHeight = MeasureLineHeight(textScale);
            Vector2 screenPosition = cluster.ScreenPosition;
            Color color = marker.Color;
            color.A = byte.MaxValue;
            Color shadow = new Color(0, 0, 0, 210);

            _overlaySprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = screenPosition + new Vector2(scale, scale),
                Size = new Vector2(markerSize * 1.25f),
                Color = shadow,
                Alignment = TextAlignment.CENTER
            });
            _overlaySprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "CircleHollow",
                Position = screenPosition,
                Size = new Vector2(markerSize),
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            if (isCluster)
            {
                string count = cluster.Count.ToString();
                Vector2 countShadowOffset = new Vector2(scale, scale);
                _overlaySprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = count,
                    Position = screenPosition + countShadowOffset,
                    RotationOrScale = textScale * 0.8f,
                    Color = shadow,
                    Alignment = TextAlignment.CENTER,
                    FontId = TextFont
                });
                _overlaySprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = count,
                    Position = screenPosition,
                    RotationOrScale = textScale * 0.8f,
                    Color = color,
                    Alignment = TextAlignment.CENTER,
                    FontId = TextFont
                });
            }

            string name = isCluster
                ? string.Format(
                    FormatingHelper.Culture,
                    LocHelper.GetLoc(MOD_PREFIX + "Gps_ClusterFormat"),
                    cluster.Count)
                : (string.IsNullOrWhiteSpace(marker.Name)
                    ? LocHelper.GetLoc(MOD_PREFIX + "Gps_Unnamed")
                    : marker.Name);
            Vector2 labelPosition = new Vector2(
                screenPosition.X + markerRadius + STATIC_GPS_LABEL_GAP_PX * scale,
                screenPosition.Y - lineHeight * 0.5f);
            Vector2 shadowOffset = new Vector2(scale, scale);
            _overlaySprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = name,
                Position = labelPosition + shadowOffset,
                RotationOrScale = textScale,
                Color = shadow,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
            _overlaySprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = name,
                Position = labelPosition,
                RotationOrScale = textScale,
                Color = color,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
        }

        void DrawStaticRadioSignalMarkers(
            List<Vector3D> planetPositions,
            List<Vector3D> displayPositions,
            List<double> planetRadii,
            List<int> parentIndices,
            Vector3D cameraTargetWorld,
            Vector2 center,
            double worldToPx,
            Vector3 cameraScreenRight,
            Vector3 cameraScreenUp,
            Vector3D cameraPositionWorld,
            Vector3D cameraViewWorld,
            float staticPlanetScale)
        {
            if (!StarMapComponent.IncludeRadioSignals)
                return;

            RefreshStaticRadioSignals();
            if (_radioSignals.Count == 0)
                return;

            float scale = Math.Max(0.5f, Scale);
            float markerSize = STATIC_RADIO_SIGNAL_MARKER_SIZE_PX * scale;
            RectangleF bounds = ViewBox;

            _radioSignalMarkerProjections.Clear();
            for (int i = 0; i < _radioSignals.Count; i++)
            {
                RadioSignalMarker marker = _radioSignals[i];
                Vector3D displayPosition = TransformPointForStaticMap(
                    marker.WorldPosition,
                    planetPositions,
                    displayPositions,
                    planetRadii,
                    parentIndices,
                    staticPlanetScale);
                double cameraDepth = CalculateStaticCameraDepth(
                    displayPosition,
                    cameraPositionWorld,
                    cameraViewWorld);
                if (cameraDepth <= STATIC_CAMERA_NEAR_CLIP_DEPTH)
                    continue;

                Vector2 screenPosition = ProjectStaticPoint(
                    displayPosition,
                    cameraTargetWorld,
                    center,
                    worldToPx,
                    cameraScreenRight,
                    cameraScreenUp);
                if (screenPosition.X < bounds.X - markerSize ||
                    screenPosition.X > bounds.Right + markerSize ||
                    screenPosition.Y < bounds.Y - markerSize ||
                    screenPosition.Y > bounds.Bottom + markerSize)
                {
                    continue;
                }

                _radioSignalMarkerProjections.Add(new RadioSignalMarkerProjection
                {
                    Marker = marker,
                    ScreenPosition = screenPosition
                });
            }

            RadioSignalMarkerLayout.Cluster(
                _radioSignalMarkerProjections,
                STATIC_RADIO_SIGNAL_CLUSTER_DISTANCE_PX * scale,
                _radioSignalMarkerClusters,
                _radioSignalMarkerClusterConsumed);

            for (int i = 0; i < _radioSignalMarkerClusters.Count; i++)
                DrawStaticRadioSignalMarker(_radioSignalMarkerClusters[i], scale, markerSize);
        }

        void RefreshStaticRadioSignals()
        {
            long frame = GetCurrentGameFrame();
            if (_lastRadioSignalRefreshFrame != long.MinValue &&
                frame >= _lastRadioSignalRefreshFrame &&
                frame - _lastRadioSignalRefreshFrame < STATIC_RADIO_SIGNAL_REFRESH_FRAMES)
            {
                return;
            }

            _lastRadioSignalRefreshFrame = frame;
            _radioSignalCollector.Collect(Block, _radioSignals);
        }

        void DrawStaticRadioSignalMarker(
            RadioSignalMarkerCluster cluster,
            float scale,
            float baseMarkerSize)
        {
            bool isCluster = cluster.Count > 1;
            RadioSignalMarker marker = cluster.RepresentativeMarker;
            float markerSize = isCluster ? baseMarkerSize * 1.35f : baseMarkerSize;
            float markerRadius = markerSize * 0.5f;
            float textScale = STATIC_RADIO_SIGNAL_LABEL_SCALE * scale;
            float lineHeight = MeasureLineHeight(textScale);
            Vector2 screenPosition = cluster.ScreenPosition;
            Color color = ResolveRadioSignalColor(marker.Relationship);
            color.A = byte.MaxValue;
            Color shadow = new Color(0, 0, 0, 210);
            string texture = isCluster ? "CircleHollow" : ResolveRadioSignalTexture(marker.Relationship);

            _overlaySprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = texture,
                Position = screenPosition + new Vector2(scale, scale),
                Size = new Vector2(markerSize * 1.25f),
                Color = shadow,
                Alignment = TextAlignment.CENTER
            });
            _overlaySprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = texture,
                Position = screenPosition,
                Size = new Vector2(markerSize),
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            if (isCluster)
            {
                string count = cluster.Count.ToString();
                Vector2 countShadowOffset = new Vector2(scale, scale);
                _overlaySprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = count,
                    Position = screenPosition + countShadowOffset,
                    RotationOrScale = textScale * 0.8f,
                    Color = shadow,
                    Alignment = TextAlignment.CENTER,
                    FontId = TextFont
                });
                _overlaySprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = count,
                    Position = screenPosition,
                    RotationOrScale = textScale * 0.8f,
                    Color = color,
                    Alignment = TextAlignment.CENTER,
                    FontId = TextFont
                });
            }

            string name = isCluster
                ? string.Format(
                    FormatingHelper.Culture,
                    LocHelper.GetLoc(MOD_PREFIX + "RadioSignal_ClusterFormat"),
                    cluster.Count)
                : GetRadioSignalName(marker.Name);
            string signalName = GetRadioSignalName(marker.Name);
            RegisterStaticMarkerHitbox(
                _staticRadioMarkerInteractiveStates,
                marker.EntityId,
                signalName,
                marker.WorldPosition,
                color,
                screenPosition,
                markerSize);
            Vector2 labelPosition = new Vector2(
                screenPosition.X + markerRadius + STATIC_RADIO_SIGNAL_LABEL_GAP_PX * scale,
                screenPosition.Y - lineHeight * 0.5f);
            Vector2 shadowOffset = new Vector2(scale, scale);
            _overlaySprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = name,
                Position = labelPosition + shadowOffset,
                RotationOrScale = textScale,
                Color = shadow,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
            _overlaySprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = name,
                Position = labelPosition,
                RotationOrScale = textScale,
                Color = color,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
        }

        void RegisterStaticMarkerHitbox(
            Dictionary<long, StaticMarkerInteractiveState> states,
            long key,
            string name,
            Vector3D position,
            Color color,
            Vector2 screenPosition,
            float markerSize)
        {
            StaticMarkerInteractiveState state;
            if (!states.TryGetValue(key, out state))
            {
                state = new StaticMarkerInteractiveState();
                state.Entry = AddLogicalChild(
                    new RectangleControl(
                        default(RectangleF),
                        CursorType.Hand,
                        null,
                        OnStaticMarkerClicked));
                state.Entry.CustomRender = RenderStaticMarkerHitbox;
                state.Entry.ClickSound = AudioHelper.HudGps3;
                state.Entry.SetDataContext(state);
                states[key] = state;
            }

            state.Name = name;
            state.Position = position;
            state.Color = color;
            state.UsedThisFrame = true;
            state.Entry.SetRect(GetStaticMarkerHitbox(screenPosition, markerSize));
            state.Entry.SetVisible(true);
            _children.Add(state.Entry);
        }

        static RectangleF GetStaticMarkerHitbox(Vector2 screenPosition, float markerSize)
        {
            float size = Math.Max(STATIC_MARKER_HITBOX_MIN_PX, markerSize * 1.25f);
            return new RectangleF(
                screenPosition.X - size * 0.5f,
                screenPosition.Y - size * 0.5f,
                size,
                size);
        }

        static void RenderStaticMarkerHitbox(ControlTemplate control, List<MySprite> sprites)
        {
        }

        void OnStaticMarkerClicked(object dataContext, object sender)
        {
            var marker = dataContext as StaticMarkerInteractiveState;
            if (marker == null)
                return;

            CreateLocalGpsCopy(marker.Name, marker.Position, marker.Color);
        }

        Color ResolveRadioSignalColor(MyRelationsBetweenPlayerAndBlock relationship)
        {
            switch (relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return ColorComponent.ResolveErrorColor();
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    return GetHeaderColor();
                default:
                    return ColorComponent.ResolveWarningColor();
            }
        }

        static string ResolveRadioSignalTexture(MyRelationsBetweenPlayerAndBlock relationship)
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

        static Vector3D TransformPointForStaticMap(
            Vector3D gpsPosition,
            List<Vector3D> planetPositions,
            List<Vector3D> displayPositions,
            List<double> planetRadii,
            List<int> parentIndices,
            float staticPlanetScale)
        {
            int nearestPlanet = -1;
            double nearestSurfaceDistance = double.MaxValue;
            for (int i = 0; i < planetPositions.Count; i++)
            {
                double centerDistance = Vector3D.Distance(gpsPosition, planetPositions[i]);
                double surfaceDistance = Math.Abs(centerDistance - planetRadii[i]);
                if (surfaceDistance < nearestSurfaceDistance)
                {
                    nearestSurfaceDistance = surfaceDistance;
                    nearestPlanet = i;
                }
            }

            if (nearestPlanet < 0 || parentIndices[nearestPlanet] < 0)
                return gpsPosition;

            Vector3D localOffset = gpsPosition - planetPositions[nearestPlanet];
            double localRange = Math.Max(
                STATIC_GPS_MOON_LOCAL_RANGE_METERS,
                planetRadii[nearestPlanet] * 5d);
            if (localOffset.LengthSquared() > localRange * localRange)
                return gpsPosition;

            return displayPositions[nearestPlanet] + localOffset * staticPlanetScale;
        }

        static float GetStaticPlanetScale(float magnification)
        {
            return MathHelper.Lerp(
                STATIC_PLANET_SCALE,
                1f,
                GetStaticRealScaleBlend(magnification));
        }

        static float GetStaticRealScaleBlend(float magnification)
        {
            float amount = MathHelper.Clamp(
                (magnification - STATIC_REAL_SCALE_BLEND_START_MAGNIFICATION) /
                (STATIC_MAX_MAGNIFICATION - STATIC_REAL_SCALE_BLEND_START_MAGNIFICATION),
                0f,
                1f);
            return amount * amount * (3f - 2f * amount);
        }

        static float GetStaticMarkerRadius(
            double planetRadiusMeters,
            bool isMoon,
            float magnification,
            double maxRealWorldToPx,
            float surfaceScale)
        {
            float baseReadableRadius =
                (isMoon ? STATIC_MOON_BODY_RADIUS_PX : STATIC_PLANET_BODY_RADIUS_PX) *
                surfaceScale;
            float readableRadius = baseReadableRadius * magnification;
            float finalRealRadius = (float)(planetRadiusMeters * maxRealWorldToPx);

            if (float.IsNaN(finalRealRadius) ||
                float.IsInfinity(finalRealRadius) ||
                finalRealRadius <= 0f)
            {
                return readableRadius;
            }

            if (magnification <= STATIC_REAL_SCALE_BLEND_START_MAGNIFICATION)
                return Math.Max(surfaceScale, readableRadius);

            float startRadius =
                baseReadableRadius * STATIC_REAL_SCALE_BLEND_START_MAGNIFICATION;
            float amount = MathHelper.Clamp(
                (magnification - STATIC_REAL_SCALE_BLEND_START_MAGNIFICATION) /
                (STATIC_MAX_MAGNIFICATION - STATIC_REAL_SCALE_BLEND_START_MAGNIFICATION),
                0f,
                1f);
            float slope = Math.Min(
                baseReadableRadius,
                Math.Max(0f, 3f * (finalRealRadius - startRadius) /
                    (STATIC_MAX_MAGNIFICATION - STATIC_REAL_SCALE_BLEND_START_MAGNIFICATION)));

            return Math.Max(
                surfaceScale,
                HermiteInterpolate(startRadius, finalRealRadius, slope, 0f, amount));
        }

        static float HermiteInterpolate(
            float start,
            float end,
            float startSlope,
            float endSlope,
            float amount)
        {
            float t2 = amount * amount;
            float t3 = t2 * amount;
            float range = STATIC_MAX_MAGNIFICATION - STATIC_REAL_SCALE_BLEND_START_MAGNIFICATION;

            return
                (2f * t3 - 3f * t2 + 1f) * start +
                (t3 - 2f * t2 + amount) * range * startSlope +
                (-2f * t3 + 3f * t2) * end +
                (t3 - t2) * range * endSlope;
        }

        static Vector2 ProjectStaticPoint(
            Vector3D worldPosition,
            Vector3D cameraTargetWorld,
            Vector2 center,
            double worldToPx,
            Vector3 screenRight,
            Vector3 screenUp)
        {
            Vector3D relative = worldPosition - cameraTargetWorld;
            return new Vector2(
                center.X + (float)(Vector3D.Dot(relative, new Vector3D(screenRight.X, screenRight.Y, screenRight.Z)) * worldToPx),
                center.Y - (float)(Vector3D.Dot(relative, new Vector3D(screenUp.X, screenUp.Y, screenUp.Z)) * worldToPx));
        }

        Vector3D ResolveStaticCameraTarget(
            List<PlanetProjection> projectedPlanets,
            List<Vector3D> displayPositions)
        {
            if (_staticFocusPlanetId == 0L)
                return _staticCameraTargetOffsetWorld;

            for (int i = 0; i < projectedPlanets.Count && i < displayPositions.Count; i++)
            {
                if (projectedPlanets[i].PlanetId == _staticFocusPlanetId)
                    return displayPositions[i] + _staticCameraTargetOffsetWorld;
            }

            _staticFocusPlanetId = 0L;
            return _staticCameraTargetOffsetWorld;
        }

        static double CalculateStaticCameraDistance(
            RectangleF viewport,
            double worldToPx,
            double halfFovY)
        {
            const double epsilon = 1e-12d;
            if (worldToPx <= epsilon)
                return 1d;

            double tanHalfFov = Math.Tan(halfFovY);
            if (tanHalfFov <= epsilon)
                return 1d;

            // BuildProjection returns the target-to-camera direction. The
            // viewport's world-space half-height places the camera beyond the
            // visible rectangle while allowing zoom to move it toward the map.
            double viewportHalfHeightWorld =
                Math.Max(1d, viewport.Height * 0.5d / worldToPx);
            return viewportHalfHeightWorld / tanHalfFov;
        }

        static double CalculateStaticCameraDepth(
            Vector3D worldPosition,
            Vector3D cameraPositionWorld,
            Vector3D targetToCameraDirection)
        {
            // Positive values lie in front of the camera. BuildProjection exposes
            // target-to-camera, so camera forward is its negation.
            return Vector3D.Dot(
                cameraPositionWorld - worldPosition,
                targetToCameraDirection);
        }

        static Vector3D BuildStaticOrbitPlaneNormal(
            Vector3D orbitRadial,
            Vector3D referenceNormal)
        {
            const double epsilon = 1e-12d;
            if (orbitRadial.LengthSquared() <= epsilon)
                return Vector3D.Up;

            Vector3D radial = Vector3D.Normalize(orbitRadial);
            if (referenceNormal.LengthSquared() <= epsilon)
                referenceNormal = Vector3D.Up;
            else
                referenceNormal = Vector3D.Normalize(referenceNormal);

            // Keep the orbit plane as close as possible to the parent orbit plane,
            // while forcing the current body's radial vector to lie in that plane.
            Vector3D normal = referenceNormal -
                              radial * Vector3D.Dot(referenceNormal, radial);
            if (normal.LengthSquared() <= epsilon)
            {
                Vector3D fallback = Math.Abs(radial.Y) < 0.9d
                    ? Vector3D.Up
                    : Vector3D.Right;
                normal = fallback - radial * Vector3D.Dot(fallback, radial);
            }
            if (normal.LengthSquared() <= epsilon)
            {
                Vector3D fallback = Vector3D.Forward;
                normal = fallback - radial * Vector3D.Dot(fallback, radial);
            }

            return Vector3D.Normalize(normal);
        }

        static void DrawProjectedOrbitRing(
            List<MySprite> sprites,
            List<MySprite> frontSprites,
            List<PlanetProjection> planets,
            long ownerPlanetId,
            Vector3D centerWorld,
            Vector3D axisXWorld,
            Vector3D axisYWorld,
            double radiusWorld,
            Vector3D cameraTargetWorld,
            Vector2 screenCenter,
            double worldToPx,
            Vector3 screenRight,
            Vector3 screenUp,
            Vector3 cameraViewDirection,
            Vector3D cameraPositionWorld,
            float lineWidth,
            Color color)
        {
            const int segments = 72;
            Vector3D cameraView = new Vector3D(
                cameraViewDirection.X,
                cameraViewDirection.Y,
                cameraViewDirection.Z);
            Vector3D previousWorld = Vector3D.Zero;
            double previousDepth = 0d;
            bool hasPrevious = false;

            for (int i = 0; i <= segments; i++)
            {
                double angle = MathHelper.TwoPi * i / segments;
                Vector3D pointWorld = centerWorld +
                                      axisXWorld * (Math.Cos(angle) * radiusWorld) +
                                      axisYWorld * (Math.Sin(angle) * radiusWorld);
                double pointDepth = CalculateStaticCameraDepth(
                    pointWorld,
                    cameraPositionWorld,
                    cameraView);

                if (hasPrevious)
                {
                    DrawCameraClippedStaticLine(
                        sprites,
                        frontSprites,
                        planets,
                        ownerPlanetId,
                        previousWorld,
                        previousDepth,
                        pointWorld,
                        pointDepth,
                        cameraTargetWorld,
                        screenCenter,
                        worldToPx,
                        screenRight,
                        screenUp,
                        lineWidth,
                        color);
                }

                previousWorld = pointWorld;
                previousDepth = pointDepth;
                hasPrevious = true;
            }
        }

        static void DrawCameraClippedStaticLine(
            List<MySprite> sprites,
            List<MySprite> frontSprites,
            List<PlanetProjection> planets,
            long ownerPlanetId,
            Vector3D startWorld,
            double startDepth,
            Vector3D endWorld,
            double endDepth,
            Vector3D cameraTargetWorld,
            Vector2 screenCenter,
            double worldToPx,
            Vector3 screenRight,
            Vector3 screenUp,
            float lineWidth,
            Color color)
        {
            bool startVisible = startDepth > STATIC_CAMERA_NEAR_CLIP_DEPTH;
            bool endVisible = endDepth > STATIC_CAMERA_NEAR_CLIP_DEPTH;
            if (!startVisible && !endVisible)
                return;

            if (startVisible != endVisible)
            {
                double depthDelta = endDepth - startDepth;
                if (Math.Abs(depthDelta) <= 1e-12d)
                    return;

                double intersectionAmount =
                    (STATIC_CAMERA_NEAR_CLIP_DEPTH - startDepth) / depthDelta;
                intersectionAmount = Math.Max(
                    0d,
                    Math.Min(1d, intersectionAmount));
                Vector3D intersection = startWorld +
                                        (endWorld - startWorld) * intersectionAmount;

                if (startVisible)
                {
                    endWorld = intersection;
                    endDepth = STATIC_CAMERA_NEAR_CLIP_DEPTH;
                }
                else
                {
                    startWorld = intersection;
                    startDepth = STATIC_CAMERA_NEAR_CLIP_DEPTH;
                }
            }

            Vector2 start = ProjectStaticPoint(
                startWorld,
                cameraTargetWorld,
                screenCenter,
                worldToPx,
                screenRight,
                screenUp);
            Vector2 end = ProjectStaticPoint(
                endWorld,
                cameraTargetWorld,
                screenCenter,
                worldToPx,
                screenRight,
                screenUp);
            AddDepthSplitStaticLineSprites(
                sprites,
                frontSprites,
                planets,
                ownerPlanetId,
                start,
                startDepth,
                end,
                endDepth,
                lineWidth,
                color);
        }

        static void AddDepthSplitStaticLineSprites(
            List<MySprite> backSprites,
            List<MySprite> frontSprites,
            List<PlanetProjection> planets,
            long ownerPlanetId,
            Vector2 start,
            double startDepth,
            Vector2 end,
            double endDepth,
            float lineWidth,
            Color color)
        {
            Vector2 delta = end - start;
            float length = delta.Length();
            if (length <= 0.001f)
                return;

            float maxPartLength = Math.Max(4f, lineWidth * 4f);
            int parts = Math.Max(1, (int)Math.Ceiling(length / maxPartLength));
            for (int i = 0; i < parts; i++)
            {
                float amount0 = (float)i / parts;
                float amount1 = (float)(i + 1) / parts;
                Vector2 partStart = Vector2.Lerp(start, end, amount0);
                Vector2 partEnd = Vector2.Lerp(start, end, amount1);
                Vector2 midpoint = Vector2.Lerp(partStart, partEnd, 0.5f);
                double midpointDepth = startDepth + (endDepth - startDepth) * ((amount0 + amount1) * 0.5d);
                List<MySprite> target = ShouldDrawStaticRingSegmentInFront(
                    planets,
                    ownerPlanetId,
                    midpoint,
                    midpointDepth,
                    lineWidth)
                    ? frontSprites
                    : backSprites;
                AddLineSprite(target, partStart, partEnd, lineWidth, color);
            }
        }

        static bool ShouldDrawStaticRingSegmentInFront(
            List<PlanetProjection> planets,
            long ownerPlanetId,
            Vector2 screenPosition,
            double depth,
            float lineWidth)
        {
            if (planets == null || planets.Count == 0)
                return false;

            bool overlapsPlanet = false;
            for (int i = 0; i < planets.Count; i++)
            {
                PlanetProjection planet = planets[i];
                if (ownerPlanetId != 0L && planet.PlanetId == ownerPlanetId)
                    continue;

                if (planet.CameraDepth <= STATIC_CAMERA_NEAR_CLIP_DEPTH ||
                    planet.MarkerRadius <= 0f)
                {
                    continue;
                }

                float radius = planet.MarkerRadius + lineWidth * 0.5f;
                if (Vector2.DistanceSquared(screenPosition, planet.ScreenPos) > radius * radius)
                    continue;

                overlapsPlanet = true;
                if (depth >= planet.CameraDepth)
                    return false;
            }

            return overlapsPlanet;
        }

        static void AddLineSprite(
            List<MySprite> sprites,
            Vector2 start,
            Vector2 end,
            float width,
            Color color)
        {
            Vector2 delta = end - start;
            float length = delta.Length();
            if (length <= 0.001f)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = (start + end) * 0.5f,
                Size = new Vector2(length, width),
                Color = color,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = (float)Math.Atan2(delta.Y, delta.X)
            });
        }

        float GetNaturalGravityMultiplier(Vector3D camPos)
        {
            var gravityProvider = GetGravityProvider();
            if (gravityProvider == null)
                return 0f;

            float naturalGravityMultiplier;
            gravityProvider.CalculateNaturalGravityInPoint(camPos, out naturalGravityMultiplier);
            return Math.Max(0f, naturalGravityMultiplier);
        }

        long GetCurrentGravityPlanetId(Vector3D camPos, Dictionary<long, MyPlanet> planets)
        {
            var gravityProvider = GetGravityProvider();
            if (gravityProvider == null || !gravityProvider.IsPositionInNaturalGravity(camPos))
                return 0;

            IMyNaturalGravityComponent gravityComponent;
            gravityProvider.GetStrongestNaturalGravityWell(camPos, out gravityComponent);
            if (gravityComponent == null)
                return 0;

            Vector3D gravityCenter = gravityComponent.Position;
            long bestId = 0;
            double bestDistSq = double.MaxValue;

            foreach (var kv in planets)
            {
                var p = kv.Value;
                if (p == null || p.MarkedForClose)
                    continue;

                double dSq = Vector3D.DistanceSquared(p.WorldMatrix.Translation, gravityCenter);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestId = kv.Key;
                }
            }

            return bestId;
        }

        float GetEffectiveVerticalFovDeg()
        {
            float configuredFov = StarMapComponent.FoV;
            return MathHelper.Clamp(
                configuredFov > 0f ? configuredFov : MAP_VERTICAL_FOV_DEFAULT_DEG,
                0.1f, 120f);
        }

        static Color ApplyAlpha(Color color, float alpha)
        {
            return new Color(color, MathHelper.Clamp(alpha, 0f, 1f));
        }

        void DrawPlanetLabels(List<MySprite> sprites, PlanetProjection planet)
        {
            if (planet.Visibility <= 0.001f)
                return;

            float nameScale = 0.65f * Scale * FontScale;
            var nameSize = FormatingHelper.GetSizeInPixel(planet.Name, TextFont, nameScale, Surface);
            float nameOffset = planet.MarkerRadius + 12f + nameSize.Y;

            if (!planet.ShouldDisplayInfo)
                return;

            var labelColor = ApplyAlpha(ForegroundColor, planet.Visibility);
            var namePos = planet.ScreenPos - new Vector2(0f, nameOffset);
            namePos.X = MathHelper.Clamp(
                namePos.X,
                ViewBox.X + nameSize.X * 0.5f,
                ViewBox.Right - nameSize.X * 0.5f);
            namePos.Y = MathHelper.Clamp(
                namePos.Y,
                ViewBox.Y + nameSize.Y * 0.5f,
                ViewBox.Bottom - nameSize.Y * 0.5f);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = planet.Name,
                Position = namePos,
                Color = labelColor,
                FontId = TextFont,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = nameScale
            });


            float distanceScale = 0.6f * Scale * FontScale;
            float distanceOffset = planet.MarkerRadius + 10f;
            string distanceText = FormatingHelper.DistanceToString((float)planet.Distance);
            var distanceSize = FormatingHelper.GetSizeInPixel(distanceText, TextFont, distanceScale, Surface);
            var distancePos = planet.ScreenPos + new Vector2(0f, distanceOffset);
            distancePos.X = MathHelper.Clamp(
                distancePos.X,
                ViewBox.X + distanceSize.X * 0.5f,
                ViewBox.Right - distanceSize.X * 0.5f);
            distancePos.Y = MathHelper.Clamp(
                distancePos.Y,
                ViewBox.Y + distanceSize.Y * 0.5f,
                ViewBox.Bottom - distanceSize.Y);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = distanceText,
                Position = distancePos,
                Color = labelColor,
                FontId = TextFont,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = distanceScale
            });

            DrawPlanetSideInfo(sprites, planet, labelColor, namePos, nameSize, distancePos, distanceSize);
        }

        void DrawPlanetSideInfo(List<MySprite> sprites, PlanetProjection planet, Color labelColor, Vector2 namePos,
            Vector2 nameSize, Vector2 distancePos, Vector2 distanceSize)
        {
            float sideInfoScale = SIDE_INFO_TEXT_SCALE * Scale * FontScale;
            float sideInfoYOffset = SIDE_INFO_Y_OFFSET_PX * Scale * FontScale;
            var lines = BuildPlanetInfoLines(planet, false);
            var lineTexts = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
                lineTexts[i] = lines[i] != null ? lines[i].GetText() : string.Empty;

            int count = lines.Count;
            var lineSizes = new Vector2[count];
            float maxLineWidth = 0f;
            float maxLineHeight = 0f;
            for (int i = 0; i < count; i++)
            {
                lineSizes[i] = FormatingHelper.GetSizeInPixel(lineTexts[i], TextFont, sideInfoScale, Surface);
                if (lineSizes[i].X > maxLineWidth)
                    maxLineWidth = lineSizes[i].X;
                if (lineSizes[i].Y > maxLineHeight)
                    maxLineHeight = lineSizes[i].Y;
            }

            bool placeOnRight = planet.ScreenPos.X <= ViewBox.Center.X;
            float lineStep = MeasureLineHeight(sideInfoScale) + 2f;
            float requiredHeight = (count - 1) * lineStep + maxLineHeight;
            float availableHeight = planet.MarkerRadius * 2f;
            float availableWidth = planet.MarkerRadius * 2f;
            bool useFallback = availableHeight < requiredHeight || availableWidth < maxLineWidth;
            float nameLeft = namePos.X - nameSize.X * 0.5f;
            float nameRight = namePos.X + nameSize.X * 0.5f;
            float nameTop = namePos.Y - nameSize.Y * 0.5f;
            float nameBottom = namePos.Y + nameSize.Y * 0.5f;
            float distLeft = distancePos.X - distanceSize.X * 0.5f;
            float distRight = distancePos.X + distanceSize.X * 0.5f;
            float distTop = distancePos.Y - distanceSize.Y * 0.5f;
            float distBottom = distancePos.Y + distanceSize.Y * 0.5f;

            Func<float, float, float, float, bool> overlapsDetails = (left, right, top, lineHeight) =>
            {
                float bottom = top + lineHeight;
                bool overlapsName = right >= nameLeft && left <= nameRight && bottom >= nameTop && top <= nameBottom;
                bool overlapsDistance =
                    right >= distLeft && left <= distRight && bottom >= distTop && top <= distBottom;
                return overlapsName || overlapsDistance;
            };

            Func<float, Vector2, float> computeAdjustedX = (yEdge, lineSize) =>
            {
                float dy = yEdge - sideInfoYOffset - planet.ScreenPos.Y;
                float inside = planet.MarkerRadius * planet.MarkerRadius - dy * dy;
                float edgeOffset = inside > 0f ? (float)Math.Sqrt(inside) : 0f;
                float x = placeOnRight
                    ? planet.ScreenPos.X + edgeOffset + SIDE_INFO_MARGIN_PX
                    : planet.ScreenPos.X - edgeOffset - SIDE_INFO_MARGIN_PX;

                x = placeOnRight
                    ? MathHelper.Clamp(x, ViewBox.X + 2f, ViewBox.Right - lineSize.X - 2f)
                    : MathHelper.Clamp(x, ViewBox.X + lineSize.X + 2f, ViewBox.Right - 2f);

                float y = MathHelper.Clamp(yEdge - sideInfoYOffset,
                    ViewBox.Y + lineSize.Y * 0.5f,
                    ViewBox.Bottom - lineSize.Y * 0.5f);
                float top = y - lineSize.Y * 0.5f;
                float left = placeOnRight ? x : x - lineSize.X;
                float right = placeOnRight ? x + lineSize.X : x;
                if (overlapsDetails(left, right, top, lineSize.Y))
                {
                    float push = SIDE_INFO_MARGIN_PX + 6f;
                    if (placeOnRight)
                    {
                        float avoidRight = Math.Max(nameRight, distRight) + push;
                        x = Math.Max(x, avoidRight);
                        x = MathHelper.Clamp(x, ViewBox.X + 2f, ViewBox.Right - lineSize.X - 2f);
                    }
                    else
                    {
                        float avoidLeft = Math.Min(nameLeft, distLeft) - push;
                        x = Math.Min(x, avoidLeft);
                        x = MathHelper.Clamp(x, ViewBox.X + lineSize.X + 2f, ViewBox.Right - 2f);
                    }
                }

                return x;
            };

            if (!useFallback)
            {
                float startYPreview = planet.ScreenPos.Y - ((count - 1) * lineStep * 0.5f);

                for (int i = 0; i < count; i++)
                {
                    float yEdge = MathHelper.Clamp(startYPreview + i * lineStep,
                        ViewBox.Y + lineSizes[i].Y * 0.5f,
                        ViewBox.Bottom - lineSizes[i].Y * 0.5f);
                    float x = computeAdjustedX(yEdge, lineSizes[i]);

                    float y = MathHelper.Clamp(yEdge - sideInfoYOffset,
                        ViewBox.Y + lineSizes[i].Y * 0.5f,
                        ViewBox.Bottom - lineSizes[i].Y * 0.5f);

                    float left = placeOnRight ? x : x - lineSizes[i].X;
                    float right = placeOnRight ? x + lineSizes[i].X : x;
                    float top = y - lineSizes[i].Y * 0.5f;
                    if (overlapsDetails(left, right, top, lineSizes[i].Y))
                    {
                        useFallback = true;
                        break;
                    }
                }
            }

            if (useFallback)
            {
                float xPlanetSide = placeOnRight
                    ? planet.ScreenPos.X + planet.MarkerRadius + SIDE_INFO_MARGIN_PX
                    : planet.ScreenPos.X - planet.MarkerRadius - SIDE_INFO_MARGIN_PX;
                float xRangeSide = placeOnRight
                    ? distancePos.X + distanceSize.X * 0.5f + SIDE_INFO_MARGIN_PX
                    : distancePos.X - distanceSize.X * 0.5f - SIDE_INFO_MARGIN_PX;
                float xBase = placeOnRight
                    ? Math.Max(xPlanetSide, xRangeSide)
                    : Math.Min(xPlanetSide, xRangeSide);
                xBase = placeOnRight
                    ? MathHelper.Clamp(xBase, ViewBox.X + 2f, ViewBox.Right - maxLineWidth - 2f)
                    : MathHelper.Clamp(xBase, ViewBox.X + maxLineWidth + 2f, ViewBox.Right - 2f);

                float startYBelowName = namePos.Y + nameSize.Y + lineStep;
                float startYFallback = MathHelper.Clamp(startYBelowName,
                    ViewBox.Y + maxLineHeight * 0.5f,
                    ViewBox.Bottom - requiredHeight);

                var fallbackAlignment = placeOnRight ? TextAlignment.LEFT : TextAlignment.RIGHT;
                bool hasPanelBounds = false;
                RectangleF panelBounds = default(RectangleF);
                for (int i = 0; i < count; i++)
                {
                    float y = MathHelper.Clamp(startYFallback + i * lineStep - sideInfoYOffset,
                        ViewBox.Y + lineSizes[i].Y * 0.5f,
                        ViewBox.Bottom - lineSizes[i].Y * 0.5f);
                    var lineBounds = DrawPlanetSideInfoLine(
                        sprites,
                        lines[i],
                        lineTexts[i],
                        new Vector2(xBase, y),
                        lineSizes[i],
                        labelColor,
                        fallbackAlignment,
                        sideInfoScale,
                        planet.ScreenPos.X);
                    IncludeBounds(ref panelBounds, ref hasPanelBounds, lineBounds);
                }

                RegisterSelectedInfoPanelBounds(planet.PlanetId, panelBounds, hasPanelBounds);
                return;
            }

            float startY = planet.ScreenPos.Y - ((count - 1) * lineStep * 0.5f);
            var alignment = placeOnRight ? TextAlignment.LEFT : TextAlignment.RIGHT;
            bool hasSidePanelBounds = false;
            RectangleF sidePanelBounds = default(RectangleF);

            for (int i = 0; i < count; i++)
            {
                float yEdge = MathHelper.Clamp(startY + i * lineStep,
                    ViewBox.Y + lineSizes[i].Y * 0.5f,
                    ViewBox.Bottom - lineSizes[i].Y * 0.5f);
                float x = computeAdjustedX(yEdge, lineSizes[i]);

                float y = MathHelper.Clamp(yEdge - sideInfoYOffset,
                    ViewBox.Y + lineSizes[i].Y * 0.5f,
                    ViewBox.Bottom - lineSizes[i].Y * 0.5f);

                var lineBounds = DrawPlanetSideInfoLine(
                    sprites,
                    lines[i],
                    lineTexts[i],
                    new Vector2(x, y),
                    lineSizes[i],
                    labelColor,
                    alignment,
                    sideInfoScale,
                    planet.ScreenPos.X);
                IncludeBounds(ref sidePanelBounds, ref hasSidePanelBounds, lineBounds);
            }

            RegisterSelectedInfoPanelBounds(planet.PlanetId, sidePanelBounds, hasSidePanelBounds);
        }

        RectangleF DrawPlanetSideInfoLine(
            List<MySprite> sprites,
            ITooltipLine line,
            string text,
            Vector2 position,
            Vector2 size,
            Color labelColor,
            TextAlignment alignment,
            float textScale,
            float planetCenterX)
        {
            var textPosition = position - new Vector2(0f, size.Y * 0.25f);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = textPosition,
                Color = labelColor,
                FontId = TextFont,
                Alignment = alignment,
                RotationOrScale = textScale
            });

            var textRect = GetTextBounds(textPosition, size, alignment);
            var panelRect = ExtendTextBoundsToPlanetCenter(textRect, planetCenterX);

            if (line == null)
                return panelRect;
            
            var cursor = line.GetCursor();
            bool hasEntry = line.IsClickable || cursor.HasValue;
            if (!hasEntry)
                return panelRect;

            var entry = new RectangleControl(
                textRect,
                cursor ?? (line.IsClickable ? CursorType.Hand : CursorType.Default),
                line.GetDataContext(),
                line.GetOnClick());
            AddLogicalChild(entry);
            entry.ClickSound = line.GetClickSound();
            entry.CustomRender = delegate(ControlTemplate renderEntry, List<MySprite> targetSprites)
            {
                if (line.IsClickable)
                    DrawTextHitboxUnderline(textRect, labelColor, targetSprites, textScale);
            };
            _children.Add(entry);
            return panelRect;
        }

        static RectangleF ExtendTextBoundsToPlanetCenter(RectangleF rect, float planetCenterX)
        {
            if (rect.X >= planetCenterX)
                return new RectangleF(planetCenterX, rect.Y, rect.Right - planetCenterX, rect.Height);

            if (rect.Right <= planetCenterX)
                return new RectangleF(rect.X, rect.Y, planetCenterX - rect.X, rect.Height);

            return rect;
        }

        void RegisterSelectedInfoPanelBounds(long planetId, RectangleF rect, bool hasBounds)
        {
            if (!UsesCursorDynamicInfoTarget())
            {
                _selectedInfoBoundsThisFrame.Clear();
                _selectedInfoKeepAliveBounds.Clear();
                return;
            }

            if (!hasBounds || planetId != _selectedInfoPlanetId)
                return;

            _selectedInfoBoundsThisFrame.Clear();
            _selectedInfoBoundsThisFrame.Add(ExpandRect(rect, 6f * Scale));
            _selectedInfoKeepAliveBounds.Clear();
            _selectedInfoKeepAliveBounds.AddRange(_selectedInfoBoundsThisFrame);
        }

        static void IncludeBounds(ref RectangleF panelBounds, ref bool hasPanelBounds, RectangleF lineBounds)
        {
            if (!hasPanelBounds)
            {
                panelBounds = lineBounds;
                hasPanelBounds = true;
                return;
            }

            float left = Math.Min(panelBounds.X, lineBounds.X);
            float top = Math.Min(panelBounds.Y, lineBounds.Y);
            float right = Math.Max(panelBounds.Right, lineBounds.Right);
            float bottom = Math.Max(panelBounds.Bottom, lineBounds.Bottom);
            panelBounds = new RectangleF(left, top, right - left, bottom - top);
        }

        static void DrawTextHitboxUnderline(RectangleF rect, Color color, List<MySprite> sprites, float textScale)
        {
            float thickness = Math.Max(1f, 1.5f * textScale);
            float y = rect.Bottom + thickness - 3f * textScale;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(rect.Center.X, y),
                Size = new Vector2(rect.Width, thickness),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        static RectangleF GetTextBounds(Vector2 position, Vector2 size, TextAlignment alignment)
        {
            float width = Math.Max(1f, size.X);
            float height = Math.Max(1f, size.Y);
            float x;

            switch (alignment)
            {
                case TextAlignment.CENTER:
                    x = position.X - width * 0.5f;
                    break;
                case TextAlignment.RIGHT:
                    x = position.X - width;
                    break;
                default:
                    x = position.X;
                    break;
            }

            return new RectangleF(x, position.Y, width, height);
        }

        static RectangleF ExpandRect(RectangleF rect, float margin)
        {
            return new RectangleF(
                rect.X - margin,
                rect.Y - margin,
                rect.Width + margin * 2f,
                rect.Height + margin * 2f);
        }

        void CachePlanetInfoLines(ref PlanetProjection planet)
        {
            planet.CachedInfoLines = BuildCachedPlanetInfoLines(planet);
            planet.CachedCompactInfoLines = BuildCachedPlanetInfoLines(planet);
        }

        List<ITooltipLine> BuildPlanetInfoLines(PlanetProjection planet, bool compactRadiusLabel)
        {
            var cachedLines = compactRadiusLabel ? planet.CachedCompactInfoLines : planet.CachedInfoLines;
            return cachedLines ?? BuildCachedPlanetInfoLines(planet);
        }

        List<ITooltipLine> BuildCachedPlanetInfoLines(PlanetProjection planet)
        {
            var lines = new List<ITooltipLine>(9)
            {
                new StaticTooltipLine(FormatPropertyLine("Radius", FormatingHelper.DistanceToString(planet.Radius))),
                new StaticTooltipLine(FormatPropertyLine("Gravity", FormatingHelper.GravityToString(planet.SurfaceGravityG))),
                new StaticTooltipLine(FormatPropertyLine("Range", FormatingHelper.DistanceToString(planet.GravityRange))),
                new StaticTooltipLine(FormatPropertyLine("Atmosphere", FormatingHelper.PercentageToString(planet.AtmosphereDensity))),
                new StaticTooltipLine(FormatPropertyLine("O2", FormatingHelper.PercentageToString(planet.OxygenDensity))),
                new StaticTooltipLine(FormatPropertyLine("Temperature", FormatingHelper.TemperatureToString(planet.AverageTemperature))),
                new StaticTooltipLine(FormatPropertyLine("Wind", FormatingHelper.WindToString(planet.MaxWindSpeed))),
                new ClickableTooltipLine(FormatPropertyLine("Position", FormatingHelper.FormatBearing(Matrix.Identity, planet.WorldPosition)),
                    planet.WorldPosition,
                    (value, sender) => { ClickOnGps(planet.Name, planet.WorldPosition, planet.GpsColor); })
                {
                    ClickSound = AudioHelper.HudGps3
                },
                GetJumpTooltipLine(planet)
            };

            return lines;
        }

        DynamicTooltipLine GetJumpTooltipLine(PlanetProjection planet)
        {
            Vector3D jumpPoint = Vector3D.Zero;
            string jumpText = FormatPropertyLine("Jump", LocHelper.GetLoc(MOD_PREFIX + "NotAvailable"));
            bool jumpClickable = false;
            long lastRun = long.MinValue;

            Action refresh = () =>
            {
                if (lastRun == _jumpPointRunCounter)
                    return;

                lastRun = _jumpPointRunCounter;
                jumpClickable = TryBuildJumpInfoLine(planet, out jumpText, out jumpPoint);
            };

            return new DynamicTooltipLine(
                getText: () =>
                {
                    refresh();
                    return jumpText;
                },
                isClickable: () =>
                {
                    refresh();
                    return jumpClickable;
                },
                getDataContext: () =>
                {
                    refresh();
                    return jumpClickable ? (object)jumpPoint : null;
                },
                getOnClick: () =>
                {
                    refresh();

                    if (!jumpClickable)
                        return null;

                    return (value, sender) =>
                    {
                        ClickOnGps(
                            string.Format(
                                FormatingHelper.Culture,
                                LocHelper.GetLoc(MOD_PREFIX + "StarMap_JumpPointNameFormat"),
                                planet.Name),
                            jumpPoint,
                            planet.GpsColor);
                    };
                },
                getCursor: () =>
                {
                    refresh();
                    var jumpDrives = _host.GridLogic?.GetTerminalBlocks<IMyJumpDrive>(GridLinkTypeEnum.Physical);
                    return jumpDrives == null || jumpDrives.Count == 0
                        ? CursorType.Arrow
                        : _busy ? CursorType.WaitCursor : CursorType.Hand;
                },
                getClickSound: () => AudioHelper.HudGps3);
        }

        bool TryBuildJumpInfoLine(PlanetProjection planet, out string text, out Vector3D jumpPoint)
        {
            jumpPoint = Vector3D.Zero;
            int etaSeconds;
            var jumpDrives = _host.GridLogic?.GetTerminalBlocks<IMyJumpDrive>(GridLinkTypeEnum.Physical);
            if (jumpDrives == null || jumpDrives.Count == 0)
            {
                text = FormatPropertyLine("Jump", LocHelper.GetLoc(MOD_PREFIX + "NotAvailable"));
                return false;
            }

            if (IsJumpPointUiThrottled(planet.PlanetId, planet.Distance, _jumpPointRunCounter, out etaSeconds))
            {
                text = FormatPropertyLine("Jump",
                    string.Format(
                        FormatingHelper.Culture,
                        LocHelper.GetLoc(MOD_PREFIX + "StarMap_JumpCalculatingFormat"),
                        etaSeconds));
                return false;
            }

            if (_host.GridLogic.TryGetPlanetJumpPoint(
                    planet.PlanetId,
                    planet.Name,
                    planet.WorldPosition,
                    planet.Radius,
                    planet.GravityRange,
                    out jumpPoint,
                    !PlanetariumMode))
            {
                text = FormatPropertyLine("Jump", FormatingHelper.FormatBearing(GetReferenceMatrix(), jumpPoint));
                return true;
            }

            text = FormatPropertyLine("Jump", LocHelper.GetLoc(MOD_PREFIX + "NotAvailable"));
            return false;
        }

        MatrixD GetReferenceMatrix()
        {
            if (Block == null)
                return MatrixD.Identity;

            MatrixD screenWorld;
            if (!TryGetReferenceWorldMatrix(out screenWorld))
                return Block.WorldMatrix;

            var forward = screenWorld.Forward;
            var right = screenWorld.Right;
            var up = screenWorld.Up;
            if (forward.Normalize() <= 1e-6 || right.Normalize() <= 1e-6 || up.Normalize() <= 1e-6)
                return Block.WorldMatrix;

            screenWorld.Forward = forward;
            screenWorld.Right = right;
            screenWorld.Up = up;
            return screenWorld;
        }

        bool TryGetReferenceWorldMatrix(out MatrixD world)
        {
            return _host.TryGetReferenceWorldMatrix(InteractionComponent.ReferenceMode, out world);
        }

        void ClickOnGps(string planetName, Vector3D position, Color color)
        {
            if (CreateLocalGpsCopy(planetName, position, color))
                SendGpsToChat(planetName, position, color);
        }

        bool CreateLocalGpsCopy(string name, Vector3D position, Color color)
        {
            var session = MyAPIGateway.Session;
            var gpsCollection = session == null ? null : session.GPS;
            if (gpsCollection == null)
                return false;

            var gps = gpsCollection.Create(GetLocalGpsName(name), string.Empty, position, false, true);
            if (gps == null)
                return false;

            gps.GPSColor = color;
            gpsCollection.AddLocalGps(gps);
            ShowGpsCreatedStatus(gps.Name);
            Host.RenderSprites();
            return true;
        }

        static string GetLocalGpsName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? LocHelper.GetLoc(MOD_PREFIX + "Gps_Unknown_Name")
                : name;
        }

        static string GetRadioSignalName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? LocHelper.GetLoc(MOD_PREFIX + "RadioSignal_Unnamed")
                : name;
        }

        void ShowGpsCreatedStatus(string name)
        {
            _gpsCreatedStatus = string.Format(
                FormatingHelper.Culture,
                LocHelper.GetLoc(MOD_PREFIX + "Gps_CreatedFormat"),
                GetLocalGpsName(name));
            _gpsCreatedStatusUntilFrame = GetCurrentGameFrame() + GPS_CREATED_STATUS_FRAMES;
        }

        void AddGpsCreatedStatus(List<MySprite> sprites)
        {
            if (sprites == null ||
                string.IsNullOrWhiteSpace(_gpsCreatedStatus) ||
                GetCurrentGameFrame() > _gpsCreatedStatusUntilFrame)
            {
                return;
            }

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = _gpsCreatedStatus,
                Position = new Vector2(
                    ViewBox.Center.X,
                    ViewBox.Bottom - 24f * Math.Max(0.5f, Scale)),
                RotationOrScale = 0.45f * Math.Max(0.5f, Scale),
                Color = ForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = TextFont
            });
        }

        void SendGpsToChat(string name, Vector3D position, Color color)
        {
            if (MyAPIGateway.Utilities == null)
                return;

            string gps = string.Format(
                CultureInfo.InvariantCulture,
                "GPS:{0}:{1:0.###}:{2:0.###}:{3:0.###}:{4}:",
                SanitizeGpsName(name),
                position.X,
                position.Y,
                position.Z,
                color.ToAHex());
            MyAPIGateway.Utilities.ShowMessage(_host.Title, gps);
        }

        static string SanitizeGpsName(string name)
        {
            return GetLocalGpsName(name).Replace(":", "_");
        }

        bool IsJumpPointUiThrottled(long planetId, double distanceMeters, long currentRun, out int etaSeconds)
        {
            _busy = true;
            
            etaSeconds = 0;
            JumpPointThrottleState state;
            if (!_jumpPointThrottleByPlanet.TryGetValue(planetId, out state))
            {
                var totalSeconds = Math.Max(1d, distanceMeters / JUMP_POINT_DISTANCE_PER_SECOND);
                state = new JumpPointThrottleState
                {
                    StartRun = currentRun,
                    DurationRuns = (long)Math.Ceiling(totalSeconds * JUMP_POINT_RUNS_PER_SECOND),
                    LastRequestRun = currentRun
                };
                _jumpPointThrottleByPlanet[planetId] = state;
                etaSeconds = (int)Math.Ceiling(state.DurationRuns / JUMP_POINT_RUNS_PER_SECOND);
                return true;
            }

            // Focus was broken (looked away): restart throttle window on next focus.
            if (currentRun - state.LastRequestRun > JUMP_POINT_RUNS_PER_SECOND)
            {
                var totalSeconds = Math.Max(1d, distanceMeters / JUMP_POINT_DISTANCE_PER_SECOND);
                state.StartRun = currentRun;
                state.DurationRuns = (long)Math.Ceiling(totalSeconds * JUMP_POINT_RUNS_PER_SECOND);
                state.LastRequestRun = currentRun;
                _jumpPointThrottleByPlanet[planetId] = state;
                etaSeconds = (int)Math.Ceiling(state.DurationRuns / JUMP_POINT_RUNS_PER_SECOND);
                return true;
            }

            long elapsedRuns = currentRun - state.StartRun;
            long remainingRuns = state.DurationRuns - elapsedRuns;
            if (remainingRuns <= 0)
            {
                state.LastRequestRun = currentRun;
                _jumpPointThrottleByPlanet[planetId] = state;
                _busy = false;
                return false;
            }

            state.LastRequestRun = currentRun;
            _jumpPointThrottleByPlanet[planetId] = state;
            etaSeconds = Math.Max(1, (int)Math.Ceiling(remainingRuns / JUMP_POINT_RUNS_PER_SECOND));
            return true;
        }

        static bool IsFullyOccludedBy(PlanetProjection front, PlanetProjection back)
        {
            if (front.Distance >= back.Distance)
                return false;

            // Fade-aware occlusion: when a front planet is partially transparent
            // (e.g. while inside its radius), it should cull less of planets behind it.
            double frontEffectiveAngularRadius = front.AngularRadius * front.Visibility;
            if (frontEffectiveAngularRadius <= back.AngularRadius)
                return false;

            double dot = MathHelper.Clamp((float)Vector3D.Dot(front.Direction, back.Direction), -1f, 1f);
            double centerSeparation = Math.Acos(dot);
            return centerSeparation <= (frontEffectiveAngularRadius - back.AngularRadius);
        }

        void DrawPlanet(PlanetProjection planet)
        {
            PlanetInteractiveState state;
            if (!_planetInteractiveStates.TryGetValue(planet.PlanetId, out state))
            {
                state = new PlanetInteractiveState();
                state.Entry = GetPlanetGlobeControl(planet.PlanetId);
                state.Entry.SetDataContext(planet.PlanetId);
                state.Entry.SetCursor(CursorType.Hand);
                state.StaticTooltip = new InteractiveTooltip(
                    () => state.Projection.Name,
                    () => BuildPlanetInfoLines(state.Projection, false),
                    () => FormatingHelper.DistanceToString((float)state.Projection.Distance),
                    GetCursor,
                    TooltipActivationMode.Click,
                    TooltipActivationMode.Click);
                _planetInteractiveStates[planet.PlanetId] = state;
            }

            state.Projection = planet;
            float diameter = planet.MarkerRadius * 2f;
            state.Entry.SetRect(new RectangleF(
                planet.ScreenPos - new Vector2(planet.MarkerRadius),
                new Vector2(diameter)));
            UpdatePlanetGlobe(state.Entry, planet);
            state.Entry.SetOnClick(PlanetariumMode ? OnStaticPlanetClicked : (Action<object, object>)null);
            state.Entry.SurfaceMiddleClicked = PlanetariumMode
                ? (Func<PlanetGlobeControl, Vector2, object, bool>)OnStaticPlanetSurfaceMiddleClicked
                : null;
            state.Entry.SetTooltip(PlanetariumMode ? state.StaticTooltip : null);
            state.UsedThisFrame = true;
            state.Entry.SetVisible(true);

            _children.Add(state.Entry);
        }

        void UpdatePlanetGlobe(PlanetGlobeControl globe, PlanetProjection planet)
        {
            globe.SetClipBounds(GetTextureBounds());
            globe.SetProjection(
                planet.ViewDirectionLocal,
                planet.ScreenRightLocal,
                planet.ScreenUpLocal);
            globe.SetRotationTransform(Matrix.Identity);
            globe.SetZoom(1f);
            globe.SetColorAlpha(planet.Visibility);
            globe.SetCubemap(ResolvePlanetCubemap(
                planet.PlanetId,
                globe.GetPreferredFaceSide()));
            globe.SetSelectionBackdrop(
                planet.ShouldDisplayInfo,
                planet.ShouldDisplayInfo
                    ? ApplyAlpha(globe.ResolveColor(ThemeResources.SurfaceColor), planet.Visibility)
                    : Color.Transparent,
                10f);
        }

        PlanetGlobeControl GetPlanetGlobeControl(long planetId)
        {
            PlanetGlobeControl control;
            if (_planetGlobeControls.TryGetValue(planetId, out control))
                return control;

            control = AddLogicalChild(
                new PlanetGlobeControl(default(RectangleF)));
            PlanetTextureQuality quality = LocalConfigManager.TextureQuality;
            control.SetRenderQuality(
                PlanetTextureQualitySettings.GetMaximumFaceSide(quality),
                PlanetTextureQualitySettings.GetTextCellSizePixels(quality));
            _planetGlobeControls[planetId] = control;
            return control;
        }

        PlanetColorCubemap ResolvePlanetCubemap(
            long planetId,
            int preferredFaceSide)
        {
            if (_closed)
                return null;

            PlanetCubemapState state = GetPlanetCubemapState(planetId);
            if (state.Cubemap != null &&
                state.Cubemap.SatisfiesFaceSide(preferredFaceSide))
            {
                if (state.Ticket != null)
                    CancelPlanetCubemapRequest(state);

                return state.Cubemap;
            }

            // Keep the last completed map visible while a sharper lazy level is
            // generated. Only planets without any completed cubemap render gray.
            if (state.Ticket != null)
            {
                if (state.RequestedFaceSide == preferredFaceSide)
                    return state.Cubemap;

                CancelPlanetCubemapRequest(state);
            }

            long frame = GetCurrentGameFrame();
            if (state.RetryFaceSide == preferredFaceSide &&
                frame < state.RetryFrame)
            {
                return state.Cubemap;
            }

            MyPlanet planet;
            if (!PlanetHelper.PlanetsById.TryGetValue(planetId, out planet) ||
                planet == null ||
                planet.MarkedForClose)
            {
                return null;
            }

            CartographyModule module = GetCartographyModule();
            if (module == null)
            {
                state.RetryFaceSide = preferredFaceSide;
                state.RetryFrame = frame + PLANET_CUBEMAP_RETRY_FRAMES;
                return state.Cubemap;
            }

            try
            {
                var request = new CartographyRequest
                {
                    PlanetEntityId = planetId,
                    PlanetRadiusMeters = planet.AverageRadius,
                    Projection = CartographyProjection.CubemapFaces,
                    Layer = CartographyLayer.Satellite,
                    MaximumFaceSide = preferredFaceSide,
                    ReturnColorCubemap = true
                };

                PlanetColorCubemap cachedCubemap;
                if (module.TryGetCachedColorCubemap(request, out cachedCubemap))
                {
                    state.Cubemap = cachedCubemap;
                    state.RetryFaceSide = int.MinValue;
                    return cachedCubemap;
                }

                int requestVersion = ++state.RequestVersion;
                state.RequestedFaceSide = preferredFaceSide;
                state.Ticket = module.RequestMap(
                    request,
                    delegate(CartographyResult result)
                    {
                        if (_closed || requestVersion != state.RequestVersion)
                            return;

                        state.Ticket = null;
                        state.RequestedFaceSide = -1;

                        if (result != null &&
                            result.Success &&
                            result.ColorCubemap != null)
                        {
                            if (state.Cubemap == null ||
                                !state.Cubemap.SatisfiesFaceSide(
                                    result.ColorCubemap.RequestedMaximumFaceSide))
                            {
                                state.Cubemap = result.ColorCubemap;
                            }

                            state.RetryFaceSide = int.MinValue;
                        }
                        else
                        {
                            state.RetryFaceSide = preferredFaceSide;
                            state.RetryFrame =
                                GetCurrentGameFrame() + PLANET_CUBEMAP_RETRY_FRAMES;
                        }

                        InvalidateStaticOrbitCache();
                        InvalidateDynamicMapCache();
                    });
            }
            catch
            {
                state.Ticket = null;
                state.RequestedFaceSide = -1;
                state.RetryFaceSide = preferredFaceSide;
                state.RetryFrame = frame + PLANET_CUBEMAP_RETRY_FRAMES;
            }

            return state.Cubemap;
        }

        CartographyModule GetCartographyModule()
        {
            EnsureCartographyEventSubscription();
            return _cartographyModule;
        }

        void EnsureCartographyEventSubscription()
        {
            CartographyModule module = LcdModSessionComponent.Client != null
                ? LcdModSessionComponent.Client.Cartography
                : null;
            if (ReferenceEquals(_cartographyModule, module))
                return;

            ClearCartographyEventSubscription();
            _cartographyModule = module;
            if (_cartographyModule != null)
                _cartographyModule.ColorCubemapCached += OnCartographyColorCubemapCached;
        }

        void ClearCartographyEventSubscription()
        {
            if (_cartographyModule != null)
                _cartographyModule.ColorCubemapCached -= OnCartographyColorCubemapCached;

            _cartographyModule = null;
        }

        void OnCartographyColorCubemapCached(CartographyColorCubemapCachedEvent cached)
        {
            if (_closed ||
                cached == null ||
                cached.ColorCubemap == null ||
                cached.Projection != CartographyProjection.CubemapFaces ||
                cached.Layer != CartographyLayer.Satellite)
            {
                return;
            }

            PlanetCubemapState state;
            if (!_planetCubemapStates.TryGetValue(cached.PlanetEntityId, out state) ||
                state == null ||
                state.RetryFaceSide == int.MinValue ||
                !cached.ColorCubemap.SatisfiesFaceSide(state.RetryFaceSide))
            {
                return;
            }

            if (state.Cubemap == null ||
                !state.Cubemap.SatisfiesFaceSide(cached.ColorCubemap.RequestedMaximumFaceSide))
            {
                state.Cubemap = cached.ColorCubemap;
            }

            state.RetryFaceSide = int.MinValue;
            state.RetryFrame = 0L;
            InvalidateStaticOrbitCache();
            InvalidateDynamicMapCache();
            Host.RenderSprites();
        }

        PlanetCubemapState GetPlanetCubemapState(long planetId)
        {
            PlanetCubemapState state;
            if (_planetCubemapStates.TryGetValue(planetId, out state))
                return state;

            state = new PlanetCubemapState();
            _planetCubemapStates[planetId] = state;
            return state;
        }

        static void CancelPlanetCubemapRequest(PlanetCubemapState state)
        {
            state.RequestVersion++;
            if (state.Ticket != null)
                state.Ticket.Cancel();

            state.Ticket = null;
            state.RequestedFaceSide = -1;
        }

        static void BuildPlanetLocalProjection(
            MyPlanet planet,
            Vector3D worldViewDirection,
            Vector3D worldScreenRight,
            Vector3D worldScreenUp,
            out Vector3 viewDirectionLocal,
            out Vector3 screenRightLocal,
            out Vector3 screenUpLocal)
        {
            if (worldViewDirection.Normalize() <= 1e-9)
                worldViewDirection = planet.WorldMatrix.Backward;
            if (worldScreenRight.Normalize() <= 1e-9)
                worldScreenRight = planet.WorldMatrix.Right;
            if (worldScreenUp.Normalize() <= 1e-9)
                worldScreenUp = planet.WorldMatrix.Up;

            viewDirectionLocal = WorldDirectionToPlanetLocal(
                planet,
                worldViewDirection);
            screenRightLocal = WorldDirectionToPlanetLocal(
                planet,
                worldScreenRight);
            screenUpLocal = WorldDirectionToPlanetLocal(
                planet,
                worldScreenUp);
        }

        static Vector3 WorldDirectionToPlanetLocal(
            MyPlanet planet,
            Vector3D worldDirection)
        {
            return new Vector3(
                (float)Vector3D.Dot(worldDirection, planet.WorldMatrix.Right),
                (float)Vector3D.Dot(worldDirection, planet.WorldMatrix.Up),
                (float)Vector3D.Dot(worldDirection, planet.WorldMatrix.Backward));
        }

        static Vector3D PlanetLocalDirectionToWorld(
            MyPlanet planet,
            Vector3 localDirection)
        {
            return planet.WorldMatrix.Right * localDirection.X +
                   planet.WorldMatrix.Up * localDirection.Y +
                   planet.WorldMatrix.Backward * localDirection.Z;
        }

        CursorType? GetCursor() => _busy ? CursorType.AppStarting : CursorType.Default;

        void OnStaticPlanetClicked(object dataContext, object sender)
        {
            if (_closed || !PlanetariumMode)
                return;

            long planetId = dataContext as long? ?? 0L;
            if (planetId == 0L)
                return;

            _staticFocusPlanetId = planetId;
            _staticCameraTargetOffsetWorld = Vector3D.Zero;
            PersistStaticCameraConfig();

            var tooltipHost = _host as InteractiveSurfaceScript;
            if (tooltipHost != null)
                tooltipHost.ShowTooltipFor(planetId);

            InvalidateStaticOrbitCache();
            Host.RenderSprites();
        }

        bool OnStaticPlanetSurfaceMiddleClicked(PlanetGlobeControl control, Vector2 screenPoint, object sender)
        {
            if (_closed || !PlanetariumMode || control == null)
                return false;

            long planetId = control.DataContext as long? ?? 0L;
            PlanetInteractiveState state;
            if (planetId == 0L ||
                !_planetInteractiveStates.TryGetValue(planetId, out state) ||
                state.Projection.PlanetId != planetId)
            {
                return false;
            }

            Vector3 localDirection;
            return control.TryGetSurfaceDirection(screenPoint, out localDirection) &&
                   FocusStaticCameraOnPlanetSurface(state.Projection, localDirection);
        }

        bool FocusStaticCameraOnPlanetSurface(PlanetProjection projection, Vector3 localDirection)
        {
            MyPlanet planet;
            if (projection.PlanetId == 0L ||
                !PlanetHelper.PlanetsById.TryGetValue(projection.PlanetId, out planet) ||
                planet == null ||
                planet.MarkedForClose)
            {
                return false;
            }

            if (!_staticPanProjectionValid ||
                _staticPanWorldUnitsPerPixel <= 0d ||
                double.IsNaN(_staticPanWorldUnitsPerPixel) ||
                double.IsInfinity(_staticPanWorldUnitsPerPixel))
            {
                return false;
            }

            if (localDirection.Normalize() <= 1e-6f)
                return false;

            Vector3D worldDirection = PlanetLocalDirectionToWorld(planet, localDirection);
            if (worldDirection.Normalize() <= 1e-9)
                return false;

            double targetRadius = Math.Max(0f, projection.MarkerRadius) * _staticPanWorldUnitsPerPixel;
            if (targetRadius <= 0d)
                return false;

            _staticFocusPlanetId = projection.PlanetId;
            _staticCameraTargetOffsetWorld = worldDirection * targetRadius;
            PersistStaticCameraConfig();
            InvalidateStaticOrbitCache();
            Host.RenderSprites();
            return true;
        }

        bool OnStaticCameraMoved(object dataContext, object sender, Vector2 delta)
        {
            if (_closed || !PlanetariumMode|| !_staticPanProjectionValid)
                return false;

            Vector3D movement =
                _staticPanScreenRightWorld * (-delta.X * _staticPanWorldUnitsPerPixel) +
                _staticPanScreenUpWorld * (delta.Y * _staticPanWorldUnitsPerPixel);
            if (movement.LengthSquared() <= 1e-12d)
                return false;

            _staticCameraTargetOffsetWorld += movement;
            PersistStaticCameraConfig();
            InvalidateStaticOrbitCache();
            Host.RenderSprites();
            return true;
        }

        void OnStaticOrbitCameraChanged(OrbitCameraControl control)
        {
            if (_closed)
                return;

            PersistStaticCameraConfig();
            InvalidateStaticOrbitCache();
            Host.RenderSprites();

            if (PlanetariumMode)
            {
                var staticCameraOrbitChanged = StaticCameraOrbitChanged;
                if (staticCameraOrbitChanged != null)
                    staticCameraOrbitChanged();
            }
        }

        public void OnLookAt(Vector2 onScreenCoordinates)
        {
            _cursorPosition = onScreenCoordinates;
            if (MyAPIGateway.Session != null)
                _lastCursorVisualContactFrame = MyAPIGateway.Session.GameplayFrameCounter;
            _eyeTracking.Receive(onScreenCoordinates);
        }

        public override void OnMouseScroll(int delta, ref bool handled)
        {
            if (delta == 0 || handled)
                return;

            if (_staticOrbitControl.ZoomByWheelDelta(delta))
                handled = true;
        }

        float GetStarMapMagnification()
        {
            return SliderFov.FovToMagnification(StarMapComponent.FoV);
        }

        bool OnStaticZoomChanged(OrbitCameraControl control, float magnification)
        {
            if (_closed)
                return false;

            float nextFov = SliderFov.MagnificationToFov(magnification);
            if (Math.Abs(StarMapComponent.FoV - nextFov) <= 0.001f)
                return false;

            StarMapComponent.FoV = nextFov;
            _lastFovChangedFrame = GetCurrentGameFrame();
            _syncConfigNextRun = true;
            LayoutChanged();
            Host.RenderSprites();
            return true;
        }

        static float NormalizeStarMapMagnification(float magnification)
        {
            return SliderFov.FovToMagnification(SliderFov.MagnificationToFov(magnification));
        }

        void DrawMessage(List<MySprite> sprites, string message, string icon, Color color, float scale = 1f)
        {
            var center = ViewBox.Center;
            float iconSize = Math.Min(ViewBox.Width, ViewBox.Height) * .4f * scale;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = icon,
                Position = center,
                Size = new Vector2(iconSize),
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = message,
                Position = new Vector2(center.X, center.Y + iconSize / 2f),
                Color = color,
                Alignment = TextAlignment.CENTER,
                FontId = TextFont,
                RotationOrScale = scale * Scale * FontScale
            });
        }

        void AddBackground(List<MySprite> sprites)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = ViewBox.Center,
                Size = new Vector2(Math.Max(ViewBox.Width, ViewBox.Height) * 2f),
                Color = new Color(BackgroundColor, 0.66f),
                Alignment = TextAlignment.CENTER
            });
        }
    }
}
