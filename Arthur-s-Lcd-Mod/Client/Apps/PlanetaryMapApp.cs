using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Custom.Camera;
using LcdMod.Client.Gui.ControlsTemplates.Custom.Planet;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Cartography;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps
{
    /// <summary>
    /// Displays the nearest planet through the reusable PlanetGlobeControl.
    /// Cubemap detail follows the projected sphere diameter up to the client-local
    /// texture-quality cap. Higher detail is requested lazily; the last completed cubemap remains
    /// visible until its replacement is ready. Only the initial load uses gray.
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
        const float CAMERA_BUTTON_MARGIN_PIXELS = 12f;
        const float CAMERA_BUTTON_GAP_PIXELS = 8f;
        const float CAMERA_BUTTON_SIZE_PIXELS = 42f;
        const float CAMERA_BUTTON_ICON_RATIO = 0.56f;
        const float CAMERA_BUTTON_SHADOW_RATIO = 0.055f;
        const float ORBIT_CONFIG_EPSILON = 0.000001f;
        const float ZOOM_CONFIG_EPSILON = 0.0001f;
        const double STATIC_CAMERA_CONFIG_EPSILON_METERS = 0.001d;

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<MySprite> _cachedShipMarkerSprites = new List<MySprite>();
        readonly List<Control> _children = new List<Control>();
        readonly PlanetGlobeControl _planetControl;
        readonly OrbitCameraControl _orbitControl;
        readonly ButtonModel _orientationButtonModel;
        readonly ButtonModel _followButtonModel;
        readonly ToggleButton _orientationButton;
        readonly ToggleButton _followButton;

        MyPlanet _planet;
        long _planetId;
        long _lastPlanetRefreshFrame = long.MinValue;
        long _nextRequestFrame;
        int _retryFaceSide = int.MinValue;
        int _requestedFaceSide = -1;
        int _requestVersion;
        CartographyTicket _ticket;
        PlanetColorCubemap _loadedCubemap;
        string _error;
        float _zoom = MAXIMUM_ZOOM;
        bool _northUp = true;
        bool _followCamera = true;
        bool _hasStaticCameraPosition;
        Vector3D _staticCameraPosition;
        bool _suppressOrbitModeChange;
        bool _syncConfigNextRun;
        bool _lastKnownConfigNorthUp = true;
        bool _lastKnownConfigFollowCamera = true;
        float _lastKnownConfigOrbitYawRadians;
        float _lastKnownConfigOrbitPitchRadians;
        float _lastKnownConfigZoom = MAXIMUM_ZOOM;
        bool _lastKnownConfigHasStaticCameraPosition;
        Vector3D _lastKnownConfigStaticCameraPosition;
        bool _closed;

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

            _orbitControl = AddLogicalChild(
                new OrbitCameraControl(default(RectangleF)));
            _orbitControl.CameraChanged = OnOrbitCameraChanged;

            _orientationButtonModel = new ButtonModel
            {
                Text = "N",
                Clicked = OnOrientationButtonClicked
            };
            _orientationButton = AddLogicalChild(
                new ToggleButton(default(RectangleF), _orientationButtonModel));
            _orientationButton.GetState = delegate { return _northUp; };
            _orientationButton.BorderThicknessPixels = 0f;
            _orientationButton.CustomRender = RenderOrientationButton;

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

            _children.Add(_planetControl);
            _children.Add(_orbitControl);
            _children.Add(_orientationButton);
            _children.Add(_followButton);

            ApplyPlanetaryMapConfig(true);

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

            UpdatePlanetControl();
            UpdateShipMarkerCache();
            UpdateCubemapDetail(frame);
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();

            if (_planet != null && !_planet.MarkedForClose)
            {
                UpdatePlanetControl();
                UpdateShipMarkerCache();
                UpdateCubemapDetail(GetCurrentFrame());
            }

            if (_planet == null)
            {
                AddCenteredStatus(LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_NoPlanet"), Host.ForegroundColor);
                RenderCameraControls();
                return _sprites;
            }

            // PlanetGlobeControl draws one gray Circle only before the first
            // cubemap is available. Detail upgrades keep the previous map visible.
            _planetControl.Render(_sprites);
            _sprites.AddRange(_cachedShipMarkerSprites);

            if (!string.IsNullOrWhiteSpace(_error))
                AddBottomStatus(_error, new Color(255, 80, 80));

            RenderCameraControls();

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

            float next = delta > 0
                ? _zoom * ZOOM_STEP
                : _zoom / ZOOM_STEP;
            next = MathHelper.Clamp(next, MINIMUM_ZOOM, MAXIMUM_ZOOM);
            if (Math.Abs(next - _zoom) <= 0.0001f)
                return;

            SetZoom(next);
            PersistPlanetaryMapConfig();
            handled = true;
            Host.RenderSprites();
        }

        public override void Close()
        {
            _closed = true;
            LocalConfigManager.TextureQualityChanged -= OnTextureQualityChanged;
            _planetId = 0L;
            CancelPendingRequest();
            _planetControl.SetCubemap(null);
            _loadedCubemap = null;
            _planet = null;
            _cachedShipMarkerSprites.Clear();
            _sprites.Clear();
            base.Close();
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
            _planet = nearest;
            _planetId = nearestId;
            _loadedCubemap = null;
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
            CartographyModule module = LcdModSessionComponent.Client != null
                ? LcdModSessionComponent.Client.Cartography
                : null;
            if (module == null)
            {
                _error = LocHelper.GetLoc(MOD_PREFIX + "PlanetaryMap_CartographyNotReady");
                ScheduleRequestRetry(faceSide);
                return;
            }

            long requestedPlanetId = planet.EntityId;
            _requestedFaceSide = faceSide;
            _error = null;

            try
            {
                var request = new CartographyRequest
                {
                    PlanetEntityId = requestedPlanetId,
                    PlanetRadiusMeters = planet.AverageRadius,
                    Projection = CartographyProjection.CubemapFaces,
                    Layer = CartographyLayer.SurfaceFarColor,
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

                    _loadedCubemap = cachedCubemap;
                    _planetControl.SetCubemap(cachedCubemap);
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
                        if (_planetId != requestedPlanetId)
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
                            if (_loadedCubemap == null ||
                                result.ColorCubemap.DetailRank > _loadedCubemap.DetailRank)
                            {
                                _loadedCubemap = result.ColorCubemap;
                            }

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

        void UpdatePlanetControl()
        {
            UpdatePlanetControlBounds();
            UpdateCameraControlBounds();
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
            float margin = CAMERA_BUTTON_MARGIN_PIXELS * scale;
            float gap = CAMERA_BUTTON_GAP_PIXELS * scale;
            float availableSize = Math.Min(
                Math.Max(1f, viewBox.Width - margin * 2f),
                Math.Max(1f, (viewBox.Height - margin * 2f - gap) / 2f));
            float size = Math.Min(CAMERA_BUTTON_SIZE_PIXELS * scale, availableSize);

            float x = viewBox.Right - margin - size;
            float y = viewBox.Bottom - margin - size;
            _followButton.SetRect(new RectangleF(x, y, size, size));
            _orientationButton.SetRect(new RectangleF(x, y - gap - size, size, size));
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

        void ApplyPlanetaryMapConfig(bool force)
        {
            PlanetaryMapConfigComponent config = PlanetaryMapComponent;
            Vector3D staticCameraPosition = GetConfigStaticCameraPosition(config);
            float zoom = ClampZoom(config.Zoom);
            bool staticCameraChanged =
                config.HasStaticCameraPosition != _lastKnownConfigHasStaticCameraPosition ||
                (config.HasStaticCameraPosition &&
                 !NearlyEqual(staticCameraPosition, _lastKnownConfigStaticCameraPosition));

            if (force || config.NorthUp != _lastKnownConfigNorthUp)
            {
                SetNorthUp(config.NorthUp);
                _lastKnownConfigNorthUp = config.NorthUp;
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
            _orientationButtonModel.Text = _northUp ? "N" : "Current";
            _orientationButton.MarkDirty();
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
            _followButtonModel.Text = _followCamera ? "Lock" : "RotationPlane";
            _followButton.MarkDirty();
        }

        void CaptureConfigSnapshot(PlanetaryMapConfigComponent config)
        {
            _lastKnownConfigNorthUp = config.NorthUp;
            _lastKnownConfigFollowCamera = config.FollowCamera;
            _lastKnownConfigOrbitYawRadians = config.OrbitYawRadians;
            _lastKnownConfigOrbitPitchRadians = config.OrbitPitchRadians;
            _lastKnownConfigZoom = ClampZoom(config.Zoom);
            _lastKnownConfigHasStaticCameraPosition = config.HasStaticCameraPosition;
            _lastKnownConfigStaticCameraPosition = GetConfigStaticCameraPosition(config);
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

        void RenderCameraControls()
        {
            _orientationButton.Render(_sprites);
            _followButton.Render(_sprites);
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
                    "N",
                    control.GetResourceColor(ThemeResources.ErrorColor, contentColor));
                return;
            }

            AddCurrentPositionGlyph(
                sprites,
                control.Bounds.Center,
                Math.Min(control.Bounds.Width, control.Bounds.Height) * CAMERA_BUTTON_ICON_RATIO,
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
                new Vector2(Math.Min(control.Bounds.Width, control.Bounds.Height) * CAMERA_BUTTON_ICON_RATIO),
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
            _planetControl.SetMaximumRenderResolution(
                PlanetTextureQualitySettings.GetMaximumFaceSide(quality));

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
            screenPosition = Host.ViewBox.Center;
            if (planet == null)
                return false;

            MatrixD shipWorld;
            if (!TryGetShipWorldMatrix(out shipWorld))
                return false;

            Vector3D radialWorld = shipWorld.Translation - planet.WorldMatrix.Translation;
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
