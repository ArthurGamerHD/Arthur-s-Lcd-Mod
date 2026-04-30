using System;
using System.Collections.Generic;
using System.Linq;
using Generated;
using Graph.Apps.Abstract;
using Graph.Apps.Utility;
using Graph.Extensions;
using Graph.Helpers;
using Graph.System;
using Graph.System.Config.Models.Apps;
using Graph.System.TerminalControls.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Graph.Apps
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
        IMyGravityProviderSystem _gravityProvider;
        readonly EyeTrackingFrameState _eyeTracking = new EyeTrackingFrameState();
        readonly List<InteractiveEntry> _interactiveEntries = new List<InteractiveEntry>();
        long _jumpPointRunCounter;

        readonly List<MySprite> _baseSprites = new List<MySprite>();
        readonly List<MySprite> _ringSprites = new List<MySprite>();
        readonly List<MySprite> _planetSprites = new List<MySprite>();
        readonly List<MySprite> _overlaySprites = new List<MySprite>();
        readonly List<MySprite> _sprites = new List<MySprite>();

        const double JUMP_POINT_RUNS_PER_SECOND = 6d; // ScriptUpdate.Update10 at 60 FPS
        struct JumpPointThrottleState
        {
            public long StartRun;
            public long DurationRuns;
            public long LastRequestRun;
        }

        readonly Dictionary<long, JumpPointThrottleState> _jumpPointThrottleByPlanet =
            new Dictionary<long, JumpPointThrottleState>();

        bool _busy;

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
            public InteractiveEntry InteractiveEntry;
        }

        struct StaticRingProjection
        {
            public Vector2 Center;
            public Vector2 Size;
            public bool IsMoonRing;
            public float SortHeight;
        }

        public const string ID = "LCDMod_StarMapSurface";
        public const string TITLE = "LCDMod_StarMapSurface";
        const float SHADE_MUL = 0.75f;
        const float OVERLAY_GROW_RATIO = 0.05f;   // relative to diameter
        const float OVERLAY_OFFSET_RATIO = 0.25f; // relative to radius
        const float POLAR_CAP_RATIO = 0.06f;      // top/bottom % of diameter
        const float EQUATOR_BAND_RATIO = 0.18f;   // % of diameter
        const float MAP_VERTICAL_FOV_DEFAULT_DEG = 70f;
        const float MAP_NEAR_CLIP_METERS = 10f;
        const float PLANET_SHADING_MIN_DIAMETER_PX = 10f;
        const float GRAVITY_FADE_MAX_MULTIPLIER = 0.1f;
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

        public override Vector2 CursorPosition { get; protected set; } = new Vector2(float.NaN, float.NaN);

        public override List<InteractiveEntry> InteractiveEntries => _interactiveEntries;

        public override CursorType CursorType { get; protected set; } = CursorType.Default;

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return StarMapDisplayModes;
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _fov = GetEffectiveVerticalFovDeg();
            _halfFovY = MathHelper.ToRadians(_fov) * 0.5;
            _lastKnownConfigFov = AppConfig != null ? AppConfig.FoV : MAP_VERTICAL_FOV_DEFAULT_DEG;

            CursorType = GetDefaultCursorType();
        }

        CursorType GetDefaultCursorType()
        {
            return AppConfig != null && AppConfig.DisplayMode == DisplayMode.Legacy
                ? CursorType.Default
                : CursorType.None;
        }

        public override void Run()
        {
            base.Run();
            if (AppConfig == null)
                return;
            _jumpPointRunCounter++;

            if (float.IsNaN(_lastKnownConfigFov) || Math.Abs(_lastKnownConfigFov - AppConfig.FoV) > 0.001f)
                LayoutChanged();

            RenderSprites(GetSprites);
        }
        
        List<MySprite> GetSprites()
        {
            _baseSprites.Clear();
            _ringSprites.Clear();
            _planetSprites.Clear();
            _overlaySprites.Clear();
            _interactiveEntries.Clear();
            _busy = false;
            CursorType = GetDefaultCursorType();
            
            bool staticMode = AppConfig != null && AppConfig.DisplayMode == DisplayMode.Legacy;
            Vector2 lookedAt;
            if (_eyeTracking.TryConsumeMapped(ViewBox, out lookedAt))
                CursorPosition = lookedAt;
            else if (!HasRecentVisualContact)
            {
                CursorPosition = new Vector2(float.NaN, float.NaN);
                ClearTooltip();
            }

            AddBackground(_baseSprites);
            DrawTitle(_overlaySprites);
            var hasPlanets = DrawPlanetMap(_planetSprites, _ringSprites, _overlaySprites);
            if (hasPlanets)
            {
                if (!staticMode)
                    DrawFovHud(_overlaySprites, _fov);
            }
            else
            {
                DrawMessage(_overlaySprites, LocHelper.Empty, "Warning", AppConfig.WarningColor, AppConfig.Scale);
            }

            _sprites.Clear();
            _sprites.AddRange(_baseSprites);
            _sprites.AddRange(_ringSprites);
            _sprites.AddRange(_planetSprites);
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

        void DrawFovHud(List<MySprite> sprites, float fovDeg)
        {
            const float textScale = 0.55f;
            double baseHalfFov = MathHelper.ToRadians(MAP_VERTICAL_FOV_DEFAULT_DEG) * 0.5;
            double currentHalfFov = MathHelper.ToRadians(Math.Max(0.1f, fovDeg)) * 0.5;
            double magnification = Math.Tan(baseHalfFov) / Math.Tan(currentHalfFov);
            string text = "MAG: " + magnification.ToString("0.##", FormatingHelper.Culture) + "x";
            var textSize = GetSizeInPixel(text, "White", textScale, Surface);
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

        bool DrawPlanetMap(List<MySprite> planetSprites, List<MySprite> ringSprites, List<MySprite> overlaySprites)
        {
            var planets = PlanetHelper.PlanetsById;
            if (planets == null || planets.Count == 0)
                return false;
            bool hasDetectedPlanets = false;

            Vector3D camPos;
            Vector3D camRight;
            Vector3D camUp;
            Vector3D camForward;
            bool staticMode = AppConfig != null && AppConfig.DisplayMode == DisplayMode.Legacy;
            if (staticMode)
            {
                return DrawStaticOrbitMap(ringSprites, planetSprites, overlaySprites, planets);
            }
            else
            {
                if (Block == null)
                    return false;

                MatrixD world = Block.WorldMatrix;
                camPos = world.Translation;
                camRight = world.Right;
                camUp = world.Up;
                camForward = world.Forward;
            }
            long gravityPlanetId = GetCurrentGravityPlanetId(camPos, planets);
            float gravityVisibility = GetGravityVisibility(camPos);

            if (_halfFovY < 1e-6)
                return false;

            double aspect = ViewBox.Width / Math.Max(1f, ViewBox.Height);
            double halfFovX = Math.Atan(Math.Tan(_halfFovY) * aspect);
            var projectedPlanets = new List<PlanetProjection>(planets.Count);

            foreach (var kv in planets)
            {
                var planet = kv.Value;
                if (planet == null || planet.MarkedForClose)
                    continue;

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
                float visibility = planet.EntityId == gravityPlanetId ? gravityVisibility : 1f;
                if (visibility <= 0.001f)
                    continue;

                // Keep drawing while any part of the planet disk overlaps the viewport.
                if (screenPos.X + markerRadius < ViewBox.X ||
                    screenPos.X - markerRadius > ViewBox.Right ||
                    screenPos.Y + markerRadius < ViewBox.Y ||
                    screenPos.Y - markerRadius > ViewBox.Bottom)
                    continue;

                float centerDistance = Vector2.Distance(screenPos, ViewBox.Center);
                bool touchesCenter = markerRadius >= centerDistance - 2 * Scale;
                string name;
                if (!PlanetHelper.PlanetNamesById.TryGetValue(planet.EntityId, out name))
                    name = planet.Name;
                string generatorName;
                PlanetHelper.PlanetGeneratorNamesById.TryGetValue(planet.EntityId, out generatorName);
                var textureKey = string.IsNullOrWhiteSpace(generatorName) ? name : generatorName;
                var generator = planet.Generator;
                var atmosphere = generator != null ? generator.Atmosphere : null;
                MyTemperatureLevel? averageTemperature = generator != null
                    ? (MyTemperatureLevel?)generator.DefaultSurfaceTemperature
                    : null;
                double surfaceGravity = generator != null ? generator.SurfaceGravity : 0d;
                double gravityFalloff = generator != null ? generator.GravityFalloffPower : 0d;
                double gravityLimitRadius = 0d;
                if (planet.MaximumRadius > 0d && surfaceGravity > 0d && gravityFalloff > 0d)
                    gravityLimitRadius = planet.MaximumRadius * Math.Pow(surfaceGravity / 0.05d, 1d / gravityFalloff);
                projectedPlanets.Add(new PlanetProjection
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
                    ShouldDisplayInfo = touchesCenter,
                    Radius = planet.AverageRadius,
                    SurfaceGravityG = (float)surfaceGravity,
                    GravityRange = (float)(Math.Max(0d, gravityLimitRadius - planet.AverageRadius)),
                    AtmosphereDensity = planet.HasAtmosphere && atmosphere != null ? atmosphere.Density : 0f,
                    OxygenDensity = planet.HasAtmosphere && atmosphere != null ? atmosphere.OxygenDensity : 0f,
                    AverageTemperature = averageTemperature,
                    MaxWindSpeed = atmosphere != null ? atmosphere.MaxWindSpeed : 0f
                });
            }

            projectedPlanets.Sort((a, b) => a.Distance.CompareTo(b.Distance)); // near -> far
            var visiblePlanets = new List<PlanetProjection>(projectedPlanets.Count);

            for (int i = 0; i < projectedPlanets.Count; i++)
            {
                var candidate = projectedPlanets[i];
                bool occluded = false;

                for (int j = 0; j < visiblePlanets.Count; j++)
                {
                    if (IsFullyOccludedBy(visiblePlanets[j], candidate))
                    {
                        occluded = true;
                        break;
                    }
                }

                if (!occluded)
                    visiblePlanets.Add(candidate);
            }

            for (int i = visiblePlanets.Count - 1; i >= 0; i--) // far -> near draw order
            {
                var planet = visiblePlanets[i];
                DrawPlanet(planetSprites, planet);
                DrawPlanetLabels(overlaySprites, planet);
            }

            return hasDetectedPlanets;
        }

        bool DrawStaticOrbitMap(
            List<MySprite> ringSprites,
            List<MySprite> planetSprites,
            List<MySprite> overlaySprites,
            Dictionary<long, MyPlanet> planets)
        {
            if (planets == null || planets.Count == 0)
                return false;
            bool hasDetectedPlanets = false;

            var validPlanets = new List<MyPlanet>(planets.Count);
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
                double distanceToBlock = Block != null ? Vector3D.Distance(pos, referencePos) : pos.Length();
                validPlanets.Add(planet);
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
                var atmosphere = generator != null ? generator.Atmosphere : null;
                MyTemperatureLevel? averageTemperature = generator != null
                    ? (MyTemperatureLevel?)generator.DefaultSurfaceTemperature
                    : null;
                double surfaceGravity = generator != null ? generator.SurfaceGravity : 0d;
                double gravityFalloff = generator != null ? generator.GravityFalloffPower : 0d;
                double gravityLimitRadius = 0d;
                if (planet.MaximumRadius > 0d && surfaceGravity > 0d && gravityFalloff > 0d)
                    gravityLimitRadius = planet.MaximumRadius * Math.Pow(surfaceGravity / 0.05d, 1d / gravityFalloff);

                projectedPlanets.Add(new PlanetProjection
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
                    MaxWindSpeed = atmosphere != null ? atmosphere.MaxWindSpeed : 0f
                });
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

            for (int order = 0; order < computeOrder.Count; order++)
            {
                int i = computeOrder[order];
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
                planet.InteractiveEntry = DrawPlanet(planetSprites, planet);
                projectedPlanets[i] = planet;
            }

            if (float.IsNaN(CursorPosition.X) || float.IsNaN(CursorPosition.Y))
                return true;

            var focusPoint = CursorPosition;
            int selectedIndex = -1;
            float selectedDistance = float.MaxValue;
            bool selectedByPlanet = false;
            bool insideKeepOpenArea = CursorInsideTooltipKeepOpenArea;

            if (CursorInsideTooltip)
            {
                for (int i = 0; i < projectedPlanets.Count; i++)
                {
                    if (!IsActiveTooltipParent(projectedPlanets[i].PlanetId))
                        continue;

                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex < 0 && insideKeepOpenArea)
            {
                for (int i = 0; i < projectedPlanets.Count; i++)
                {
                    var planet = projectedPlanets[i];
                    if (!IsActiveTooltipParent(planet.PlanetId))
                        continue;

                    selectedIndex = i;
                    selectedByPlanet = planet.InteractiveEntry != null && planet.InteractiveEntry.Hit(focusPoint);
                    break;
                }
            }

            if (selectedIndex < 0)
            {
                for (int i = 0; i < projectedPlanets.Count; i++)
                {
                    var planet = projectedPlanets[i];
                    if (planet.InteractiveEntry == null || !planet.InteractiveEntry.Hit(focusPoint))
                        continue;
                    float d = Vector2.Distance(planet.ScreenPos, focusPoint);
                    if (d < selectedDistance)
                    {
                        selectedDistance = d;
                        selectedIndex = i;
                        selectedByPlanet = true;
                    }
                }
            }

            if (selectedIndex >= 0)
            {
                var selected = projectedPlanets[selectedIndex];
                selected.ShouldDisplayInfo = true;
                bool pointerInsideTooltip = DrawTooltip(
                    overlaySprites,
                    selected.InteractiveEntry,
                    selected.Name,
                    BuildPlanetInfoLines(selected, false),
                    FormatingHelper.DistanceToString((float)selected.Distance));
                CursorType = selectedByPlanet
                    ? CursorType.Hand
                    : CursorInsideClickableTooltipContent
                    ? CursorType.Hand
                    : _busy && pointerInsideTooltip
                    ? CursorType.AppStarting
                    : CursorType.Default;
            }
            else
            {
                ClearTooltip();
            }

            return true;
        }

        void DrawEllipseRing(List<MySprite> sprites, Vector2 centerPos, Vector2 ellipseSize, float lineWidth, Color lineColor, Color backColor)
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

        bool TryGetStaticCamera(Dictionary<long, MyPlanet> planets, out Vector3D camPos, out Vector3D camRight, out Vector3D camUp, out Vector3D camForward)
        {
            camRight = Vector3D.Right;
            camUp = Vector3D.Up;
            camForward = Vector3D.Backward;
            camPos = Vector3D.Zero;

            if (planets == null || planets.Count == 0)
                return false;

            var centers = new List<Vector3D>(planets.Count);
            var radii = new List<double>(planets.Count);
            Vector3D center = Vector3D.Zero;
            foreach (var kv in planets)
            {
                var p = kv.Value;
                if (p == null || p.MarkedForClose || p.AverageRadius <= 0d)
                    continue;

                var c = p.WorldMatrix.Translation;
                centers.Add(c);
                radii.Add(p.AverageRadius);
                center += c;
            }

            if (centers.Count == 0)
                return false;

            center /= centers.Count;

            // Build covariance matrix around centroid.
            double xx = 0d;
            double xy = 0d;
            double xz = 0d;
            double yy = 0d;
            double yz = 0d;
            double zz = 0d;
            for (int i = 0; i < centers.Count; i++)
            {
                var d = centers[i] - center;
                xx += d.X * d.X;
                xy += d.X * d.Y;
                xz += d.X * d.Z;
                yy += d.Y * d.Y;
                yz += d.Y * d.Z;
                zz += d.Z * d.Z;
            }

            // Symmetric covariance matrix:
            // [xx xy xz]
            // [xy yy yz]
            // [xz yz zz]
            // Find major axes by power iteration + deflation, then normal = cross(major1, major2).
            Vector3D major1 = Vector3D.Normalize(new Vector3D(1d, 0d, 0d));
            for (int it = 0; it < 12; it++)
            {
                Vector3D next = new Vector3D(
                    xx * major1.X + xy * major1.Y + xz * major1.Z,
                    xy * major1.X + yy * major1.Y + yz * major1.Z,
                    xz * major1.X + yz * major1.Y + zz * major1.Z);
                if (next.LengthSquared() <= 1e-10)
                    break;
                major1 = Vector3D.Normalize(next);
            }

            double lambda1 =
                major1.X * (xx * major1.X + xy * major1.Y + xz * major1.Z) +
                major1.Y * (xy * major1.X + yy * major1.Y + yz * major1.Z) +
                major1.Z * (xz * major1.X + yz * major1.Y + zz * major1.Z);

            double bxx = xx - lambda1 * major1.X * major1.X;
            double bxy = xy - lambda1 * major1.X * major1.Y;
            double bxz = xz - lambda1 * major1.X * major1.Z;
            double byy = yy - lambda1 * major1.Y * major1.Y;
            double byz = yz - lambda1 * major1.Y * major1.Z;
            double bzz = zz - lambda1 * major1.Z * major1.Z;

            Vector3D major2 = Vector3D.Normalize(new Vector3D(0d, 1d, 0d));
            if (Math.Abs(Vector3D.Dot(major2, major1)) > 0.9)
                major2 = Vector3D.Normalize(new Vector3D(0d, 0d, 1d));

            for (int it = 0; it < 12; it++)
            {
                Vector3D next = new Vector3D(
                    bxx * major2.X + bxy * major2.Y + bxz * major2.Z,
                    bxy * major2.X + byy * major2.Y + byz * major2.Z,
                    bxz * major2.X + byz * major2.Y + bzz * major2.Z);

                // Keep orthogonal to first axis.
                next -= major1 * Vector3D.Dot(next, major1);
                if (next.LengthSquared() <= 1e-10)
                    break;
                major2 = Vector3D.Normalize(next);
            }

            Vector3D normal = Vector3D.Cross(major1, major2);
            if (normal.LengthSquared() <= 1e-8)
                normal = Vector3D.Up;
            else
                normal = Vector3D.Normalize(normal);

            double aspect = ViewBox.Width / Math.Max(1f, ViewBox.Height);
            double halfFovYAt1X = MathHelper.ToRadians(MAP_VERTICAL_FOV_DEFAULT_DEG) * 0.5;
            double halfFovXAt1X = Math.Atan(Math.Tan(halfFovYAt1X) * aspect);
            double tanX = Math.Tan(halfFovXAt1X);
            double tanY = Math.Tan(halfFovYAt1X);
            if (tanX <= 1e-6 || tanY <= 1e-6)
                return false;

            // Evaluate both normal directions (up/down) and pick the tighter fit.
            double distPlus;
            double distMinus;
            if (!TryComputeStaticDistance(center, centers, radii, normal, tanX, tanY, out distPlus))
                return false;
            if (!TryComputeStaticDistance(center, centers, radii, -normal, tanX, tanY, out distMinus))
                return false;

            Vector3D viewDir = distPlus <= distMinus ? normal : -normal;
            double cameraDistance = Math.Min(distPlus, distMinus);
            if (cameraDistance < 1d)
                cameraDistance = 1d;

            camPos = center - viewDir * cameraDistance;
            camForward = Vector3D.Normalize(center - camPos);

            Vector3D worldUp = Vector3D.Up;
            camRight = Vector3D.Cross(worldUp, camForward);
            if (camRight.LengthSquared() < 1e-8)
                camRight = Vector3D.Cross(Vector3D.Right, camForward);
            if (camRight.LengthSquared() < 1e-8)
                camRight = Vector3D.Right;
            else
                camRight = Vector3D.Normalize(camRight);
            camUp = Vector3D.Normalize(Vector3D.Cross(camForward, camRight));
            return true;
        }

        bool TryComputeStaticDistance(Vector3D center, List<Vector3D> centers, List<double> radii, Vector3D viewDir, double tanX, double tanY, out double distance)
        {
            distance = 0d;
            if (centers == null || centers.Count == 0)
                return false;

            // Build camera basis for this direction.
            Vector3D worldUp = Vector3D.Up;
            Vector3D right = Vector3D.Cross(worldUp, viewDir);
            if (right.LengthSquared() < 1e-8)
                right = Vector3D.Cross(Vector3D.Right, viewDir);
            if (right.LengthSquared() < 1e-8)
                return false;
            right = Vector3D.Normalize(right);
            Vector3D up = Vector3D.Normalize(Vector3D.Cross(viewDir, right));

            double required = 0d;
            for (int i = 0; i < centers.Count; i++)
            {
                Vector3D rel = centers[i] - center;
                double r = radii[i];

                double localX = Math.Abs(Vector3D.Dot(rel, right));
                double localY = Math.Abs(Vector3D.Dot(rel, up));
                double localZ = Vector3D.Dot(rel, viewDir);

                double needX = (localX + r) / tanX - localZ;
                double needY = (localY + r) / tanY - localZ;
                double needNear = MAP_NEAR_CLIP_METERS + r - localZ;
                double need = Math.Max(needNear, Math.Max(needX, needY));
                if (need > required)
                    required = need;
            }

            distance = required * 1.2d;
            return true;
        }

        float GetGravityVisibility(Vector3D camPos)
        {
            var gravityProvider = GetGravityProvider();
            if (gravityProvider == null)
                return 1f;

            float naturalGravityMultiplier;
            gravityProvider.CalculateNaturalGravityInPoint(camPos, out naturalGravityMultiplier);
            float t = MathHelper.Clamp(naturalGravityMultiplier / GRAVITY_FADE_MAX_MULTIPLIER, 0f, 1f);
            return 1f - t;
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
            float configuredFov = AppConfig != null ? AppConfig.FoV : MAP_VERTICAL_FOV_DEFAULT_DEG;
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
            var nameSize = GetSizeInPixel(planet.Name, "White", nameScale, Surface);
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
            var distanceSize = GetSizeInPixel(distanceText, "White", distanceScale, Surface);
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

        void DrawPlanetSideInfo(List<MySprite> sprites, PlanetProjection planet, Color labelColor, Vector2 namePos, Vector2 nameSize, Vector2 distancePos, Vector2 distanceSize)
        {
            float sideInfoScale = SIDE_INFO_TEXT_SCALE * Scale * FontScale;
            float sideInfoYOffset = SIDE_INFO_Y_OFFSET_PX * Scale * FontScale;
            var lines = BuildPlanetInfoLines(planet, false);
            var lineTexts = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
                lineTexts[i] = lines[i] != null ? lines[i].ToString() : string.Empty;

            int count = lines.Count;
            var lineSizes = new Vector2[count];
            float maxLineWidth = 0f;
            float maxLineHeight = 0f;
            for (int i = 0; i < count; i++)
            {
                lineSizes[i] = GetSizeInPixel(lineTexts[i], "White", sideInfoScale, Surface);
                if (lineSizes[i].X > maxLineWidth)
                    maxLineWidth = lineSizes[i].X;
                if (lineSizes[i].Y > maxLineHeight)
                    maxLineHeight = lineSizes[i].Y;
            }

            bool placeOnRight = planet.ScreenPos.X <= ViewBox.Center.X;
            float lineStep = GetSizeInPixel("Ag", "White", sideInfoScale, Surface).Y + 2f;
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
                bool overlapsDistance = right >= distLeft && left <= distRight && bottom >= distTop && top <= distBottom;
                return overlapsName || overlapsDistance;
            };

            Func<float, Vector2, float> computeAdjustedX = (yEdge, lineSize) =>
            {
                float dy = yEdge - planet.ScreenPos.Y;
                float inside = planet.MarkerRadius * planet.MarkerRadius - dy * dy;
                float edgeOffset = inside > 0f ? (float)Math.Sqrt(inside) : 0f;
                float x = placeOnRight
                    ? planet.ScreenPos.X + edgeOffset + SIDE_INFO_MARGIN_PX
                    : planet.ScreenPos.X - edgeOffset - SIDE_INFO_MARGIN_PX;

                if (placeOnRight)
                    x = MathHelper.Clamp(x, ViewBox.X + 2f, ViewBox.Right - lineSize.X - 2f);
                else
                    x = MathHelper.Clamp(x, ViewBox.X + lineSize.X + 2f, ViewBox.Right - 2f);

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
                    float yEdge = MathHelper.Clamp(startYPreview + i * lineStep - lineSizes[i].Y * 0.5f,
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
                if (placeOnRight)
                    xBase = MathHelper.Clamp(xBase, ViewBox.X + 2f, ViewBox.Right - maxLineWidth - 2f);
                else
                    xBase = MathHelper.Clamp(xBase, ViewBox.X + maxLineWidth + 2f, ViewBox.Right - 2f);

                float startYBelowName = namePos.Y + nameSize.Y + lineStep;
                float startYFallback = MathHelper.Clamp(startYBelowName,
                    ViewBox.Y + maxLineHeight * 0.5f,
                    ViewBox.Bottom - requiredHeight);

                var fallbackAlignment = placeOnRight ? TextAlignment.LEFT : TextAlignment.RIGHT;
                for (int i = 0; i < count; i++)
                {
                    float y = MathHelper.Clamp(startYFallback + i * lineStep - sideInfoYOffset,
                        ViewBox.Y + lineSizes[i].Y * 0.5f,
                        ViewBox.Bottom - lineSizes[i].Y * 0.5f);
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXT,
                        Data = lineTexts[i],
                        Position = new Vector2(xBase, y),
                        Color = labelColor,
                        FontId = "White",
                        Alignment = fallbackAlignment,
                        RotationOrScale = sideInfoScale
                    });
                }

                return;
            }

            float startY = planet.ScreenPos.Y - ((count - 1) * lineStep * 0.5f);
            var alignment = placeOnRight ? TextAlignment.LEFT : TextAlignment.RIGHT;

            for (int i = 0; i < count; i++)
            {
                float yEdge = MathHelper.Clamp(startY + i * lineStep - lineSizes[i].Y * 0.5f,
                    ViewBox.Y + lineSizes[i].Y * 0.5f,
                    ViewBox.Bottom - lineSizes[i].Y * 0.5f);
                float x = computeAdjustedX(yEdge, lineSizes[i]);

                float y = MathHelper.Clamp(yEdge - sideInfoYOffset,
                    ViewBox.Y + lineSizes[i].Y * 0.5f,
                    ViewBox.Bottom - lineSizes[i].Y * 0.5f);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = lineTexts[i],
                    Position = new Vector2(x, y),
                    Color = labelColor,
                    FontId = "White",
                    Alignment = alignment,
                    RotationOrScale = sideInfoScale
                });
            }
        }

        List<object> BuildPlanetInfoLines(PlanetProjection planet, bool compactRadiusLabel)
        {
            string radiusKey = compactRadiusLabel
                ? "LCDMod_StarMap_Info_RadiusShort"
                : "LCDMod_StarMap_Info_Radius";

            var lines = new List<object>(9)
            {
                string.Format(FormatingHelper.Culture, LocHelper.GetLoc(radiusKey),
                    FormatingHelper.DistanceToString(planet.Radius)),
                string.Format(FormatingHelper.Culture, LocHelper.GetLoc("LCDMod_StarMap_Info_Gravity"),
                    FormatingHelper.GravityToString(planet.SurfaceGravityG)),
                LocHelper.GetLoc("BlockPropertyTitle_OreDetectorRange") + ": " +
                    FormatingHelper.DistanceToString(planet.GravityRange),
                string.Format(FormatingHelper.Culture, LocHelper.GetLoc("LCDMod_StarMap_Info_Atmosphere_Short"),
                    FormatingHelper.PercentageToString(planet.AtmosphereDensity)),
                string.Format(FormatingHelper.Culture, LocHelper.GetLoc("LCDMod_StarMap_Info_O2"),
                    FormatingHelper.PercentageToString(planet.OxygenDensity)),
                string.Format(FormatingHelper.Culture, LocHelper.GetLoc("LCDMod_StarMap_Info_Temperature"),
                    FormatingHelper.TemperatureToString(planet.AverageTemperature)),
                string.Format(FormatingHelper.Culture, LocHelper.GetLoc("LCDMod_StarMap_Info_Wind"),
                    FormatingHelper.WindToString(planet.MaxWindSpeed))
            };

            lines.Add(new ClickableText("Position: " + FormatWorldVector(planet.WorldPosition), planet.WorldPosition));

            if (!compactRadiusLabel && GridLogic != null)
            {
                int etaSeconds;
                var jumpDrives = GridLogic.GetJumpDrives();
                if (jumpDrives == null || jumpDrives.Count == 0)
                {
                    lines.Add("Jump: unavailable");
                }
                else if (IsJumpPointUiThrottled(planet.PlanetId, planet.Distance, _jumpPointRunCounter, out etaSeconds))
                {
                    lines.Add(string.Format(FormatingHelper.Culture, "Calculating... (eta {0} sec)", etaSeconds));
                    _busy = true;
                }
                else
                {
                    Vector3D jumpPoint;
                    if (GridLogic.TryGetPlanetJumpPoint(
                            planet.PlanetId,
                            planet.Name,
                            planet.WorldPosition,
                            planet.Radius,
                            planet.GravityRange,
                            out jumpPoint))
                    {
                        lines.Add(new ClickableText("Jump: " + FormatWorldVector(jumpPoint), jumpPoint));
                    }
                    else
                    {
                        lines.Add("Jump: unavailable");
                    }
                }
            }

            return lines;
        }

        static string FormatWorldVector(Vector3D value)
        {
            return string.Format(FormatingHelper.Culture, "({0:0}, {1:0}, {2:0})", value.X, value.Y, value.Z);
        }

        bool IsJumpPointUiThrottled(long planetId, double distanceMeters, long currentRun, out int etaSeconds)
        {
            etaSeconds = 0;
            JumpPointThrottleState state;
            if (!_jumpPointThrottleByPlanet.TryGetValue(planetId, out state))
            {
                var totalSeconds = Math.Max(1d, 3d + (distanceMeters / 1000000d));
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
                var totalSeconds = Math.Max(1d, 3d + (distanceMeters / 1000000d));
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

        InteractiveEntry DrawPlanet(List<MySprite> sprites, PlanetProjection planet)
        {
            var center = planet.ScreenPos;
            var radius = planet.MarkerRadius;
            var texture = planet.Texture;
            var entry = new InteractiveCircleEntry(center, radius, CursorType.Hand, planet.PlanetId);
            _interactiveEntries.Add(entry);
            
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
                    Size = new Vector2(diameter + 10 * Scale),
                    Color = ApplyAlpha(AppConfig.HeaderColor, planet.Visibility),
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

            if (diameter < PLANET_SHADING_MIN_DIAMETER_PX)
                return entry;

            int targetX = (int)(center.X - radius);
            int targetY = (int)(center.Y - radius);
            int targetW = Math.Max(1, (int)diameter);
            int targetH = Math.Max(1, (int)diameter);

            int targetRight = targetX + targetW;
            int targetBottom = targetY + targetH;

            var viewBounds = new Rectangle(
                (int)ViewBox.X,
                (int)ViewBox.Y,
                Math.Max(1, (int)ViewBox.Width),
                Math.Max(1, (int)ViewBox.Height));

            int clipX = Math.Max(viewBounds.X, targetX);
            int clipY = Math.Max(viewBounds.Y, targetY);
            int clipRight = Math.Min(viewBounds.Right, targetRight);
            int clipBottom = Math.Min(viewBounds.Bottom, targetBottom);

            if (clipRight <= clipX || clipBottom <= clipY)
                return entry;

            int splitX = MathHelper.Clamp((int)Math.Floor(center.X), clipX, clipRight);
            int shadowLeft = clipX;
            int shadowRight = splitX;
            int rightLeft = splitX;
            int rightRight = clipRight;

            if (rightRight > rightLeft)
            {

                if (baseColor.A != 255)
                {
                    var rightClip = new Rectangle(rightLeft, clipY, rightRight - rightLeft, clipBottom - clipY);
                    sprites.Add(MySprite.CreateClipRect(rightClip));

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
                
                if (baseColor.A != 255)
                    sprites.Add(MySprite.CreateClearClipRect());
            }

            
            if (shadowRight > shadowLeft)
            {
                var leftClip = new Rectangle(shadowLeft, clipY, shadowRight - shadowLeft, clipBottom - clipY);
                sprites.Add(MySprite.CreateClipRect(leftClip));

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
                sprites.Add(MySprite.CreateClearClipRect());
            }
            
            int litLeft = clipX;
            int litRight = clipRight;
            if (texture.PolarCapColor.HasValue)
                DrawPolarCaps(sprites, center, radius,
                    litLeft, litRight,
                    ApplyAlpha(texture.PolarCapColor.Value, planet.Visibility));
            if (texture.EquatorColor.HasValue)
                DrawEquator(sprites, center, radius,
                    litLeft, litRight,
                    ApplyAlpha(texture.EquatorColor.Value, planet.Visibility));
            return entry;
        }

        void DrawPolarCaps(List<MySprite> sprites, Vector2 center, float radius, int litLeft, int litRight, Color capColor)
        {
            float diameter = radius * 2f;
            float capHeight = diameter * POLAR_CAP_RATIO;

            float planetTop = center.Y - radius;
            float planetBottom = center.Y + radius;
            if (litRight <= litLeft)
                return;

            int topY = Math.Max((int)ViewBox.Y, (int)Math.Floor(planetTop));
            int topBottom = Math.Min((int)ViewBox.Bottom, (int)Math.Ceiling(planetTop + capHeight));
            if (topBottom > topY)
            {
                var topClip = new Rectangle(litLeft, topY, litRight - litLeft, topBottom - topY);
                sprites.Add(MySprite.CreateClipRect(topClip));
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = center,
                    Size = new Vector2(diameter),
                    Color = capColor,
                    Alignment = TextAlignment.CENTER
                });
                sprites.Add(MySprite.CreateClearClipRect());
            }

            int bottomY = Math.Max((int)ViewBox.Y, (int)Math.Floor(planetBottom - capHeight));
            int bottomBottom = Math.Min((int)ViewBox.Bottom, (int)Math.Ceiling(planetBottom));
            if (bottomBottom > bottomY)
            {
                var bottomClip = new Rectangle(litLeft, bottomY, litRight - litLeft, bottomBottom - bottomY);
                sprites.Add(MySprite.CreateClipRect(bottomClip));
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = center,
                    Size = new Vector2(diameter),
                    Color = capColor,
                    Alignment = TextAlignment.CENTER
                });
                sprites.Add(MySprite.CreateClearClipRect());
            }
        }

        void DrawEquator(List<MySprite> sprites, Vector2 center, float radius, int litLeft, int litRight, Color equatorColor)
        {
            float diameter = radius * 2f;
            float equatorHeight = diameter * EQUATOR_BAND_RATIO;
            float halfEquator = equatorHeight * 0.5f;
            if (litRight <= litLeft)
                return;

            int bandTop = Math.Max((int)ViewBox.Y, (int)Math.Floor(center.Y - halfEquator));
            int bandBottom = Math.Min((int)ViewBox.Bottom, (int)Math.Ceiling(center.Y + halfEquator));
            if (bandBottom <= bandTop)
                return;

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
            sprites.Add(MySprite.CreateClearClipRect());
        }
        
        protected override void OnLookAt(Vector2 onScreenCoordinates)
        {
            _eyeTracking.Receive(onScreenCoordinates);
            RenderSprites(GetSprites);
        }
    }
}
