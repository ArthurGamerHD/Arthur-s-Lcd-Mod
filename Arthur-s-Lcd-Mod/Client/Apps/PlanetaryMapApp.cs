using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Custom.Camera;
using LcdMod.Client.Gui.ControlsTemplates.Custom.Planet;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Cartography;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps
{
    /// <summary>
    /// Displays the nearest planet through the reusable PlanetGlobeControl.
    /// Cubemap detail follows the projected sphere diameter up to the client-local
    /// texture-quality cap. Higher detail is requested lazily; the last completed cubemap remains
    /// visible until its replacement is ready. Only the first load of a layer uses gray.
    /// </summary>
    [LcdApp(29, Name = "PlanetaryMap")]
    [ConfigComponent(APP, typeof(PlanetaryMapConfigComponent), PropertyName = "PlanetaryMapComponent")]
    public sealed partial class PlanetaryMapApp : App, IApp
    {
        const float MINIMUM_ZOOM = 0.25f;
        const float MAXIMUM_ZOOM = 16f;
        const float ZOOM_STEP = 1.25f;
        const long PLANET_REFRESH_FRAMES = 180L;
        const long REQUEST_RETRY_FRAMES = 600L;
        const float SHIP_MARKER_SCREEN_RATIO = 0.07f;
        const float SHIP_MARKER_MINIMUM_SIZE = 16f;
        const float SHIP_MARKER_MAXIMUM_SIZE = 38f;
        const float SHIP_MARKER_TRIANGLE_OFFSET = 0.18f;
        const float SHIP_MARKER_SPHERE_OFFSET = 0.26f;
        const float SHIP_MARKER_SPHERE_SIZE = 0.56f;
        const float ORIENTATION_ICON_RATIO = 0.58f;
        const float ICON_MARGIN_PIXELS = 12f;
        const float ICON_GAP_PIXELS = 8f;
        const float ICON_SIZE_PIXELS = 42f;
        const float ICON_RATIO = 0.75f;
        const float CAMERA_BUTTON_SHADOW_RATIO = 0.055f;
        const double STREET_CAMERA_HEIGHT_METERS = 2.5d;
        const float ICON_DRAG_GHOST_OPACITY = 0.82f;
        const float ORBIT_CONFIG_EPSILON = 0.000001f;
        const float ZOOM_CONFIG_EPSILON = 0.0001f;
        const double STATIC_CAMERA_CONFIG_EPSILON_METERS = 0.001d;
        const double GPS_SURFACE_TARGET_TOLERANCE_METERS = 10000d;
        const double GPS_SURFACE_TERRAIN_MARGIN_METERS = 5000d;
        const double GPS_SURFACE_MAXIMUM_RADIUS_RATIO = 0.25d;
        const double GPS_SURFACE_MINIMUM_ALLOWED_TOLERANCE_METERS = 1000d;
        const float GPS_MARKER_SIZE_PIXELS = 12f;
        const float GPS_LABEL_SCALE = 0.5f;
        const float GPS_LABEL_GAP_PIXELS = 5f;
        const float GPS_CLUSTER_DISTANCE_PIXELS = 30f;
        const float MARKER_HITBOX_MIN_PIXELS = 18f;
        const long RADIO_SIGNAL_REFRESH_FRAMES = 60L;
        const float RADIO_SIGNAL_MARKER_SIZE_PIXELS = 14f;
        const float RADIO_SIGNAL_LABEL_SCALE = 0.5f;
        const float RADIO_SIGNAL_LABEL_GAP_PIXELS = 5f;
        const float RADIO_SIGNAL_CLUSTER_DISTANCE_PIXELS = 30f;
        const long GPS_CREATED_STATUS_FRAMES = 180L;
        const int LAYER_PREVIEW_REQUEST_FACE_SIDE = 16;
        const int LAYER_PREVIEW_MIP_SIDE = 8;

        static readonly CartographyLayer[] MapLayers =
        {
            CartographyLayer.Satellite,
            CartographyLayer.Terrain,
            CartographyLayer.Biomes
        };

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<MySprite> _cachedShipMarkerSprites = new List<MySprite>();
        readonly Dictionary<CartographyLayer, PlanetColorCubemap> _loadedCubemaps =
            new Dictionary<CartographyLayer, PlanetColorCubemap>();
        readonly Dictionary<CartographyLayer, CartographyTicket> _layerPreviewTickets =
            new Dictionary<CartographyLayer, CartographyTicket>();
        readonly Dictionary<CartographyLayer, long> _layerPreviewRetryFrames =
            new Dictionary<CartographyLayer, long>();
        CartographyModule _cartographyModule;
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
        readonly PlanetGlobeControl _planetControl;
        readonly OrbitCameraControl _orbitControl;
        readonly ButtonModel _orientationButtonModel;
        readonly ButtonModel _followButtonModel;
        readonly ToggleButton _orientationButton;
        readonly ToggleButton _followButton;
        readonly SquareComboBox<CartographyLayer> _layerSelector;
        readonly RectangleControl _streetViewControl;
        readonly Random _streetViewRandom = new Random();
        readonly Dictionary<long, StaticMarkerInteractiveState> _radioSignalMarkerInteractiveStates =
            new Dictionary<long, StaticMarkerInteractiveState>();

        sealed class StaticMarkerInteractiveState
        {
            public RectangleControl Entry;
            public string Name;
            public Vector3D Position;
            public Color Color;
            public bool UsedThisFrame;
        }

        MyPlanet _planet;
        long _planetId;
        long _lastPlanetRefreshFrame = long.MinValue;
        long _nextRequestFrame;
        int _retryFaceSide = int.MinValue;
        int _requestedFaceSide = -1;
        int _requestVersion;
        int _layerPreviewRequestVersion;
        CartographyTicket _ticket;
        PlanetColorCubemap _loadedCubemap;
        string _error;
        CartographyLayer _layer = CartographyLayer.Satellite;
        float _zoom = MAXIMUM_ZOOM;
        bool _northUp = true;
        bool _followCamera = true;
        bool _hasStaticCameraPosition;
        Vector3D _staticCameraPosition;
        bool _suppressOrbitModeChange;
        bool _syncConfigNextRun;
        bool _lastKnownConfigNorthUp = true;
        bool _lastKnownConfigFollowCamera = true;
        CartographyLayer _lastKnownConfigLayer = CartographyLayer.Satellite;
        float _lastKnownConfigOrbitYawRadians;
        float _lastKnownConfigOrbitPitchRadians;
        float _lastKnownConfigZoom = MAXIMUM_ZOOM;
        bool _lastKnownConfigHasStaticCameraPosition;
        Vector3D _lastKnownConfigStaticCameraPosition;
        long _lastRadioSignalRefreshFrame = long.MinValue;
        int _lastCreatedSurfaceGpsHash;
        bool _hasLastCreatedSurfaceGps;
        string _gpsCreatedStatus;
        long _gpsCreatedStatusUntilFrame = long.MinValue;
        bool _streetViewDragging;
        bool _streetViewAvailable;
        Vector2 _streetViewDragPointer;
        bool _closed;

        public event Action SurfaceGpsCreated;
        public event Action CameraOrbitChanged;

        public bool CanCreateSurfaceGps
        {
            get { return _planet != null && !_planet.MarkedForClose; }
        }

        public bool CanOrbitCamera
        {
            get { return CanCreateSurfaceGps; }
        }

        public PlanetaryMapApp(IAppHost host)
            : this(host, Matrix.Identity)
        {
        }

        public PlanetaryMapApp(IAppHost host, Matrix rotationTransform)
            : base(host)
        {
            _planetControl = AddLogicalChild(
                new PlanetGlobeControl(default(RectangleF)));
            _planetControl.SetRotationTransform(rotationTransform);
            _planetControl.SetZoom(_zoom);
            _planetControl.SetCursor(CursorType.Hand);
            _planetControl.SurfaceClicked = OnPlanetSurfaceClicked;
            _planetControl.SurfaceMiddleClicked = OnPlanetSurfaceMiddleClicked;
            _planetControl.ClickSound = AudioHelper.HudGps3;
            _planetControl.ClickSounds[ControlClickButton.Middle] = AudioHelper.HudClick;

            _orbitControl = AddLogicalChild(
                new OrbitCameraControl(default(RectangleF)));
            _orbitControl.CameraChanged = OnOrbitCameraChanged;
            _orbitControl.ZoomStep = ZOOM_STEP;
            _orbitControl.ZoomValueProvider = delegate { return _zoom; };
            _orbitControl.NormalizeZoomValue = ClampZoom;
            _orbitControl.ZoomChanged = OnOrbitZoomChanged;

            _orientationButtonModel = new ButtonModel
            {
                Text = LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_CardinalNorthShort"),
                Clicked = OnOrientationButtonClicked
            };
            _orientationButton = AddLogicalChild(
                new ToggleButton(default(RectangleF), _orientationButtonModel));
            _orientationButton.GetState = delegate { return _northUp; };
            _orientationButton.BorderThicknessPixels = 0f;
            _orientationButton.CustomRender = RenderOrientationButton;
            _orientationButton.SetTooltip(new InteractiveTooltip(
                GetOrientationTooltipTitle,
                () => new ITooltipLine[]
                {
                    new StaticTooltipLine(GetOrientationTooltipText())
                }));

            _followButtonModel = new ButtonModel
            {
                Text = "Lock",
                Clicked = OnFollowButtonClicked
            };
            _followButton = AddLogicalChild(
                new ToggleButton(default(RectangleF), _followButtonModel));
            _followButton.GetState = delegate { return _followCamera; };
            _followButton.BorderThicknessPixels = 0f;
            _followButton.CustomRender = RenderFollowButton;
            _followButton.SetTooltip(new InteractiveTooltip(
                GetFollowTooltipTitle,
                () => new ITooltipLine[]
                {
                    new StaticTooltipLine(GetFollowTooltipText())
                }));

            _layerSelector = AddLogicalChild(
                new SquareComboBox<CartographyLayer>(
                    MapLayers,
                    GetMapLayerLabel,
                    null,
                    OnMapLayerChanged,
                    delegate { Host.RenderSprites(); },
                    CreateMapLayerTooltip,
                    RenderMapLayerPreview));
            _layerSelector.OpenDirection = SquareComboBoxOpenDirection.Right;
            _layerSelector.OptionGapPixels = ICON_GAP_PIXELS;
            _layerSelector.BorderThicknessPixels = 0f;
            _layerSelector.ConfigureTooltips(TooltipPlacement.Above, true);
            _layerSelector.SetSelectedValue(_layer);
            _layerSelector.SetTooltip(_layerSelector.PrepareTooltip(new InteractiveTooltip(
                () => string.Format(
                    FormatingHelper.Culture,
                    LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_MapLayerFormat"),
                    GetMapLayerLabel(_layer)),
                () => new ITooltipLine[]
                {
                    new StaticTooltipLine(LocHelper.GetLoc(
                        MOD_PREFIX + "PlanetaryMap_MapLayer_Tooltip"))
                })));

            _streetViewControl = AddLogicalChild(
                new RectangleControl(default(RectangleF), CursorType.Hand));
            _streetViewControl.CustomRender = RenderStreetViewControl;
            _streetViewControl.SetDraggable();
            _streetViewControl.SetOnBeginDrag(OnStreetViewDragBegin);
            _streetViewControl.SetOnDrag(OnStreetViewDragged);
            _streetViewControl.SetOnEndDrag(OnStreetViewDragEnd);
            var streetViewTooltip = new InteractiveTooltip(
                () => LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_StreetView_Title"),
                () => new ITooltipLine[]
                {
                    new StaticTooltipLine(LocHelper.GetLoc(MOD_PREFIX + (_streetViewAvailable
                        ? "PlanetaryMap_StreetView_Available_Tooltip"
                        : "PlanetaryMap_StreetView_Unavailable_Tooltip")))
                });
            streetViewTooltip.Placement = TooltipPlacement.Above;
            _streetViewControl.SetTooltip(streetViewTooltip);

            _children.Add(_planetControl);
            _children.Add(_orbitControl);
            _children.Add(_orientationButton);
            _children.Add(_followButton);
            _children.Add(_layerSelector);
            _children.Add(_streetViewControl);

            ApplyPlanetaryMapConfig(true);

            EnsureCartographyEventSubscription();
            LocalConfigManager.TextureQualityChanged += OnTextureQualityChanged;
            ApplyTextureQuality(LocalConfigManager.TextureQuality, false);
        }

        /// <summary>
        /// Rotation applied to the displayed globe without rebuilding the
        /// cartography cubemap.
        /// </summary>
        public Matrix RotationTransform
        {
            get { return _planetControl.RotationTransform; }
            set
            {
                _planetControl.SetRotationTransform(value);
                if (!_closed)
                    Host.RenderSprites();
            }
        }

        public override IReadOnlyList<Control> VisualChildren
        {
            get { return _children; }
        }

        public CursorType RequestedCursorType { get; private set; } = CursorType.Default;

        public override void Update()
        {
            EnsureCartographyEventSubscription();
            ApplyPlanetaryMapConfig(false);
            SyncConfigIfNeeded();

            long frame = GetCurrentFrame();

            if (_planet == null ||
                _planet.MarkedForClose ||
                frame - _lastPlanetRefreshFrame >= PLANET_REFRESH_FRAMES)
            {
                _lastPlanetRefreshFrame = frame;
                ResolveNearestPlanet();
            }

            UpdateStreetViewAvailability();
            UpdatePlanetControl();
            UpdateShipMarkerCache();
            UpdateCubemapDetail(frame);
            UpdateLayerPreviewCubemaps(frame);
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();
            BeginMarkerInteractiveFrame();
            UpdateStreetViewAvailability();

            if (_planet != null && !_planet.MarkedForClose)
            {
                UpdatePlanetControl();
                UpdateShipMarkerCache();
                long frame = GetCurrentFrame();
                UpdateCubemapDetail(frame);
                UpdateLayerPreviewCubemaps(frame);
            }

            if (_planet == null)
            {
                AddCenteredStatus(LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_NoPlanet"), Host.ForegroundColor);
                RenderCameraControls();
                FinalizeMarkerInteractiveFrame();
                return _sprites;
            }

            // PlanetGlobeControl draws one gray Circle only before the current layer
            // has a cubemap. Detail upgrades keep the previous map visible.
            _planetControl.Render(_sprites);
            AddGpsMarkers(_sprites);
            AddRadioSignalMarkers(_sprites);
            _sprites.AddRange(_cachedShipMarkerSprites);

            if (!string.IsNullOrWhiteSpace(_error))
                AddBottomStatus(_error, new Color(255, 80, 80));
            else
                AddGpsCreatedStatus();

            RenderCameraControls();
            FinalizeMarkerInteractiveFrame();

            return _sprites;
        }

        public override void LayoutChanged()
        {
            UpdatePlanetControlBounds();
            UpdateCameraControlBounds();
        }

        public override void OnMouseScroll(int delta, ref bool handled)
        {
            if (handled || delta == 0)
                return;

            if (!_orbitControl.ZoomByWheelDelta(delta))
                return;

            handled = true;
        }

        public override void Close()
        {
            _closed = true;
            ClearCartographyEventSubscription();
            LocalConfigManager.TextureQualityChanged -= OnTextureQualityChanged;
            _planetId = 0L;
            _orbitControl.StopCameraInertia();
            CancelPendingRequest();
            CancelLayerPreviewRequests();
            _planetControl.SetCubemap(null);
            _loadedCubemap = null;
            _loadedCubemaps.Clear();
            _layerSelector.Close();
            _streetViewDragging = false;
            _planet = null;
            _cachedShipMarkerSprites.Clear();
            _gpsEntries.Clear();
            _radioSignals.Clear();
            _radioSignalMarkerInteractiveStates.Clear();
            _sprites.Clear();
            base.Close();
        }

        void BeginMarkerInteractiveFrame()
        {
            foreach (StaticMarkerInteractiveState state in _radioSignalMarkerInteractiveStates.Values)
                state.UsedThisFrame = false;
        }

        void FinalizeMarkerInteractiveFrame()
        {
            HideUnusedMarkerEntries(_radioSignalMarkerInteractiveStates);
        }

        static void HideUnusedMarkerEntries(Dictionary<long, StaticMarkerInteractiveState> states)
        {
            foreach (StaticMarkerInteractiveState state in states.Values)
            {
                if (!state.UsedThisFrame && state.Entry != null)
                    state.Entry.SetVisible(false);
            }
        }

        void ResolveNearestPlanet()
        {
            MyPlanet nearest = null;
            double nearestSurfaceDistance = double.MaxValue;
            Vector3D position = Host.Block.WorldMatrix.Translation;

            foreach (var pair in PlanetHelper.PlanetsById)
            {
                MyPlanet candidate = pair.Value;
                if (candidate == null || candidate.MarkedForClose)
                    continue;

                double radius = candidate.AverageRadius > 0d
                    ? candidate.AverageRadius
                    : candidate.MaximumRadius;
                double centerDistance = Vector3D.Distance(
                    position,
                    candidate.WorldMatrix.Translation);
                double surfaceDistance = Math.Max(0d, centerDistance - radius);

                if (surfaceDistance >= nearestSurfaceDistance)
                    continue;

                nearest = candidate;
                nearestSurfaceDistance = surfaceDistance;
            }

            long nearestId = nearest?.EntityId ?? 0L;
            if (nearestId == _planetId)
            {
                _planet = nearest;
                return;
            }

            CancelPendingRequest();
            CancelLayerPreviewRequests();
            _planet = nearest;
            _planetId = nearestId;
            _loadedCubemap = null;
            _loadedCubemaps.Clear();
            _planetControl.SetCubemap(null);
            _error = null;
            _retryFaceSide = int.MinValue;
            _nextRequestFrame = 0L;
        }

        void UpdateCubemapDetail(long frame)
        {
            if (_planet == null || _planet.MarkedForClose)
            {
                _planetControl.SetCubemap(null);
                return;
            }

            int preferredFaceSide = _planetControl.GetPreferredFaceSide();
            if (_loadedCubemap != null &&
                _loadedCubemap.SatisfiesFaceSide(preferredFaceSide))
            {
                if (_ticket != null)
                    CancelPendingRequest();

                _planetControl.SetCubemap(_loadedCubemap);
                _error = null;
                return;
            }

            // Keep the last completed map visible while a sharper request runs.
            // The control will sample its best available mip until the replacement
            // is atomically swapped in by the completion callback.
            _planetControl.SetCubemap(_loadedCubemap);

            if (_ticket != null)
            {
                if (_requestedFaceSide == preferredFaceSide)
                    return;

                CancelPendingRequest();
            }

            if (_retryFaceSide == preferredFaceSide && frame < _nextRequestFrame)
                return;

            RequestCubemap(_planet, preferredFaceSide);
        }

        void RequestCubemap(MyPlanet planet, int faceSide)
        {
            CartographyModule module = GetCartographyModule();
            if (module == null)
            {
                _error = LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_CartographyNotReady");
                ScheduleRequestRetry(faceSide);
                return;
            }

            long requestedPlanetId = planet.EntityId;
            CartographyLayer requestedLayer = _layer;
            _requestedFaceSide = faceSide;
            _error = null;

            try
            {
                var request = new CartographyRequest
                {
                    PlanetEntityId = requestedPlanetId,
                    PlanetRadiusMeters = planet.AverageRadius,
                    Projection = CartographyProjection.CubemapFaces,
                    Layer = requestedLayer,
                    MaximumFaceSide = faceSide,
                    ReturnColorCubemap = true
                };

                PlanetColorCubemap cachedCubemap;
                string cachedFailure;
                if (module.TryGetCachedColorCubemap(
                        request,
                        out cachedCubemap,
                        out cachedFailure))
                {
                    if (cachedFailure != null)
                    {
                        _requestedFaceSide = -1;
                        _retryFaceSide = int.MinValue;
                        _error = cachedFailure;
                        return;
                    }

                    StoreLayerCubemap(requestedLayer, cachedCubemap);
                    _planetControl.SetCubemap(_loadedCubemap);
                    _requestedFaceSide = -1;
                    _retryFaceSide = int.MinValue;
                    _error = null;
                    return;
                }

                int requestVersion = ++_requestVersion;
                _ticket = module.RequestMap(
                    request,
                    delegate(CartographyResult result)
                    {
                        if (_closed || requestVersion != _requestVersion)
                            return;

                        _ticket = null;
                        _requestedFaceSide = -1;
                        if (_planetId != requestedPlanetId || _layer != requestedLayer)
                            return;

                        if (result == null || !result.Success || result.ColorCubemap == null)
                        {
                            if (result != null && !string.IsNullOrWhiteSpace(result.Error))
                                LogHelper.Log(VRage.Utils.MyLogSeverity.Warning,
                                    "Planetary map cartography failed: " + result.Error);

                            _error = result != null
                                ? GetCartographyErrorText(result.Error)
                                : LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_NoResult");
                            ScheduleRequestRetry(faceSide);
                        }
                        else
                        {
                            StoreLayerCubemap(requestedLayer, result.ColorCubemap);

                            _retryFaceSide = int.MinValue;
                            _error = null;
                        }
                    });
            }
            catch (Exception error)
            {
                LogHelper.Log(VRage.Utils.MyLogSeverity.Warning,
                    "Planetary map cartography request failed: " + error.Message);
                _ticket = null;
                _requestedFaceSide = -1;
                _error = LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_CartographyFailed");
                ScheduleRequestRetry(faceSide);
            }
        }

        static string GetCartographyErrorText(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
                return LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_NoColorCubemap");

            return LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_CartographyFailed");
        }

        void CancelPendingRequest()
        {
            _requestVersion++;
            if (_ticket != null)
                _ticket.Cancel();

            _ticket = null;
            _requestedFaceSide = -1;
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
                cached.PlanetEntityId != _planetId)
            {
                return;
            }

            bool redraw = false;
            if (cached.Layer == _layer &&
                _retryFaceSide != int.MinValue &&
                cached.ColorCubemap.SatisfiesFaceSide(_retryFaceSide))
            {
                _retryFaceSide = int.MinValue;
                _nextRequestFrame = 0L;
                _error = null;
                StoreLayerCubemap(cached.Layer, cached.ColorCubemap);
                _planetControl.SetCubemap(_loadedCubemap);
                redraw = true;
            }

            if (_layerPreviewRetryFrames.ContainsKey(cached.Layer) &&
                cached.ColorCubemap.SatisfiesFaceSide(LAYER_PREVIEW_REQUEST_FACE_SIDE))
            {
                _layerPreviewRetryFrames.Remove(cached.Layer);
                if (StoreLayerCubemap(cached.Layer, cached.ColorCubemap))
                    redraw = true;
            }

            if (redraw)
                Host.RenderSprites();
        }

        void UpdateLayerPreviewCubemaps(long frame)
        {
            if (_planet == null || _planet.MarkedForClose || _ticket != null ||
                _loadedCubemap == null ||
                !_loadedCubemap.SatisfiesFaceSide(_planetControl.GetPreferredFaceSide()))
            {
                return;
            }

            CartographyModule module = GetCartographyModule();
            if (module == null)
                return;

            for (int i = 0; i < MapLayers.Length; i++)
            {
                CartographyLayer layer = MapLayers[i];
                if (layer == _layer || _layerPreviewTickets.ContainsKey(layer))
                    continue;

                PlanetColorCubemap cubemap;
                if (_loadedCubemaps.TryGetValue(layer, out cubemap) &&
                    cubemap != null &&
                    cubemap.SatisfiesFaceSide(LAYER_PREVIEW_REQUEST_FACE_SIDE))
                {
                    continue;
                }

                long retryFrame;
                if (_layerPreviewRetryFrames.TryGetValue(layer, out retryFrame) &&
                    frame < retryFrame)
                {
                    continue;
                }

                RequestLayerPreview(module, _planet, layer);
            }
        }

        void RequestLayerPreview(
            CartographyModule module,
            MyPlanet planet,
            CartographyLayer layer)
        {
            long requestedPlanetId = planet.EntityId;
            var request = new CartographyRequest
            {
                PlanetEntityId = requestedPlanetId,
                PlanetRadiusMeters = planet.AverageRadius,
                Projection = CartographyProjection.CubemapFaces,
                Layer = layer,
                MaximumFaceSide = LAYER_PREVIEW_REQUEST_FACE_SIDE,
                ReturnColorCubemap = true
            };

            try
            {
                PlanetColorCubemap cachedCubemap;
                string cachedFailure;
                if (module.TryGetCachedColorCubemap(
                        request,
                        out cachedCubemap,
                        out cachedFailure))
                {
                    if (cachedFailure != null)
                    {
                        _layerPreviewRetryFrames[layer] = long.MaxValue;
                        return;
                    }

                    _layerPreviewRetryFrames.Remove(layer);
                    StoreLayerCubemap(layer, cachedCubemap);
                    return;
                }

                int requestVersion = _layerPreviewRequestVersion;
                _layerPreviewTickets[layer] = module.RequestMap(
                    request,
                    delegate(CartographyResult result)
                    {
                        if (_closed || requestVersion != _layerPreviewRequestVersion ||
                            _planetId != requestedPlanetId)
                        {
                            return;
                        }

                        _layerPreviewTickets.Remove(layer);
                        if (result == null || !result.Success || result.ColorCubemap == null)
                        {
                            _layerPreviewRetryFrames[layer] =
                                GetCurrentFrame() + REQUEST_RETRY_FRAMES;
                            return;
                        }

                        _layerPreviewRetryFrames.Remove(layer);
                        if (StoreLayerCubemap(layer, result.ColorCubemap))
                            Host.RenderSprites();
                    });
            }
            catch (Exception error)
            {
                LogHelper.Log(VRage.Utils.MyLogSeverity.Warning,
                    "Planetary map layer preview request failed: " + error.Message);
                _layerPreviewTickets.Remove(layer);
                _layerPreviewRetryFrames[layer] =
                    GetCurrentFrame() + REQUEST_RETRY_FRAMES;
            }
        }

        bool StoreLayerCubemap(CartographyLayer layer, PlanetColorCubemap cubemap)
        {
            if (cubemap == null)
                return false;

            PlanetColorCubemap existing;
            if (_loadedCubemaps.TryGetValue(layer, out existing) &&
                existing != null &&
                existing.DetailRank >= cubemap.DetailRank)
            {
                return false;
            }

            _loadedCubemaps[layer] = cubemap;
            if (_layer == layer)
                _loadedCubemap = cubemap;
            return true;
        }

        void CancelLayerPreviewRequests()
        {
            _layerPreviewRequestVersion++;
            foreach (CartographyTicket ticket in _layerPreviewTickets.Values)
            {
                if (ticket != null)
                    ticket.Cancel();
            }

            _layerPreviewTickets.Clear();
            _layerPreviewRetryFrames.Clear();
        }

        void UpdatePlanetControl()
        {
            UpdatePlanetControlBounds();
            UpdateCameraControlBounds();
            _planetControl.SetEnabled(_planet != null && !_planet.MarkedForClose);
            _planetControl.SetZoom(_zoom);
            _planetControl.SetClipBounds(Host.ViewBox);

            if (_planet != null)
                UpdatePlanetProjection(_planet);
        }

        void UpdatePlanetControlBounds()
        {
            _planetControl.SetRect(Host.ViewBox);
        }

        void UpdateCameraControlBounds()
        {
            RectangleF viewBox = Host.ViewBox;
            _orbitControl.SetRect(viewBox);

            float scale = Math.Max(0.5f, Host.ConfiguredScale);
            float margin = ICON_MARGIN_PIXELS * scale;
            float gap = ICON_GAP_PIXELS * scale;
            float availableSize = Math.Min(
                Math.Max(1f, viewBox.Width - margin * 2f),
                Math.Max(1f, (viewBox.Height - margin * 2f - gap) / 2f));
            float size = Math.Min(ICON_SIZE_PIXELS * scale, availableSize);

            float x = viewBox.Right - margin - size;
            float y = viewBox.Bottom - margin - size;
            _followButton.SetRect(new RectangleF(x, y, size, size));
            _orientationButton.SetRect(new RectangleF(x, y - gap - size, size, size));

            float layerX = viewBox.X + margin;
            float layerRowWidth = Math.Max(1f, x - gap - layerX);
            float layerSquareWidth = Math.Max(1f,
                layerRowWidth - gap * MapLayers.Length);
            float layerSize = Math.Min(
                size,
                layerSquareWidth / (MapLayers.Length + 1));
            float layerY = viewBox.Bottom - margin - layerSize;
            _layerSelector.Configure(
                new RectangleF(layerX, layerY, layerSize, layerSize),
                scale);
            _streetViewControl.SetRect(new RectangleF(
                layerX,
                layerY - gap - layerSize,
                layerSize,
                layerSize));
        }

        void UpdatePlanetProjection(MyPlanet planet)
        {
            Vector3 baseViewDirection = GetPlanetLocalViewDirection(planet);
            Vector3 referenceUpDirection = _northUp
                ? Vector3.Up
                : GetPlanetLocalForwardDirection(planet);

            Vector3 viewDirection;
            Vector3 screenRightDirection;
            Vector3 screenUpDirection;
            _orbitControl.BuildProjection(
                baseViewDirection,
                referenceUpDirection,
                out viewDirection,
                out screenRightDirection,
                out screenUpDirection);
            _planetControl.SetProjection(
                viewDirection,
                screenRightDirection,
                screenUpDirection);
        }

        Vector3 GetPlanetLocalViewDirection(MyPlanet planet)
        {
            Vector3D cameraPosition = GetCameraWorldPosition();
            Vector3D worldDirection = cameraPosition - planet.WorldMatrix.Translation;

            if (worldDirection.Normalize() <= 1e-9)
                worldDirection = planet.WorldMatrix.Backward;

            Vector3 local = WorldDirectionToPlanetLocal(planet, worldDirection);

            if (local.Normalize() <= 1e-6f)
                return Vector3.Backward;

            return local;
        }

        Vector3 GetPlanetLocalForwardDirection(MyPlanet planet)
        {
            MatrixD shipWorld;
            if (TryGetShipWorldMatrix(out shipWorld))
            {
                Vector3 localForward = WorldDirectionToPlanetLocal(
                    planet,
                    shipWorld.Forward);
                if (localForward.Normalize() > 1e-6f)
                    return localForward;
            }

            return Vector3.Up;
        }

        Vector3D GetCameraWorldPosition()
        {
            if (!_followCamera && _hasStaticCameraPosition)
                return _staticCameraPosition;

            MatrixD shipWorld;
            if (TryGetShipWorldMatrix(out shipWorld))
                return shipWorld.Translation;

            return Host.Block != null
                ? Host.Block.WorldMatrix.Translation
                : Vector3D.Zero;
        }

        void OnOrbitCameraChanged(OrbitCameraControl control)
        {
            if (_closed)
                return;

            if (!_suppressOrbitModeChange && _followCamera)
            {
                SetFollowCamera(false, false);
            }

            PersistPlanetaryMapConfig();

            UpdatePlanetControl();
            Host.RenderSprites();
            RaiseCameraOrbitChanged();
        }

        bool OnOrbitZoomChanged(OrbitCameraControl control, float zoom)
        {
            if (_closed)
                return false;

            float next = ClampZoom(zoom);
            if (Math.Abs(next - _zoom) <= ZOOM_CONFIG_EPSILON)
                return false;

            SetZoom(next);
            PersistPlanetaryMapConfig();
            Host.RenderSprites();
            return true;
        }

        void RaiseCameraOrbitChanged()
        {
            var cameraOrbitChanged = CameraOrbitChanged;
            if (cameraOrbitChanged != null)
                cameraOrbitChanged();
        }

        void OnOrientationButtonClicked(ButtonModel model, object sender)
        {
            SetNorthUp(!_northUp);
            PersistPlanetaryMapConfig();
            UpdatePlanetControl();
            Host.RenderSprites();
        }

        void OnFollowButtonClicked(ButtonModel model, object sender)
        {
            SetFollowCamera(!_followCamera, true);
            PersistPlanetaryMapConfig();
            UpdatePlanetControl();
            Host.RenderSprites();
        }

        void OnMapLayerChanged(CartographyLayer layer)
        {
            SetMapLayer(layer);
            PersistPlanetaryMapConfig();
            Host.RenderSprites();
        }

        void ApplyPlanetaryMapConfig(bool force)
        {
            PlanetaryMapConfigComponent config = PlanetaryMapComponent;
            Vector3D staticCameraPosition = GetConfigStaticCameraPosition(config);
            float zoom = ClampZoom(config.Zoom);
            CartographyLayer layer = NormalizeMapLayer(config.MapLayer);
            bool staticCameraChanged =
                config.HasStaticCameraPosition != _lastKnownConfigHasStaticCameraPosition ||
                (config.HasStaticCameraPosition &&
                 !NearlyEqual(staticCameraPosition, _lastKnownConfigStaticCameraPosition));

            if (force || config.NorthUp != _lastKnownConfigNorthUp)
            {
                SetNorthUp(config.NorthUp);
                _lastKnownConfigNorthUp = config.NorthUp;
            }

            if (force || layer != _lastKnownConfigLayer)
            {
                SetMapLayer(layer);
                _lastKnownConfigLayer = layer;
            }

            if (force ||
                !NearlyEqual(config.OrbitYawRadians, _lastKnownConfigOrbitYawRadians) ||
                !NearlyEqual(config.OrbitPitchRadians, _lastKnownConfigOrbitPitchRadians))
            {
                _orbitControl.SetOrbit(
                    config.OrbitYawRadians,
                    config.OrbitPitchRadians,
                    false);
                _lastKnownConfigOrbitYawRadians = config.OrbitYawRadians;
                _lastKnownConfigOrbitPitchRadians = config.OrbitPitchRadians;
            }

            if (force || !NearlyEqualZoom(zoom, _lastKnownConfigZoom))
            {
                SetZoom(zoom);
                _lastKnownConfigZoom = zoom;
            }

            if (force ||
                config.FollowCamera != _lastKnownConfigFollowCamera ||
                staticCameraChanged)
            {
                ApplyFollowCameraConfig(config, staticCameraPosition, force);
                CaptureConfigSnapshot(config);
            }
        }

        void PersistPlanetaryMapConfig()
        {
            PlanetaryMapConfigComponent config = PlanetaryMapComponent;
            bool changed = false;
            float orbitYawRadians = _orbitControl.YawRadians;
            float orbitPitchRadians = _orbitControl.PitchRadians;

            if (config.NorthUp != _northUp)
            {
                config.NorthUp = _northUp;
                changed = true;
            }

            if (config.FollowCamera != _followCamera)
            {
                config.FollowCamera = _followCamera;
                changed = true;
            }

            if (config.MapLayer != (int)_layer)
            {
                config.MapLayer = (int)_layer;
                changed = true;
            }

            if (!NearlyEqual(config.OrbitYawRadians, orbitYawRadians))
            {
                config.OrbitYawRadians = orbitYawRadians;
                changed = true;
            }

            if (!NearlyEqual(config.OrbitPitchRadians, orbitPitchRadians))
            {
                config.OrbitPitchRadians = orbitPitchRadians;
                changed = true;
            }

            if (!NearlyEqualZoom(config.Zoom, _zoom))
            {
                config.Zoom = _zoom;
                changed = true;
            }

            if (config.HasStaticCameraPosition != _hasStaticCameraPosition)
            {
                config.HasStaticCameraPosition = _hasStaticCameraPosition;
                changed = true;
            }

            if (_hasStaticCameraPosition &&
                !NearlyEqual(GetConfigStaticCameraPosition(config), _staticCameraPosition))
            {
                config.StaticCameraPositionX = _staticCameraPosition.X;
                config.StaticCameraPositionY = _staticCameraPosition.Y;
                config.StaticCameraPositionZ = _staticCameraPosition.Z;
                changed = true;
            }

            CaptureConfigSnapshot(config);

            if (changed)
                _syncConfigNextRun = true;
        }

        void SyncConfigIfNeeded()
        {
            if (!_syncConfigNextRun)
                return;

            _syncConfigNextRun = false;
            if (Host.Block != null && Host.ProviderConfig != null)
                ConfigManager.Sync(Host.Block, Host.ProviderConfig);
        }

        void SetNorthUp(bool northUp)
        {
            _northUp = northUp;
            _orientationButtonModel.Text = "N";
            _orientationButton.MarkDirty();
        }

        void SetMapLayer(CartographyLayer layer)
        {
            layer = NormalizeMapLayer((int)layer);
            _layerSelector.SetSelectedValue(layer);
            if (_layer == layer)
                return;

            CancelPendingRequest();
            CancelLayerPreviewRequests();
            _layer = layer;

            PlanetColorCubemap cached;
            _loadedCubemap = _loadedCubemaps.TryGetValue(layer, out cached)
                ? cached
                : null;
            _planetControl.SetCubemap(_loadedCubemap);
            _error = null;
            _retryFaceSide = int.MinValue;
            _nextRequestFrame = 0L;
        }

        void SetZoom(float zoom)
        {
            _zoom = ClampZoom(zoom);
            _planetControl.SetZoom(_zoom);
        }

        void ApplyFollowCameraConfig(
            PlanetaryMapConfigComponent config,
            Vector3D staticCameraPosition,
            bool force)
        {
            if (config.FollowCamera)
            {
                SetFollowCamera(true, !force);
                return;
            }

            if (config.HasStaticCameraPosition)
            {
                _staticCameraPosition = staticCameraPosition;
                _hasStaticCameraPosition = true;
            }
            else
            {
                _staticCameraPosition = GetCameraWorldPosition();
                _hasStaticCameraPosition = true;
            }

            _followCamera = false;
            UpdateFollowButtonState();
        }

        void SetFollowCamera(bool follow, bool resetOrbitWhenFollowing)
        {
            if (follow)
            {
                _followCamera = true;
                _hasStaticCameraPosition = false;

                if (resetOrbitWhenFollowing)
                {
                    _suppressOrbitModeChange = true;
                    try
                    {
                        _orbitControl.ResetOrbit();
                    }
                    finally
                    {
                        _suppressOrbitModeChange = false;
                    }
                }
            }
            else
            {
                if (_followCamera || !_hasStaticCameraPosition)
                {
                    _staticCameraPosition = GetCameraWorldPosition();
                    _hasStaticCameraPosition = true;
                }

                _followCamera = false;
            }

            UpdateFollowButtonState();
        }

        void UpdateFollowButtonState()
        {
            _followButtonModel.Text = GetFollowTooltipTitle();
            _followButton.MarkDirty();
        }

        void CaptureConfigSnapshot(PlanetaryMapConfigComponent config)
        {
            _lastKnownConfigNorthUp = config.NorthUp;
            _lastKnownConfigFollowCamera = config.FollowCamera;
            _lastKnownConfigLayer = NormalizeMapLayer(config.MapLayer);
            _lastKnownConfigOrbitYawRadians = config.OrbitYawRadians;
            _lastKnownConfigOrbitPitchRadians = config.OrbitPitchRadians;
            _lastKnownConfigZoom = ClampZoom(config.Zoom);
            _lastKnownConfigHasStaticCameraPosition = config.HasStaticCameraPosition;
            _lastKnownConfigStaticCameraPosition = GetConfigStaticCameraPosition(config);
        }

        static CartographyLayer NormalizeMapLayer(int value)
        {
            switch ((CartographyLayer)value)
            {
                case CartographyLayer.Terrain:
                    return CartographyLayer.Terrain;
                case CartographyLayer.Biomes:
                    return CartographyLayer.Biomes;
                default:
                    return CartographyLayer.Satellite;
            }
        }

        static Vector3D GetConfigStaticCameraPosition(PlanetaryMapConfigComponent config)
        {
            return new Vector3D(
                config.StaticCameraPositionX,
                config.StaticCameraPositionY,
                config.StaticCameraPositionZ);
        }

        static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) <= ORBIT_CONFIG_EPSILON;
        }

        static bool NearlyEqualZoom(float left, float right)
        {
            return Math.Abs(left - right) <= ZOOM_CONFIG_EPSILON;
        }

        static float ClampZoom(float zoom)
        {
            if (float.IsNaN(zoom) || float.IsInfinity(zoom))
                return MAXIMUM_ZOOM;

            return MathHelper.Clamp(zoom, MINIMUM_ZOOM, MAXIMUM_ZOOM);
        }

        static bool NearlyEqual(Vector3D left, Vector3D right)
        {
            return Vector3D.DistanceSquared(left, right) <=
                   STATIC_CAMERA_CONFIG_EPSILON_METERS * STATIC_CAMERA_CONFIG_EPSILON_METERS;
        }

        static string GetMapLayerLabel(CartographyLayer layer)
        {
            switch (NormalizeMapLayer((int)layer))
            {
                case CartographyLayer.Terrain:
                    return LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_Layer_Terrain");
                case CartographyLayer.Biomes:
                    return LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_Layer_Biomes");
                default:
                    return LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_Layer_Satellite");
            }
        }

        void RenderMapLayerPreview(
            ControlTemplate control,
            RectangleF bounds,
            CartographyLayer layer,
            Color outlineColor,
            List<MySprite> sprites)
        {
            PlanetColorCubemap cubemap;
            if (!_loadedCubemaps.TryGetValue(
                    NormalizeMapLayer((int)layer),
                    out cubemap) || cubemap == null)
            {
                AddMapLayerPreviewRect(
                    sprites,
                    bounds,
                    control.GetResourceColor(
                        ThemeResources.SurfaceContainerHighestColor,
                        new Color(24, 24, 24)));
                return;
            }

            PlanetCubeFace face = GetPlayerPreviewFace();
            int mipLevel = cubemap.SelectMipLevel(LAYER_PREVIEW_MIP_SIDE);
            int mipResolution = cubemap.GetMipResolution(mipLevel);
            int sampleSide = Math.Max(1, Math.Min(LAYER_PREVIEW_MIP_SIDE, mipResolution));
            float cellWidth = bounds.Width / sampleSide;
            float cellHeight = bounds.Height / sampleSide;

            for (int y = 0; y < sampleSide; y++)
            {
                float v = (y + 0.5f) / sampleSide;
                for (int x = 0; x < sampleSide; x++)
                {
                    float u = (x + 0.5f) / sampleSide;
                    Color color = cubemap.SampleFace(face, u, v, mipLevel);
                    AddMapLayerPreviewRect(
                        sprites,
                        new RectangleF(
                            bounds.X + x * cellWidth,
                            bounds.Y + y * cellHeight,
                            Math.Max(1f, cellWidth + 0.5f),
                            Math.Max(1f, cellHeight + 0.5f)),
                        color);
                }
            }
        }

        PlanetCubeFace GetPlayerPreviewFace()
        {
            if (_planet == null || _planet.MarkedForClose)
                return PlanetCubeFace.Front;

            MatrixD playerWorld;
            Vector3D playerPosition = TryGetShipWorldMatrix(out playerWorld)
                ? playerWorld.Translation
                : (Host.Block != null
                    ? Host.Block.WorldMatrix.Translation
                    : _planet.WorldMatrix.Translation + _planet.WorldMatrix.Backward);
            Vector3D worldDirection = playerPosition - _planet.WorldMatrix.Translation;
            if (worldDirection.Normalize() <= 1e-9)
                worldDirection = _planet.WorldMatrix.Backward;

            Vector3 localDirection = WorldDirectionToPlanetLocal(_planet, worldDirection);
            if (localDirection.Normalize() <= 1e-6f)
                return PlanetCubeFace.Front;

            PlanetCubeFace face;
            float u;
            float v;
            PlanetMapSource.DirectionToFaceUv(localDirection, out face, out u, out v);
            return face;
        }

        static void AddMapLayerPreviewRect(
            List<MySprite> sprites,
            RectangleF bounds,
            Color color)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = bounds.Center,
                Size = bounds.Size,
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        static InteractiveTooltip CreateMapLayerTooltip(CartographyLayer layer)
        {
            string description;
            switch (NormalizeMapLayer((int)layer))
            {
                case CartographyLayer.Terrain:
                    description = LocHelper.GetLoc(
                        MOD_PREFIX + "PlanetaryMap_Layer_Terrain_Tooltip");
                    break;
                case CartographyLayer.Biomes:
                    description = LocHelper.GetLoc(
                        MOD_PREFIX + "PlanetaryMap_Layer_Biomes_Tooltip");
                    break;
                default:
                    description = LocHelper.GetLoc(
                        MOD_PREFIX + "PlanetaryMap_Layer_Satellite_Tooltip");
                    break;
            }

            return new InteractiveTooltip(
                GetMapLayerLabel(layer),
                new ITooltipLine[] { new StaticTooltipLine(description) });
        }

        string GetOrientationTooltipTitle()
        {
            return LocHelper.GetLoc(MOD_PREFIX + (_northUp
                ? "PlanetaryMap_NorthUp_Title"
                : "PlanetaryMap_CurrentUp_Title"));
        }

        string GetOrientationTooltipText()
        {
            return LocHelper.GetLoc(MOD_PREFIX + (_northUp
                ? "PlanetaryMap_NorthUp_Tooltip"
                : "PlanetaryMap_CurrentUp_Tooltip"));
        }

        string GetFollowTooltipTitle()
        {
            return LocHelper.GetLoc(MOD_PREFIX + (_followCamera
                ? "PlanetaryMap_FollowCamera_Title"
                : "PlanetaryMap_FreeCamera_Title"));
        }

        string GetFollowTooltipText()
        {
            return LocHelper.GetLoc(MOD_PREFIX + (_followCamera
                ? "PlanetaryMap_FollowCamera_Tooltip"
                : "PlanetaryMap_FreeCamera_Tooltip"));
        }

        void RenderCameraControls()
        {
            _orientationButton.Render(_sprites);
            _followButton.Render(_sprites);
            _layerSelector.Render(_sprites);
            _streetViewControl.Render(_sprites);

            if (_streetViewDragging)
                RenderStreetViewDragGhost(_sprites);
        }

        void RenderStreetViewControl(ControlTemplate control, List<MySprite> sprites)
        {
            RectangleF bounds = control.Bounds;
            Color iconColor = control.Enabled
                ? control.GetResourceColor(ThemeResources.AccentColor, Host.ForegroundColor)
                : control.GetResourceColor(ThemeResources.DisabledColor, Host.ForegroundColor);
            AddMarkerTexture(
                sprites,
                "StreetView",
                bounds.Center,
                new Vector2(Math.Min(bounds.Width, bounds.Height) * ICON_RATIO),
                iconColor,
                0f);
        }

        void RenderStreetViewDragGhost(List<MySprite> sprites)
        {
            float size = Math.Min(_streetViewControl.Bounds.Width, _streetViewControl.Bounds.Height);
            if (size <= 0f)
                return;

            Vector3 localDirection;
            Vector3D surfacePosition;
            bool validDrop = _streetViewAvailable &&
                             _planet != null &&
                             !_planet.MarkedForClose &&
                             TryGetSurfaceClickPosition(
                                 _planet,
                                 _streetViewDragPointer,
                                 out localDirection,
                                 out surfacePosition);

            Color mask = validDrop
                ? _streetViewControl.GetResourceColor(ThemeResources.AccentColor, Host.ForegroundColor)
                : _streetViewControl.GetResourceColor(ThemeResources.DisabledColor, Host.ForegroundColor);
            mask.A = (byte)(byte.MaxValue * ICON_DRAG_GHOST_OPACITY);

            Color shadow = _streetViewControl.GetResourceColor(
                ThemeResources.SuccessColor,
                new Color(0, 0, 0, 160));
            shadow.A = (byte)(shadow.A * ICON_DRAG_GHOST_OPACITY);

            AddMarkerTexture(
                sprites,
                "CircleHollow",
                _streetViewDragPointer,
                new Vector2(size/3),
                shadow,
                0f);
            AddMarkerTexture(
                sprites,
                "StreetView",
                _streetViewDragPointer + new Vector2(size/3, -size/3),
                new Vector2(size * ICON_RATIO),
                mask,
                0f);
        }

        void RenderOrientationButton(ControlTemplate control, List<MySprite> sprites)
        {
            RenderCircularCameraButton(control, sprites, _northUp);
            Color contentColor = GetCameraButtonContentColor(control, _northUp);

            if (_northUp)
            {
                AddCenteredButtonText(
                    control,
                    sprites,
                    _orientationButtonModel.Text,
                    control.GetResourceColor(ThemeResources.ErrorColor, contentColor));
                return;
            }

            AddCurrentPositionGlyph(
                sprites,
                control.Bounds.Center,
                Math.Min(control.Bounds.Width, control.Bounds.Height) * ORIENTATION_ICON_RATIO,
                contentColor);
        }

        void RenderFollowButton(ControlTemplate control, List<MySprite> sprites)
        {
            RenderCircularCameraButton(control, sprites, _followCamera);
            Color contentColor = GetCameraButtonContentColor(control, _followCamera);
            AddMarkerTexture(
                sprites,
                _followCamera ? "Lock" : "RotationPlane",
                control.Bounds.Center,
                new Vector2(Math.Min(control.Bounds.Width, control.Bounds.Height) * ICON_RATIO),
                contentColor,
                0f);
        }

        void RenderCircularCameraButton(ControlTemplate control, List<MySprite> sprites, bool selected)
        {
            RectangleF bounds = control.Bounds;
            float diameter = Math.Min(bounds.Width, bounds.Height);
            if (diameter <= 0f)
                return;

            Color fill = selected
                ? control.GetResourceColor(ThemeResources.AccentContainerColor, control.BackgroundColor)
                : control.GetResourceColor(ThemeResources.SurfaceContainerHighColor, control.BackgroundColor);

            if (control.IsPointerOver)
            {
                fill = selected
                    ? control.GetResourceColor(ThemeResources.AccentColor, fill)
                    : control.GetResourceColor(ThemeResources.SurfaceContainerHighestColor, fill);
            }

            if (control.IsPressed)
                fill = control.GetResourceColor(ThemeResources.SecondaryContainerColor, fill);

            Color outline = selected
                ? control.GetResourceColor(ThemeResources.AccentColor, Host.ForegroundColor)
                : control.GetResourceColor(ThemeResources.BorderVariantColor, Host.ForegroundColor);
            Color shadow = control.GetResourceColor(ThemeResources.ShadowColor, new Color(0, 0, 0, 160));
            Vector2 shadowOffset = new Vector2(Math.Max(1f, diameter * CAMERA_BUTTON_SHADOW_RATIO));

            AddMarkerTexture(sprites, "Circle", bounds.Center + shadowOffset, new Vector2(diameter), shadow, 0f);
            AddMarkerTexture(sprites, "Circle", bounds.Center, new Vector2(diameter), fill, 0f);
            AddMarkerTexture(sprites, "CircleHollow", bounds.Center, new Vector2(diameter * 1.02f), outline, 0f);
        }

        Color GetCameraButtonContentColor(ControlTemplate control, bool selected)
        {
            if (control.IsPressed)
                return control.GetResourceColor(ThemeResources.OnSecondaryContainerColor, control.TextColor);

            if (selected && control.IsPointerOver)
                return control.GetResourceColor(ThemeResources.OnAccentColor, control.TextColor);

            if (selected)
                return control.GetResourceColor(ThemeResources.OnAccentContainerColor, control.TextColor);

            return control.GetResourceColor(ThemeResources.OnSurfaceColor, Host.ForegroundColor);
        }

        void AddCenteredButtonText(ControlTemplate control, List<MySprite> sprites, string text, Color color)
        {
            float textScale = 0.82f * control.LayoutScale * control.FontScale;
            Vector2 textSize = control.MeasureText(text, textScale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(
                    control.Bounds.Center.X,
                    control.Bounds.Center.Y - textSize.Y * 0.5f),
                RotationOrScale = textScale,
                Color = color,
                Alignment = TextAlignment.CENTER,
                FontId = control.TextFont
            });
        }

        void AddCurrentPositionGlyph(List<MySprite> sprites, Vector2 center, float size, Color color)
        {
            Vector2 direction = new Vector2(0f, -1f);
            float rotation = GetTriangleRotation(direction);
            float sphereSize = size * SHIP_MARKER_SPHERE_SIZE;
            Vector2 triangleCenter = center + direction * (size * SHIP_MARKER_TRIANGLE_OFFSET);
            Vector2 sphereCenter = center - direction * (size * SHIP_MARKER_SPHERE_OFFSET);

            AddMarkerTexture(sprites, "Circle", sphereCenter, new Vector2(sphereSize), color, 0f);
            AddMarkerTexture(sprites, "Triangle", triangleCenter, new Vector2(size), color, rotation);
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
            _planetControl.SetRenderQuality(
                PlanetTextureQualitySettings.GetMaximumFaceSide(quality),
                PlanetTextureQualitySettings.GetTextCellSizePixels(quality));

            CancelPendingRequest();
            _retryFaceSide = int.MinValue;
            _nextRequestFrame = 0L;

            if (redraw)
                Host.RenderSprites();
        }

        void UpdateShipMarkerCache()
        {
            _cachedShipMarkerSprites.Clear();
            if (_planet != null && !_planet.MarkedForClose)
                AddCurrentShipMarker(_cachedShipMarkerSprites);
        }

        void AddCurrentShipMarker(List<MySprite> sprites)
        {
            RectangleF viewBox = Host.ViewBox;
            float shortSide = Math.Min(viewBox.Width, viewBox.Height);
            if (shortSide <= 0f)
                return;

            Vector2 direction = GetShipFacingScreenDirection(_planet);
            float rotation = GetTriangleRotation(direction);
            float minimumSize = SHIP_MARKER_MINIMUM_SIZE * Host.ConfiguredScale;
            float maximumSize = Math.Max(
                minimumSize,
                SHIP_MARKER_MAXIMUM_SIZE * Host.ConfiguredScale);
            float markerSize = MathHelper.Clamp(
                shortSide * SHIP_MARKER_SCREEN_RATIO,
                minimumSize,
                maximumSize);
            float sphereSize = markerSize * SHIP_MARKER_SPHERE_SIZE;

            Vector2 center;
            if (!TryGetShipSurfaceScreenPosition(_planet, out center))
                center = viewBox.Center;
            Vector2 triangleCenter = center + direction * (markerSize * SHIP_MARKER_TRIANGLE_OFFSET);
            Vector2 sphereCenter = center - direction * (markerSize * SHIP_MARKER_SPHERE_OFFSET);

            Color markerColor = Host.ForegroundColor;
            Color shadowColor = new Color(0, 0, 0, 190);

            AddMarkerTexture(
                sprites,
                "Circle",
                sphereCenter,
                new Vector2(sphereSize * 1.25f),
                shadowColor,
                0f);
            AddMarkerTexture(
                sprites,
                "Triangle",
                triangleCenter,
                new Vector2(markerSize * 1.16f),
                shadowColor,
                rotation);
            AddMarkerTexture(
                sprites,
                "Circle",
                sphereCenter,
                new Vector2(sphereSize),
                markerColor,
                0f);
            AddMarkerTexture(
                sprites,
                "Triangle",
                triangleCenter,
                new Vector2(markerSize),
                markerColor,
                rotation);
        }

        bool TryGetShipSurfaceScreenPosition(MyPlanet planet, out Vector2 screenPosition)
        {
            MatrixD shipWorld;
            if (!TryGetShipWorldMatrix(out shipWorld))
            {
                screenPosition = Host.ViewBox.Center;
                return false;
            }

            return TryGetSurfaceScreenPosition(planet, shipWorld.Translation, out screenPosition);
        }

        bool TryGetSurfaceScreenPosition(
            MyPlanet planet,
            Vector3D worldPosition,
            out Vector2 screenPosition)
        {
            screenPosition = Host.ViewBox.Center;
            if (planet == null)
                return false;

            Vector3D radialWorld = worldPosition - planet.WorldMatrix.Translation;
            if (radialWorld.Normalize() <= 1e-9)
                return false;

            Vector3 radialLocal = WorldDirectionToPlanetLocal(planet, radialWorld);
            if (radialLocal.Normalize() <= 1e-6f)
                return false;

            radialLocal = TransformByRotation(radialLocal, _planetControl.RotationTransform);
            if (radialLocal.Normalize() <= 1e-6f)
                return false;

            float x = Vector3.Dot(radialLocal, _planetControl.ScreenRightDirection);
            float y = Vector3.Dot(radialLocal, _planetControl.ScreenUpDirection);
            float z = Vector3.Dot(radialLocal, _planetControl.ViewDirection);

            var tangent = new Vector2(x, y);
            float tangentLength = tangent.Length();
            if (z < 0f)
            {
                if (tangentLength <= 0.0001f)
                    return false;

                tangent /= tangentLength;
                x = tangent.X;
                y = tangent.Y;
            }

            RectangleF viewBox = Host.ViewBox;
            float sphereRadius = Math.Min(viewBox.Width, viewBox.Height) * _zoom * 0.5f;
            screenPosition = new Vector2(
                viewBox.Center.X + x * sphereRadius,
                viewBox.Center.Y - y * sphereRadius);
            return true;
        }

        void AddGpsMarkers(List<MySprite> sprites)
        {
            if (_planet == null || _planet.MarkedForClose)
                return;

            PlanetaryMapConfigComponent config = PlanetaryMapComponent;
            GpsDisplayWaypoint[] alwaysDisplayed = config.AlwaysDisplayedGpsWaypoints ?? Array.Empty<GpsDisplayWaypoint>();
            int[] legacyAlwaysDisplayed = config.AlwaysDisplayedGpsHashes ?? Array.Empty<int>();
            bool needsLiveGps = config.DisplayMyGps || legacyAlwaysDisplayed.Length != 0 || _hasLastCreatedSurfaceGps;
            if (!needsLiveGps && alwaysDisplayed.Length == 0)
                return;

            float scale = Math.Max(0.5f, Host.ConfiguredScale);
            float markerSize = GPS_MARKER_SIZE_PIXELS * scale;
            RectangleF bounds = Host.ViewBox;

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
                        bool forceLastCreated = IsLastCreatedSurfaceGps(gps) &&
                                                !GpsMarkerLayout.ContainsWaypointSourceHash(alwaysDisplayed, gps.Hash);
                        if ((!forceLastCreated &&
                             !GpsMarkerLayout.ShouldRenderLiveGps(
                                 gps,
                                 config.DisplayMyGps,
                                 alwaysDisplayed,
                                 legacyAlwaysDisplayed)))
                        {
                            continue;
                        }

                        AddGpsMarkerProjection(GpsMarkerLayout.FromGps(gps), bounds, markerSize);
                    }
                }
            }

            for (int i = 0; i < alwaysDisplayed.Length; i++)
            {
                GpsMarker marker;
                if (!GpsMarkerLayout.TryCreateMarker(alwaysDisplayed[i], out marker))
                    continue;

                AddGpsMarkerProjection(marker, bounds, markerSize);
            }

            GpsMarkerLayout.Cluster(
                _gpsMarkerProjections,
                GPS_CLUSTER_DISTANCE_PIXELS * scale,
                _gpsMarkerClusters,
                _gpsMarkerClusterConsumed);

            for (int i = 0; i < _gpsMarkerClusters.Count; i++)
                DrawGpsMarker(sprites, _gpsMarkerClusters[i], scale, markerSize);
        }

        void AddGpsMarkerProjection(GpsMarker marker, RectangleF bounds, float markerSize)
        {
            if (!IsPositionNearPlanetSurface(_planet, marker.WorldPosition))
                return;

            Vector2 screenPosition;
            if (!TryGetSurfaceScreenPosition(_planet, marker.WorldPosition, out screenPosition) ||
                screenPosition.X < bounds.X - markerSize ||
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

        void DrawGpsMarker(
            List<MySprite> sprites,
            GpsMarkerCluster cluster,
            float scale,
            float baseMarkerSize)
        {
            GpsMarker marker = cluster.RepresentativeMarker;

            bool isCluster = cluster.Count > 1;
            float markerSize = isCluster ? baseMarkerSize * 1.35f : baseMarkerSize;
            float markerRadius = markerSize * 0.5f;
            Vector2 screenPosition = cluster.ScreenPosition;
            Color color = marker.Color;
            color.A = byte.MaxValue;
            Color shadow = new Color(0, 0, 0, 210);

            AddMarkerTexture(
                sprites,
                "Circle",
                screenPosition + new Vector2(scale, scale),
                new Vector2(markerSize * 1.25f),
                shadow,
                0f);
            AddMarkerTexture(
                sprites,
                "CircleHollow",
                screenPosition,
                new Vector2(markerSize),
                color,
                0f);

            float textScale = GPS_LABEL_SCALE * scale;
            if (isCluster)
            {
                string count = cluster.Count.ToString();
                Vector2 countShadowOffset = new Vector2(scale, scale);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = count,
                    Position = screenPosition + countShadowOffset,
                    RotationOrScale = textScale * 0.8f,
                    Color = shadow,
                    Alignment = TextAlignment.CENTER,
                    FontId = TextFont
                });
                sprites.Add(new MySprite
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
            float lineHeight = MeasureLineHeight(textScale);
            Vector2 labelPosition = new Vector2(
                screenPosition.X + markerRadius + GPS_LABEL_GAP_PIXELS * scale,
                screenPosition.Y - lineHeight * 0.5f);
            Vector2 shadowOffset = new Vector2(scale, scale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = name,
                Position = labelPosition + shadowOffset,
                RotationOrScale = textScale,
                Color = shadow,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
            sprites.Add(new MySprite
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

        void AddRadioSignalMarkers(List<MySprite> sprites)
        {
            if (_planet == null || _planet.MarkedForClose ||
                !PlanetaryMapComponent.IncludeRadioSignals)
            {
                return;
            }

            RefreshRadioSignals();
            if (_radioSignals.Count == 0)
                return;

            float scale = Math.Max(0.5f, Host.ConfiguredScale);
            float markerSize = RADIO_SIGNAL_MARKER_SIZE_PIXELS * scale;
            RectangleF bounds = Host.ViewBox;

            _radioSignalMarkerProjections.Clear();
            for (int i = 0; i < _radioSignals.Count; i++)
            {
                RadioSignalMarker marker = _radioSignals[i];
                Vector2 screenPosition;
                if (!TryGetSurfaceScreenPosition(_planet, marker.WorldPosition, out screenPosition) ||
                    screenPosition.X < bounds.X - markerSize ||
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
                RADIO_SIGNAL_CLUSTER_DISTANCE_PIXELS * scale,
                _radioSignalMarkerClusters,
                _radioSignalMarkerClusterConsumed);

            for (int i = 0; i < _radioSignalMarkerClusters.Count; i++)
                DrawRadioSignalMarker(sprites, _radioSignalMarkerClusters[i], scale, markerSize);
        }

        void RefreshRadioSignals()
        {
            long frame = GetCurrentFrame();
            if (_lastRadioSignalRefreshFrame != long.MinValue &&
                frame >= _lastRadioSignalRefreshFrame &&
                frame - _lastRadioSignalRefreshFrame < RADIO_SIGNAL_REFRESH_FRAMES)
            {
                return;
            }

            _lastRadioSignalRefreshFrame = frame;
            _radioSignalCollector.Collect(Host.Block, _radioSignals);
        }

        void DrawRadioSignalMarker(
            List<MySprite> sprites,
            RadioSignalMarkerCluster cluster,
            float scale,
            float baseMarkerSize)
        {
            bool isCluster = cluster.Count > 1;
            RadioSignalMarker marker = cluster.RepresentativeMarker;
            float markerSize = isCluster ? baseMarkerSize * 1.35f : baseMarkerSize;
            float markerRadius = markerSize * 0.5f;
            Vector2 screenPosition = cluster.ScreenPosition;
            Color color = ResolveRadioSignalColor(marker.Relationship);
            color.A = byte.MaxValue;
            Color shadow = new Color(0, 0, 0, 210);
            string texture = isCluster ? "CircleHollow" : ResolveRadioSignalTexture(marker.Relationship);

            AddMarkerTexture(
                sprites,
                texture,
                screenPosition + new Vector2(scale, scale),
                new Vector2(markerSize * 1.25f),
                shadow,
                0f);
            AddMarkerTexture(
                sprites,
                texture,
                screenPosition,
                new Vector2(markerSize),
                color,
                0f);

            float textScale = RADIO_SIGNAL_LABEL_SCALE * scale;
            if (isCluster)
            {
                string count = cluster.Count.ToString();
                Vector2 countShadowOffset = new Vector2(scale, scale);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = count,
                    Position = screenPosition + countShadowOffset,
                    RotationOrScale = textScale * 0.8f,
                    Color = shadow,
                    Alignment = TextAlignment.CENTER,
                    FontId = TextFont
                });
                sprites.Add(new MySprite
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
            RegisterMarkerHitbox(
                _radioSignalMarkerInteractiveStates,
                marker.EntityId,
                signalName,
                marker.WorldPosition,
                color,
                screenPosition,
                markerSize);
            float lineHeight = MeasureLineHeight(textScale);
            Vector2 labelPosition = new Vector2(
                screenPosition.X + markerRadius + RADIO_SIGNAL_LABEL_GAP_PIXELS * scale,
                screenPosition.Y - lineHeight * 0.5f);
            Vector2 shadowOffset = new Vector2(scale, scale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = name,
                Position = labelPosition + shadowOffset,
                RotationOrScale = textScale,
                Color = shadow,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
            sprites.Add(new MySprite
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

        void RegisterMarkerHitbox(
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
                        OnMarkerClicked));
                state.Entry.CustomRender = RenderMarkerHitbox;
                state.Entry.ClickSound = AudioHelper.HudGps3;
                state.Entry.SetDataContext(state);
                InsertMarkerVisualChild(state.Entry);
                states[key] = state;
            }

            state.Name = name;
            state.Position = position;
            state.Color = color;
            state.UsedThisFrame = true;
            state.Entry.SetRect(GetMarkerHitbox(screenPosition, markerSize));
            state.Entry.SetVisible(true);
        }

        void InsertMarkerVisualChild(Control entry)
        {
            if (entry == null || _children.Contains(entry))
                return;

            int index = _children.IndexOf(_orientationButton);
            if (index < 0)
                index = _children.IndexOf(_followButton);
            if (index < 0)
                index = _children.Count;

            _children.Insert(index, entry);
        }

        static RectangleF GetMarkerHitbox(Vector2 screenPosition, float markerSize)
        {
            float size = Math.Max(MARKER_HITBOX_MIN_PIXELS, markerSize * 1.25f);
            return new RectangleF(
                screenPosition.X - size * 0.5f,
                screenPosition.Y - size * 0.5f,
                size,
                size);
        }

        static void RenderMarkerHitbox(ControlTemplate control, List<MySprite> sprites)
        {
        }

        void UpdateStreetViewAvailability()
        {
            bool available = CanUseStreetView();
            if (_streetViewAvailable == available && _streetViewControl.Enabled == available)
                return;

            _streetViewAvailable = available;
            _streetViewControl.SetEnabled(available);
            if (!available)
                _streetViewDragging = false;
        }

        bool CanUseStreetView()
        {
            if (_planet == null || _planet.MarkedForClose)
                return false;

            var session = MyAPIGateway.Session;
            if (session == null)
                return false;

            if (session.SessionSettings != null && session.SessionSettings.EnableSpectator)
                return true;

            var player = session.Player;
            ulong steamUserId = player != null ? player.SteamUserId : 0UL;
            if (steamUserId == 0UL || !session.IsUserAdmin(steamUserId))
                return false;

            return session.CreativeMode || MyAPIGateway.Session.HasCreativeRights;
        }

        void OnStreetViewDragBegin(object dataContext, object sender)
        {
            if (!_streetViewAvailable)
                return;

            _layerSelector.Close();
            _streetViewDragging = true;
            if (!TryGetPointerPosition(sender, out _streetViewDragPointer))
                _streetViewDragPointer = _streetViewControl.Bounds.Center;
            Host.RenderSprites();
        }

        bool OnStreetViewDragged(object dataContext, object sender, Vector2 delta)
        {
            if (!_streetViewDragging || !_streetViewAvailable)
                return false;

            Vector2 pointer;
            if (TryGetPointerPosition(sender, out pointer))
                _streetViewDragPointer = pointer;
            else
                _streetViewDragPointer += delta;

            Host.RenderSprites();
            return true;
        }

        void OnStreetViewDragEnd(object dataContext, object sender)
        {
            if (!_streetViewDragging)
                return;

            Vector2 pointer;
            if (TryGetPointerPosition(sender, out pointer))
                _streetViewDragPointer = pointer;

            bool shouldSpawn = _streetViewAvailable;
            _streetViewDragging = false;
            Host.RenderSprites();

            if (shouldSpawn)
                TrySpawnStreetView(_streetViewDragPointer);
        }

        bool TrySpawnStreetView(Vector2 screenPoint)
        {
            if (!CanUseStreetView() || _planet == null || _planet.MarkedForClose)
                return false;

            Vector3 localDirection;
            Vector3D surfacePosition;
            if (!TryGetSurfaceClickPosition(
                    _planet,
                    screenPoint,
                    out localDirection,
                    out surfacePosition))
            {
                return false;
            }

            Vector3D up = surfacePosition - _planet.WorldMatrix.Translation;
            if (up.Normalize() <= 1e-9)
                return false;

            Vector3D forward = GetRandomHorizonDirection(up);
            if (forward.Normalize() <= 1e-9)
                return false;

            Vector3D cameraPosition =
                surfacePosition + up * STREET_CAMERA_HEIGHT_METERS;
            MatrixD cameraWorld = MatrixD.CreateWorld(cameraPosition, forward, up);
            MatrixD viewMatrix = MatrixD.Invert(cameraWorld);

            var session = MyAPIGateway.Session;
            if (session == null)
                return false;

            session.SetCameraController(
                MyCameraControllerEnum.Spectator,
                null,
                cameraPosition);

            MySpectatorCameraController
                spectator = MyAPIGateway.Session.CameraController as MySpectatorCameraController;
            if (spectator != null)
            {
                spectator.SpectatorCameraMovement =
                    MySpectatorCameraMovementEnum.UserControlled;
                spectator.SetViewMatrix((Matrix)viewMatrix);
                ScheduleStreetViewNotification();
            }

            return true;
        }

        static void ScheduleStreetViewNotification()
        {
            var utilities = MyAPIGateway.Utilities;
            if (utilities == null)
                return;

            IMyHudNotification notification = utilities.CreateNotification(
                LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_StreetView_ActiveNotification"),
                500);
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

        Vector3D GetRandomHorizonDirection(Vector3D up)
        {
            Vector3D reference = Math.Abs(Vector3D.Dot(up, Vector3D.Up)) < 0.95d
                ? Vector3D.Up
                : Vector3D.Right;
            Vector3D tangent = Vector3D.Cross(reference, up);
            if (tangent.Normalize() <= 1e-9)
            {
                reference = Vector3D.Forward;
                tangent = Vector3D.Cross(reference, up);
                if (tangent.Normalize() <= 1e-9)
                    return Vector3D.Zero;
            }

            Vector3D bitangent = Vector3D.Cross(up, tangent);
            if (bitangent.Normalize() <= 1e-9)
                return Vector3D.Zero;

            double angle = _streetViewRandom.NextDouble() * MathHelper.TwoPi;
            return tangent * Math.Cos(angle) + bitangent * Math.Sin(angle);
        }

        static bool TryGetPointerPosition(object sender, out Vector2 position)
        {
            position = default(Vector2);
            var screen = sender as IEyeTracking;
            if (screen == null)
                return false;

            position = screen.CursorPosition + screen.HitTestOffset;
            return !float.IsNaN(position.X) &&
                   !float.IsInfinity(position.X) &&
                   !float.IsNaN(position.Y) &&
                   !float.IsInfinity(position.Y);
        }

        bool OnPlanetSurfaceClicked(PlanetGlobeControl control, Vector2 screenPoint, object sender)
        {
            if (_planet == null || _planet.MarkedForClose)
                return false;

            Vector3 localDirection;
            Vector3D surfacePosition;
            return TryGetSurfaceClickPosition(
                       _planet,
                       screenPoint,
                       out localDirection,
                       out surfacePosition) &&
                   CreateSurfaceGps(localDirection, surfacePosition);
        }

        bool OnPlanetSurfaceMiddleClicked(PlanetGlobeControl control, Vector2 screenPoint, object sender)
        {
            if (_planet == null || _planet.MarkedForClose)
                return false;

            Vector3 localDirection;
            return control.TryGetSurfaceDirection(screenPoint, out localDirection) &&
                   FocusCameraOnSurfaceDirection(localDirection);
        }

        bool FocusCameraOnSurfaceDirection(Vector3 localDirection)
        {
            if (_planet == null || _planet.MarkedForClose)
                return false;

            if (localDirection.Normalize() <= 1e-6f)
                return false;

            Vector3D worldDirection = PlanetLocalDirectionToWorld(_planet, localDirection);
            if (worldDirection.Normalize() <= 1e-9)
                return false;

            Vector3D center = _planet.WorldMatrix.Translation;
            double cameraDistance = Vector3D.Distance(GetCameraWorldPosition(), center);
            if (double.IsNaN(cameraDistance) ||
                double.IsInfinity(cameraDistance) ||
                cameraDistance <= 0d)
            {
                cameraDistance = GetPlanetSurfaceClickSampleRadius(_planet);
            }

            double minimumDistance = GetPlanetSurfaceClickSampleRadius(_planet);
            if (minimumDistance > 0d)
                cameraDistance = Math.Max(cameraDistance, minimumDistance);

            if (cameraDistance <= 0d)
                return false;

            _staticCameraPosition = center + worldDirection * cameraDistance;
            _hasStaticCameraPosition = true;
            _followCamera = false;
            UpdateFollowButtonState();
            _orbitControl.SetOrbit(0f, 0f, false);
            PersistPlanetaryMapConfig();
            UpdatePlanetControl();
            Host.RenderSprites();
            RaiseCameraOrbitChanged();
            return true;
        }

        bool TryGetSurfaceClickPosition(
            MyPlanet planet,
            Vector2 screenPoint,
            out Vector3 localDirection,
            out Vector3D surfacePosition)
        {
            localDirection = Vector3.Zero;
            surfacePosition = Vector3D.Zero;
            if (planet == null)
                return false;

            if (!_planetControl.TryGetSurfaceDirection(screenPoint, out localDirection))
                return false;

            Vector3D worldDirection = PlanetLocalDirectionToWorld(planet, localDirection);
            if (worldDirection.Normalize() <= 1e-9)
                return false;

            double radiusMeters = GetPlanetSurfaceClickSampleRadius(planet);
            if (radiusMeters <= 0d)
                return false;

            Vector3D samplePosition =
                planet.WorldMatrix.Translation +
                worldDirection * radiusMeters;
            surfacePosition = planet.GetClosestSurfacePointGlobal(samplePosition);
            return IsFinite(surfacePosition);
        }

        void OnMarkerClicked(object dataContext, object sender)
        {
            var marker = dataContext as StaticMarkerInteractiveState;
            if (marker == null)
                return;

            CreateLocalGpsCopy(marker.Name, marker.Position, marker.Color);
        }

        bool CreateSurfaceGps(Vector3 localDirection, Vector3D position)
        {
            var session = MyAPIGateway.Session;
            var gpsCollection = session == null ? null : session.GPS;
            if (gpsCollection == null)
                return false;

            var gps = gpsCollection.Create(
                FormatSurfaceGpsName(localDirection),
                string.Empty,
                position,
                false,
                true);
            if (gps == null)
                return false;

            gpsCollection.AddLocalGps(gps);
            _lastCreatedSurfaceGpsHash = gps.Hash;
            _hasLastCreatedSurfaceGps = true;
            ShowGpsCreatedStatus(gps.Name);
            Host.RenderSprites();

            var surfaceGpsCreated = SurfaceGpsCreated;
            if (surfaceGpsCreated != null)
                surfaceGpsCreated();
            return true;
        }

        bool IsLastCreatedSurfaceGps(IMyGps gps)
        {
            return _hasLastCreatedSurfaceGps &&
                   gps != null &&
                   gps.Hash == _lastCreatedSurfaceGpsHash;
        }

        void CreateLocalGpsCopy(string name, Vector3D position, Color color)
        {
            var session = MyAPIGateway.Session;
            var gpsCollection = session == null ? null : session.GPS;
            if (gpsCollection == null) return;

            var gps = gpsCollection.Create(GetLocalGpsName(name), string.Empty, position, false, true);
            if (gps == null) return;

            gps.GPSColor = color;
            gpsCollection.AddLocalGps(gps);
            ShowGpsCreatedStatus(gps.Name);
            Host.RenderSprites();
        }

        static string GetLocalGpsName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? LocHelper.GetLoc(MOD_PREFIX + "Gps_Unknown_Name")
                : name;
        }

        static string FormatSurfaceGpsName(Vector3 localDirection)
        {
            double latitude = 0d;
            double longitude = 0d;
            if (localDirection.Normalize() > 1e-6f)
            {
                latitude = Math.Asin(ClampUnit(localDirection.Y)) * 180d / Math.PI;
                longitude = Math.Atan2(localDirection.X, localDirection.Z) * 180d / Math.PI;
            }

            return string.Format(
                FormatingHelper.Culture,
                LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_CoordinateFormat"),
                LocHelper.GetLoc(MOD_PREFIX + (latitude < 0d
                    ? "PlanetaryMap_CardinalSouthShort"
                    : "PlanetaryMap_CardinalNorthShort")),
                Math.Abs(latitude),
                LocHelper.GetLoc(MOD_PREFIX + (longitude < 0d
                    ? "PlanetaryMap_CardinalWestShort"
                    : "PlanetaryMap_CardinalEastShort")),
                Math.Abs(longitude));
        }

        static string GetRadioSignalName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? LocHelper.GetLoc(MOD_PREFIX + "RadioSignal_Unnamed")
                : name;
        }

        static double ClampUnit(double value)
        {
            if (value < -1d)
                return -1d;
            if (value > 1d)
                return 1d;
            return value;
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

        static bool IsPositionNearPlanetSurface(MyPlanet planet, Vector3D position)
        {
            if (planet == null)
                return false;

            double averageRadius = planet.AverageRadius > 0d
                ? planet.AverageRadius
                : planet.MaximumRadius;
            if (averageRadius <= 0d)
                return false;

            double maximumRadius = planet.MaximumRadius > 0d
                ? planet.MaximumRadius
                : averageRadius;
            double terrainVariation = Math.Abs(maximumRadius - averageRadius);
            double desiredTolerance = Math.Max(
                GPS_SURFACE_TARGET_TOLERANCE_METERS,
                terrainVariation + GPS_SURFACE_TERRAIN_MARGIN_METERS);
            double tolerance = Math.Max(
                GPS_SURFACE_MINIMUM_ALLOWED_TOLERANCE_METERS,
                Math.Min(
                    desiredTolerance,
                    averageRadius * GPS_SURFACE_MAXIMUM_RADIUS_RATIO));
            double radialDistance = Vector3D.Distance(
                position,
                planet.WorldMatrix.Translation);
            return Math.Abs(radialDistance - averageRadius) <= tolerance;
        }

        Vector2 GetShipFacingScreenDirection(MyPlanet planet)
        {
            MatrixD shipWorld;
            Vector2 direction;
            if (TryGetShipWorldMatrix(out shipWorld) &&
                (TryProjectWorldDirectionToScreen(planet, shipWorld.Forward, out direction) ||
                 TryProjectWorldDirectionToScreen(planet, shipWorld.Up, out direction)))
            {
                return direction;
            }

            return new Vector2(0f, -1f);
        }

        bool TryGetShipWorldMatrix(out MatrixD world)
        {
            if (Host.TryGetReferenceWorldMatrix((int)ReferenceMode.Controller, out world))
                return true;

            if (Host.Block != null && Host.Block.CubeGrid != null)
            {
                world = Host.Block.CubeGrid.WorldMatrix;
                return true;
            }

            if (Host.Block != null)
            {
                world = Host.Block.WorldMatrix;
                return true;
            }

            world = MatrixD.Identity;
            return false;
        }

        bool TryProjectWorldDirectionToScreen(
            MyPlanet planet,
            Vector3D worldDirection,
            out Vector2 screenDirection)
        {
            screenDirection = Vector2.Zero;
            if (planet == null || worldDirection.Normalize() <= 1e-9)
                return false;

            Vector3 localDirection = WorldDirectionToPlanetLocal(planet, worldDirection);
            if (localDirection.Normalize() <= 1e-6f)
                return false;

            localDirection = TransformByRotation(
                localDirection,
                _planetControl.RotationTransform);
            if (localDirection.Normalize() <= 1e-6f)
                return false;

            Vector3 viewDirection = _planetControl.ViewDirection;
            Vector3 projected = localDirection -
                                viewDirection * Vector3.Dot(localDirection, viewDirection);
            if (projected.Normalize() <= 1e-6f)
                return false;

            screenDirection = new Vector2(
                Vector3.Dot(projected, _planetControl.ScreenRightDirection),
                -Vector3.Dot(projected, _planetControl.ScreenUpDirection));
            return TryNormalize(ref screenDirection);
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

        static Vector3 TransformByRotation(Vector3 direction, Matrix rotation)
        {
            return new Vector3(
                direction.X * rotation.M11 +
                direction.Y * rotation.M21 +
                direction.Z * rotation.M31,
                direction.X * rotation.M12 +
                direction.Y * rotation.M22 +
                direction.Z * rotation.M32,
                direction.X * rotation.M13 +
                direction.Y * rotation.M23 +
                direction.Z * rotation.M33);
        }

        static double GetPlanetSurfaceClickSampleRadius(MyPlanet planet)
        {
            if (planet == null)
                return 0d;

            double radius = Math.Max(planet.AverageRadius, planet.MaximumRadius);
            return radius > 0d ? radius : 0d;
        }

        static bool IsFinite(Vector3D value)
        {
            return !double.IsNaN(value.X) &&
                   !double.IsInfinity(value.X) &&
                   !double.IsNaN(value.Y) &&
                   !double.IsInfinity(value.Y) &&
                   !double.IsNaN(value.Z) &&
                   !double.IsInfinity(value.Z);
        }

        static bool TryNormalize(ref Vector2 direction)
        {
            float lengthSquared = direction.LengthSquared();
            if (lengthSquared <= 0.000001f)
                return false;

            direction *= 1f / (float)Math.Sqrt(lengthSquared);
            return true;
        }

        static float GetTriangleRotation(Vector2 direction)
        {
            return (float)Math.Atan2(direction.Y, direction.X) + MathHelper.PiOver2;
        }

        static void AddMarkerTexture(
            List<MySprite> sprites,
            string sprite,
            Vector2 position,
            Vector2 size,
            Color color,
            float rotation)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = sprite,
                Position = position,
                Size = size,
                Color = color,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = rotation
            });
        }

        void ScheduleRequestRetry(int faceSide)
        {
            _retryFaceSide = faceSide;
            _nextRequestFrame = GetCurrentFrame() + REQUEST_RETRY_FRAMES;
        }

        static long GetCurrentFrame()
        {
            return MyAPIGateway.Session != null
                ? MyAPIGateway.Session.GameplayFrameCounter
                : 0L;
        }

        void ShowGpsCreatedStatus(string name)
        {
            _gpsCreatedStatus = string.Format(
                FormatingHelper.Culture,
                LocHelper.GetLoc(MOD_PREFIX + "Gps_CreatedFormat"),
                GetLocalGpsName(name));
            _gpsCreatedStatusUntilFrame = GetCurrentFrame() + GPS_CREATED_STATUS_FRAMES;
        }

        void AddGpsCreatedStatus()
        {
            if (string.IsNullOrWhiteSpace(_gpsCreatedStatus) ||
                GetCurrentFrame() > _gpsCreatedStatusUntilFrame)
            {
                return;
            }

            AddBottomStatus(_gpsCreatedStatus, Host.ForegroundColor);
        }

        void AddCenteredStatus(string text, Color color)
        {
            _sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = Host.ViewBox.Center,
                RotationOrScale = 0.7f * Host.ConfiguredScale,
                Color = color,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });
        }

        void AddBottomStatus(string text, Color color)
        {
            _sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(
                    Host.ViewBox.Center.X,
                    Host.ViewBox.Bottom - 24f * Host.ConfiguredScale),
                RotationOrScale = 0.45f * Host.ConfiguredScale,
                Color = color,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });
        }
    }
}
