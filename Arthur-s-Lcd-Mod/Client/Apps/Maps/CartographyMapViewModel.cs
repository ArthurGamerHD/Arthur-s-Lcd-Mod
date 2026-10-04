using System;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using Jakaria.API;
using LcdMod.Client.Modules.Cartography;
using LcdMod.Client.Modules.Cartography.Maps;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Maps
{
    internal interface IMapHost
    {
        CartographyModule Cartography { get; }
        MatrixD? ReferenceWorldMatrix { get; }
        void PlayClick();
        void PlayGps();
        void RequestRender();
    }

    internal sealed class MapLayerItem : ObservableObject
    {
        bool _isSelected;
        bool _isShown;
        PlanetColorCubemap _cubemap;
        PlanetCubeFace _face;

        public MapLayerItem(CartographyMapViewModel owner, CartographyLayer layer, string name, string description)
        {
            Layer = layer;
            Name = name;
            Tip = name + "\n" + description;
            Command = new DelegateCommand(_ => owner.SelectLayer(layer));
        }

        public CartographyLayer Layer { get; }
        public string Name { get; }
        public string Tip { get; }
        public ICommand Command { get; }
        public bool IsSelected => _isSelected;
        public bool IsShown => _isShown;
        public PlanetColorCubemap Cubemap => _cubemap;
        public PlanetCubeFace Face => _face;

        internal void Update(bool selected, bool shown, PlanetColorCubemap cubemap, PlanetCubeFace face)
        {
            RaiseAndSetIfChanged(ref _isSelected, selected, nameof(IsSelected));
            RaiseAndSetIfChanged(ref _isShown, shown, nameof(IsShown));
            if (!ReferenceEquals(_cubemap, cubemap))
            {
                _cubemap = cubemap;
                RaisePropertyChanged<PlanetColorCubemap>(nameof(Cubemap));
            }
            RaiseAndSetIfChanged(ref _face, face, nameof(Face));
        }
    }

    /// <summary>
    /// The Star/Planetary map screen model backported from the standalone Minimap: owns the cartography renderer, the
    /// shared view state and the camera input, and repaints the map texture when the view changes.
    /// </summary>
    internal abstract class CartographyMapViewModel : ObservableObject, IMapSurface, IAsyncMapSurface, IMapSurfaceDrag, IDisposable
    {
        protected const float PLANET_MINIMUM_ZOOM = .55f;
        protected const float PLANET_MAXIMUM_ZOOM = 16f;
        protected const float STAR_MINIMUM_ZOOM = CartographyMapRenderer.StarMinimumZoom;
        protected const float STAR_MAXIMUM_ZOOM = CartographyMapRenderer.StarMaximumZoom;
        const float ORBIT_SENSITIVITY = .009f;
        const float COMPASS_NORTH_CORRECTION = -MathHelper.PiOver4;
        const int PLANET_REFRESH_FRAMES = 18;
        const int PLAYER_REFRESH_FRAMES = 3;

        static readonly CartographyLayer[] MapLayers =
        {
            CartographyLayer.Satellite,
            CartographyLayer.Terrain,
            CartographyLayer.Materials,
            CartographyLayer.Biomes
        };

        protected readonly MapData Data = new MapData();
        protected readonly MapViewState State = new MapViewState();
        protected readonly IMapHost Host;
        readonly MapMode _mode;
        readonly List<MapLayerItem> _layers = new List<MapLayerItem>();
        readonly List<MapMarker> _markers = new List<MapMarker>();
        CartographyMapRenderer _renderer;
        CartographyModule _rendererModule;
        RectangleF _canvas;
        bool _dirty = true;
        int _version;
        int _markersVersion;
        int _updates;
        bool _layersExpanded;
        bool _isNorthLocked = true;
        bool _isFollowing = true;
        float _orientationRotation;
        bool _waterVisible;
        bool _waterEnabled;
        bool _isWaterHidden;
        bool _orbitLinesShown = true;
        bool _isCentered = true;
        string _status = string.Empty;
        bool _hasStatus;
        Color _statusColor = Color.White;
        bool _hasMap;
        readonly PlanetSurfaceDrag _surfaceDrag = new PlanetSurfaceDrag();
        readonly MapOrbitInertia _orbitInertia = new MapOrbitInertia();
        Vector2 _dragOrigin;
        bool _grabInitialized;
        bool _grabActive;

        protected CartographyMapViewModel(IMapHost host, MapMode mode)
        {
            Host = host;
            _mode = mode;
            Data.ReferenceProvider = () => Host.ReferenceWorldMatrix;
            State.Zoom = mode == MapMode.StarMap ? 1f : PLANET_MAXIMUM_ZOOM;
            for (int i = 0; i < MapLayers.Length; i++)
                _layers.Add(new MapLayerItem(this, MapLayers[i], LayerName(MapLayers[i]), LayerDescription(MapLayers[i])));
            OrientationCommand = new DelegateCommand(_ => ToggleOrientation());
            FollowCommand = new DelegateCommand(_ => ToggleFollow());
            WaterCommand = new DelegateCommand(_ => ToggleWater());
            OrbitLinesCommand = new DelegateCommand(_ => ToggleOrbitLines());
            RecenterCommand = new DelegateCommand(_ => Recenter());
        }

        public int Version => _version;
        public int MarkersVersion => _markersVersion;
        public IReadOnlyList<MapMarker> Markers => _markers;
        public IReadOnlyList<MapLayerItem> Layers => _layers;
        public ICommand OrientationCommand { get; }
        public ICommand FollowCommand { get; }
        public ICommand WaterCommand { get; }
        public ICommand OrbitLinesCommand { get; }
        public ICommand RecenterCommand { get; }
        public bool IsNorthLocked => _isNorthLocked;
        public string OrientationTip => Tooltip(_isNorthLocked ? "NorthUp" : "CurrentUp");
        public string FollowTip => Tooltip(_isFollowing ? "FollowCamera" : "FreeCamera");
        public Transform OrientationTransform => new RotateTransform(MathHelper.ToDegrees(_orientationRotation));
        public IMapSurface Surface => this;
        public BackgroundBrush WaterIcon { get; } = Mask("WaterIcon");
        public BackgroundBrush CompassIcon { get; } = Mask("CompassIcon");
        public BackgroundBrush FollowIcon => _isFollowing ? LockIcon : FreeIcon;
        public BackgroundBrush OrbitLinesIconMask => _orbitLinesShown ? OrbitOnIcon : OrbitOffIcon;
        public bool IsCentered => _isCentered;
        public BackgroundBrush RecenterIcon => _isCentered ? LockIcon : FreeIcon;
        public bool IsFollowing => _isFollowing;
        public bool WaterVisible => _waterVisible;
        public bool WaterEnabled => _waterEnabled;
        public bool IsWaterHidden => _isWaterHidden;
        public bool OrbitLinesShown => _orbitLinesShown;
        public string Status => _status;
        public bool HasStatus => _hasStatus;
        public Color StatusColor => _statusColor;
        public bool HasMap => _hasMap;
        public bool IsPlanetary => _mode == MapMode.PlanetaryMap;
        public bool IsStarMap => _mode == MapMode.StarMap;

        static readonly BackgroundBrush LockIcon = Mask("Lock");
        static readonly BackgroundBrush FreeIcon = Mask("RotationPlane");
        static readonly BackgroundBrush OrbitOnIcon = Mask("OrbitOn");
        static readonly BackgroundBrush OrbitOffIcon = Mask("OrbitOff");

        static BackgroundBrush Mask(string texture)
        {
            return new ImageBrush(texture) { Stretch = Stretch.Uniform };
        }

        protected MapMode Mode => _mode;
        protected RectangleF Canvas => _canvas;
        protected CartographyMapRenderer Renderer => _renderer;
        protected bool HasCanvas => _canvas.Width > 0f && _canvas.Height > 0f;

        public void Update()
        {
            EnsureRenderer();
            AdvanceOrbitInertia();
            if (_updates++ % PLANET_REFRESH_FRAMES == 0)
            {
                Data.RefreshPlanets();
                if (ResolveFocusedPlanet() == null)
                    FocusReferencePlanet();
                _dirty = true;
            }
            else if (_updates % PLAYER_REFRESH_FRAMES == 0)
            {
                Data.UpdatePlayerPosition();
                if (State.FollowPlayer)
                    _dirty = true;
            }

            if (_renderer != null)
                _renderer.UpdateRequests(Data, _mode, State);
            RefreshPresentation();
            RefreshMarkers();
            if (_dirty)
                Invalidate();
        }

        public void Invalidate()
        {
            _dirty = false;
            RaiseAndSetIfChanged(ref _version, _version + 1, nameof(Version));
        }

        protected void MarkDirty()
        {
            _dirty = true;
        }

        void EnsureRenderer()
        {
            CartographyModule module = Host.Cartography;
            if (_renderer != null && ReferenceEquals(_rendererModule, module))
                return;
            if (_renderer != null)
                _renderer.Dispose();
            _rendererModule = module;
            _renderer = module == null ? null : new CartographyMapRenderer(module) { Changed = MarkDirty };
            _dirty = true;
        }

        void FocusReferencePlanet()
        {
            PlanetSnapshot planet = Data.FindPlayerPlanet() ?? Data.FindClosestPlanet();
            State.FocusedPlanetId = planet == null ? 0L : planet.EntityId;
            State.StarTargetOffsetWorld = Vector3D.Zero;
        }

        protected PlanetSnapshot ResolveFocusedPlanet()
        {
            return Data.FindPlanet(State.FocusedPlanetId) ?? (_mode == MapMode.PlanetaryMap ? Data.FindClosestPlanet() : null);
        }

        protected PlanetColorCubemap GetLayerCubemap(CartographyLayer layer)
        {
            PlanetSnapshot planet = ResolveFocusedPlanet();
            return _renderer == null || planet == null ? null : _renderer.GetCubemap(planet.EntityId, layer);
        }

        void RefreshPresentation()
        {
            if (RaiseAndSetIfChanged(ref _isNorthLocked, State.PlanetOrientation == PlanetOrientationMode.NorthLocked,
                    nameof(IsNorthLocked)))
                RaisePropertyChanged<string>(nameof(OrientationTip));
            float rotation = GetOrientationIconRotation();
            if (Math.Abs(rotation - _orientationRotation) > .0001f)
            {
                _orientationRotation = rotation;
                RaisePropertyChanged<Transform>(nameof(OrientationTransform));
            }
            if (RaiseAndSetIfChanged(ref _isFollowing, State.FollowPlayer, nameof(IsFollowing)))
            {
                RaisePropertyChanged<BackgroundBrush>(nameof(FollowIcon));
                RaisePropertyChanged<string>(nameof(FollowTip));
            }
            bool centered = IsStarMap
                ? State.StarTargetOffsetWorld.LengthSquared() < 1e-6
                : State.PlanetCenterOffsetNormalized.LengthSquared() < 1e-8f;
            if (RaiseAndSetIfChanged(ref _isCentered, centered, nameof(IsCentered)))
                RaisePropertyChanged<BackgroundBrush>(nameof(RecenterIcon));
            if (RaiseAndSetIfChanged(ref _orbitLinesShown, State.OrbitalLinesVisible, nameof(OrbitLinesShown)))
                RaisePropertyChanged<BackgroundBrush>(nameof(OrbitLinesIconMask));

            PlanetColorCubemap cubemap = GetLayerCubemap(State.Layer);
            bool waterApi = WaterModAPI.Registered;
            RaiseAndSetIfChanged(ref _waterVisible, IsPlanetary && waterApi, nameof(WaterVisible));
            RaiseAndSetIfChanged(ref _waterEnabled, waterApi && cubemap != null && cubemap.WaterOverlay != null,
                nameof(WaterEnabled));
            RaiseAndSetIfChanged(ref _isWaterHidden, !State.WaterVisible, nameof(IsWaterHidden));

            PlanetCubeFace face = GetReferencePreviewFace();
            for (int i = 0; i < _layers.Count; i++)
            {
                MapLayerItem item = _layers[i];
                bool selected = item.Layer == State.Layer;
                item.Update(selected, selected || _layersExpanded, GetLayerCubemap(item.Layer), face);
            }

            string status;
            Color color;
            bool hasStatus = TryGetStatus(out status, out color);
            RaiseAndSetIfChanged(ref _hasStatus, hasStatus, nameof(HasStatus));
            RaiseAndSetIfChanged(ref _status, hasStatus ? status : string.Empty, nameof(Status));
            RaiseAndSetIfChanged(ref _statusColor, color, nameof(StatusColor));
            RaiseAndSetIfChanged(ref _hasMap, _renderer != null && (IsStarMap || ResolveFocusedPlanet() != null),
                nameof(HasMap));
        }

        protected virtual bool TryGetStatus(out string status, out Color color)
        {
            status = null;
            color = Color.White;
            return false;
        }

        protected string RendererError => _renderer == null ? null : _renderer.Error;

        void RefreshMarkers()
        {
            _markers.Clear();
            if (HasCanvas && _renderer != null)
                CollectMarkers(_markers);
            RaiseAndSetIfChanged(ref _markersVersion, _markersVersion + 1, nameof(MarkersVersion));
        }

        protected virtual void CollectMarkers(List<MapMarker> markers)
        {
        }

        protected bool IsInsideCanvas(Vector2 pixel, float margin)
        {
            return pixel.X >= _canvas.X - margin && pixel.X <= _canvas.Right + margin &&
                   pixel.Y >= _canvas.Y - margin && pixel.Y <= _canvas.Bottom + margin;
        }

        float GetOrientationIconRotation()
        {
            float north;
            if (_renderer == null || !_renderer.TryGetPlanetNorthScreenRotation(Data, State, out north))
                north = 0f;
            return COMPASS_NORTH_CORRECTION + north;
        }

        PlanetCubeFace GetReferencePreviewFace()
        {
            PlanetSnapshot snapshot = ResolveFocusedPlanet();
            if (snapshot == null || snapshot.Planet == null || snapshot.Planet.MarkedForClose)
                return PlanetCubeFace.Front;
            Vector3D worldDirection = Data.PlayerPosition - snapshot.Position;
            if (worldDirection.Normalize() <= 1e-9)
                worldDirection = snapshot.Planet.WorldMatrix.Backward;
            Vector3 local = new Vector3(
                (float)Vector3D.Dot(worldDirection, snapshot.Planet.WorldMatrix.Right),
                (float)Vector3D.Dot(worldDirection, snapshot.Planet.WorldMatrix.Up),
                (float)Vector3D.Dot(worldDirection, snapshot.Planet.WorldMatrix.Backward));
            if (local.Normalize() <= .000001f)
                return PlanetCubeFace.Front;
            PlanetCubeFace face;
            float u;
            float v;
            PlanetMapSource.DirectionToFaceUv(local, out face, out u, out v);
            return face;
        }

        public void SelectLayer(CartographyLayer layer)
        {
            if (!_layersExpanded && layer == State.Layer)
            {
                _layersExpanded = true;
                RefreshPresentation();
                return;
            }

            _layersExpanded = false;
            if (layer != State.Layer)
            {
                State.Layer = layer;
                if (_renderer != null)
                    _renderer.UpdateRequests(Data, _mode, State);
                ViewChanged();
            }
            RefreshPresentation();
            Host.PlayClick();
        }

        void ToggleOrientation()
        {
            State.PlanetOrientation = State.PlanetOrientation == PlanetOrientationMode.NorthLocked
                ? PlanetOrientationMode.HeadingLocked
                : PlanetOrientationMode.NorthLocked;
            State.HasPlanetFreeReferenceUpWorld = false;
            ResetPlanetOrbit();
            ViewChanged();
            Host.PlayClick();
        }

        void ToggleFollow()
        {
            SetFollowPlayer(!State.FollowPlayer);
            ViewChanged();
            Host.PlayClick();
        }

        void ToggleWater()
        {
            if (!_waterEnabled)
                return;
            State.WaterVisible = !State.WaterVisible;
            ViewChanged();
            Host.PlayClick();
        }

        void ToggleOrbitLines()
        {
            State.OrbitalLinesVisible = !State.OrbitalLinesVisible;
            ViewChanged();
            Host.PlayClick();
        }

        void Recenter()
        {
            _orbitInertia.Stop();
            if (IsStarMap)
                State.StarTargetOffsetWorld = Vector3D.Zero;
            else
                State.PlanetCenterOffsetNormalized = Vector2.Zero;
            ViewChanged();
            Host.PlayClick();
        }

        protected void SetFollowPlayer(bool follow)
        {
            if (State.FollowPlayer == follow)
                return;
            if (follow)
            {
                State.FollowPlayer = true;
                State.HasStaticCameraPosition = false;
                ResetStarOrbit();
                ResetPlanetOrbit();
            }
            else
            {
                Data.UpdatePlayerPosition();
                State.StaticCameraPosition = Data.PlayerPosition;
                State.HasStaticCameraPosition = true;
                State.FollowPlayer = false;
            }
        }

        void UnlockPlanetOrientation()
        {
            if (State.PlanetOrientation == PlanetOrientationMode.Free)
                return;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            Vector3D referenceUp = Vector3D.Up;
            if (State.PlanetOrientation == PlanetOrientationMode.HeadingLocked && Data.HasPlayerWorldMatrix)
                referenceUp = Data.PlayerWorldMatrix.Forward;
            else if (planet != null && planet.Planet != null)
                referenceUp = planet.Planet.WorldMatrix.Up;
            if (referenceUp.Normalize() <= 1e-9)
                referenceUp = Vector3D.Up;
            State.PlanetFreeReferenceUpWorld = referenceUp;
            State.HasPlanetFreeReferenceUpWorld = true;
            State.PlanetOrientation = PlanetOrientationMode.Free;
        }

        protected void ViewChanged()
        {
            _dirty = true;
            OnViewChanged();
            RefreshPresentation();
            RefreshMarkers();
            Invalidate();
            Host.RequestRender();
        }

        protected virtual void OnViewChanged()
        {
        }

        void IMapSurface.SetCanvasSize(Vector2I size)
        {
            _canvas = new RectangleF(0f, 0f, size.X, size.Y);
            _dirty = true;
            RefreshMarkers();
        }

        void IMapSurface.Paint(PixelCanvas canvas)
        {
            if (_renderer == null)
            {
                canvas.Clear(Color.Transparent);
                return;
            }
            _renderer.Render(canvas, Data, _mode, State);
        }

        bool IAsyncMapSurface.RequestTexture(PixelCanvas canvas, Action<Exception> completed)
        {
            if (_renderer == null)
                return false;
            return _renderer.RequestRender(canvas, Data, _mode, State, error =>
            {
                completed(error);
                Host.RequestRender();
            });
        }

        protected virtual bool CanOrbit => true;

        void IMapSurfaceDrag.BeginDrag(Vector2 pixel)
        {
            _orbitInertia.Begin(OrbitTime,
                IsPlanetary ? State.PlanetOrbitView : State.StarOrbitView,
                IsPlanetary ? State.PlanetOrbitRight : State.StarOrbitRight,
                IsPlanetary ? State.PlanetOrbitUp : State.StarOrbitUp);
            _dragOrigin = pixel;
            _grabInitialized = false;
            _grabActive = false;
        }

        void IMapSurfaceDrag.DragTo(Vector2 pixel, Vector2 delta)
        {
            if (!CanOrbit)
                return;
            if (!IsPlanetary)
            {
                ((IMapSurface)this).Drag(delta);
                return;
            }

            // Use the same integer centre/radius as the texture rasterizer, including
            // zoom and the temporary offset used during the Star -> Planet handoff.
            var center = new Vector2(
                (int)_canvas.Width / 2 + (int)Math.Round(State.PlanetCenterOffsetNormalized.X * _canvas.Width),
                (int)_canvas.Height / 2 + (int)Math.Round(State.PlanetCenterOffsetNormalized.Y * _canvas.Height));
            float radius = (int)(Math.Min(_canvas.Width, _canvas.Height) * .47f *
                MathHelper.Clamp(State.Zoom, .0001f, 16f));
            if (!_grabInitialized)
            {
                _grabInitialized = true;
                _grabActive = _surfaceDrag.Begin(_dragOrigin, center, radius,
                    State.PlanetOrbitView, State.PlanetOrbitRight, State.PlanetOrbitUp);
                if (_grabActive)
                {
                    UnlockPlanetOrientation();
                    if (State.FollowPlayer)
                        SetFollowPlayer(false);
                }
            }
            if (!_grabActive)
            {
                ((IMapSurface)this).Drag(delta);
                return;
            }

            Vector3 view;
            Vector3 right;
            Vector3 up;
            _surfaceDrag.Move(pixel, center, radius, out view, out right, out up);
            NormalizeSeOrbit(ref view, ref right, ref up);
            State.PlanetOrbitView = view;
            State.PlanetOrbitRight = right;
            State.PlanetOrbitUp = up;
            _orbitInertia.Record(OrbitTime, view, right, up);
            ViewChanged();
        }

        void IMapSurfaceDrag.EndDrag()
        {
            _orbitInertia.Release(OrbitTime);
            _grabInitialized = false;
            _grabActive = false;
        }

        void IMapSurfaceDrag.CancelDrag()
        {
            _orbitInertia.Stop();
            _grabInitialized = false;
            _grabActive = false;
        }

        static double OrbitTime
        {
            get
            {
                var session = Sandbox.ModAPI.MyAPIGateway.Session;
                return session == null ? 0d : session.GameplayFrameCounter / 60d;
            }
        }

        void AdvanceOrbitInertia()
        {
            if (!CanOrbit)
            {
                _orbitInertia.Stop();
                return;
            }
            Vector3 view = IsPlanetary ? State.PlanetOrbitView : State.StarOrbitView;
            Vector3 right = IsPlanetary ? State.PlanetOrbitRight : State.StarOrbitRight;
            Vector3 up = IsPlanetary ? State.PlanetOrbitUp : State.StarOrbitUp;
            if (!_orbitInertia.Advance(OrbitTime, ref view, ref right, ref up))
                return;
            NormalizeSeOrbit(ref view, ref right, ref up);
            if (IsPlanetary)
            {
                State.PlanetOrbitView = view;
                State.PlanetOrbitRight = right;
                State.PlanetOrbitUp = up;
            }
            else
            {
                State.StarOrbitView = view;
                State.StarOrbitRight = right;
                State.StarOrbitUp = up;
            }
            _dirty = true;
            OnViewChanged();
            Host.RequestRender();
        }

        void IMapSurface.Drag(Vector2 delta)
        {
            if (!CanOrbit)
                return;
            if (IsPlanetary)
            {
                UnlockPlanetOrientation();
                RotatePlanetOrbit(-delta.X * ORBIT_SENSITIVITY, delta.Y * ORBIT_SENSITIVITY);
            }
            else
            {
                RotateStarOrbit(delta.X * ORBIT_SENSITIVITY, delta.Y * ORBIT_SENSITIVITY);
            }
            if (State.FollowPlayer)
                SetFollowPlayer(false);
            _orbitInertia.Record(OrbitTime,
                IsPlanetary ? State.PlanetOrbitView : State.StarOrbitView,
                IsPlanetary ? State.PlanetOrbitRight : State.StarOrbitRight,
                IsPlanetary ? State.PlanetOrbitUp : State.StarOrbitUp);
            ViewChanged();
        }

        void IMapSurface.MiddleDrag(Vector2 delta)
        {
            if (IsStarMap && CanOrbit && _renderer != null && _renderer.PanStarMap(State, delta))
                ViewChanged();
        }

        bool IMapSurface.Scroll(int delta)
        {
            return delta != 0 && HandleScroll(delta);
        }

        protected virtual bool HandleScroll(int delta)
        {
            float multiplier = IsStarMap ? 1.15f : 1.25f;
            float next = State.Zoom * (delta > 0 ? multiplier : 1f / multiplier);
            next = IsStarMap
                ? MathHelper.Clamp(next, STAR_MINIMUM_ZOOM, STAR_MAXIMUM_ZOOM)
                : MathHelper.Clamp(next, PLANET_MINIMUM_ZOOM, PLANET_MAXIMUM_ZOOM);
            if (Math.Abs(next - State.Zoom) <= .0001f)
                return false;
            State.Zoom = next;
            ViewChanged();
            return true;
        }

        void IMapSurface.MiddleClick(Vector2 pixel)
        {
            OnMiddleClick(pixel);
        }

        void IMapSurface.Click(Vector2 pixel)
        {
            OnClick(pixel);
        }

        void IMapSurface.DoubleClick(Vector2 pixel)
        {
            OnDoubleClick(pixel);
        }

        void IMapSurface.MarkerClicked(MapMarker marker)
        {
            OnMarkerClicked(marker);
        }

        void IMapSurface.StreetView(Vector2 pixel)
        {
            OnStreetView(pixel);
        }

        protected virtual void OnClick(Vector2 pixel)
        {
        }

        protected virtual void OnDoubleClick(Vector2 pixel)
        {
        }

        protected virtual void OnMiddleClick(Vector2 pixel)
        {
        }

        protected virtual void OnMarkerClicked(MapMarker marker)
        {
        }

        protected virtual void OnStreetView(Vector2 pixel)
        {
        }

        protected bool TryGetSurfacePosition(Vector2 pixel, out Vector3D position)
        {
            position = Vector3D.Zero;
            return _renderer != null && HasCanvas && _renderer.TryGetSurfacePosition(_canvas, pixel, Data, State, out position);
        }

        protected bool TryProjectSurface(Vector3D world, out Vector2 pixel)
        {
            pixel = default(Vector2);
            return _renderer != null && HasCanvas &&
                   _renderer.TryProjectPlanetSurfacePosition(_canvas, world, Data, State, out pixel);
        }

        protected bool TryProjectStar(Vector3D world, out Vector2 pixel)
        {
            pixel = default(Vector2);
            return _renderer != null && HasCanvas && _renderer.TryProjectStarMapPosition(_canvas, world, Data, State, out pixel);
        }

        protected bool TryGetPlayerOverlay(out Vector2 pixel, out float rotation)
        {
            pixel = default(Vector2);
            rotation = 0f;
            if (_renderer == null || !HasCanvas || !Data.HasPlayerWorldMatrix ||
                !_renderer.TryGetPlanetPlayerOverlay(_canvas, Data, State, Data.PlayerWorldMatrix, out pixel, out rotation))
                return false;
            rotation += COMPASS_NORTH_CORRECTION;
            return true;
        }

        protected IReadOnlyList<StarMapLabel> StarLabels => _renderer == null ? null : _renderer.StarMapLabels;

        protected bool FocusPlanet(long planetId)
        {
            if (Data.FindPlanet(planetId) == null)
                return false;
            _orbitInertia.Stop();
            State.FocusedPlanetId = planetId;
            State.StarTargetOffsetWorld = Vector3D.Zero;
            ViewChanged();
            return true;
        }

        protected void ResetPlanetOrbit()
        {
            _orbitInertia.Stop();
            State.PlanetOrbitView = Vector3.Backward;
            State.PlanetOrbitRight = Vector3.Right;
            State.PlanetOrbitUp = Vector3.Up;
        }

        protected void ResetStarOrbit()
        {
            _orbitInertia.Stop();
            State.StarOrbitView = new Vector3(0f, .6f, .8f);
            State.StarOrbitRight = new Vector3(1f, 0f, 0f);
            State.StarOrbitUp = new Vector3(0f, .8f, -.6f);
        }

        /// <summary>The orbit basis as view (Backward) then Up, the gimbal-free form persisted in the configuration.</summary>
        protected float[] GetOrbitBasis(bool star)
        {
            Vector3 view = star ? State.StarOrbitView : State.PlanetOrbitView;
            Vector3 up = star ? State.StarOrbitUp : State.PlanetOrbitUp;
            return new[] { view.X, view.Y, view.Z, up.X, up.Y, up.Z };
        }

        protected bool TrySetOrbitBasis(float[] basis, bool star)
        {
            if (basis == null || basis.Length != 6)
                return false;
            var view = new Vector3(basis[0], basis[1], basis[2]);
            var up = new Vector3(basis[3], basis[4], basis[5]);
            if (!IsFinite(view) || !IsFinite(up) || view.LengthSquared() < .5f || up.LengthSquared() < .5f)
                return false;
            Vector3 right = Vector3.Cross(up, view);
            NormalizeSeOrbit(ref view, ref right, ref up);
            if (star)
            {
                State.StarOrbitView = view;
                State.StarOrbitRight = right;
                State.StarOrbitUp = up;
            }
            else
            {
                State.PlanetOrbitView = view;
                State.PlanetOrbitRight = right;
                State.PlanetOrbitUp = up;
            }
            return true;
        }

        protected static bool SameBasis(float[] left, float[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return left == right || (left?.Length ?? 0) == 0 && (right?.Length ?? 0) == 0;
            for (int i = 0; i < left.Length; i++)
                if (Math.Abs(left[i] - right[i]) > .000001f)
                    return false;
            return true;
        }

        protected void SetStarOrbitAngles(float yaw, float pitch)
        {
            ResetStarOrbit();
            RotateStarOrbit(yaw, pitch);
        }

        /// <summary>Planet orbit as yaw/pitch of its view vector, the form stored in the LCD configuration.</summary>
        protected void GetPlanetOrbitAngles(out float yaw, out float pitch)
        {
            Vector3 view = State.PlanetOrbitView;
            yaw = (float)Math.Atan2(view.X, view.Z);
            pitch = (float)Math.Asin(MathHelper.Clamp(view.Y, -1f, 1f));
        }

        protected void SetPlanetOrbitAngles(float yaw, float pitch)
        {
            ResetPlanetOrbit();
            RotatePlanetOrbit(yaw, pitch);
        }

        void RotateStarOrbit(float horizontal, float vertical)
        {
            Vector3 view = State.StarOrbitView;
            Vector3 right = State.StarOrbitRight;
            Vector3 up = State.StarOrbitUp;
            NormalizeSeOrbit(ref view, ref right, ref up);
            if (Math.Abs(horizontal) > .000001f)
            {
                view = RotateAroundAxis(view, up, horizontal);
                right = RotateAroundAxis(right, up, horizontal);
            }
            if (Math.Abs(vertical) > .000001f)
            {
                view = RotateAroundAxis(view, right, vertical);
                up = RotateAroundAxis(up, right, vertical);
            }
            NormalizeSeOrbit(ref view, ref right, ref up);
            State.StarOrbitView = view;
            State.StarOrbitRight = right;
            State.StarOrbitUp = up;
        }

        void RotatePlanetOrbit(float horizontal, float vertical)
        {
            Vector3 view = State.PlanetOrbitView;
            Vector3 right = State.PlanetOrbitRight;
            Vector3 up = State.PlanetOrbitUp;
            NormalizeSeOrbit(ref view, ref right, ref up);
            if (Math.Abs(horizontal) > .000001f)
            {
                view = RotateAroundAxis(view, up, horizontal);
                right = RotateAroundAxis(right, up, horizontal);
            }
            if (Math.Abs(vertical) > .000001f)
            {
                view = RotateAroundAxis(view, right, -vertical);
                up = RotateAroundAxis(up, right, -vertical);
            }
            NormalizeSeOrbit(ref view, ref right, ref up);
            State.PlanetOrbitView = view;
            State.PlanetOrbitRight = right;
            State.PlanetOrbitUp = up;
        }

        static Vector3 RotateAroundAxis(Vector3 value, Vector3 axis, float angle)
        {
            if (axis.Normalize() <= .000001f)
                return value;
            float cosine = (float)Math.Cos(angle);
            float sine = (float)Math.Sin(angle);
            return value * cosine + Vector3.Cross(axis, value) * sine + axis * Vector3.Dot(axis, value) * (1f - cosine);
        }

        static void NormalizeSeOrbit(ref Vector3 backward, ref Vector3 right, ref Vector3 up)
        {
            if (!IsFinite(backward) || backward.Normalize() <= .000001f)
                backward = Vector3.Backward;
            if (!IsFinite(right))
                right = Vector3.Right;
            right -= backward * Vector3.Dot(right, backward);
            if (right.Normalize() <= .000001f)
            {
                Vector3 referenceUp = IsFinite(up) ? up : Vector3.Up;
                right = Vector3.Cross(referenceUp, backward);
                if (!IsFinite(right) || right.Normalize() <= .000001f)
                {
                    referenceUp = Math.Abs(Vector3.Dot(backward, Vector3.Up)) > .98f ? Vector3.Forward : Vector3.Up;
                    right = Vector3.Cross(referenceUp, backward);
                    if (right.Normalize() <= .000001f)
                        right = Vector3.Right;
                }
            }
            up = Vector3.Cross(backward, right);
            if (!IsFinite(up) || up.Normalize() <= .000001f)
                up = Vector3.Up;
            right = Vector3.Cross(up, backward);
            if (!IsFinite(right) || right.Normalize() <= .000001f)
                right = Vector3.Right;
        }

        static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.X) && !float.IsInfinity(value.X) &&
                   !float.IsNaN(value.Y) && !float.IsInfinity(value.Y) &&
                   !float.IsNaN(value.Z) && !float.IsInfinity(value.Z);
        }

        static string LayerName(CartographyLayer layer)
        {
            switch (layer)
            {
                case CartographyLayer.Terrain:
                    return LocalizedLayer("Terrain");
                case CartographyLayer.Materials:
                    return LocalizedLayer("Materials");
                case CartographyLayer.Biomes:
                    return LocalizedLayer("Biomes");
                default:
                    return LocalizedLayer("Satellite");
            }
        }

        static string LocalizedLayer(string name)
        {
            return Helpers.LocHelper.GetLoc(Common.Helpers.Constants.MOD_PREFIX + "PlanetaryMap_Layer_" + name);
        }

        static string LayerDescription(CartographyLayer layer)
        {
            return Helpers.LocHelper.GetLoc(Common.Helpers.Constants.MOD_PREFIX + "PlanetaryMap_Layer_" + layer + "_Tooltip");
        }

        protected static string Tooltip(string name)
        {
            return Helpers.LocHelper.GetLoc(Common.Helpers.Constants.MOD_PREFIX + "PlanetaryMap_" + name + "_Title") + "\n" +
                   Helpers.LocHelper.GetLoc(Common.Helpers.Constants.MOD_PREFIX + "PlanetaryMap_" + name + "_Tooltip");
        }

        public virtual void Dispose()
        {
            _orbitInertia.Stop();
            if (_renderer != null)
                _renderer.Dispose();
            _renderer = null;
            _rendererModule = null;
        }
    }
}
