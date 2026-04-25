using System;
using System.Collections.Generic;
using Generated;
using Graph.Apps.Abstract;
using Graph.Extensions;
using Graph.Helpers;
using Graph.System;
using Graph.System.TerminalControls.Generic;
using Sandbox.Game.Entities;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace Graph.Apps
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class StarMapSurfaceScript : SurfaceScriptBase, IUsesTerminalControl<SliderFov>
    {
        float _fov;
        double _halfFovY;
        float _lastKnownConfigFov = float.NaN;
        IMyGravityProviderSystem _gravityProvider;

        struct PlanetProjection
        {
            public string Name;
            public PlanetHelper.PlanetTextureStyle Texture;
            public Vector3D Direction;
            public double Distance;
            public float Visibility;
            public double AngularRadius;
            public Vector2 ScreenPos;
            public float MarkerRadius;
            public bool ShouldDisplayInfo;
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

        protected override string DefaultTitle => TITLE;

        public StarMapSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _fov = GetEffectiveVerticalFovDeg();
            _halfFovY = MathHelper.ToRadians(_fov) * 0.5;
            _lastKnownConfigFov = Config != null ? Config.FoV : MAP_VERTICAL_FOV_DEFAULT_DEG;
        }

        public override void Run()
        {
            base.Run();
            if (Config == null)
                return;

            if (float.IsNaN(_lastKnownConfigFov) || Math.Abs(_lastKnownConfigFov - Config.FoV) > 0.001f)
                LayoutChanged();

            using (var frame = Surface.DrawFrame())
            {
                var baseSprites = new List<MySprite>();
                var overlaySprites = new List<MySprite>();
                AddBackground(baseSprites);
                DrawPlanetMap(baseSprites, overlaySprites);
                DrawFovHud(baseSprites, _fov);
                DrawTitle(overlaySprites);
                frame.AddRange(baseSprites);
                frame.AddRange(overlaySprites);
            }
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
            string text = "FOV: " + fovDeg.ToString("0.#", FormatingHelper.Culture) + "º";
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

        void DrawPlanetMap(List<MySprite> planetSprites, List<MySprite> overlaySprites)
        {
            var planets = PlanetHelper.PlanetsById;
            if (planets == null || planets.Count == 0 || Block == null)
                return;

            MatrixD world = Block.WorldMatrix;
            Vector3D camPos = world.Translation;
            Vector3D camRight = world.Right;
            Vector3D camUp = world.Up;
            Vector3D camForward = world.Forward;
            long gravityPlanetId = GetCurrentGravityPlanetId(camPos, planets);
            float gravityVisibility = GetGravityVisibility(camPos);

            if (_halfFovY < 1e-6)
                return;

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
                projectedPlanets.Add(new PlanetProjection
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "Unknown Planet" : name,
                    Texture = PlanetHelper.ResolvePlanetTexture(name),
                    Direction = delta / distance,
                    Distance = distance,
                    Visibility = visibility,
                    AngularRadius = angularRadius,
                    ScreenPos = screenPos,
                    MarkerRadius = markerRadius,
                    ShouldDisplayInfo = touchesCenter
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
            float configuredFov = Config != null ? Config.FoV : MAP_VERTICAL_FOV_DEFAULT_DEG;
            return MathHelper.Clamp(
                configuredFov > 0f ? configuredFov : MAP_VERTICAL_FOV_DEFAULT_DEG,
                1f, 120f);
        }

        static Color ApplyAlpha(Color color, float alpha)
        {
            return new Color(color, MathHelper.Clamp(alpha, 0f, 1f));
        }

        void DrawPlanetLabels(List<MySprite> sprites, PlanetProjection planet)
        {
            if (planet.Visibility <= 0.001f)
                return;

            const float nameScale = 0.65f;
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



            const float distanceScale = 0.6f;
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

        void DrawPlanet(List<MySprite> sprites, PlanetProjection planet)
        {
            var center = planet.ScreenPos;
            var radius = planet.MarkerRadius;
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
                    Size = new Vector2(diameter + 10 * Scale),
                    Color = ApplyAlpha(Config.HeaderColor, planet.Visibility),
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
                return;

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
                return;

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
    }
}
