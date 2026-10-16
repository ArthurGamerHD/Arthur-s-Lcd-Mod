using System;
using System.Collections.Generic;
using Jakaria.API;
using Cartography;
using Adk.Gui.Rendering;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;
using VRage.Utils;

namespace Cartography.Maps
{
    public enum MapMode
    {
        StarMap,
        PlanetaryMap
    }

    public enum PlanetOrientationMode
    {
        NorthLocked,
        HeadingLocked,
        Free
    }

    public sealed class MapViewState
    {
        public float Yaw;
        public float Pitch;
        // Star-map orbit is an orthonormal camera basis in real SE world space.
        // StarOrbitView is the camera Backward / target->camera direction (+Z in
        // camera-local coordinates); therefore the camera looks along -Z, matching
        // Space Engineers' view-matrix convention.
        public Vector3 StarOrbitView { get; set; } = new Vector3(0f, 0.6f, 0.8f);
        public Vector3 StarOrbitRight { get; set; } = new Vector3(1f, 0f, 0f);
        public Vector3 StarOrbitUp { get; set; } = new Vector3(0f, 0.8f, -0.6f);
        // Planetary-map orbit is kept as an orthonormal screen basis instead of
        // Euler yaw/pitch. This lets drag input rotate around the current screen
        // axes, so horizontal/vertical motion remains intuitive at arbitrary
        // orientations and does not hit the pole/gimbal singularity.
        public Vector3 PlanetOrbitView { get; set; } = Vector3.Backward;
        public Vector3 PlanetOrbitRight { get; set; } = Vector3.Right;
        public Vector3 PlanetOrbitUp { get; set; } = Vector3.Up;
        public float Zoom = 1f;
        public PlanetOrientationMode PlanetOrientation = PlanetOrientationMode.NorthLocked;
        public bool HasPlanetFreeReferenceUpWorld;
        public Vector3D PlanetFreeReferenceUpWorld;
        public bool FollowPlayer = true;
        public bool HasStaticCameraPosition;
        public Vector3D StaticCameraPosition;
        public long FocusedPlanetId;
        public Vector3D StarTargetOffsetWorld;
        // Temporary screen-space offset used while crossing from the system
        // frame into the focused planet frame. It eases to zero during the
        // mode transition so a panned/off-center focused planet does not jump.
        public Vector2 PlanetCenterOffsetNormalized;
        public bool WaterVisible = true;
        public bool RoadsVisible = true;
        public bool RenderSkybox;
        public bool OrbitalLinesVisible = true;
        public CartographyLayer Layer = CartographyLayer.Satellite;
        // A first-person Star Map camera (the LCD HUD). When set, it replaces the
        // orbit camera: Translation/Right/Up/Backward are used as-is and the
        // vertical field of view sets the focal length.
        public bool HasFixedStarCamera;
        public MatrixD FixedStarCamera = MatrixD.Identity;
        public float FixedStarCameraVerticalFovRadians = MathHelper.ToRadians(70f);
    }

    public sealed class StarMapLabel
    {
        public long PlanetId;
        public string Text;
        public Vector2 NormalizedPosition;
        public float NormalizedRadius;
    }

    /// <summary>
    /// Standalone adapter around the original cartography pipeline. The source
    /// cubemaps are decoded asynchronously, then sampled directly into the new
    /// renderer-owned generated texture.
    /// </summary>
    public sealed class CartographyMapRenderer : IDisposable
    {
        // Zero requests the source planet-data resolution. The full cubemap already
        // contains a complete 2x mip chain, so the live map can choose detail at
        // sample time instead of regenerating the source for each quality level.
        const int PlanetaryFaceSide = 0;
        const int StarMapFaceSide = 64;
        const int PreviewFaceSide = 16;
        const int MaximumStarMapRequests = 24;
        public const float StarMinimumZoom = 0.25f;
        public const float StarMaximumZoom = 108f;
        const double StarVerticalFovRadians = Math.PI / 3d;
        const double StarSystemHalfSpanFraction = 0.42d;
        // Match Planet Map minimum zoom (0.55 * 0.47 ~= 0.2585 of the short side)
        // at the Star Map maximum zoom. This keeps the wheel handoff visually continuous.
        const double StarMaximumFocusedRadiusFraction = 0.2585d;
        const float MaximumOrbitStrokeWidth = 2f;
        const float MinimumOrbitStrokeWidth = 1f;
        const float VectorEpsilon = 0.000001f;
        const double GpsSurfaceTargetToleranceMeters = 10000d;
        const double GpsSurfaceTerrainMarginMeters = 5000d;
        const double GpsSurfaceMaximumRadiusRatio = 0.25d;
        const double GpsSurfaceMinimumAllowedToleranceMeters = 1000d;
        const byte DefaultOrbitStyle = 0;
        const byte FocusedOrbitStyle = 1;
        const byte CurrentOrbitStyle = 2;
        static readonly Color DefaultOrbitColor = new Color(255, 255, 255, 150);
        static readonly Color FocusedOrbitColor = new Color(0x83, 0xad, 0x10, 150);
        static readonly Color CurrentOrbitColor = new Color(117, 201, 241, 150);
        static readonly Color FocusedPlanetIndicatorColor = new Color(0x83, 0xad, 0x10, 190);
        static readonly Color CurrentPlanetIndicatorColor = new Color(117, 201, 241, 190);
        static readonly Color WaterOverlayTint = new Color(75, 110, 127, 200);

        struct CubemapKey : IEquatable<CubemapKey>
        {
            public long PlanetId;
            public CartographyLayer Layer;

            public bool Equals(CubemapKey other)
            {
                return PlanetId == other.PlanetId && Layer == other.Layer;
            }

            public override bool Equals(object obj)
            {
                return obj is CubemapKey && Equals((CubemapKey)obj);
            }

            public override int GetHashCode()
            {
                return unchecked(PlanetId.GetHashCode() * 397 ^ (int)Layer);
            }
        }

        sealed class PendingRequest
        {
            public int FaceSide;
            public int Version;
            public CartographyTicket Ticket;
        }

        struct DepthLineSegment
        {
            public Vector2 Start;
            public Vector2 End;
            public float StartDepth;
            public float EndDepth;
            public float Width;
            public byte Style;
        }

        struct StarCamera
        {
            public Vector3D PositionWorld;
            public Vector3D TargetWorld;
            // Space Engineers world/view convention: Right, Up, Backward. The
            // camera looks along -Backward, so points in front have a negative Z.
            public Vector3 Backward;
            public Vector3 Right;
            public Vector3 Up;
            public double FocalLengthPixels;
            public double NearDepth;
            public double DistanceToTarget;
            public int ScreenCenterX;
            public int ScreenCenterY;
        }

        readonly CartographyModule _cartography;
        readonly Dictionary<CubemapKey, PlanetColorCubemap> _cubemaps =
            new Dictionary<CubemapKey, PlanetColorCubemap>();
        readonly Dictionary<CubemapKey, PendingRequest> _pending =
            new Dictionary<CubemapKey, PendingRequest>();
        readonly Dictionary<CubemapKey, int> _versions =
            new Dictionary<CubemapKey, int>();
        readonly List<StarMapLabel> _starMapLabels = new List<StarMapLabel>();
        readonly List<DepthLineSegment> _starOrbitSegments = new List<DepthLineSegment>();
        readonly List<int> _starOrbitTouchedPixels = new List<int>();
        float[] _starDepthBuffer;
        float[] _starOrbitCoverageBuffer;
        byte[] _starOrbitStyleBuffer;
        Vector3D _starPanScreenRightWorld;
        Vector3D _starPanScreenUpWorld;
        double _starPanWorldUnitsPerPixel;
        bool _starPanProjectionValid;
        bool _disposed;
        bool _renderPending;
        MapRenderFrame _preparingFrame;

        public CartographyMapRenderer(CartographyModule cartography)
        {
            _cartography = cartography;
            if (_cartography != null)
            {
                _cartography.ColorCubemapCached += OnColorCubemapCached;
                _cartography.PlanetInvalidated += OnPlanetInvalidated;
                _cartography.Skybox.Changed += RaiseChanged;
            }
        }

        public Action Changed { get; set; }
        public string Error { get; private set; }
        public IReadOnlyList<StarMapLabel> StarMapLabels => _starMapLabels;

        public bool TryGetPlanetNorthScreenRotation(
            MapData data,
            MapViewState state,
            out float clockwiseRotation)
        {
            clockwiseRotation = 0f;
            if (state == null)
                return false;

            PlanetSnapshot snapshot = ResolvePlanet(data, state);
            MyPlanet planet = snapshot == null ? null : snapshot.Planet;
            if (planet == null || planet.MarkedForClose)
                return false;

            Vector3 view;
            Vector3 right;
            Vector3 up;
            BuildProjection(planet, data, state, out view, out right, out up);

            // Planet-local +Y is its real north axis. In screen coordinates the
            // projected direction is (dot(N, Right), -dot(N, Up)); DrawPixels uses
            // clockwise radians, hence atan2(screenX, -screenY).
            float screenX = Vector3.Dot(Vector3.Up, right);
            float screenUp = Vector3.Dot(Vector3.Up, up);
            if (!IsFinite(screenX) || !IsFinite(screenUp) ||
                screenX * screenX + screenUp * screenUp <=
                VectorEpsilon * VectorEpsilon)
            {
                return false;
            }

            clockwiseRotation = (float)Math.Atan2(screenX, screenUp);
            return IsFinite(clockwiseRotation);
        }

        public bool PanStarMap(MapViewState state, Vector2 delta)
        {
            if (state == null || !_starPanProjectionValid ||
                _starPanWorldUnitsPerPixel <= 0d ||
                double.IsNaN(_starPanWorldUnitsPerPixel) ||
                double.IsInfinity(_starPanWorldUnitsPerPixel))
            {
                return false;
            }

            Vector3D movement =
                _starPanScreenRightWorld * (-delta.X * _starPanWorldUnitsPerPixel) +
                _starPanScreenUpWorld * (delta.Y * _starPanWorldUnitsPerPixel);
            if (movement.LengthSquared() <= 1e-12d)
                return false;

            state.StarTargetOffsetWorld += movement;
            return true;
        }

        public void UpdateRequests(MapData data, MapMode mode, MapViewState state)
        {
            if (_disposed || _cartography == null || data == null)
                return;

            if (state.RenderSkybox)
                _cartography.Skybox.Update();

            if (mode == MapMode.PlanetaryMap)
            {
                PlanetSnapshot planet = ResolvePlanet(data, state);
                if (planet == null)
                    return;

                EnsureRequested(planet, state.Layer, PlanetaryFaceSide);
                EnsureRequested(planet, CartographyLayer.Satellite, PreviewFaceSide);
                EnsureRequested(planet, CartographyLayer.Terrain, PreviewFaceSide);
                EnsureRequested(planet, CartographyLayer.Materials, PreviewFaceSide);
                EnsureRequested(planet, CartographyLayer.Biomes, PreviewFaceSide);
                return;
            }

            // Star Map uses the same selected cartography layer as Planet Map.
            // Keep low-resolution previews for the focused planet so the layer
            // selector remains useful in both modes, while the visible layer is
            // progressively requested for all rendered planets.
            PlanetSnapshot focusedPlanet = ResolvePlanet(data, state);
            if (focusedPlanet != null)
            {
                EnsureRequested(focusedPlanet, CartographyLayer.Satellite, PreviewFaceSide);
                EnsureRequested(focusedPlanet, CartographyLayer.Terrain, PreviewFaceSide);
                EnsureRequested(focusedPlanet, CartographyLayer.Materials, PreviewFaceSide);
                EnsureRequested(focusedPlanet, CartographyLayer.Biomes, PreviewFaceSide);
            }

            int count = Math.Min(data.Planets.Count, MaximumStarMapRequests);
            int faceSide = GetStarMapFaceSide(state.Zoom);
            for (int i = 0; i < count; i++)
                EnsureRequested(data.Planets[i], state.Layer, faceSide);

            // Pre-warm the focused planet's full source while approaching the
            // Star->Planet boundary. This gives the camera-frame morph a full
            // resolution destination instead of crossfading into a late decode.
            if (focusedPlanet != null && state.Zoom >= StarMaximumZoom * 0.5f)
                EnsureRequested(focusedPlanet, state.Layer, PlanetaryFaceSide);

            if (state.HasFixedStarCamera)
            {
                PlanetSnapshot nearPlanet = data.FindPlayerPlanet();
                if (nearPlanet != null)
                    EnsureRequested(nearPlanet, state.Layer, PlanetaryFaceSide);
            }
        }

        static PlanetSnapshot ResolvePlanet(MapData data, MapViewState state)
        {
            if (data == null)
                return null;

            PlanetSnapshot focused = state == null ? null : data.FindPlanet(state.FocusedPlanetId);
            return focused ?? data.FindClosestPlanet();
        }

        public PlanetColorCubemap GetCubemap(long planetId, CartographyLayer layer)
        {
            PlanetColorCubemap cubemap;
            _cubemaps.TryGetValue(new CubemapKey { PlanetId = planetId, Layer = layer }, out cubemap);
            return cubemap;
        }

        public void Render(PixelCanvas canvas, MapData data, MapMode mode, MapViewState state)
        {
            PrepareFrame(canvas, data, mode, state).Paint(canvas, (first, end, paint) =>
            {
                for (int row = first; row < end; row++)
                    paint(row);
            });
        }

