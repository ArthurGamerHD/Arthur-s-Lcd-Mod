using System;
using System.Collections.Generic;
using System.Globalization;
using Generated;
using LcdMod.Client.Extensions;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.Apps.Abstract.InteractiveSurfaceScript;
using SliderFov = LcdMod.Client.Terminal.Controls.Generic.SliderFov;

namespace LcdMod.Client.Apps
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class StarMapSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderFov>,
        IMultiDisplayMode
    {
        protected override ConfigKind ConfigKind => ConfigKind.StarMap;
        float _fov;
        double _halfFovY;
        float _lastKnownConfigFov = float.NaN;
        long _lastFovChangedFrame = long.MinValue;
        bool _syncConfigNextRun;
        IMyGravityProviderSystem _gravityProvider;
        readonly EyeTrackingFrameState _eyeTracking = new EyeTrackingFrameState();

        long _jumpPointRunCounter;

        readonly List<MySprite> _baseSprites = new List<MySprite>();
        readonly List<MySprite> _groundSprites = new List<MySprite>();
        readonly List<MySprite> _groundOcclusionSprites = new List<MySprite>();
        readonly List<MySprite> _ringSprites = new List<MySprite>();
        readonly List<MySprite> _overlaySprites = new List<MySprite>();
        readonly List<MySprite> _sprites = new List<MySprite>();

        // for Static map. These sprites and hit targets only change when the
        // surface layout changes, so they are built once and reused across Run() calls.
        // for Dynamic map. They change based on World Matrix or Cursor Movement
        bool _staticOrbitCacheValid;
        readonly List<MySprite> _cachedStaticBaseSprites = new List<MySprite>();
        readonly List<MySprite> _cachedStaticTitleSprites = new List<MySprite>();
        readonly List<MySprite> _cachedStaticRingSprites = new List<MySprite>();
        readonly List<InteractiveEntry> _cachedInteractiveEntries = new List<InteractiveEntry>();

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
        bool _suppressDynamicOverlays;
        int _artificialHorizonLastRadarAlt;
        int _artificialHorizonVerticalSpeed;
        long _artificialHorizonLastRadarAltFrame = long.MinValue;
        long _artificialHorizonLastRadarAltPlanetId;
        bool _artificialHorizonShowAltWarning;
        long _artificialHorizonAltWarningShownAt;

        struct PlanetProjection
        {
            public long PlanetId;
            public string Name;
            public PlanetHelper.PlanetTextureStyle Texture;
            public Vector3D WorldPosition;
            public Vector3D Direction;
            public double Distance;
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
            public Vector2 Center;
            public Vector2 Size;
            public bool IsMoonRing;
            public float SortHeight;
        }

        public const string ID = "LcdMod_StarMapSurface";
        public const string TITLE = "LcdMod_StarMapSurface";
        const float SHADE_MUL = 0.75f;
        const float OVERLAY_GROW_RATIO = 0.05f; // relative to diameter
        const float OVERLAY_OFFSET_RATIO = 0.25f; // relative to radius
        const float POLAR_CAP_RATIO = 0.06f; // top/bottom % of diameter
        const float EQUATOR_BAND_RATIO = 0.18f; // % of diameter
        const float SURFACE_GROUND_COLOR_TRANSITION_DEG = 2f; // soft transition between base/equator/polar surface colors
        const float MAP_VERTICAL_FOV_DEFAULT_DEG = 70f;
        const long MAGNIFICATION_HUD_VISIBLE_FRAMES = 300L;
        const float MAP_NEAR_CLIP_METERS = 10f;
        const float PLANET_SHADING_MIN_DIAMETER_PX = 10f;
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
        const float STATIC_PLANET_BODY_RADIUS_PX = 10f;
        const float STATIC_MOON_BODY_RADIUS_PX = 5f;
        const float STATIC_ORBIT_OUTWARD_FROM_PLANET = 0.1f;
        const float STATIC_ORBIT_LINE_WIDTH_PX = 6f;
        const double STATIC_ORBIT_MIN_RING_METERS = 100000d;
        const double STATIC_PARENT_ORBIT_MAX_METERS = 300000d;
        const float STATIC_ORBIT_Y_SQUASH = 0.55f;

        static readonly List<MyTerminalControlComboBoxItem> StarMapDisplayModes =
            new List<MyTerminalControlComboBoxItem>
            {
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)DisplayMode.Grid,
                    Value = VRage.Utils.MyStringId.GetOrCompute("Dynamic")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)DisplayMode.Legacy,
                    Value = VRage.Utils.MyStringId.GetOrCompute("Static")
                }
            };

        protected override string DefaultTitle => TITLE;

        public StarMapSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        public override CursorType CursorType { get; protected set; } = CursorType.Default;

        protected override bool RendersInteractiveEntriesInGetSprites
        {
            get { return true; }
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return StarMapDisplayModes;
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _fov = GetEffectiveVerticalFovDeg();
            _halfFovY = MathHelper.ToRadians(_fov) * 0.5;
            _lastKnownConfigFov = AppConfig?.FoV ?? MAP_VERTICAL_FOV_DEFAULT_DEG;
            InvalidateStaticOrbitCache();
            InvalidateDynamicMapCache();
            RebuildPropertyLabelCache();

            CursorType = GetDefaultCursorType();
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

        string BuildPropertyLocKey(string name) => "LcdMod_" + name + (AppConfig != null && AppConfig.DisplayMode == (int)DisplayMode.Grid ? "_Short" : string.Empty);

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

        CursorType GetDefaultCursorType()
        {
            return AppConfig != null && AppConfig.DisplayMode == (int)DisplayMode.Legacy
                ? CursorType.Default
                : CursorType.None;
        }


        void InvalidateStaticOrbitCache()
        {
            _staticOrbitCacheValid = false;
            _cachedStaticBaseSprites.Clear();
            _cachedStaticTitleSprites.Clear();
            _cachedStaticRingSprites.Clear();
            _cachedInteractiveEntries.Clear();
        }

        void InvalidateDynamicMapCache()
        {
            _dynamicMapCacheValid = false;
            _cachedDynamicGroundSprites.Clear();
            _cachedDynamicGroundOcclusionSprites.Clear();
            _cachedDynamicRingSprites.Clear();
            _cachedOverlaySprites.Clear();
            _cachedInteractiveEntries.Clear();
        }
        
        public override void SafeRun()
        {
            if (AppConfig == null)
                return;
            _jumpPointRunCounter++;

            if (_syncConfigNextRun)
            {
                _syncConfigNextRun = false;
                if (Block != null && ProviderConfig != null)
                    ConfigManager.Sync(Block, ProviderConfig);
            }

            bool hadKnownFov = !float.IsNaN(_lastKnownConfigFov);
            if (!hadKnownFov || Math.Abs(_lastKnownConfigFov - AppConfig.FoV) > 0.001f)
            {
                if (hadKnownFov)
                    _lastFovChangedFrame = GetCurrentGameFrame();

                LayoutChanged();
            }

            RenderSprites();
        }

        protected override List<MySprite> GetSprites()
        {
            _baseSprites.Clear();
            _groundSprites.Clear();
            _groundOcclusionSprites.Clear();
            _ringSprites.Clear();
            _overlaySprites.Clear();
            InteractiveList.Clear();
            CursorType = GetDefaultCursorType();
            _suppressDynamicOverlays = false;

            bool staticMode = AppConfig.DisplayMode == (int)DisplayMode.Legacy;
            bool hasPlanets;

            if (staticMode && _staticOrbitCacheValid)
            {
                _baseSprites.AddRange(_cachedStaticBaseSprites);
                _overlaySprites.AddRange(_cachedStaticTitleSprites);

                hasPlanets = DrawPlanetMap(_groundSprites, _groundOcclusionSprites, _ringSprites, _overlaySprites);
            }
            else if (staticMode)
            {
                AddBackground(_baseSprites);
                DrawTitle(_overlaySprites);

                _cachedStaticBaseSprites.Clear();
                _cachedStaticBaseSprites.AddRange(_baseSprites);
                _cachedStaticTitleSprites.Clear();
                _cachedStaticTitleSprites.AddRange(_overlaySprites);

                hasPlanets = DrawPlanetMap(_groundSprites, _groundOcclusionSprites, _ringSprites, _overlaySprites);
            }
            else
            {
                hasPlanets = DrawPlanetMap(_groundSprites, _groundOcclusionSprites, _ringSprites, _overlaySprites);
                
                _baseSprites.AddRange(_groundSprites);
                AddBackground(_baseSprites);
                DrawTitle(_overlaySprites);
            }

            if (hasPlanets)
            {
                if (!staticMode && ShouldDrawFovHud())
                    DrawFovHud(_overlaySprites, _fov);
            }
            else
            {
                DrawMessage(_overlaySprites, LocHelper.Empty, "Warning", AppConfig.WarningColor, AppConfig.Scale);
            }

            _sprites.Clear();
            _sprites.AddRange(_baseSprites);
            _sprites.AddRange(_ringSprites);
            RenderInteractiveEntryVisuals(_sprites);

            _sprites.AddRange(_groundOcclusionSprites);
            _sprites.AddRange(_overlaySprites);
            return _sprites;
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

        void QueueArtificialHorizonRenderNextFrame() => LcdModClientComponent.RunNextFrame.Add(RenderSprites);

        void DrawFovHud(List<MySprite> sprites, float fovDeg)
        {
            const float textScale = 0.55f;
            double baseHalfFov = MathHelper.ToRadians(MAP_VERTICAL_FOV_DEFAULT_DEG) * 0.5;
            double currentHalfFov = MathHelper.ToRadians(Math.Max(0.1f, fovDeg)) * 0.5;
            double magnification = Math.Tan(baseHalfFov) / Math.Tan(currentHalfFov);
            string text = "MAG: " + magnification.ToString("0.##", FormatingHelper.Culture) + "x";
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, Surface);
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
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });
        }

        bool DrawPlanetMap(
            List<MySprite> groundSprites,
            List<MySprite> groundOcclusionSprites,
            List<MySprite> ringSprites,
            List<MySprite> overlaySprites)
        {
            var planets = PlanetHelper.PlanetsById;
            if (planets == null || planets.Count == 0)
                return false;
            bool hasDetectedPlanets = false;

            bool staticMode = AppConfig != null && AppConfig.DisplayMode == (int)DisplayMode.Legacy;
            if (staticMode)
                return DrawStaticOrbitMap(ringSprites, planets);

            if (Block == null)
                return false;

            MatrixD world = Block.WorldMatrix;
            int groundStartIndex = groundSprites.Count;
            int groundOcclusionStartIndex = groundOcclusionSprites.Count;
            int ringStartIndex = ringSprites.Count;
            int overlayStartIndex = overlaySprites.Count;
            if (TryUseDynamicMapCache(groundSprites, groundOcclusionSprites, ringSprites, overlaySprites, planets.Count, world))
                return true;

            var camPos = world.Translation;
            var camRight = world.Right;
            var camUp = world.Up;
            var camForward = world.Forward;

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
                world,
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
                string generatorName;
                PlanetHelper.PlanetGeneratorNamesById.TryGetValue(planet.EntityId, out generatorName);
                var textureKey = string.IsNullOrWhiteSpace(generatorName) ? name : generatorName;
                var generator = planet.Generator;
                var atmosphere = generator?.Atmosphere;
                MyTemperatureLevel? averageTemperature = generator?.DefaultSurfaceTemperature;
                double surfaceGravity = planet.GetInitArguments.SurfaceGravity;
                double gravityFalloff =  planet.GetInitArguments.GravityFalloff;
                double gravityLimitRadius = 0d;
                if (planet.MaximumRadius > 0d && surfaceGravity > 0d && gravityFalloff > 0d)
                    gravityLimitRadius = planet.MaximumRadius * Math.Pow(surfaceGravity / 0.05d, 1d / gravityFalloff);
                var projection = new PlanetProjection
                {
                    PlanetId = planet.EntityId,
                    Name = string.IsNullOrWhiteSpace(name) ? "Unknown Planet" : name,
                    Texture = PlanetHelper.ResolvePlanetTexture(textureKey),
                    WorldPosition = planet.WorldMatrix.Translation,
                    Direction = delta / distance,
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
                        world,
                        gravityPlanetId,
                        planets,
                        _suppressDynamicOverlays);
            }
            else
            {
                DrawArtificialHorizonSpaceOverlay(overlaySprites, world, _suppressDynamicOverlays);
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
                world);
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
            InteractiveList.AddRange(_cachedInteractiveEntries);
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
            _staticOrbitCacheValid = false;
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
            _cachedInteractiveEntries.Clear();
            _cachedInteractiveEntries.AddRange(InteractiveList);
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
                AppConfig != null ? AppConfig.Scale : Scale);
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
                FontId = "White",
                Alignment = alignment,
                RotationOrScale = textScale
            });
        }

        Color GetArtificialHorizonWarningColor()
        {
            return AppConfig != null ? AppConfig.WarningColor : ForegroundColor;
        }

        Color GetArtificialHorizonErrorColor()
        {
            return (AppConfig != null ? AppConfig.ErrorColor : ForegroundColor)
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
            // equator, PI/2 radians means either pole. The threshold centers mirror
            // DrawEquator()/DrawPolarCaps(), but surface mode blends across a small
            // angular window so the terrain color does not snap at band boundaries.
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
            Vector2 textureSize = Surface != null ? Surface.TextureSize : Vector2.Zero;
            return new RectangleF(
                0f,
                0f,
                Math.Max(1f, textureSize.X),
                Math.Max(1f, textureSize.Y));
        }

        void RestoreTextureClip(List<MySprite> sprites)
        {
            sprites.Add(MySprite.CreateClearClipRect());
        }

        bool DrawStaticOrbitMap(
            List<MySprite> ringSprites,
            Dictionary<long, MyPlanet> planets)
        {
            if (planets == null || planets.Count == 0)
                return false;

            if (_staticOrbitCacheValid)
            {
                ringSprites.AddRange(_cachedStaticRingSprites);
                InteractiveList.AddRange(_cachedInteractiveEntries);
                return true;
            }

            bool hasDetectedPlanets = false;

            var positions = new List<Vector3D>(planets.Count);
            var radii = new List<double>(planets.Count);
            var projectedPlanets = new List<PlanetProjection>(planets.Count);
            var parentIndex = new List<int>(planets.Count);
            var orbitDistances = new List<double>(planets.Count);
            var orbitPlanarDistances = new List<double>(planets.Count);
            var orbitAngles = new List<double>(planets.Count);
            var screenPos = new List<Vector2>(planets.Count);
            var ringProjections = new List<StaticRingProjection>(planets.Count);
            double maxOrbitWithRadius = 1d;

            var referencePos = Block?.GetPosition() ?? Vector3D.Zero;

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
                double distanceToBlock = Block != null ? Vector3D.Distance(pos, referencePos) : pos.Length();
                positions.Add(pos);
                radii.Add(radius);
                parentIndex.Add(-1);
                double orbitMeters = pos.Length();
                double orbitPlanarMeters = Math.Sqrt(pos.X * pos.X + pos.Z * pos.Z);
                double orbitAngle = Math.Atan2(pos.Z, pos.X);
                orbitDistances.Add(orbitMeters);
                orbitPlanarDistances.Add(orbitPlanarMeters);
                orbitAngles.Add(orbitAngle);
                screenPos.Add(Vector2.Zero);
                if (orbitMeters + radius > maxOrbitWithRadius)
                    maxOrbitWithRadius = orbitMeters + radius;

                string name;
                if (!PlanetHelper.PlanetNamesById.TryGetValue(planet.EntityId, out name))
                    name = planet.Name;
                string generatorName;
                PlanetHelper.PlanetGeneratorNamesById.TryGetValue(planet.EntityId, out generatorName);
                var textureKey = string.IsNullOrWhiteSpace(generatorName) ? name : generatorName;
                var generator = planet.Generator;
                var atmosphere = generator?.Atmosphere;
                MyTemperatureLevel? averageTemperature = generator?.DefaultSurfaceTemperature;
                double surfaceGravity = planet.GetInitArguments.SurfaceGravity;
                double gravityFalloff = planet.GetInitArguments.GravityFalloff;
                double gravityLimitRadius = 0d;
                if (planet.MaximumRadius > 0d && surfaceGravity > 0d && gravityFalloff > 0d)
                    gravityLimitRadius = planet.MaximumRadius * Math.Pow(surfaceGravity / 0.05d, 1d / gravityFalloff);

                var projection = new PlanetProjection
                {
                    PlanetId = planet.EntityId,
                    Name = string.IsNullOrWhiteSpace(name) ? "Unknown Planet" : name,
                    Texture = PlanetHelper.ResolvePlanetTexture(textureKey),
                    WorldPosition = pos,
                    Direction = Vector3D.Zero,
                    Distance = distanceToBlock,
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
                    MaxWindSpeed = atmosphere?.MaxWindSpeed ?? 0f
                };
                CachePlanetInfoLines(ref projection);
                projectedPlanets.Add(projection);
            }

            if (!hasDetectedPlanets)
                return false;

            // Smaller planets close to larger ones orbit those larger planets in static map.
            for (int i = 0; i < projectedPlanets.Count; i++)
            {
                double childRadius = radii[i];
                int bestParent = -1;
                double bestDist = double.MaxValue;
                for (int j = 0; j < projectedPlanets.Count; j++)
                {
                    if (i == j)
                        continue;
                    if (radii[j] <= childRadius)
                        continue;

                    double d = Vector3D.Distance(positions[i], positions[j]);
                    if (d <= STATIC_PARENT_ORBIT_MAX_METERS && d < bestDist)
                    {
                        bestDist = d;
                        bestParent = j;
                    }
                }

                parentIndex[i] = bestParent;
                if (bestParent >= 0)
                {
                    Vector3D rel = positions[i] - positions[bestParent];
                    orbitDistances[i] = rel.Length();
                    orbitPlanarDistances[i] = Math.Sqrt(rel.X * rel.X + rel.Z * rel.Z);
                    orbitAngles[i] = Math.Atan2(rel.Z, rel.X);
                }
            }

            float maxOrbitPxByWidth = ViewBox.Width * 0.45f;
            float maxOrbitPxByHeight = (ViewBox.Height * 0.45f) / Math.Max(0.1f, STATIC_ORBIT_Y_SQUASH);
            float maxOrbitPx = Math.Min(maxOrbitPxByWidth, maxOrbitPxByHeight);
            if (maxOrbitPx <= 1f)
                return false;

            double worldToPx = maxOrbitPx / maxOrbitWithRadius;
            var ringColor = ApplyAlpha(ForegroundColor, 0.15f);
            var center = ViewBox.Center;
            var computeOrder = new List<int>(projectedPlanets.Count);
            for (int i = 0; i < projectedPlanets.Count; i++)
                computeOrder.Add(i);
            computeOrder.Sort((a, b) => radii[b].CompareTo(radii[a])); // parents (larger) first

            foreach (var i in computeOrder)
            {
                double orbitMeters = orbitDistances[i];
                double orbitPlanarMeters = orbitPlanarDistances[i];
                float orbitRadiusX = (float)(orbitMeters * worldToPx);
                float orbitRadiusY = (float)(orbitPlanarMeters * worldToPx * STATIC_ORBIT_Y_SQUASH);
                if (parentIndex[i] >= 0)
                {
                    orbitRadiusX *= STATIC_PLANET_SCALE;
                    orbitRadiusY *= STATIC_PLANET_SCALE;
                }

                var proj = projectedPlanets[i];
                bool isMoon = parentIndex[i] >= 0;
                float markerRadius = (isMoon ? STATIC_MOON_BODY_RADIUS_PX : STATIC_PLANET_BODY_RADIUS_PX) * Scale;
                float orbitLinePad = markerRadius * STATIC_ORBIT_OUTWARD_FROM_PLANET;
                float ringRadiusX = orbitRadiusX + orbitLinePad;
                float ringRadiusY = orbitRadiusY + orbitLinePad;
                Vector2 ringCenter = parentIndex[i] >= 0 ? screenPos[parentIndex[i]] : center;

                if (orbitMeters >= STATIC_ORBIT_MIN_RING_METERS && ringRadiusX >= 2f)
                {
                    ringProjections.Add(new StaticRingProjection
                    {
                        Center = ringCenter,
                        Size = new Vector2(ringRadiusX * 2f, ringRadiusY * 2f),
                        IsMoonRing = isMoon,
                        SortHeight = ringRadiusY
                    });
                }

                double angle = orbitAngles[i];
                var pScreen = new Vector2(
                    ringCenter.X + (float)Math.Cos(angle) * orbitRadiusX,
                    ringCenter.Y + (float)Math.Sin(angle) * orbitRadiusY);
                proj.ScreenPos = pScreen;
                screenPos[i] = pScreen;
                proj.MarkerRadius = markerRadius;
                projectedPlanets[i] = proj;
            }

            ringProjections.Sort((a, b) =>
            {
                if (a.IsMoonRing != b.IsMoonRing)
                    return a.IsMoonRing ? 1 : -1; // main rings first, moon rings after

                return b.SortHeight.CompareTo(a.SortHeight); // taller rings first inside each group
            });
            float ringLineWidth = Math.Max(1f, STATIC_ORBIT_LINE_WIDTH_PX * Scale);
            for (int i = 0; i < ringProjections.Count; i++)
                DrawEllipseRing(ringSprites, ringProjections[i].Center, ringProjections[i].Size, ringLineWidth,
                    ringColor, BackgroundColor);

            projectedPlanets.Sort((a, b) => a.MarkerRadius.CompareTo(b.MarkerRadius));
            for (int i = 0; i < projectedPlanets.Count; i++)
            {
                var planet = projectedPlanets[i];
                DrawPlanet(planet);
                projectedPlanets[i] = planet;
            }

            _dynamicMapCacheValid = false;
            _cachedDynamicRingSprites.Clear();
            _cachedOverlaySprites.Clear();
            _cachedStaticRingSprites.Clear();
            _cachedStaticRingSprites.AddRange(ringSprites);
            _cachedInteractiveEntries.Clear();
            _cachedInteractiveEntries.AddRange(InteractiveList);
            _staticOrbitCacheValid = true;
            return true;
        }

        void DrawEllipseRing(List<MySprite> sprites, Vector2 centerPos, Vector2 ellipseSize, float lineWidth,
            Color lineColor, Color backColor)
        {
            if (ellipseSize.X <= 0f || ellipseSize.Y <= 0f || lineWidth <= 0f)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = centerPos,
                Size = ellipseSize,
                Color = lineColor,
                Alignment = TextAlignment.CENTER
            });

            Vector2 innerSize = ellipseSize - lineWidth * Vector2.One;
            if (innerSize.X <= 0f || innerSize.Y <= 0f)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = centerPos,
                Size = innerSize,
                Color = backColor,
                Alignment = TextAlignment.CENTER
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
            float configuredFov = AppConfig?.FoV ?? MAP_VERTICAL_FOV_DEFAULT_DEG;
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
            var nameSize = FormatingHelper.GetSizeInPixel(planet.Name, "White", nameScale, Surface);
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
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = nameScale
            });


            float distanceScale = 0.6f * Scale * FontScale;
            float distanceOffset = planet.MarkerRadius + 10f;
            string distanceText = FormatingHelper.DistanceToString((float)planet.Distance);
            var distanceSize = FormatingHelper.GetSizeInPixel(distanceText, "White", distanceScale, Surface);
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
                FontId = "White",
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
                lineSizes[i] = FormatingHelper.GetSizeInPixel(lineTexts[i], "White", sideInfoScale, Surface);
                if (lineSizes[i].X > maxLineWidth)
                    maxLineWidth = lineSizes[i].X;
                if (lineSizes[i].Y > maxLineHeight)
                    maxLineHeight = lineSizes[i].Y;
            }

            bool placeOnRight = planet.ScreenPos.X <= ViewBox.Center.X;
            float lineStep = FormatingHelper.GetSizeInPixel("Ag", "White", sideInfoScale, Surface).Y + 2f;
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
                FontId = "White",
                Alignment = alignment,
                RotationOrScale = textScale
            });

            var textRect = GetTextBounds(textPosition, size, alignment);
            var rect = ExtendTextBoundsToPlanetCenter(textRect, planetCenterX);

            if (line == null)
                return rect;
            
            var cursor = line.GetCursor();
            bool hasEntry = line.IsClickable || cursor.HasValue;
            if (!hasEntry)
                return rect;

            var entry = new InteractiveRectangleEntry(
                rect,
                cursor ?? (line.IsClickable ? CursorType.Hand : CursorType.Default),
                line.GetDataContext(),
                line.GetOnClick());
            entry.ClickSound = line.GetClickSound();
            entry.CustomRender = delegate(InteractiveEntry renderEntry, InteractiveRenderContext context, List<MySprite> targetSprites)
            {
                if (line.IsClickable)
                    DrawTextHitboxUnderline(textRect, labelColor, targetSprites, textScale);
            };
            InteractiveList.Add(entry);
            return rect;
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
                    (value, sender) => { ClickOnGps(planet.Name, planet.WorldPosition, planet.Texture.BaseColor); })
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
            string jumpText = FormatPropertyLine("Jump", LocHelper.GetLoc("LcdMod_NotAvailable"));
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
                        ClickOnGps("JumpPoint_" + planet.Name, jumpPoint, planet.Texture.BaseColor);
                    };
                },
                getCursor: () =>
                {
                    refresh();
                    var jumpDrives = GridLogic != null ? GridLogic.GetJumpDrives() : null;
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
            var jumpDrives = GridLogic != null ? GridLogic.GetJumpDrives() : null;
            if (jumpDrives == null || jumpDrives.Count == 0)
            {
                text = FormatPropertyLine("Jump", LocHelper.GetLoc("LcdMod_NotAvailable"));
                return false;
            }

            if (IsJumpPointUiThrottled(planet.PlanetId, planet.Distance, _jumpPointRunCounter, out etaSeconds))
            {
                text = FormatPropertyLine("Jump",
                    string.Format(FormatingHelper.Culture, "Calculating... (eta {0} sec)", etaSeconds));
                return false;
            }

            if (GridLogic.TryGetPlanetJumpPoint(
                    planet.PlanetId,
                    planet.Name,
                    planet.WorldPosition,
                    planet.Radius,
                    planet.GravityRange,
                    out jumpPoint,
                    AppConfig.DisplayMode == 0))
            {
                text = FormatPropertyLine("Jump", FormatingHelper.FormatBearing(GetReferenceMatrix(), jumpPoint));
                return true;
            }

            text = FormatPropertyLine("Jump", LocHelper.GetLoc("LcdMod_NotAvailable"));
            return false;
        }

        MatrixD GetReferenceMatrix()
        {
            return Block != null ? Block.WorldMatrix : MatrixD.Identity;
        }

        void ClickOnGps(string planetName, Vector3D position, Color color)
        {
            var gps = MyAPIGateway.Session.GPS.Create(planetName, string.Empty, position, true, true);
            gps.GPSColor = color;
            MyAPIGateway.Session.GPS.AddLocalGps(gps);
            SendGpsToChat(planetName, position, color);
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
            MyAPIGateway.Utilities.ShowMessage(Title, gps);
        }

        static string SanitizeGpsName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? "Unknown"
                : name.Replace(":", "_");
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
            var center = planet.ScreenPos;
            var radius = planet.MarkerRadius;
            var entry = new InteractiveCircleEntry(center, radius, CursorType.Hand, planet.PlanetId);
            if (AppConfig != null && AppConfig.DisplayMode == (int)DisplayMode.Legacy)
            {
                entry.SetTooltip(new InteractiveTooltip(
                    () => planet.Name,
                    BuildPlanetInfoLines(planet, false),
                    () => FormatingHelper.DistanceToString((float)planet.Distance),
                    GetCursor, TooltipActivationMode.Click, TooltipActivationMode.Click));
            }
            entry.CustomRender = delegate(InteractiveEntry renderEntry, InteractiveRenderContext context, List<MySprite> targetSprites)
            {
                DrawPlanetVisual(targetSprites, planet, renderEntry, context);
                RestoreTextureClip(targetSprites);
            };
            InteractiveList.Add(entry);
        }

        void DrawPlanetVisual(
            List<MySprite> sprites,
            PlanetProjection planet,
            InteractiveEntry entry,
            InteractiveRenderContext context)
        {
            var circle = entry as InteractiveCircleEntry;
            var center = circle != null ? circle.Center : planet.ScreenPos;
            var radius = circle != null ? circle.Radius : planet.MarkerRadius;
            var texture = planet.Texture;

            var baseColor = ApplyAlpha(texture.BaseColor, planet.Visibility);
            float diameter = radius * 2f;
            float overlayDiameter = diameter * (1f + OVERLAY_GROW_RATIO);
            float overlayOffsetX = radius * OVERLAY_OFFSET_RATIO;
            var shadeColor = ApplyAlpha(texture.BaseColor.MulValue(SHADE_MUL), planet.Visibility);

            if (planet.ShouldDisplayInfo)
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = center,
                    Size = new Vector2(diameter + 10 * context.Scale),
                    Color = ApplyAlpha(context.PanelColor, planet.Visibility),
                    Alignment = TextAlignment.CENTER
                });
            }

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = center,
                Size = new Vector2(diameter),
                Color = baseColor,
                Alignment = TextAlignment.CENTER
            });

            if (diameter < PLANET_SHADING_MIN_DIAMETER_PX) return;

            int targetX = (int)(center.X - radius);
            int targetY = (int)(center.Y - radius);
            int targetW = Math.Max(1, (int)diameter);
            int targetH = Math.Max(1, (int)diameter);

            int targetRight = targetX + targetW;
            int targetBottom = targetY + targetH;

            RectangleF textureBounds = GetTextureBounds();
            var viewBounds = new Rectangle(
                (int)Math.Floor(textureBounds.X),
                (int)Math.Floor(textureBounds.Y),
                Math.Max(1, (int)Math.Ceiling(textureBounds.Width)),
                Math.Max(1, (int)Math.Ceiling(textureBounds.Height)));

            int clipX = Math.Max(viewBounds.X, targetX);
            int clipY = Math.Max(viewBounds.Y, targetY);
            int clipRight = Math.Min(viewBounds.Right, targetRight);
            int clipBottom = Math.Min(viewBounds.Bottom, targetBottom);

            if (clipRight <= clipX || clipBottom <= clipY) return;

            int splitX = MathHelper.Clamp((int)Math.Floor(center.X), clipX, clipRight);
            int shadowLeft = clipX;
            int shadowRight = splitX;
            int rightLeft = splitX;
            int rightRight = clipRight;
            bool hasLocalClip = false;

            if (rightRight > rightLeft)
            {
                if (baseColor.A != 255)
                {
                    var rightClip = new Rectangle(rightLeft, clipY, rightRight - rightLeft, clipBottom - clipY);
                    sprites.Add(MySprite.CreateClipRect(rightClip));
                    hasLocalClip = true;

                    // this is technically a "color correction" sprite,
                    // since when the alpha is != 0 the left side will have a shadow bellow the base pass,
                    // I need to add this here too so the color does not differ from different sides
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Circle",
                        Position = center,
                        Size = new Vector2(diameter),
                        Color = shadeColor,
                        Alignment = TextAlignment.CENTER
                    });
                }


                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = center,
                    Size = new Vector2(diameter),
                    Color = baseColor,
                    Alignment = TextAlignment.CENTER
                });
            }


            if (shadowRight > shadowLeft)
            {
                var leftClip = new Rectangle(shadowLeft, clipY, shadowRight - shadowLeft, clipBottom - clipY);
                sprites.Add(MySprite.CreateClipRect(leftClip));
                hasLocalClip = true;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = center,
                    Size = new Vector2(diameter),
                    Color = shadeColor,
                    Alignment = TextAlignment.CENTER
                });

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = center + new Vector2(overlayOffsetX, 0f),
                    Size = new Vector2(overlayDiameter),
                    Color = baseColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            int litLeft = clipX;
            int litRight = clipRight;
            if (texture.PolarCapColor.HasValue)
                hasLocalClip |= DrawPolarCaps(sprites, center, radius,
                    litLeft, litRight,
                    ApplyAlpha(texture.PolarCapColor.Value, planet.Visibility));
            if (texture.EquatorColor.HasValue)
                hasLocalClip |= DrawEquator(sprites, center, radius,
                    litLeft, litRight,
                    ApplyAlpha(texture.EquatorColor.Value, planet.Visibility));
            if (hasLocalClip)
                RestoreTextureClip(sprites);
        }

        CursorType? GetCursor() => _busy ? CursorType.AppStarting : CursorType.Default;

        bool DrawPolarCaps(List<MySprite> sprites, Vector2 center, float radius, int litLeft, int litRight,
            Color capColor)
        {
            float diameter = radius * 2f;
            float capHeight = diameter * POLAR_CAP_RATIO;

            float planetTop = center.Y - radius;
            float planetBottom = center.Y + radius;
            if (litRight <= litLeft)
                return false;

            bool hasLocalClip = false;

            int topY = Math.Max((int)ViewBox.Y, (int)Math.Floor(planetTop));
            int topBottom = Math.Min((int)ViewBox.Bottom, (int)Math.Ceiling(planetTop + capHeight));
            if (topBottom > topY)
            {
                var topClip = new Rectangle(litLeft, topY, litRight - litLeft, topBottom - topY);
                sprites.Add(MySprite.CreateClipRect(topClip));
                hasLocalClip = true;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = center,
                    Size = new Vector2(diameter),
                    Color = capColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            int bottomY = Math.Max((int)ViewBox.Y, (int)Math.Floor(planetBottom - capHeight));
            int bottomBottom = Math.Min((int)ViewBox.Bottom, (int)Math.Ceiling(planetBottom));
            if (bottomBottom > bottomY)
            {
                var bottomClip = new Rectangle(litLeft, bottomY, litRight - litLeft, bottomBottom - bottomY);
                sprites.Add(MySprite.CreateClipRect(bottomClip));
                hasLocalClip = true;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = center,
                    Size = new Vector2(diameter),
                    Color = capColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            return hasLocalClip;
        }

        bool DrawEquator(List<MySprite> sprites, Vector2 center, float radius, int litLeft, int litRight,
            Color equatorColor)
        {
            float diameter = radius * 2f;
            float equatorHeight = diameter * EQUATOR_BAND_RATIO;
            float halfEquator = equatorHeight * 0.5f;
            if (litRight <= litLeft)
                return false;

            int bandTop = Math.Max((int)ViewBox.Y, (int)Math.Floor(center.Y - halfEquator));
            int bandBottom = Math.Min((int)ViewBox.Bottom, (int)Math.Ceiling(center.Y + halfEquator));
            if (bandBottom <= bandTop)
                return false;

            var bandClip = new Rectangle(litLeft, bandTop, litRight - litLeft, bandBottom - bandTop);
            sprites.Add(MySprite.CreateClipRect(bandClip));
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = center,
                Size = new Vector2(diameter),
                Color = equatorColor,
                Alignment = TextAlignment.CENTER
            });
            return true;
        }

        protected override void OnLookAt(Vector2 onScreenCoordinates)
        {
            _eyeTracking.Receive(onScreenCoordinates);
            base.OnLookAt(onScreenCoordinates);
        }

        protected override void OnMouseScroll(int delta, ref bool handled)
        {
            base.OnMouseScroll(delta, ref handled);
            
            if (AppConfig == null || delta == 0 || handled)
                return;

            float magnification = SliderFov.FovToMagnification(AppConfig.FoV);
            float step = delta > 0 ? 1.1f : 1f / 1.1f;
            float nextMagnification = magnification * step;
            float nextFov = SliderFov.MagnificationToFov(nextMagnification);

            if (Math.Abs(AppConfig.FoV - nextFov) <= 0.001f)
                return;

            AppConfig.FoV = nextFov;
            _lastFovChangedFrame = GetCurrentGameFrame();
            _lastKnownConfigFov = float.NaN;
            _syncConfigNextRun = true;
        }
    }
}