        public bool RequestRender(PixelCanvas canvas, MapData data, MapMode mode, MapViewState state,
            Action<Exception> completed)
        {
            if (_disposed || _renderPending || MyAPIGateway.Parallel == null)
                return false;

            // Capture all entity/camera state on the caller (simulation/UI) thread.
            // No subsequent request may mutate the frame's renderer caches until
            // its completion callback has run on this same thread.
            MapRenderFrame frame = PrepareFrame(canvas, data, mode, state);
            var parallel = MyAPIGateway.Parallel;
            Exception failure = null;
            _renderPending = true;
            try
            {
                parallel.Start(() =>
                {
                    try
                    {
                        frame.Paint(canvas, (first, end, paint) => parallel.For(first, end, paint));
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                }, () =>
                {
                    _renderPending = false;
                    if (!_disposed && failure != null)
                    {
                        Error = failure.Message;
                        RaiseChanged();
                    }
                    completed(failure);
                });
            }
            catch
            {
                _renderPending = false;
                throw;
            }
            return true;
        }

        MapRenderFrame PrepareFrame(PixelCanvas canvas, MapData data, MapMode mode, MapViewState state)
        {
            var frame = new MapRenderFrame();
            _preparingFrame = frame;
            try
            {
                if (mode == MapMode.StarMap)
                    RenderStarMap(canvas, data, state);
                else
                    RenderPlanetaryMap(canvas, data, state);
            }
            finally
            {
                _preparingFrame = null;
            }
            return frame;
        }

        /// <summary>
        /// Renders an overscanned, player-centred tangent texture for the in-game
        /// HUD. The square result can rotate behind its viewport without exposing
        /// empty corners and shows nearby terrain instead of the complete globe.
        /// </summary>
        public void RenderPlanetaryMinimap(
            PixelCanvas canvas,
            MapData data,
            MapViewState state,
            float zoom,
            float tangentExtentScale)
        {
            canvas.Clear(Color.Transparent);
            PlanetSnapshot snapshot = ResolvePlanet(data, state);
            MyPlanet planet = snapshot == null ? null : snapshot.Planet;
            if (planet == null || planet.MarkedForClose)
                return;

            Vector3 centerDirection;
            Vector3 mapRight;
            Vector3 mapUp;
            if (!TryBuildPlanetMinimapBasis(
                    planet,
                    data,
                    state,
                    out centerDirection,
                    out mapRight,
                    out mapUp))
            {
                return;
            }

            zoom = MathHelper.Clamp(zoom, 0.25f, 8f);
            tangentExtentScale = MathHelper.Clamp(tangentExtentScale, 1f, 4f);
            float halfTangentHeight = 0.18f * tangentExtentScale / zoom;
            float tangentUnitsPerPixel = halfTangentHeight * 2f / canvas.Height;
            float verticalFov = 2f * (float)Math.Atan(halfTangentHeight);
            PlanetColorCubemap cubemap = GetCubemap(snapshot.EntityId, state.Layer);
            int mip = cubemap == null
                ? 0
                : cubemap.SelectMipLevel(
                    canvas.Height * MathHelper.PiOver2 / Math.Max(0.0001f, verticalFov));

            float centerX = canvas.Width * 0.5f;
            float centerY = canvas.Height * 0.5f;
            for (int y = 0; y < canvas.Height; y++)
            for (int x = 0; x < canvas.Width; x++)
            {
                float tangentX = (x + 0.5f - centerX) * tangentUnitsPerPixel;
                float tangentY = (centerY - y - 0.5f) * tangentUnitsPerPixel;
                Vector3 direction = centerDirection +
                                    mapRight * tangentX +
                                    mapUp * tangentY;
                if (direction.Normalize() <= VectorEpsilon)
                    direction = centerDirection;

                Color color = cubemap == null
                    ? new Color(82, 86, 89, 245)
                    : cubemap.Sample(direction, mip);
                if (cubemap != null && state.WaterVisible && cubemap.WaterOverlay != null)
                {
                    Color water = cubemap.WaterOverlay.Sample(direction, mip);
                    if (water.A != 0)
                        color = CompositeWaterTint(color, water.A);
                }
                canvas.SetPixel(x, y, color);
            }
        }

        public bool TransferStarCameraToPlanet(
            MapData data,
            MapViewState state,
            int canvasWidth,
            int canvasHeight)
        {
            const string handoff = "Star->Planet";
            PlanetSnapshot snapshot = ResolvePlanet(data, state);
            MyPlanet planet = snapshot == null ? null : snapshot.Planet;
            if (state == null)
                return AbortCameraHandoff(handoff, "view state is null");
            if (planet == null || planet.MarkedForClose)
                return AbortCameraHandoff(handoff, "focused planet is unavailable or closing");
            if (!IsFinite(state.Zoom) || state.Zoom <= 0f)
                return AbortCameraHandoff(handoff, "Star zoom is invalid");
            if (!IsFinite(state.StarTargetOffsetWorld))
                return AbortCameraHandoff(handoff, "Star focus offset contains NaN/Infinity");
            if (snapshot == null || !IsFinite(snapshot.Position) ||
                !IsFinite(snapshot.Radius) || snapshot.Radius <= 0d)
            {
                return AbortCameraHandoff(handoff, "focused planet snapshot position/radius is invalid");
            }
            if (!ValidatePlanetWorldBasis(planet, handoff))
                return false;

            StarCamera starCamera;
            if (!TryBuildStarCamera(
                    data,
                    state,
                    canvasWidth,
                    canvasHeight,
                    out starCamera))
            {
                return AbortCameraHandoff(handoff, "could not rebuild the outgoing Star camera");
            }

            // The billboard is centred on the planet but its visible hemisphere
            // faces the actual virtual-camera position. For a panned/off-axis
            // planet this is deliberately not identical to camera.Backward.
            Vector3D planetToCamera = starCamera.PositionWorld - snapshot.Position;
            if (planetToCamera.Normalize() <= 1e-9)
                return AbortCameraHandoff(handoff, "Star camera is at the planet centre");
            Vector3 worldView = (Vector3)planetToCamera;
            Vector3 worldRight = starCamera.Right;
            Vector3 worldUp = starCamera.Up;
            string reason;
            if (!TryNormalizeSeCameraBasis(ref worldView, ref worldRight, ref worldUp, out reason))
                return AbortCameraHandoff(handoff, "invalid Star billboard basis: " + reason);

            Vector3 desiredView = WorldDirectionToPlanetLocal(
                planet, new Vector3D(worldView.X, worldView.Y, worldView.Z));
            Vector3 desiredRight = WorldDirectionToPlanetLocal(
                planet, new Vector3D(worldRight.X, worldRight.Y, worldRight.Z));
            Vector3 desiredUp = WorldDirectionToPlanetLocal(
                planet, new Vector3D(worldUp.X, worldUp.Y, worldUp.Z));
            if (!TryNormalizeSeCameraBasis(ref desiredView, ref desiredRight, ref desiredUp, out reason))
                return AbortCameraHandoff(handoff, "world-to-planet basis conversion failed: " + reason);

            // Build the exact base frame used by Planet Map Free mode, but keep all
            // candidate state local until the complete handoff has been verified.
            Vector3 baseView = Vector3.Backward;
            Vector3D mapViewPosition = GetMapViewPosition(data, state);
            if (!IsFinite(mapViewPosition))
                return AbortCameraHandoff(handoff, "map view position contains NaN/Infinity");
            Vector3D worldDirection = mapViewPosition - planet.WorldMatrix.Translation;
            if (worldDirection.Normalize() > 1e-9)
            {
                baseView = WorldDirectionToPlanetLocal(planet, worldDirection);
                if (!IsFinite(baseView) || baseView.Normalize() <= VectorEpsilon)
                    return AbortCameraHandoff(handoff, "planet base view is degenerate");
            }

            Vector3 referenceUp = desiredUp;
            if (!IsFinite(referenceUp) || referenceUp.Normalize() <= VectorEpsilon)
                return AbortCameraHandoff(handoff, "planet reference Up is degenerate");

            Vector3 baseRight = Vector3.Cross(referenceUp, baseView);
            if (!IsFinite(baseRight) || baseRight.Normalize() <= VectorEpsilon)
            {
                referenceUp = Math.Abs(Vector3.Dot(baseView, Vector3.Up)) > 0.98f
                    ? Vector3.Forward
                    : Vector3.Up;
                baseRight = Vector3.Cross(referenceUp, baseView);
                if (!IsFinite(baseRight) || baseRight.Normalize() <= VectorEpsilon)
                    return AbortCameraHandoff(handoff, "could not construct planet base Right");
            }

            Vector3 baseUp = Vector3.Cross(baseView, baseRight);
            if (!IsFinite(baseUp) || baseUp.Normalize() <= VectorEpsilon)
                return AbortCameraHandoff(handoff, "could not construct planet base Up");
            if (!TryNormalizeSeCameraBasis(ref baseView, ref baseRight, ref baseUp, out reason))
                return AbortCameraHandoff(handoff, "planet base frame is invalid: " + reason);

            Vector3 orbitView = ProjectionVectorInBasis(
                desiredView, baseRight, baseUp, baseView);
            Vector3 orbitRight = ProjectionVectorInBasis(
                desiredRight, baseRight, baseUp, baseView);
            Vector3 orbitUp = ProjectionVectorInBasis(
                desiredUp, baseRight, baseUp, baseView);
            if (!TryNormalizeSeCameraBasis(ref orbitView, ref orbitRight, ref orbitUp, out reason))
                return AbortCameraHandoff(handoff, "planet orbit basis is invalid: " + reason);

            // Commit only after every source, transform and destination basis passed.
            state.PlanetOrientation = PlanetOrientationMode.Free;
            state.PlanetFreeReferenceUpWorld =
                new Vector3D(worldUp.X, worldUp.Y, worldUp.Z);
            state.HasPlanetFreeReferenceUpWorld = true;
            state.PlanetOrbitView = orbitView;
            state.PlanetOrbitRight = orbitRight;
            state.PlanetOrbitUp = orbitUp;
            return true;
        }

        public bool TransferPlanetCameraToStar(MapData data, MapViewState state)
        {
            const string handoff = "Planet->Star";
            PlanetSnapshot snapshot = ResolvePlanet(data, state);
            MyPlanet planet = snapshot == null ? null : snapshot.Planet;
            if (state == null)
                return AbortCameraHandoff(handoff, "view state is null");
            if (planet == null || planet.MarkedForClose)
                return AbortCameraHandoff(handoff, "focused planet is unavailable or closing");
            if (!IsFinite(state.Zoom) || state.Zoom <= 0f)
                return AbortCameraHandoff(handoff, "Planet zoom is invalid");
            if (snapshot == null || !IsFinite(snapshot.Position) ||
                !IsFinite(snapshot.Radius) || snapshot.Radius <= 0d)
            {
                return AbortCameraHandoff(handoff, "focused planet snapshot position/radius is invalid");
            }
            if (!ValidatePlanetWorldBasis(planet, handoff))
                return false;

            Vector3 sourceOrbitView = state.PlanetOrbitView;
            Vector3 sourceOrbitRight = state.PlanetOrbitRight;
            Vector3 sourceOrbitUp = state.PlanetOrbitUp;
            string reason;
            if (!TryNormalizeSeCameraBasis(
                    ref sourceOrbitView,
                    ref sourceOrbitRight,
                    ref sourceOrbitUp,
                    out reason))
            {
                return AbortCameraHandoff(handoff, "invalid Planet orbit basis: " + reason);
            }
            if (state.PlanetOrientation == PlanetOrientationMode.Free &&
                state.HasPlanetFreeReferenceUpWorld &&
                !IsFinite(state.PlanetFreeReferenceUpWorld))
            {
                return AbortCameraHandoff(handoff, "Planet Free reference Up contains NaN/Infinity");
            }
            if (!IsFinite(GetMapViewPosition(data, state)))
                return AbortCameraHandoff(handoff, "map view position contains NaN/Infinity");

            Vector3 localView;
            Vector3 localRight;
            Vector3 localUp;
            BuildProjection(planet, data, state, out localView, out localRight, out localUp);
            if (!TryNormalizeSeCameraBasis(ref localView, ref localRight, ref localUp, out reason))
                return AbortCameraHandoff(handoff, "Planet Map projection basis is invalid: " + reason);

            Vector3D worldViewD = PlanetLocalDirectionToWorld(planet, localView);
            Vector3D worldRightD = PlanetLocalDirectionToWorld(planet, localRight);
            Vector3D worldUpD = PlanetLocalDirectionToWorld(planet, localUp);
            if (!IsFinite(worldViewD) || !IsFinite(worldRightD) || !IsFinite(worldUpD))
                return AbortCameraHandoff(handoff, "planet-to-world conversion produced NaN/Infinity");

            Vector3 worldView = (Vector3)worldViewD;
            Vector3 worldRight = (Vector3)worldRightD;
            Vector3 worldUp = (Vector3)worldUpD;
            if (!TryNormalizeSeCameraBasis(ref worldView, ref worldRight, ref worldUp, out reason))
                return AbortCameraHandoff(handoff, "world camera basis is invalid: " + reason);

            // Commit transactionally. In SE terms this is Matrix.Right / Matrix.Up /
            // Matrix.Backward, with Forward == Cross(Up, Right).
            state.StarOrbitView = worldView;
            state.StarOrbitRight = worldRight;
            state.StarOrbitUp = worldUp;
            state.Yaw = 0f;
            state.Pitch = 0f;
            state.StarTargetOffsetWorld = Vector3D.Zero;
            return true;
        }

        public float FindStarZoomForFocusedRadius(
            MapData data,
            MapViewState state,
            int canvasWidth,
            int canvasHeight,
            float desiredRadiusPixels)
        {
            PlanetSnapshot focused = ResolvePlanet(data, state);
            if (focused == null || data == null || data.Planets.Count == 0 ||
                focused.Radius <= 0d || state == null)
            {
                return StarMaximumZoom;
            }

            Vector3D cameraTargetWorld = state.StarTargetOffsetWorld + focused.Position;
            double focalLength;
            double farDistance;
            double nearDistance;
            GetStarCameraDistanceRange(
                data,
                state,
                cameraTargetWorld,
                canvasWidth,
                canvasHeight,
                out focalLength,
                out farDistance,
                out nearDistance);

            Vector3 cameraBackward;
            Vector3 cameraRight;
            Vector3 cameraUp;
            BuildStaticProjection(
                state,
                out cameraBackward,
                out cameraRight,
                out cameraUp);
            double focusBackwardOffset = Vector3D.Dot(
                focused.Position - cameraTargetWorld,
                cameraBackward);
            double desired = Math.Max(1d, desiredRadiusPixels);
            double desiredCameraDistance = focusBackwardOffset +
                                           focalLength * focused.Radius / desired;
            if (!IsFinite(desiredCameraDistance) || desiredCameraDistance <= 0d)
                return StarMaximumZoom;

            return CameraDistanceToStarZoom(
                desiredCameraDistance,
                farDistance,
                nearDistance);
        }

        static void GetStarCameraDistanceRange(
            MapData data,
            MapViewState state,
            Vector3D cameraTargetWorld,
            int canvasWidth,
            int canvasHeight,
            out double focalLength,
            out double farDistance,
            out double nearDistance)
        {
            // This matches Matrix.CreatePerspectiveFieldOfView: the supplied FOV
            // is vertical, so focal length is derived from viewport height. Width
            // only participates when fitting the complete system into the frame.
            double viewportHeight = Math.Max(1d, canvasHeight);
            double shortSide = Math.Max(1d, Math.Min(canvasWidth, canvasHeight));
            focalLength = viewportHeight * 0.5d /
                          Math.Tan(StarVerticalFovRadians * 0.5d);

            double targetExtent = 1d;
            double largestRadius = 1d;
            if (data != null)
            {
                for (int i = 0; i < data.Planets.Count; i++)
                {
                    PlanetSnapshot planet = data.Planets[i];
                    double radius = Math.Max(0d, planet.Radius);
                    largestRadius = Math.Max(largestRadius, radius);
                    targetExtent = Math.Max(
                        targetExtent,
                        Vector3D.Distance(cameraTargetWorld, planet.Position) + radius);
                }
            }

            double halfSpanPixels = Math.Max(1d, shortSide * StarSystemHalfSpanFraction);
            farDistance = targetExtent + focalLength * targetExtent / halfSpanPixels;

            PlanetSnapshot focused = ResolvePlanet(data, state);
            double focusRadius = focused == null || focused.Radius <= 0d
                ? largestRadius
                : focused.Radius;
            double maximumFocusedRadiusPixels = Math.Max(
                1d,
                shortSide * StarMaximumFocusedRadiusFraction);
            nearDistance = focalLength * focusRadius /
                           maximumFocusedRadiusPixels;

            // Never allow maximum zoom to place the virtual camera on or inside
            // the focused sphere. The perspective handoff is only valid while the
            // camera is outside the object it is about to hand to Planet Map.
            nearDistance = Math.Max(nearDistance, focusRadius * 1.05d + 1d);
            farDistance = Math.Max(farDistance, nearDistance * 1.01d);
        }

        static double StarZoomToCameraDistance(
            float zoom,
            double farDistance,
            double nearDistance)
        {
            float clampedZoom = MathHelper.Clamp(zoom, StarMinimumZoom, StarMaximumZoom);
            double zoomRange = StarMaximumZoom / (double)StarMinimumZoom;
            double amount = Math.Log(clampedZoom / (double)StarMinimumZoom) /
                            Math.Log(zoomRange);
            amount = Math.Max(0d, Math.Min(1d, amount));

            // Interpolate logarithmically so every multiplicative wheel step moves
            // the camera by a comparable ratio instead of merely changing a scale.
            return Math.Exp(
                Math.Log(farDistance) * (1d - amount) +
                Math.Log(nearDistance) * amount);
        }

        static float CameraDistanceToStarZoom(
            double cameraDistance,
            double farDistance,
            double nearDistance)
        {
            cameraDistance = Math.Max(nearDistance, Math.Min(farDistance, cameraDistance));
            double denominator = Math.Log(nearDistance / farDistance);
            double amount = Math.Abs(denominator) <= 1e-12d
                ? 1d
                : Math.Log(cameraDistance / farDistance) / denominator;
            amount = Math.Max(0d, Math.Min(1d, amount));
            double zoom = StarMinimumZoom *
                          Math.Pow(StarMaximumZoom / (double)StarMinimumZoom, amount);
            return MathHelper.Clamp((float)zoom, StarMinimumZoom, StarMaximumZoom);
        }

        static Vector3 ProjectionVectorInBasis(
            Vector3 value,
            Vector3 baseRight,
            Vector3 baseUp,
            Vector3 baseView)
        {
            return new Vector3(
                Vector3.Dot(value, baseRight),
                Vector3.Dot(value, baseUp),
                Vector3.Dot(value, baseView));
        }

        public bool TryGetSurfacePosition(
            RectangleF mapBounds,
            Vector2 screenPoint,
            MapData data,
            MapViewState state,
            out Vector3D surfacePosition)
        {
            surfacePosition = Vector3D.Zero;
            PlanetSnapshot snapshot = ResolvePlanet(data, state);
            MyPlanet planet = snapshot == null ? null : snapshot.Planet;
            if (planet == null || planet.MarkedForClose)
                return false;

            Vector3 localDirection;
            if (!TryGetSurfaceDirection(
                    mapBounds,
                    screenPoint,
                    planet,
                    data,
                    state,
                    out localDirection))
                return false;

            Vector3D worldDirection = PlanetLocalDirectionToWorld(planet, localDirection);
            if (worldDirection.Normalize() <= 1e-9)
                return false;

            double radius = planet.MaximumRadius > 0d
                ? planet.MaximumRadius
                : planet.AverageRadius;
            Vector3D sample = planet.WorldMatrix.Translation + worldDirection * radius;
            surfacePosition = planet.GetClosestSurfacePointGlobal(sample);
            return IsFinite(surfacePosition);
        }

        public bool TryProjectPlanetSurfacePosition(
            RectangleF mapBounds,
            Vector3D worldPosition,
            MapData data,
            MapViewState state,
            out Vector2 screenPosition)
        {
            screenPosition = mapBounds.Center;
            PlanetSnapshot snapshot = ResolvePlanet(data, state);
            MyPlanet planet = snapshot == null ? null : snapshot.Planet;
            if (planet == null || planet.MarkedForClose ||
                !IsPositionNearPlanetSurface(planet, worldPosition))
            {
                return false;
            }

            Vector3D radialWorld = worldPosition - planet.WorldMatrix.Translation;
            if (radialWorld.Normalize() <= 1e-9)
                return false;

            Vector3 radialLocal = WorldDirectionToPlanetLocal(planet, radialWorld);
            if (radialLocal.Normalize() <= VectorEpsilon)
                return false;

            Vector3 view;
            Vector3 right;
            Vector3 up;
            BuildProjection(planet, data, state, out view, out right, out up);
            float x = Vector3.Dot(radialLocal, right);
            float y = Vector3.Dot(radialLocal, up);
            float z = Vector3.Dot(radialLocal, view);

            // Match the LCD map: markers on the hidden hemisphere remain useful
            // by clamping to the globe limb in their radial screen direction.
            var tangent = new Vector2(x, y);
            float tangentLength = tangent.Length();
            if (z < 0f)
            {
                if (tangentLength <= VectorEpsilon)
                    return false;
                tangent /= tangentLength;
                x = tangent.X;
                y = tangent.Y;
            }

            Vector2 center = mapBounds.Center + new Vector2(
                state.PlanetCenterOffsetNormalized.X * mapBounds.Width,
                state.PlanetCenterOffsetNormalized.Y * mapBounds.Height);
            float displayRadius = Math.Min(mapBounds.Width, mapBounds.Height) * 0.47f *
                                  MathHelper.Clamp(state.Zoom, 0.0001f, 16f);
            screenPosition = new Vector2(
                center.X + x * displayRadius,
                center.Y - y * displayRadius);
            return IsFinite(screenPosition.X) && IsFinite(screenPosition.Y);
        }

        public bool TryProjectStarMapPosition(
            RectangleF mapBounds,
            Vector3D worldPosition,
            MapData data,
            MapViewState state,
            out Vector2 screenPosition)
        {
            screenPosition = mapBounds.Center;
            int width = Math.Max(1, (int)Math.Round(mapBounds.Width));
            int height = Math.Max(1, (int)Math.Round(mapBounds.Height));
            StarCamera camera;
            Vector2 projected;
            double cameraZ;
            if (!TryBuildStarCamera(data, state, width, height, out camera) ||
                !TryProjectPerspectivePoint(
                    worldPosition,
                    camera,
                    out projected,
                    out cameraZ))
            {
                return false;
            }

            screenPosition = new Vector2(
                mapBounds.X + projected.X,
                mapBounds.Y + projected.Y);
            return IsFinite(screenPosition.X) && IsFinite(screenPosition.Y);
        }

        public bool TryProjectStarMapSurfaceMarker(
            RectangleF mapBounds,
            Vector3D worldPosition,
            PlanetSnapshot parentPlanet,
            MapData data,
            MapViewState state,
            out Vector2 screenPosition,
            out float maximumIconSize)
        {
            screenPosition = mapBounds.Center;
            maximumIconSize = 0f;
            if (parentPlanet == null || parentPlanet.Radius <= 0f)
                return false;

            int width = Math.Max(1, (int)Math.Round(mapBounds.Width));
            int height = Math.Max(1, (int)Math.Round(mapBounds.Height));
            StarCamera camera;
            if (!TryBuildStarCamera(data, state, width, height, out camera))
                return false;

            Vector3D surfaceNormal = worldPosition - parentPlanet.Position;
            Vector3D planetToCamera = camera.PositionWorld - parentPlanet.Position;
            if (surfaceNormal.Normalize() <= 1e-9 ||
                planetToCamera.Normalize() <= 1e-9 ||
                Vector3D.Dot(surfaceNormal, planetToCamera) <= 0d)
            {
                return false;
            }

            Vector2 projected;
            double stationCameraZ;
            if (!TryProjectPerspectivePoint(
                    worldPosition,
                    camera,
                    out projected,
                    out stationCameraZ))
            {
                return false;
            }

            double planetCameraZ = Vector3D.Dot(
                parentPlanet.Position - camera.PositionWorld,
                camera.Backward);
            float planetRadius = GetPerspectiveMarkerRadius(
                parentPlanet.Radius,
                -planetCameraZ,
                camera.FocalLengthPixels);
            if (planetRadius <= 0f)
                return false;

            screenPosition = new Vector2(
                mapBounds.X + projected.X,
                mapBounds.Y + projected.Y);
            maximumIconSize = planetRadius * 0.1f;
            return IsFinite(screenPosition.X) && IsFinite(screenPosition.Y) &&
                   IsFinite(maximumIconSize) && maximumIconSize > 0f;
        }

        /// <summary>
        /// Projects the live player position and heading into the full-screen
        /// planetary map without painting either into its generated texture.
        /// </summary>
        public bool TryGetPlanetPlayerOverlay(
            RectangleF mapBounds,
            MapData data,
            MapViewState state,
            MatrixD livePlayerWorldMatrix,
            out Vector2 screenPosition,
            out float clockwiseRotation)
        {
            screenPosition = mapBounds.Center;
            clockwiseRotation = 0f;
            PlanetSnapshot snapshot = ResolvePlanet(data, state);
            MyPlanet planet = snapshot == null ? null : snapshot.Planet;
            if (planet == null || planet.MarkedForClose ||
                !IsPositionNearPlanetSurface(
                    planet,
                    livePlayerWorldMatrix.Translation) ||
                !TryProjectPlanetSurfacePosition(
                    mapBounds,
                    livePlayerWorldMatrix.Translation,
                    data,
                    state,
                    out screenPosition))
            {
                return false;
            }

            Vector3 view;
            Vector3 right;
            Vector3 up;
            BuildProjection(planet, data, state, out view, out right, out up);
            Vector3 localForward = WorldDirectionToPlanetLocal(
                planet,
                livePlayerWorldMatrix.Forward);
            Vector3 projected = localForward -
                                view * Vector3.Dot(localForward, view);
            if (projected.Normalize() <= VectorEpsilon)
                return true;

            float screenX = Vector3.Dot(projected, right);
            float screenUp = Vector3.Dot(projected, up);
            if (!IsFinite(screenX) || !IsFinite(screenUp) ||
                screenX * screenX + screenUp * screenUp <=
                VectorEpsilon * VectorEpsilon)
            {
                return true;
            }

            clockwiseRotation = (float)Math.Atan2(screenX, screenUp);
            return IsFinite(clockwiseRotation);
        }

        public bool TryProjectPlanetMinimapPosition(
            RectangleF mapBounds,
            Vector3D worldPosition,
            MapData data,
            MapViewState state,
            float zoom,
            out Vector2 screenPosition)
        {
            screenPosition = mapBounds.Center;
            PlanetSnapshot snapshot = ResolvePlanet(data, state);
            MyPlanet planet = snapshot == null ? null : snapshot.Planet;
            if (planet == null || planet.MarkedForClose ||
                mapBounds.Width <= 0f || mapBounds.Height <= 0f ||
                !IsPositionNearPlanetSurface(planet, worldPosition))
            {
                return false;
            }

            Vector3 radial = WorldDirectionToPlanetLocal(
                planet,
                worldPosition - planet.WorldMatrix.Translation);
            if (radial.Normalize() <= VectorEpsilon)
                return false;

            Vector3 centerDirection;
            Vector3 mapRight;
            Vector3 mapUp;
            if (!TryBuildPlanetMinimapBasis(
                    planet,
                    data,
                    state,
                    out centerDirection,
                    out mapRight,
                    out mapUp))
            {
                return false;
            }

            float depth = Vector3.Dot(radial, centerDirection);
            if (depth <= VectorEpsilon)
                return false;

            float halfTangentHeight = 0.18f / MathHelper.Clamp(zoom, 0.25f, 8f);
            float pixelsPerTangentUnit = mapBounds.Height / (halfTangentHeight * 2f);
            screenPosition = new Vector2(
                mapBounds.Center.X + Vector3.Dot(radial, mapRight) / depth * pixelsPerTangentUnit,
                mapBounds.Center.Y - Vector3.Dot(radial, mapUp) / depth * pixelsPerTangentUnit);
            return IsFinite(screenPosition.X) && IsFinite(screenPosition.Y);
        }

        /// <summary>
        /// Projects a live player heading into the same tangent basis used by
        /// the current minimap texture. The returned angle is clockwise from
        /// screen-up and can be used directly by the screen-space renderer.
        /// </summary>
        public bool TryGetPlanetMinimapPlayerRotation(
            MapData data,
            MapViewState state,
            MatrixD playerWorldMatrix,
            out float clockwiseRotation)
        {
            clockwiseRotation = 0f;
            if (data == null)
                return false;

            PlanetSnapshot snapshot = ResolvePlanet(data, state);
            MyPlanet planet = snapshot == null ? null : snapshot.Planet;
            if (planet == null || planet.MarkedForClose)
                return false;

            Vector3 centerDirection;
            Vector3 mapRight;
            Vector3 mapUp;
            if (!TryBuildPlanetMinimapBasis(
                    planet,
                    data,
                    state,
                    out centerDirection,
                    out mapRight,
                    out mapUp))
            {
                return false;
            }

            Vector3 localForward = WorldDirectionToPlanetLocal(
                planet,
                playerWorldMatrix.Forward);
            Vector3 projected = localForward -
                                centerDirection * Vector3.Dot(localForward, centerDirection);
            if (projected.Normalize() <= VectorEpsilon)
                return false;

            float screenX = Vector3.Dot(projected, mapRight);
            float screenUp = Vector3.Dot(projected, mapUp);
            if (!IsFinite(screenX) || !IsFinite(screenUp) ||
                screenX * screenX + screenUp * screenUp <= VectorEpsilon * VectorEpsilon)
            {
                return false;
            }

            clockwiseRotation = (float)Math.Atan2(screenX, screenUp);
            return IsFinite(clockwiseRotation);
        }

        void EnsureRequested(PlanetSnapshot planet, CartographyLayer layer, int faceSide)
        {
            if (planet == null || planet.Planet == null || planet.Planet.MarkedForClose)
                return;

            var key = new CubemapKey { PlanetId = planet.EntityId, Layer = layer };
            PlanetColorCubemap loaded;
            bool needsWaterOverlay = WaterModAPI.Registered &&
                                     WaterModAPI.HasWater(planet.Planet);
            if (_cubemaps.TryGetValue(key, out loaded) &&
                loaded != null && loaded.SatisfiesFaceSide(faceSide) &&
                (!needsWaterOverlay || loaded.WaterOverlay != null))
            {
                return;
            }

            PendingRequest pending;
            if (_pending.TryGetValue(key, out pending))
            {
                if (GetFaceSideDetailRank(pending.FaceSide) >= GetFaceSideDetailRank(faceSide))
                    return;
                pending.Ticket.Cancel();
                _pending.Remove(key);
            }

            var request = new CartographyRequest
            {
                PlanetEntityId = planet.EntityId,
                PlanetGeneratorSubtype = planet.Planet.Generator == null
                    ? null
                    : planet.Planet.Generator.Id.SubtypeName,
                PlanetRadiusMeters = planet.Radius,
                Projection = CartographyProjection.CubemapFaces,
                Layer = layer,
                MaximumFaceSide = faceSide,
                ReturnColorCubemap = true
            };

            PlanetColorCubemap cached;
            string cachedFailure;
            if (_cartography.TryGetCachedColorCubemap(request, out cached, out cachedFailure))
            {
                if (cached != null)
                    StoreCubemap(key, cached);
                else if (!string.IsNullOrWhiteSpace(cachedFailure))
                    Error = cachedFailure;
                return;
            }

            int version;
            _versions.TryGetValue(key, out version);
            version++;
            _versions[key] = version;

            try
            {
                CartographyTicket ticket = _cartography.RequestMap(
                    request,
                    result => OnRequestCompleted(key, version, result));
                _pending[key] = new PendingRequest
                {
                    FaceSide = faceSide,
                    Version = version,
                    Ticket = ticket
                };
            }
            catch (Exception error)
            {
                Error = error.Message;
                RaiseChanged();
            }
        }

        static int GetFaceSideDetailRank(int faceSide)
        {
            return faceSide <= 0 ? int.MaxValue : faceSide;
        }

        void OnColorCubemapCached(CartographyColorCubemapCachedEvent cached)
        {
            if (_disposed || cached == null || cached.ColorCubemap == null)
                return;

            StoreCubemap(
                new CubemapKey
                {
                    PlanetId = cached.PlanetEntityId,
                    Layer = cached.Layer
                },
                cached.ColorCubemap);
        }

        void OnPlanetInvalidated(CartographyPlanetInvalidatedEvent invalidated)
        {
            if (_disposed || invalidated == null)
                return;

            var loadedKeys = new List<CubemapKey>();
            foreach (CubemapKey key in _cubemaps.Keys)
            {
                if (key.PlanetId == invalidated.PlanetEntityId)
                    loadedKeys.Add(key);
            }
            for (int i = 0; i < loadedKeys.Count; i++)
                _cubemaps.Remove(loadedKeys[i]);

            var pendingKeys = new List<CubemapKey>();
            foreach (KeyValuePair<CubemapKey, PendingRequest> pair in _pending)
            {
                if (pair.Key.PlanetId != invalidated.PlanetEntityId)
                    continue;
                if (pair.Value.Ticket != null)
                    pair.Value.Ticket.Cancel();
                pendingKeys.Add(pair.Key);
            }
            for (int i = 0; i < pendingKeys.Count; i++)
            {
                _pending.Remove(pendingKeys[i]);
                _versions.Remove(pendingKeys[i]);
            }

            RaiseChanged();
        }

        void OnRequestCompleted(CubemapKey key, int version, CartographyResult result)
        {
            if (_disposed)
                return;

            int currentVersion;
            if (!_versions.TryGetValue(key, out currentVersion) || currentVersion != version)
                return;

            _pending.Remove(key);
            if (result == null || !result.Success || result.ColorCubemap == null)
            {
                if (result != null && !result.Cancelled)
                    Error = result.Error;
                RaiseChanged();
                return;
            }

            Error = null;
            StoreCubemap(key, result.ColorCubemap);
        }

        void StoreCubemap(CubemapKey key, PlanetColorCubemap cubemap)
        {
            PlanetColorCubemap current;
            if (_cubemaps.TryGetValue(key, out current) &&
                current != null && current.DetailRank >= cubemap.DetailRank &&
                !(current.WaterOverlay == null && cubemap.WaterOverlay != null))
            {
                return;
            }

            _cubemaps[key] = cubemap;
            RaiseChanged();
        }

        void RenderStarMap(PixelCanvas canvas, MapData data, MapViewState state)
        {
            _starMapLabels.Clear();
            MapRenderFrame frame = _preparingFrame;
            bool waterVisible = state.WaterVisible;
            StarCamera camera;
            if (!TryBuildStarCamera(data, state, canvas.Width, canvas.Height, out camera))
                return;

            AddSkyboxBackground(state, -camera.Backward, camera.Right, camera.Up,
                (float)camera.FocalLengthPixels, camera.ScreenCenterX, camera.ScreenCenterY);
            int count = data.Planets.Count;
            if (count == 0)
                return;

            var positions = new Vector3D[count];
            var displayPositions = new Vector3D[count];
            var parents = new int[count];
            var orbitDistances = new double[count];
            var orbitNormals = new Vector3D[count];
            for (int i = 0; i < count; i++)
            {
                positions[i] = data.Planets[i].Position;
                parents[i] = -1;
                orbitNormals[i] = Vector3D.Up;
            }

            for (int i = 0; i < count; i++)
            {
                int parent = -1;
                double bestDistance = double.MaxValue;
                for (int j = 0; j < count; j++)
                {
                    if (i == j || data.Planets[j].Radius <= data.Planets[i].Radius)
                        continue;
                    double distance = Vector3D.Distance(positions[i], positions[j]);
                    if (distance <= 300000d && distance < bestDistance)
                    {
                        parent = j;
                        bestDistance = distance;
                    }
                }
                parents[i] = parent;
                Vector3D center = parent >= 0 ? positions[parent] : Vector3D.Zero;
                orbitDistances[i] = Vector3D.Distance(positions[i], center);
            }

            var order = new List<int>(count);
            for (int i = 0; i < count; i++)
                order.Add(i);
            order.Sort((a, b) => data.Planets[b].Radius.CompareTo(data.Planets[a].Radius));

            // Keep all orbital positions in physical world metres. Zoom no longer
            // rescales these coordinates; it changes the Star camera distance.
            for (int i = 0; i < count; i++)
            {
                // Keep the Star Map in physical world scale at all times. There is
                // no moon-distance expansion and no readability radius multiplier.
                displayPositions[i] = positions[i];
            }
            for (int orderIndex = 0; orderIndex < order.Count; orderIndex++)
            {
                int i = order[orderIndex];
                int parent = parents[i];
                Vector3D center = parent >= 0 ? displayPositions[parent] : Vector3D.Zero;
                orbitNormals[i] = BuildOrbitPlaneNormal(
                    displayPositions[i] - center,
                    parent >= 0 ? orbitNormals[parent] : Vector3D.Up);
            }

            // Star Map is a real perspective camera. The focus point is fixed in
            // world space; zoom moves the camera along its SE Backward (+Z) axis.
            // Apparent planet/orbit size therefore follows camera-space distance.
            double focalLength = camera.FocalLengthPixels;
            double cameraDistance = camera.DistanceToTarget;
            double cameraNearDepth = camera.NearDepth;
            double cameraExtent = GetStarTargetExtent(data, camera.TargetWorld);

            Vector3 panRightWorld = camera.Right;
            Vector3 panUpWorld = camera.Up;
            _starPanScreenRightWorld = new Vector3D(
                panRightWorld.X, panRightWorld.Y, panRightWorld.Z);
            _starPanScreenUpWorld = new Vector3D(
                panUpWorld.X, panUpWorld.Y, panUpWorld.Z);
            _starPanWorldUnitsPerPixel = focalLength > 1e-12d
                ? cameraDistance / focalLength
                : 0d;
            _starPanProjectionValid = _starPanWorldUnitsPerPixel > 0d &&
                                      IsFinite(_starPanWorldUnitsPerPixel);

            var planetScreenPositions = new Vector2[count];
            var planetCameraZs = new double[count];
            var planetMarkerRadii = new float[count];
            var planetVisible = new bool[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 projected;
                double cameraSpaceZ;
                bool centerVisible = TryProjectPerspectivePoint(
                    displayPositions[i],
                    camera,
                    out projected,
                    out cameraSpaceZ);
                planetScreenPositions[i] = projected;
                planetCameraZs[i] = cameraSpaceZ;

                double centerDepth = -cameraSpaceZ;
                planetMarkerRadii[i] = GetPerspectiveMarkerRadius(
                    data.Planets[i].Radius,
                    centerDepth,
                    focalLength);
                planetVisible[i] = centerVisible &&
                    centerDepth > data.Planets[i].Radius + cameraNearDepth &&
                    planetMarkerRadii[i] > 0f;
            }

            // Z-buffer values use SE camera-space Z in world metres. Visible points
            // are negative; larger (less-negative) Z is nearer, matching ViewMatrix.
            order.Sort((a, b) =>
            {
                int depth = planetCameraZs[a].CompareTo(planetCameraZs[b]);
                if (depth != 0)
                    return depth;
                return planetMarkerRadii[a].CompareTo(planetMarkerRadii[b]);
            });

            frame.Initialize = () => frame.DepthBuffer = PrepareStarDepthBuffer(canvas.Width, canvas.Height);
            _starOrbitSegments.Clear();
            PlanetSnapshot currentPlanet = data.FindPlayerPlanet();
            long currentPlanetId = currentPlanet == null ? 0L : currentPlanet.EntityId;
            long focusedPlanetId = state.FocusedPlanetId;
            if (state.OrbitalLinesVisible)
            {
                double nearestStrokeDistance = Math.Max(cameraNearDepth, cameraDistance - cameraExtent);
                double farthestStrokeDistance = cameraDistance + cameraExtent;

                for (int i = 0; i < count; i++)
                {
                    if (orbitDistances[i] < 100000d)
                        continue;
                    int parent = parents[i];
                    Vector3D ringCenter = parent >= 0 ? displayPositions[parent] : Vector3D.Zero;
                    Vector3D radial = displayPositions[i] - ringCenter;
                    if (radial.Normalize() <= 1e-9)
                        continue;
                    Vector3D tangent = Vector3D.Cross(orbitNormals[i], radial);
                    if (tangent.Normalize() <= 1e-9)
                        continue;

                    long orbitPlanetId = data.Planets[i].EntityId;
                    byte orbitStyle = orbitPlanetId == currentPlanetId
                        ? CurrentOrbitStyle
                        : orbitPlanetId == focusedPlanetId
                            ? FocusedOrbitStyle
                            : DefaultOrbitStyle;

                    BuildDepthTestedOrbitRing(
                        _starOrbitSegments,
                        ringCenter,
                        radial,
                        tangent,
                        orbitDistances[i],
                        camera,
                        nearestStrokeDistance,
                        farthestStrokeDistance,
                        orbitStyle);
                }
            }

            if (state.HasFixedStarCamera)
            {
                for (int i = 0; i < count; i++)
                {
                    PlanetSnapshot planet = data.Planets[i];
                    if (planetVisible[i] || planet.Planet == null ||
                        -planetCameraZs[i] + planet.Radius <= cameraNearDepth)
                        continue;
                    PlanetColorCubemap nearCubemap = GetCubemap(planet.EntityId, state.Layer);
                    MatrixD inverseWorld = MatrixD.Transpose(planet.Planet.WorldMatrix.GetOrientation());
                    // The worker uses these value fields only, never the live entity.
                    var capturedPlanet = new PlanetSnapshot { Position = planet.Position, Radius = planet.Radius };
                    frame.Commands.Add((target, first, end) => RaycastNearPlanet(target, camera, capturedPlanet,
                        nearCubemap, waterVisible, frame.DepthBuffer, inverseWorld, first, end));
                }
            }

            // Render opaque billboards far-to-near. Their screen radii come from
            // camera-forward depth and their depth spheres remain in world metres.
            for (int orderIndex = 0; orderIndex < order.Count; orderIndex++)
            {
                int i = order[orderIndex];
                if (!planetVisible[i])
                    continue;

                PlanetSnapshot planet = data.Planets[i];
                Vector2 projected = planetScreenPositions[i];
                int radius = (int)Math.Round(planetMarkerRadii[i]);
                if (radius <= 0)
                    continue;
                PlanetColorCubemap cubemap = GetCubemap(
                    planet.EntityId,
                    state.Layer);

                Vector3D planetToCameraWorld = camera.PositionWorld - planet.Position;
                if (planetToCameraWorld.Normalize() <= 1e-9)
                    continue;

                Vector3 localView = WorldDirectionToPlanetLocal(
                    planet.Planet,
                    planetToCameraWorld);
                Vector3 localRight = WorldDirectionToPlanetLocal(
                    planet.Planet,
                    new Vector3D(camera.Right.X, camera.Right.Y, camera.Right.Z));
                Vector3 localUp = WorldDirectionToPlanetLocal(
                    planet.Planet,
                    new Vector3D(camera.Up.X, camera.Up.Y, camera.Up.Z));
                NormalizeProjection(ref localView, ref localRight, ref localUp);
                Vector3 localCameraBackward = WorldDirectionToPlanetLocal(
                    planet.Planet,
                    new Vector3D(camera.Backward.X, camera.Backward.Y, camera.Backward.Z));
                if (localCameraBackward.Normalize() <= VectorEpsilon)
                    localCameraBackward = localView;

                float centerDepth = (float)planetCameraZs[i];
                float depthRadius = planet.Radius;
                frame.Commands.Add((target, first, end) => DrawGlobe(
                    target,
                    (int)projected.X,
                    (int)projected.Y,
                    radius,
                    cubemap,
                    localView,
                    localRight,
                    localUp,
                    waterVisible,
                    frame.DepthBuffer,
                    centerDepth,
                    depthRadius,
                    true,
                    (float)-cameraNearDepth,
                    localCameraBackward, first, end));
                if (planet.EntityId == currentPlanetId)
                {
                    frame.Commands.Add((target, first, end) => DrawCircleRows(target,
                        (int)projected.X, (int)projected.Y, radius + 3, CurrentPlanetIndicatorColor, first, end));
                }
                else if (planet.EntityId == focusedPlanetId)
                {
                    frame.Commands.Add((target, first, end) => DrawCircleRows(target,
                        (int)projected.X, (int)projected.Y, radius + 3, FocusedPlanetIndicatorColor, first, end));
                }
                _starMapLabels.Add(new StarMapLabel
                {
                    PlanetId = planet.EntityId,
                    Text = planet.Name,
                    NormalizedPosition = new Vector2(
                        projected.X / canvas.Width,
                        projected.Y / canvas.Height),
                    NormalizedRadius = radius / (float)canvas.Width
                });
            }

            if (state.OrbitalLinesVisible)
                frame.Finish = target => DrawDepthTestedOrbitLines(
                    target,
                    frame.DepthBuffer,
                    _starOrbitSegments);
        }

        void RenderPlanetaryMap(PixelCanvas canvas, MapData data, MapViewState state)
        {
            MapRenderFrame frame = _preparingFrame;
            PlanetSnapshot planet = ResolvePlanet(data, state);
            if (planet == null)
                return;

            PlanetColorCubemap cubemap = GetCubemap(planet.EntityId, state.Layer);
            int centerX = canvas.Width / 2 +
                (int)Math.Round(state.PlanetCenterOffsetNormalized.X * canvas.Width);
            int centerY = canvas.Height / 2 +
                (int)Math.Round(state.PlanetCenterOffsetNormalized.Y * canvas.Height);
            // Permit a temporary sub-minimum visual zoom during Star->Planet
            // transition. Normal Planet Map wheel input still clamps at 0.25.
            float visualZoom = MathHelper.Clamp(state.Zoom, 0.0001f, 16f);
            int radius = (int)(Math.Min(canvas.Width, canvas.Height) * 0.47f * visualZoom);

            Vector3 view;
            Vector3 right;
            Vector3 up;
            BuildProjection(planet.Planet, data, state, out view, out right, out up);
            // The orthographic globe uses the map's 60-degree vertical sky FOV.
            // Star Chart instead supplies its actual orbit/HUD camera projection.
            AddSkyboxBackground(state,
                (Vector3)PlanetLocalDirectionToWorld(planet.Planet, -view),
                (Vector3)PlanetLocalDirectionToWorld(planet.Planet, right),
                (Vector3)PlanetLocalDirectionToWorld(planet.Planet, up),
                (float)(canvas.Height * .5d / Math.Tan(StarVerticalFovRadians * .5d)),
                canvas.Width * .5f, canvas.Height * .5f);
            bool waterVisible = state.WaterVisible;
            frame.Commands.Add((target, first, end) => DrawGlobe(target, centerX, centerY, radius,
                cubemap, view, right, up, waterVisible, firstRow: first, endRow: end));
            if (state.RoadsVisible)
                AddRoadLines(frame, planet, view, right, up, centerX, centerY, radius);
            if (state.Zoom <= 1.1f)
            {
                PlanetSnapshot currentPlanet = data.FindPlayerPlanet();
                Color indicatorColor = currentPlanet != null &&
                                       currentPlanet.EntityId == planet.EntityId
                    ? CurrentPlanetIndicatorColor
                    : FocusedPlanetIndicatorColor;
                frame.Commands.Add((target, first, end) => DrawCircleRows(target,
                    centerX, centerY, radius + 3, indicatorColor, first, end));
            }

        }

        // Built-road overlay style. Opacities are 0-255 and apply on top of the
        // zoom fade below.
        static readonly Color RoadFillColor = new Color(0xaa, 0xb4, 0xc8);
        const byte RoadFillOpacity = 127;
        static readonly Color RoadBorderColor = new Color(0x5e, 0x63, 0x6e);
        const byte RoadBorderOpacity = 255;
        const int RoadBorderPixels = 2;
        // Roads narrower on screen than RoadFadeStartPixels fade out and vanish
        // at RoadFadeEndPixels, so zooming out doesn't leave a web of bordered
        // hairlines. The fade is logarithmic in width, i.e. linear in zoom
        // steps: half opacity at the geometric mean (0.5px for 2px..0.125px).
        const float RoadFadeStartPixels = 2f;
        const float RoadFadeEndPixels = 0.125f;
        // Mask values: high byte is the class (fill beats border), low byte the
        // road's fade, so a plain max keeps the strongest covering road.
        const int RoadMaskBorder = 1 << 8;
        const int RoadMaskFill = 2 << 8;

        /// <summary>
        /// Built roads from the Voxel Cubemap API, drawn over the globe at their
        /// physical width (one-pixel floor) with a border. Segments only; no
        /// nodes, view only. Projection happens here; the strokes rasterize in
        /// the row bands into a coverage mask that is blended once, so
        /// overlapping segments and roads don't stack their translucency.
        /// </summary>
        void AddRoadLines(MapRenderFrame frame, PlanetSnapshot planet, Vector3 view,
            Vector3 right, Vector3 up, int centerX, int centerY, int radius)
        {
            if (_cartography == null || radius <= 0 || planet.Radius <= 0f)
                return;
            IReadOnlyList<PlanetRoadLine> roads = _cartography.GetPlanetRoads(planet.Planet);
            if (roads.Count == 0)
                return;

            Vector3D viewD = view;
            Vector3D rightD = right;
            Vector3D upD = up;
            var points = new List<Vector2[]>(roads.Count);
            var visibility = new List<bool[]>(roads.Count);
            var fillRadii = new List<double>(roads.Count);
            var fades = new List<byte>(roads.Count);
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            double maxOuterRadius = 0;
            for (int r = 0; r < roads.Count; r++)
            {
                PlanetRoadLine road = roads[r];
                // Unknown widths draw as a full-strength one-pixel road.
                double pixelWidth = road.WidthMeters <= 0.0
                    ? RoadFadeStartPixels
                    : road.WidthMeters * radius / planet.Radius;
                double fade = Math.Log(Math.Max(1e-9, pixelWidth) / RoadFadeEndPixels) /
                              Math.Log(RoadFadeStartPixels / RoadFadeEndPixels);
                byte fadeByte = (byte)Math.Round(255 * Math.Max(0, Math.Min(1, fade)));
                if (fadeByte == 0)
                    continue;

                int samples = (int)Math.Max(16, Math.Min(1024, Math.Ceiling(road.Arc * radius * 2)));
                var pixels = new Vector2[samples + 1];
                var visible = new bool[samples + 1];
                bool any = false;
                for (int i = 0; i <= samples; i++)
                {
                    Vector3D direction = road.Evaluate(i / (double)samples);
                    // Only the hemisphere facing the camera is drawn.
                    if (Vector3D.Dot(direction, viewD) < 0.0)
                        continue;
                    var pixel = new Vector2(
                        (float)(centerX + Vector3D.Dot(direction, rightD) * radius),
                        (float)(centerY - Vector3D.Dot(direction, upD) * radius));
                    pixels[i] = pixel;
                    visible[i] = true;
                    minY = Math.Min(minY, pixel.Y);
                    maxY = Math.Max(maxY, pixel.Y);
                    any = true;
                }
                if (!any)
                    continue;
                int width = (int)Math.Max(1, Math.Min(4096, Math.Round(pixelWidth)));
                double fillRadius = Math.Max(Math.Sqrt(0.5), width / 2.0);
                points.Add(pixels);
                visibility.Add(visible);
                fillRadii.Add(fillRadius);
                fades.Add(fadeByte);
                maxOuterRadius = Math.Max(maxOuterRadius, fillRadius + RoadBorderPixels);
            }
            if (points.Count == 0)
                return;

            int roadTop = (int)Math.Floor(minY - maxOuterRadius);
            int roadBottom = (int)Math.Ceiling(maxY + maxOuterRadius) + 1;
            frame.Commands.Add((target, first, end) =>
            {
                int top = Math.Max(first, Math.Max(0, roadTop));
                int bottom = Math.Min(end, Math.Min(target.Height, roadBottom));
                if (top >= bottom)
                    return;
                int width = target.Width;
                var mask = new ushort[(bottom - top) * width];
                // Each segment marks its outer capsule as border and its inner
                // capsule as fill; the max keeps fill over any border.
                for (int r = 0; r < points.Count; r++)
                {
                    Vector2[] pixels = points[r];
                    bool[] visible = visibility[r];
                    double fillRadius = fillRadii[r];
                    ushort border = (ushort)(RoadMaskBorder | fades[r]);
                    ushort fill = (ushort)(RoadMaskFill | fades[r]);
                    for (int i = 1; i < pixels.Length; i++)
                    {
                        if (!visible[i - 1] || !visible[i])
                            continue;
                        if (RoadBorderPixels > 0)
                            ScreenSpaceRoadStroke.Mark(mask, width, top, bottom,
                                pixels[i - 1], pixels[i], fillRadius + RoadBorderPixels, border);
                        ScreenSpaceRoadStroke.Mark(mask, width, top, bottom,
                            pixels[i - 1], pixels[i], fillRadius, fill);
                    }
                }

                Color fillColor = RoadFillColor;
                fillColor.A = RoadFillOpacity;
                Color borderColor = RoadBorderColor;
                borderColor.A = RoadBorderOpacity;
                for (int y = top; y < bottom; y++)
                {
                    int row = (y - top) * width;
                    for (int x = 0; x < width; x++)
                    {
                        int value = mask[row + x];
                        if (value == 0)
                            continue;
                        float coverage = (value & 0xff) / 255f;
                        target.BlendPixel(x, y,
                            value >= RoadMaskFill ? fillColor : borderColor, coverage);
                    }
                }
            });
        }

        void AddSkyboxBackground(MapViewState state, Vector3 forwardWorld,
            Vector3 rightWorld, Vector3 upWorld, float focalLength, float centerX, float centerY)
        {
            if (!state.RenderSkybox || _cartography == null || _cartography.Skybox.Cubemap == null)
                return;
            var skybox = _cartography.Skybox.Cubemap;
            Matrix orientation = _cartography.Skybox.WorldToCube;
            Vector3 forward = SessionSkybox.CubeDirection(forwardWorld, orientation);
            Vector3 right = SessionSkybox.CubeDirection(rightWorld, orientation);
            Vector3 up = SessionSkybox.CubeDirection(upWorld, orientation);
            // First color pass, with no depth writes: every planet and orbit line
            // remains in front of this infinite-distance background.
            _preparingFrame.Commands.Add((target, first, end) =>
            {
                for (int y = first; y < end; y++)
                {
                    Vector3 row = forward + up * ((centerY - y - .5f) / focalLength);
                    for (int x = 0; x < target.Width; x++)
                        target.SetPixel(x, y, skybox.Sample(row + right * ((x + .5f - centerX) / focalLength)));
                }
            });
        }

        static void DrawGlobe(
            PixelCanvas canvas,
            int centerX,
            int centerY,
            int radius,
            PlanetColorCubemap cubemap,
            Vector3 view,
            Vector3 right,
            Vector3 up,
            bool waterVisible,
            float[] depthBuffer = null,
            float centerDepth = 0f,
            float depthRadius = 0f,
            bool depthTestExisting = true,
            float cameraNearZ = float.PositiveInfinity,
            Vector3? cameraBackwardLocal = null,
            int firstRow = 0,
            int endRow = int.MaxValue)
        {
            if (radius <= 0)
                return;

            int left = Math.Max(0, centerX - radius);
            int rightEdge = Math.Min(canvas.Width - 1, centerX + radius);
            int top = Math.Max(0, Math.Max(firstRow, centerY - radius));
            int bottom = Math.Min(canvas.Height - 1, Math.Min(endRow - 1, centerY + radius));
            int displaySide = Math.Max(1, radius * 2);
            int mip = cubemap == null ? 0 : cubemap.SelectMipLevel(displaySide);
            int mipResolution = cubemap == null
                ? displaySide
                : Math.Max(1, cubemap.GetMipResolution(mip));

            // Match the LCD globe renderer: never create more independent globe
            // samples than the selected cubemap mip can actually provide. The
            // standalone texture may be 1080p/4K, but a 512x512 face should still
            // look like a 512-sample logical globe stretched to the display size.
            // Keeping the circle hit/silhouette test at display resolution avoids
            // a jagged outer mask while removing the false extra detail that bends
            // and stretches around the limb when the output texture outruns the mip.
            int logicalSide = Math.Max(1, Math.Min(displaySide, mipResolution));
            float inverseRadius = 1f / radius;
            for (int y = top; y <= bottom; y++)
            {
                float displaySphereY = -(y - centerY) * inverseRadius;
                for (int x = left; x <= rightEdge; x++)
                {
                    float displaySphereX = (x - centerX) * inverseRadius;
                    float displayRadiusSquared =
                        displaySphereX * displaySphereX + displaySphereY * displaySphereY;
                    if (displayRadiusSquared > 1f)
                        continue;

                    int depthIndex = y * canvas.Width + x;
                    float displaySphereZ = (float)Math.Sqrt(
                        Math.Max(0f, 1f - displayRadiusSquared));
                    Vector3 displaySurfaceDirection =
                        view * displaySphereZ +
                        right * displaySphereX +
                        up * displaySphereY;
                    float surfaceDepth = centerDepth;
                    if (cameraBackwardLocal.HasValue && depthRadius != 0f)
                    {
                        // The visible hemisphere faces the camera position, while
                        // camera-space Z is measured along one shared Backward axis.
                        // Projecting the physical surface offset onto that axis keeps
                        // off-axis billboards and orbit lines in the same depth space.
                        surfaceDepth += Vector3.Dot(
                            displaySurfaceDirection,
                            cameraBackwardLocal.Value) * depthRadius;
                    }
                    // SE view space looks down -Z. Anything on or behind the near
                    // plane (a larger camera-space Z) is clipped before it can write
                    // color or occlusion depth. Planet Map leaves cameraNearZ at +inf.
                    if (surfaceDepth >= cameraNearZ)
                        continue;

                    // Star-map depth is physical SE camera-space Z in metres. With
                    // perspective radii there is no fake readability scale, so planets
                    // can depth-test against each other and orbit lines using the same
                    // camera-space convention.
                    if (depthBuffer != null && depthTestExisting &&
                        surfaceDepth <= depthBuffer[depthIndex])
                    {
                        continue;
                    }

                    float sphereX = QuantizeSphereCoordinate(displaySphereX, logicalSide);
                    float sphereY = QuantizeSphereCoordinate(displaySphereY, logicalSide);
                    float radiusSquared = sphereX * sphereX + sphereY * sphereY;
                    if (radiusSquared > 1f)
                    {
                        // A display pixel can be inside the smooth circle while its
                        // logical texel centre lies just beyond the unit disc. Clamp
                        // that texel to the tangent instead of punching edge holes.
                        float inverseLength = 0.999999f / (float)Math.Sqrt(radiusSquared);
                        sphereX *= inverseLength;
                        sphereY *= inverseLength;
                        radiusSquared = sphereX * sphereX + sphereY * sphereY;
                    }

                    float sphereZ = (float)Math.Sqrt(Math.Max(0f, 1f - radiusSquared));
                    Vector3 direction = view * sphereZ + right * sphereX + up * sphereY;
                    if (direction.Normalize() <= VectorEpsilon)
                        direction = view;

                    Color color = cubemap == null
                        ? new Color(82, 86, 89)
                        : cubemap.Sample(direction, mip);
                    if (cubemap != null && waterVisible && cubemap.WaterOverlay != null)
                    {
                        Color water = cubemap.WaterOverlay.Sample(direction, mip);
                        if (water.A != 0)
                            color = CompositeWaterTint(color, water.A);
                    }
                    canvas.SetPixel(x, y, color);
                    if (depthBuffer != null)
                        depthBuffer[depthIndex] = surfaceDepth;
                }
            }
        }

        static void DrawCircleRows(PixelCanvas canvas, int centerX, int centerY, int radius,
            Color color, int firstRow, int endRow)
        {
            int outer = radius * radius;
            int inner = Math.Max(0, radius - 2);
            inner *= inner;
            int left = Math.Max(0, centerX - radius);
            int right = Math.Min(canvas.Width - 1, centerX + radius);
            int top = Math.Max(firstRow, centerY - radius);
            int bottom = Math.Min(endRow - 1, centerY + radius);
            for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                int dx = x - centerX;
                int dy = y - centerY;
                int distance = dx * dx + dy * dy;
                if (distance <= outer && distance >= inner)
                    canvas.SetPixel(x, y, color);
            }
        }

        static float QuantizeSphereCoordinate(float coordinate, int logicalSide)
        {
            if (logicalSide <= 1)
                return 0f;

            float normalized = MathHelper.Clamp(coordinate * 0.5f + 0.5f, 0f, 1f);
            int cell = Math.Min(
                logicalSide - 1,
                Math.Max(0, (int)Math.Floor(normalized * logicalSide)));
            return ((cell + 0.5f) / logicalSide) * 2f - 1f;
        }

        static bool TryBuildPlanetMinimapBasis(
            MyPlanet planet,
            MapData data,
            MapViewState state,
            out Vector3 centerDirection,
            out Vector3 right,
            out Vector3 up)
        {
            centerDirection = Vector3.Backward;
            right = Vector3.Right;
            up = Vector3.Up;
            if (planet == null || data == null)
                return false;

            centerDirection = WorldDirectionToPlanetLocal(
                planet,
                data.PlayerPosition - planet.WorldMatrix.Translation);
            if (centerDirection.Normalize() <= VectorEpsilon)
                return false;

            Vector3 referenceUp = Vector3.Up;
            if (state != null &&
                state.PlanetOrientation == PlanetOrientationMode.HeadingLocked &&
                data.HasPlayerWorldMatrix)
            {
                referenceUp = WorldDirectionToPlanetLocal(
                    planet,
                    data.PlayerWorldMatrix.Forward);
            }

            up = referenceUp - centerDirection * Vector3.Dot(referenceUp, centerDirection);
            if (up.Normalize() <= VectorEpsilon)
            {
                referenceUp = Math.Abs(Vector3.Dot(centerDirection, Vector3.Forward)) < 0.98f
                    ? Vector3.Forward
                    : Vector3.Right;
                up = referenceUp - centerDirection * Vector3.Dot(referenceUp, centerDirection);
                if (up.Normalize() <= VectorEpsilon)
                    return false;
            }

            right = Vector3.Cross(up, centerDirection);
            if (right.Normalize() <= VectorEpsilon)
                return false;
            up = Vector3.Cross(centerDirection, right);
            return up.Normalize() > VectorEpsilon;
        }

        static bool TryGetSurfaceDirection(
            RectangleF mapBounds,
            Vector2 point,
            MyPlanet planet,
            MapData data,
            MapViewState state,
            out Vector3 localDirection)
        {
            localDirection = Vector3.Zero;
            float displayRadius = Math.Min(mapBounds.Width, mapBounds.Height) * 0.47f *
                                  MathHelper.Clamp(state.Zoom, 0.0001f, 16f);
            if (displayRadius <= 0f)
                return false;

            Vector2 center = mapBounds.Center + new Vector2(
                state.PlanetCenterOffsetNormalized.X * mapBounds.Width,
                state.PlanetCenterOffsetNormalized.Y * mapBounds.Height);
            float sphereX = (point.X - center.X) / displayRadius;
            float sphereY = -(point.Y - center.Y) / displayRadius;
            float radiusSquared = sphereX * sphereX + sphereY * sphereY;
            if (radiusSquared > 1f)
                return false;

            Vector3 view;
            Vector3 right;
            Vector3 up;
            BuildProjection(planet, data, state, out view, out right, out up);
            float sphereZ = (float)Math.Sqrt(Math.Max(0f, 1f - radiusSquared));
            localDirection = view * sphereZ + right * sphereX + up * sphereY;
            return localDirection.Normalize() > VectorEpsilon;
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
                GpsSurfaceTargetToleranceMeters,
                terrainVariation + GpsSurfaceTerrainMarginMeters);
            double tolerance = Math.Max(
                GpsSurfaceMinimumAllowedToleranceMeters,
                Math.Min(
                    desiredTolerance,
                    averageRadius * GpsSurfaceMaximumRadiusRatio));
            double radialDistance = Vector3D.Distance(
                position,
                planet.WorldMatrix.Translation);
            return Math.Abs(radialDistance - averageRadius) <= tolerance;
        }

        static int GetStarMapFaceSide(float magnification)
        {
            int desired = MathHelper.Clamp(
                (int)Math.Ceiling(StarMapFaceSide * Math.Sqrt(Math.Max(1f, magnification))),
                StarMapFaceSide,
                512);
            int side = StarMapFaceSide;
            while (side < desired)
                side *= 2;
            return Math.Min(side, 512);
        }

        static double GetStarTargetExtent(MapData data, Vector3D cameraTargetWorld)
        {
            double extent = 1d;
            if (data == null)
                return extent;

            for (int i = 0; i < data.Planets.Count; i++)
            {
                PlanetSnapshot planet = data.Planets[i];
                extent = Math.Max(
                    extent,
                    Vector3D.Distance(cameraTargetWorld, planet.Position) +
                    Math.Max(0d, planet.Radius));
            }
            return extent;
        }

        static float GetPerspectiveMarkerRadius(
            double planetRadiusMeters,
            double cameraForwardDepth,
            double focalLength)
        {
            if (!IsFinite(planetRadiusMeters) || !IsFinite(cameraForwardDepth) ||
                !IsFinite(focalLength) || planetRadiusMeters <= 0d ||
                cameraForwardDepth <= planetRadiusMeters || focalLength <= 0d)
            {
                return 0f;
            }

            // A screen-facing billboard follows the same perspective division as
            // every other projected point: its scale is based on forward depth.
            double radius = focalLength * planetRadiusMeters / cameraForwardDepth;
            if (!IsFinite(radius) || radius <= 0d || radius > float.MaxValue)
                return 0f;
            return (float)radius;
        }

        static float GetOrbitStrokeWidth(
            double distanceToCamera,
            double nearestDistance,
            double farthestDistance)
        {
            double range = farthestDistance - nearestDistance;
            if (range <= 1e-9d || double.IsNaN(range) || double.IsInfinity(range))
                return MaximumOrbitStrokeWidth;

            float amount = MathHelper.Clamp(
                (float)((distanceToCamera - nearestDistance) / range),
                0f,
                1f);
            return MathHelper.Lerp(
                MaximumOrbitStrokeWidth,
                MinimumOrbitStrokeWidth,
                amount);
        }

        static void BuildStaticProjection(
            MapViewState state,
            out Vector3 view,
            out Vector3 right,
            out Vector3 up)
        {
            // StarOrbit* is target -> camera in real SE world coordinates. The
            // view direction is Matrix.Backward (+Z), so camera Forward is -view.
            view = state.StarOrbitView;
            right = state.StarOrbitRight;
            up = state.StarOrbitUp;
            NormalizeProjection(ref view, ref right, ref up);
        }

        static bool TryBuildStarCamera(
            MapData data,
            MapViewState state,
            int canvasWidth,
            int canvasHeight,
            out StarCamera camera)
        {
            camera = default(StarCamera);
            if (data == null || state == null || canvasWidth <= 0 || canvasHeight <= 0 ||
                !IsFinite(state.Zoom) || state.Zoom <= 0f ||
                !IsFinite(state.StarTargetOffsetWorld))
            {
                return false;
            }

            if (state.HasFixedStarCamera)
                return TryBuildFixedStarCamera(state, canvasWidth, canvasHeight, out camera);

            Vector3 backward;
            Vector3 right;
            Vector3 up;
            BuildStaticProjection(state, out backward, out right, out up);

            Vector3D targetWorld = state.StarTargetOffsetWorld;
            PlanetSnapshot focused = data.FindPlanet(state.FocusedPlanetId);
            if (focused != null)
                targetWorld += focused.Position;

            double focalLength;
            double farDistance;
            double nearDistance;
            GetStarCameraDistanceRange(
                data,
                state,
                targetWorld,
                canvasWidth,
                canvasHeight,
                out focalLength,
                out farDistance,
                out nearDistance);
            double distance = StarZoomToCameraDistance(
                state.Zoom,
                farDistance,
                nearDistance);
            double nearDepth = Math.Max(1d, distance * 1e-6d);

            camera = new StarCamera
            {
                TargetWorld = targetWorld,
                Backward = backward,
                Right = right,
                Up = up,
                FocalLengthPixels = focalLength,
                NearDepth = nearDepth,
                DistanceToTarget = distance,
                ScreenCenterX = canvasWidth / 2,
                ScreenCenterY = canvasHeight / 2
            };
            camera.PositionWorld = targetWorld +
                new Vector3D(backward.X, backward.Y, backward.Z) * distance;
            return IsFinite(camera.PositionWorld) &&
                   IsFinite(camera.FocalLengthPixels) &&
                   IsFinite(camera.NearDepth) &&
                   IsFinite(camera.DistanceToTarget);
        }

        static bool TryBuildFixedStarCamera(MapViewState state, int canvasWidth, int canvasHeight, out StarCamera camera)
        {
            const double fixedTargetDistance = 1000000d;
            MatrixD world = state.FixedStarCamera;
            double halfFov = MathHelper.Clamp(state.FixedStarCameraVerticalFovRadians, 0.001f, 3.1f) * 0.5d;
            camera = new StarCamera
            {
                PositionWorld = world.Translation,
                TargetWorld = world.Translation + world.Forward * fixedTargetDistance,
                Backward = (Vector3)world.Backward,
                Right = (Vector3)world.Right,
                Up = (Vector3)world.Up,
                FocalLengthPixels = canvasHeight * 0.5d / Math.Tan(halfFov),
                NearDepth = 10d,
                DistanceToTarget = fixedTargetDistance,
                ScreenCenterX = canvasWidth / 2,
                ScreenCenterY = canvasHeight / 2
            };
            return IsFinite(camera.PositionWorld) && IsFinite(camera.FocalLengthPixels);
        }

        /// <summary>
        /// Ray casts a planet the fixed camera is close to (too close for a billboard): every pixel whose view ray
        /// hits the sphere samples the cubemap at the hit point and writes its camera-space depth.
        /// </summary>
        static void RaycastNearPlanet(
            PixelCanvas canvas,
            StarCamera camera,
            PlanetSnapshot planet,
            PlanetColorCubemap cubemap,
            bool waterVisible,
            float[] depthBuffer,
            MatrixD planetInverseWorld,
            int firstRow,
            int endRow)
        {
            Vector3D offset = camera.PositionWorld - planet.Position;
            double distance = offset.Length();
            double radius = Math.Min(planet.Radius, distance * 0.999d);
            if (radius <= 0d)
                return;
            double c = offset.LengthSquared() - radius * radius;
            var forward = new Vector3D(-camera.Backward.X, -camera.Backward.Y, -camera.Backward.Z);
            var right = new Vector3D(camera.Right.X, camera.Right.Y, camera.Right.Z);
            var up = new Vector3D(camera.Up.X, camera.Up.Y, camera.Up.Z);
            double focal = camera.FocalLengthPixels;
            for (int y = firstRow; y < Math.Min(canvas.Height, endRow); y++)
            {
                double screenY = camera.ScreenCenterY - (y + 0.5d);
                for (int x = 0; x < canvas.Width; x++)
                {
                    Vector3D ray = forward * focal + right * (x + 0.5d - camera.ScreenCenterX) + up * screenY;
                    ray.Normalize();
                    double b = Vector3D.Dot(offset, ray);
                    double discriminant = b * b - c;
                    if (discriminant < 0d)
                        continue;
                    double t = -b - Math.Sqrt(discriminant);
                    if (t <= camera.NearDepth)
                        continue;
                    float depth = (float)(-t * Vector3D.Dot(ray, forward));
                    int index = y * canvas.Width + x;
                    if (depth <= depthBuffer[index])
                        continue;

                    Vector3 local = (Vector3)Vector3D.TransformNormal(offset + ray * t, planetInverseWorld);
                    if (local.Normalize() <= VectorEpsilon)
                        continue;
                    Color color;
                    if (cubemap == null)
                        color = new Color(82, 86, 89);
                    else
                    {
                        int mip = cubemap.SelectMipLevel((float)(radius * focal / t));
                        color = cubemap.Sample(local, mip);
                        if (waterVisible && cubemap.WaterOverlay != null)
                        {
                            Color water = cubemap.WaterOverlay.Sample(local, mip);
                            if (water.A != 0)
                                color = CompositeWaterTint(color, water.A);
                        }
                    }
                    canvas.SetPixel(x, y, color);
                    depthBuffer[index] = depth;
                }
            }
        }

        static bool TryProjectPerspectivePoint(
            Vector3D worldPoint,
            StarCamera camera,
            out Vector2 projected,
            out double cameraZ)
        {
            Vector3D cameraRelativePoint = worldPoint - camera.PositionWorld;
            cameraZ = Vector3D.Dot(cameraRelativePoint, camera.Backward);
            if (!IsFinite(cameraZ) || cameraZ >= -camera.NearDepth)
            {
                projected = Vector2.Zero;
                return false;
            }

            return TryProjectPerspectivePointFromCameraZ(
                cameraRelativePoint,
                camera,
                cameraZ,
                out projected);
        }

        static bool TryProjectPerspectivePointFromCameraZ(
            Vector3D cameraRelativePoint,
            StarCamera camera,
            double cameraZ,
            out Vector2 projected)
        {
            double depth = -cameraZ;
            double x = Vector3D.Dot(cameraRelativePoint, camera.Right);
            double y = Vector3D.Dot(cameraRelativePoint, camera.Up);
            if (!IsFinite(depth) || !IsFinite(x) || !IsFinite(y) ||
                !IsFinite(camera.FocalLengthPixels) || depth <= 1e-12d ||
                camera.FocalLengthPixels <= 0d)
            {
                projected = Vector2.Zero;
                return false;
            }

            double screenX = camera.ScreenCenterX +
                             camera.FocalLengthPixels * x / depth;
            double screenY = camera.ScreenCenterY -
                             camera.FocalLengthPixels * y / depth;
            if (!IsFinite(screenX) || !IsFinite(screenY) ||
                screenX < -float.MaxValue || screenX > float.MaxValue ||
                screenY < -float.MaxValue || screenY > float.MaxValue)
            {
                projected = Vector2.Zero;
                return false;
            }

            projected = new Vector2((float)screenX, (float)screenY);
            return true;
        }

        static Vector3D BuildOrbitPlaneNormal(Vector3D radial, Vector3D referenceNormal)
        {
            if (radial.Normalize() <= 1e-9)
                return Vector3D.Up;
            if (referenceNormal.Normalize() <= 1e-9)
                referenceNormal = Vector3D.Up;
            Vector3D tangent = Vector3D.Cross(referenceNormal, radial);
            if (tangent.Normalize() <= 1e-9)
            {
                referenceNormal = Math.Abs(Vector3D.Dot(radial, Vector3D.Up)) > 0.98d
                    ? Vector3D.Forward
                    : Vector3D.Up;
                tangent = Vector3D.Cross(referenceNormal, radial);
                if (tangent.Normalize() <= 1e-9)
                    return Vector3D.Up;
            }
            Vector3D normal = Vector3D.Cross(radial, tangent);
            return normal.Normalize() <= 1e-9 ? Vector3D.Up : normal;
        }

        float[] PrepareStarDepthBuffer(int width, int height)
        {
            int length = Math.Max(0, width * height);
            if (_starDepthBuffer == null || _starDepthBuffer.Length != length)
                _starDepthBuffer = new float[length];

            for (int i = 0; i < _starDepthBuffer.Length; i++)
                _starDepthBuffer[i] = float.NegativeInfinity;
            return _starDepthBuffer;
        }

        static void BuildDepthTestedOrbitRing(
            List<DepthLineSegment> segmentsOut,
            Vector3D centerWorld,
            Vector3D axisX,
            Vector3D axisY,
            double radius,
            StarCamera camera,
            double nearestStrokeDistance,
            double farthestStrokeDistance,
            byte style)
        {
            Vector3D centerRelativeCamera = centerWorld - camera.PositionWorld;
            double centerDepth = -Vector3D.Dot(
                centerRelativeCamera,
                camera.Backward);
            double nearestRingDepth = Math.Max(
                camera.NearDepth,
                centerDepth - Math.Abs(radius));
            double projectedRadius = camera.FocalLengthPixels * Math.Abs(radius) /
                                     Math.Max(camera.NearDepth, nearestRingDepth);
            double projectedCircumference = MathHelper.TwoPi * projectedRadius;
            int segments = Math.Max(72, Math.Min(2048,
                (int)Math.Ceiling(projectedCircumference / 3d)));

            Vector3D previousPoint = Vector3D.Zero;
            double previousCameraZ = 0d;
            double previousDistanceToCamera = 0d;
            double cameraNearZ = -camera.NearDepth;

            for (int i = 0; i <= segments; i++)
            {
                double angle = MathHelper.TwoPi * i / segments;
                Vector3D pointWorld = centerWorld +
                                      axisX * (Math.Cos(angle) * radius) +
                                      axisY * (Math.Sin(angle) * radius);
                Vector3D point = pointWorld - camera.PositionWorld;
                double cameraZ = Vector3D.Dot(point, camera.Backward);
                double distanceToCamera = point.Length();

                if (i > 0)
                {
                    Vector3D clippedStartPoint = previousPoint;
                    Vector3D clippedEndPoint = point;
                    double clippedStartZ = previousCameraZ;
                    double clippedEndZ = cameraZ;

                    bool startVisible = clippedStartZ < cameraNearZ;
                    bool endVisible = clippedEndZ < cameraNearZ;
                    if (startVisible || endVisible)
                    {
                        if (startVisible != endVisible)
                        {
                            double denominator = clippedEndZ - clippedStartZ;
                            if (Math.Abs(denominator) > 1e-12d)
                            {
                                double amount = (cameraNearZ - clippedStartZ) / denominator;
                                amount = Math.Max(0d, Math.Min(1d, amount));
                                Vector3D clippedPoint = clippedStartPoint +
                                    (clippedEndPoint - clippedStartPoint) * amount;
                                if (!startVisible)
                                {
                                    clippedStartPoint = clippedPoint;
                                    clippedStartZ = cameraNearZ;
                                }
                                else
                                {
                                    clippedEndPoint = clippedPoint;
                                    clippedEndZ = cameraNearZ;
                                }
                            }
                        }

                        Vector2 projectedStart;
                        Vector2 projectedEnd;
                        if (TryProjectPerspectivePointFromCameraZ(
                                clippedStartPoint,
                                camera,
                                clippedStartZ,
                                out projectedStart) &&
                            TryProjectPerspectivePointFromCameraZ(
                                clippedEndPoint,
                                camera,
                                clippedEndZ,
                                out projectedEnd))
                        {
                            float strokeWidth = GetOrbitStrokeWidth(
                                (previousDistanceToCamera + distanceToCamera) * 0.5d,
                                nearestStrokeDistance,
                                farthestStrokeDistance);
                            segmentsOut.Add(new DepthLineSegment
                            {
                                Start = projectedStart,
                                End = projectedEnd,
                                StartDepth = (float)clippedStartZ,
                                EndDepth = (float)clippedEndZ,
                                Width = strokeWidth,
                                Style = style
                            });
                        }
                    }
                }

                previousPoint = point;
                previousCameraZ = cameraZ;
                previousDistanceToCamera = distanceToCamera;
            }
        }

        void DrawDepthTestedOrbitLines(
            PixelCanvas canvas,
            float[] depthBuffer,
            List<DepthLineSegment> segments)
        {
            int requiredLength = Math.Max(0, canvas.Width * canvas.Height);
            if (_starOrbitCoverageBuffer == null ||
                _starOrbitCoverageBuffer.Length != requiredLength ||
                _starOrbitStyleBuffer == null ||
                _starOrbitStyleBuffer.Length != requiredLength)
            {
                _starOrbitCoverageBuffer = new float[requiredLength];
                _starOrbitStyleBuffer = new byte[requiredLength];
            }
            _starOrbitTouchedPixels.Clear();

            // Build the coverage of the complete path before blending any pixel.
            // Taking the maximum segment coverage is equivalent to evaluating the
            // distance to the union of all stroked sections: joins have no holes,
            // and coincident anti-aliased edges cannot darken through alpha buildup.
            for (int i = 0; i < segments.Count; i++)
            {
                DepthLineSegment segment = segments[i];
                RasterizeDepthTestedLineCoverage(
                    canvas,
                    depthBuffer,
                    _starOrbitCoverageBuffer,
                    _starOrbitStyleBuffer,
                    _starOrbitTouchedPixels,
                    segment.Start,
                    segment.StartDepth,
                    segment.End,
                    segment.EndDepth,
                    segment.Width,
                    segment.Style);
            }

            for (int i = 0; i < _starOrbitTouchedPixels.Count; i++)
            {
                int index = _starOrbitTouchedPixels[i];
                int y = index / canvas.Width;
                int x = index - y * canvas.Width;
                canvas.BlendPixel(
                    x,
                    y,
                    GetOrbitColor(_starOrbitStyleBuffer[index]),
                    _starOrbitCoverageBuffer[index]);
                _starOrbitCoverageBuffer[index] = 0f;
            }
            _starOrbitTouchedPixels.Clear();
        }

        static void RasterizeDepthTestedLineCoverage(
            PixelCanvas canvas,
            float[] depthBuffer,
            float[] coverageBuffer,
            byte[] styleBuffer,
            List<int> touchedPixels,
            Vector2 start,
            float startDepth,
            Vector2 end,
            float endDepth,
            float thickness,
            byte style)
        {
            // Analytic screen-space coverage gives the 1-2 px orbit strokes a
            // sub-pixel edge instead of snapping a Bresenham-style brush to whole
            // pixels. Width remains continuous, so distance falloff can smoothly
            // move between the 2 px near stroke and 1 px far stroke.
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;
            float lengthSquared = dx * dx + dy * dy;
            float halfWidth = Math.Max(0.5f, thickness * 0.5f);
            float rasterRadius = halfWidth + 0.5f;

            int left = Math.Max(0, (int)Math.Floor(Math.Min(start.X, end.X) - rasterRadius));
            int right = Math.Min(canvas.Width - 1, (int)Math.Ceiling(Math.Max(start.X, end.X) + rasterRadius));
            int top = Math.Max(0, (int)Math.Floor(Math.Min(start.Y, end.Y) - rasterRadius));
            int bottom = Math.Min(canvas.Height - 1, (int)Math.Ceiling(Math.Max(start.Y, end.Y) + rasterRadius));

            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    float amount;
                    if (lengthSquared <= VectorEpsilon * VectorEpsilon)
                    {
                        amount = 0f;
                    }
                    else
                    {
                        amount = ((x - start.X) * dx + (y - start.Y) * dy) / lengthSquared;
                        amount = MathHelper.Clamp(amount, 0f, 1f);
                    }

                    float closestX = start.X + dx * amount;
                    float closestY = start.Y + dy * amount;
                    float distanceX = x - closestX;
                    float distanceY = y - closestY;
                    float distance = (float)Math.Sqrt(
                        distanceX * distanceX + distanceY * distanceY);
                    float coverage = MathHelper.Clamp(
                        halfWidth + 0.5f - distance,
                        0f,
                        1f);
                    if (coverage <= 0f)
                        continue;

                    // Perspective-correct line depth: reciprocal positive camera
                    // depth is linear in screen space. Convert back to SE camera Z
                    // (negative in front of the camera) for the shared depth buffer.
                    float startReciprocalDepth = -1f / startDepth;
                    float endReciprocalDepth = -1f / endDepth;
                    float reciprocalDepth = MathHelper.Lerp(
                        startReciprocalDepth,
                        endReciprocalDepth,
                        amount);
                    if (reciprocalDepth <= 0f ||
                        float.IsNaN(reciprocalDepth) ||
                        float.IsInfinity(reciprocalDepth))
                    {
                        continue;
                    }
                    float depth = -1f / reciprocalDepth;
                    int index = y * canvas.Width + x;
                    // Depth is SE camera-space Z: visible values are negative and
                    // larger (less-negative) values are nearer. Strict comparison
                    // keeps an orbit tangent to a planet surface from bleeding
                    // through the opaque globe.
                    if (depth <= depthBuffer[index])
                        continue;

                    if (coverageBuffer[index] <= 0f)
                    {
                        touchedPixels.Add(index);
                        styleBuffer[index] = style;
                    }
                    else if (style > styleBuffer[index])
                    {
                        // Highlighted orbits retain their identity at crossings:
                        // current (blue) takes precedence over focused (green),
                        // which takes precedence over ordinary white paths.
                        styleBuffer[index] = style;
                    }
                    coverageBuffer[index] = Math.Max(
                        coverageBuffer[index],
                        coverage);
                }
            }
        }

        static Color GetOrbitColor(byte style)
        {
            if (style == CurrentOrbitStyle)
                return CurrentOrbitColor;
            if (style == FocusedOrbitStyle)
                return FocusedOrbitColor;
            return DefaultOrbitColor;
        }

        static bool AbortCameraHandoff(string handoff, string reason)
        {
            MyLog.Default.WriteLineAndConsole(string.Format(
                "[Cartography] Camera handoff {0} aborted: {1}",
                handoff,
                string.IsNullOrEmpty(reason) ? "unknown validation failure" : reason));
            return false;
        }

        static bool ValidatePlanetWorldBasis(MyPlanet planet, string handoff)
        {
            if (planet == null)
                return AbortCameraHandoff(handoff, "planet is null");

            Vector3D right = planet.WorldMatrix.Right;
            Vector3D up = planet.WorldMatrix.Up;
            Vector3D backward = planet.WorldMatrix.Backward;
            if (!IsFinite(right) || !IsFinite(up) || !IsFinite(backward) ||
                !IsFinite(planet.WorldMatrix.Translation))
            {
                return AbortCameraHandoff(handoff, "planet WorldMatrix contains NaN/Infinity");
            }

            if (right.Normalize() <= 1e-9 ||
                up.Normalize() <= 1e-9 ||
                backward.Normalize() <= 1e-9)
            {
                return AbortCameraHandoff(handoff, "planet WorldMatrix has a degenerate axis");
            }

            double handedness = Vector3D.Dot(Vector3D.Cross(backward, right), up);
            double maxDot = Math.Max(
                Math.Abs(Vector3D.Dot(right, up)),
                Math.Max(
                    Math.Abs(Vector3D.Dot(right, backward)),
                    Math.Abs(Vector3D.Dot(up, backward))));
            if (!IsFinite(handedness) || handedness < 0.999d || maxDot > 0.001d)
            {
                return AbortCameraHandoff(
                    handoff,
                    string.Format(
                        "planet WorldMatrix is not an SE Right/Up/Backward basis (hand={0:0.000000}, orth={1:0.000000})",
                        handedness,
                        maxDot));
            }
            return true;
        }

        // Matches Space Engineers' camera/world-matrix hand. In the supplied SE
        // source, MySpectatorCameraController rebuilds Forward = Cross(Up, Right);
        // equivalently Backward = Cross(Right, Up) and Cross(Backward, Right) = Up.
        static bool TryNormalizeSeCameraBasis(
            ref Vector3 backward,
            ref Vector3 right,
            ref Vector3 up,
            out string reason)
        {
            reason = null;
            if (!IsFinite(backward) || !IsFinite(right) || !IsFinite(up))
            {
                reason = "basis contains NaN/Infinity";
                return false;
            }

            Vector3 inputBackward = backward;
            Vector3 inputRight = right;
            Vector3 inputUp = up;
            if (inputBackward.Normalize() <= VectorEpsilon ||
                inputRight.Normalize() <= VectorEpsilon ||
                inputUp.Normalize() <= VectorEpsilon)
            {
                reason = "basis contains a zero/degenerate axis";
                return false;
            }

            float inputHandedness = Vector3.Dot(
                Vector3.Cross(inputBackward, inputRight),
                inputUp);
            if (!IsFinite(inputHandedness) || inputHandedness <= 0.25f)
            {
                reason = string.Format(
                    "basis is mirrored/left-handed (Cross(Backward,Right).Up={0:0.000000})",
                    inputHandedness);
                return false;
            }

            backward = inputBackward;
            Vector3 suppliedUp = inputUp;
            right = inputRight - backward * Vector3.Dot(inputRight, backward);
            if (!IsFinite(right) || right.Normalize() <= VectorEpsilon)
            {
                reason = "Right is parallel to Backward";
                return false;
            }

            Vector3 rebuiltUp = Vector3.Cross(backward, right);
            if (!IsFinite(rebuiltUp) || rebuiltUp.Normalize() <= VectorEpsilon)
            {
                reason = "could not reconstruct Up from Backward/Right";
                return false;
            }

            // Preserve the input screen-Up hemisphere without changing SE hand.
            // If Up points the other way, flip Right and Up together.
            if (Vector3.Dot(rebuiltUp, suppliedUp) < 0f)
            {
                right = -right;
                rebuiltUp = -rebuiltUp;
            }
            up = rebuiltUp;

            right = Vector3.Cross(up, backward);
            if (!IsFinite(right) || right.Normalize() <= VectorEpsilon)
            {
                reason = "could not rebuild Right from Up/Backward";
                return false;
            }
            up = Vector3.Cross(backward, right);
            if (!IsFinite(up) || up.Normalize() <= VectorEpsilon)
            {
                reason = "could not finalize Up";
                return false;
            }

            float handedness = Vector3.Dot(Vector3.Cross(backward, right), up);
            float orthogonality = Math.Max(
                Math.Abs(Vector3.Dot(backward, right)),
                Math.Max(
                    Math.Abs(Vector3.Dot(backward, up)),
                    Math.Abs(Vector3.Dot(right, up))));
            if (!IsFinite(handedness) || !IsFinite(orthogonality) ||
                handedness < 0.999f || orthogonality > 0.001f)
            {
                reason = string.Format(
                    "normalized basis failed verification (hand={0:0.000000}, orth={1:0.000000})",
                    handedness,
                    orthogonality);
                return false;
            }
            return true;
        }

        static void NormalizeProjection(ref Vector3 view, ref Vector3 right, ref Vector3 up)
        {
            Vector3 suppliedUp = up;
            string reason;
            if (TryNormalizeSeCameraBasis(ref view, ref right, ref up, out reason))
                return;

            // Runtime rendering is allowed to recover from old/corrupt state; camera
            // handoffs are not. Reconstruct the SE Right/Up/Backward basis from the
            // usable axes while preserving the requested Up hemisphere when possible.
            if (!IsFinite(view) || view.Normalize() <= VectorEpsilon)
                view = Vector3.Backward;

            Vector3 referenceUp = suppliedUp;
            if (!IsFinite(referenceUp) || referenceUp.Normalize() <= VectorEpsilon ||
                Math.Abs(Vector3.Dot(referenceUp, view)) > 0.999f)
            {
                referenceUp = Math.Abs(Vector3.Dot(view, Vector3.Up)) > 0.98f
                    ? Vector3.Forward
                    : Vector3.Up;
            }

            right = Vector3.Cross(referenceUp, view);
            if (!IsFinite(right) || right.Normalize() <= VectorEpsilon)
                right = Vector3.Right;

            up = Vector3.Cross(view, right);
            if (!IsFinite(up) || up.Normalize() <= VectorEpsilon)
                up = Vector3.Up;

            if (IsFinite(suppliedUp) && Vector3.Dot(up, suppliedUp) < 0f)
            {
                right = -right;
                up = -up;
            }

            right = Vector3.Cross(up, view);
            if (right.Normalize() <= VectorEpsilon)
                right = Vector3.Right;
            up = Vector3.Cross(view, right);
            if (up.Normalize() <= VectorEpsilon)
                up = Vector3.Up;
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
        }

        static bool IsFinite(Vector3D value)
        {
            return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
        }

        static void BuildProjection(
            MyPlanet planet,
            MapData data,
            MapViewState state,
            out Vector3 view,
            out Vector3 right,
            out Vector3 up)
        {
            Vector3 baseView = Vector3.Backward;
            if (planet != null)
            {
                Vector3D worldDirection = GetMapViewPosition(data, state) -
                                          planet.WorldMatrix.Translation;
                if (worldDirection.Normalize() > 1e-9)
                {
                    baseView = WorldDirectionToPlanetLocal(planet, worldDirection);
                    if (baseView.Normalize() <= VectorEpsilon)
                        baseView = Vector3.Backward;
                }
            }

            Vector3 referenceUp = Vector3.Up;
            if (planet != null && state.PlanetOrientation == PlanetOrientationMode.HeadingLocked &&
                data != null && data.HasPlayerWorldMatrix)
            {
                referenceUp = WorldDirectionToPlanetLocal(
                    planet,
                    data.PlayerWorldMatrix.Forward);
                if (referenceUp.Normalize() <= VectorEpsilon)
                    referenceUp = Vector3.Up;
            }
            else if (planet != null && state.PlanetOrientation == PlanetOrientationMode.Free &&
                     state.HasPlanetFreeReferenceUpWorld)
            {
                referenceUp = WorldDirectionToPlanetLocal(
                    planet,
                    state.PlanetFreeReferenceUpWorld);
                if (referenceUp.Normalize() <= VectorEpsilon)
                    referenceUp = Vector3.Up;
            }
            Vector3 baseRight = Vector3.Cross(referenceUp, baseView);
            if (baseRight.Normalize() <= VectorEpsilon)
            {
                referenceUp = Math.Abs(Vector3.Dot(baseView, Vector3.Up)) > 0.98f
                    ? Vector3.Forward
                    : Vector3.Up;
                baseRight = Vector3.Cross(referenceUp, baseView);
                if (baseRight.Normalize() <= VectorEpsilon)
                    baseRight = Vector3.Right;
            }

            Vector3 baseUp = Vector3.Cross(baseView, baseRight);
            if (baseUp.Normalize() <= VectorEpsilon)
                baseUp = referenceUp;

            // Compose the base planet/controller frame with the accumulated
            // screen-space orbit basis. The orbit basis is updated around its
            // own current Up/Right axes by MinimapScreen, rather than around a
            // fixed planetary north axis. Star Map now uses the same persistent
            // basis approach in world space, avoiding its old pole singularity.
            Vector3 orbitView = state.PlanetOrbitView;
            Vector3 orbitRight = state.PlanetOrbitRight;
            Vector3 orbitUp = state.PlanetOrbitUp;
            NormalizeOrbitBasis(ref orbitView, ref orbitRight, ref orbitUp);

            view = ComposeProjectionVector(
                orbitView, baseRight, baseUp, baseView);
            right = ComposeProjectionVector(
                orbitRight, baseRight, baseUp, baseView);
            up = ComposeProjectionVector(
                orbitUp, baseRight, baseUp, baseView);
            NormalizeProjection(ref view, ref right, ref up);
        }

        static Vector3 ComposeProjectionVector(
            Vector3 orbitVector,
            Vector3 baseRight,
            Vector3 baseUp,
            Vector3 baseView)
        {
            return baseRight * orbitVector.X +
                   baseUp * orbitVector.Y +
                   baseView * orbitVector.Z;
        }

        static void NormalizeOrbitBasis(
            ref Vector3 view,
            ref Vector3 right,
            ref Vector3 up)
        {
            // Planet orbit and Star orbit use the exact same SE camera hand.
            NormalizeProjection(ref view, ref right, ref up);
        }

        static Vector3D GetMapViewPosition(MapData data, MapViewState state)
        {
            if (state != null && !state.FollowPlayer && state.HasStaticCameraPosition)
                return state.StaticCameraPosition;

            if (data != null && MyAPIGateway.Session != null && MyAPIGateway.Session.Player != null)
                return data.PlayerPosition;

            return GetCameraPosition();
        }

        static Vector3 WorldDirectionToPlanetLocal(MyPlanet planet, Vector3D worldDirection)
        {
            return new Vector3(
                (float)Vector3D.Dot(worldDirection, planet.WorldMatrix.Right),
                (float)Vector3D.Dot(worldDirection, planet.WorldMatrix.Up),
                (float)Vector3D.Dot(worldDirection, planet.WorldMatrix.Backward));
        }

        static Vector3D PlanetLocalDirectionToWorld(MyPlanet planet, Vector3 localDirection)
        {
            return planet.WorldMatrix.Right * localDirection.X +
                   planet.WorldMatrix.Up * localDirection.Y +
                   planet.WorldMatrix.Backward * localDirection.Z;
        }

        static Vector3D GetCameraPosition()
        {
            if (MyAPIGateway.Session != null && MyAPIGateway.Session.Camera != null)
                return MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            if (MyAPIGateway.Session != null && MyAPIGateway.Session.Player != null)
                return MyAPIGateway.Session.Player.GetPosition();
            return Vector3D.Zero;
        }

        static Color CompositeWaterTint(Color background, byte coverage)
        {
            int overlayAlpha = (coverage * WaterOverlayTint.A + 127) / 255;
            int inverseAlpha = 255 - overlayAlpha;
            int alpha = Math.Min(255, overlayAlpha + background.A * inverseAlpha / 255);
            if (alpha == 0)
                return Color.Transparent;

            int red = WaterOverlayTint.R * overlayAlpha +
                      background.R * background.A * inverseAlpha / 255;
            int green = WaterOverlayTint.G * overlayAlpha +
                        background.G * background.A * inverseAlpha / 255;
            int blue = WaterOverlayTint.B * overlayAlpha +
                       background.B * background.A * inverseAlpha / 255;
            return new Color(
                (byte)Math.Min(255, (red + alpha / 2) / alpha),
                (byte)Math.Min(255, (green + alpha / 2) / alpha),
                (byte)Math.Min(255, (blue + alpha / 2) / alpha),
                (byte)alpha);
        }

        void RaiseChanged()
        {
            Action changed = Changed;
            if (changed != null)
                changed();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_cartography != null)
            {
                _cartography.ColorCubemapCached -= OnColorCubemapCached;
                _cartography.PlanetInvalidated -= OnPlanetInvalidated;
                _cartography.Skybox.Changed -= RaiseChanged;
            }
            foreach (PendingRequest pending in _pending.Values)
            {
                if (pending.Ticket != null)
                    pending.Ticket.Cancel();
            }
            _pending.Clear();
            _cubemaps.Clear();
            Changed = null;
        }
    }
}
