using System;
using System.Collections.Generic;
using Acrux.Rendering;
using LcdMod.Client.Apps.Maps;
using VRageMath;

namespace LcdMod.Client.Apps.Radar
{
    /// <summary>
    /// Perspective orbit camera over the radar plane. Radar space is X right, Y backward, Z up, in meters around the
    /// reference block; screen pixels grow right and down.
    /// </summary>
    internal struct RadarCamera
    {
        public const float MINIMUM_PITCH = .08f;
        public const float MAXIMUM_PITCH = 1.53f;
        const float DISTANCE_RANGES = 2.4f;

        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Right;
        public Vector3 Up;
        public Vector2 Center;
        public float Focal;
        public float Near;
        public float Distance;
        public float ReferenceDistance;

        public static RadarCamera Create(RadarScene scene, Vector2I canvas)
        {
            float range = Math.Max(1f, scene.WorldRange);
            float pitch = MathHelper.Clamp(scene.Pitch, MINIMUM_PITCH, MAXIMUM_PITCH);
            float cosPitch = (float)Math.Cos(pitch);
            float sinPitch = (float)Math.Sin(pitch);
            var direction = new Vector3((float)Math.Sin(scene.Yaw) * cosPitch, (float)Math.Cos(scene.Yaw) * cosPitch, sinPitch);
            var target = new Vector3(scene.PanMeters.X, scene.PanMeters.Y, 0f);
            var camera = new RadarCamera();
            camera.ReferenceDistance = range * DISTANCE_RANGES;
            camera.Distance = camera.ReferenceDistance / Math.Max(.0001f, scene.ZoomScale);
            camera.Position = target + direction * camera.Distance;
            camera.Forward = -direction;
            camera.Right = Vector3.Normalize(new Vector3(-camera.Forward.Y, camera.Forward.X, 0f));
            camera.Up = Vector3.Cross(camera.Forward, camera.Right);
            camera.Center = new Vector2(canvas.X * .5f, canvas.Y * .46f);
            float fit = Math.Min(canvas.X * .45f, canvas.Y * .40f / Math.Max(.35f, sinPitch));
            camera.Focal = DISTANCE_RANGES * fit;
            camera.Near = camera.Distance * .05f;
            return camera;
        }

        public float Depth(Vector3 point)
        {
            return Vector3.Dot(point - Position, Forward);
        }

        public bool TryProject(Vector3 point, out Vector2 pixel)
        {
            Vector3 v = point - Position;
            float depth = Vector3.Dot(v, Forward);
            if (depth < Near)
            {
                pixel = default(Vector2);
                return false;
            }
            pixel = Center + new Vector2(Vector3.Dot(v, Right), -Vector3.Dot(v, Up)) * (Focal / depth);
            return true;
        }

        public bool TryProjectSegment(Vector3 a, Vector3 b, out Vector2 pa, out Vector2 pb)
        {
            float da = Depth(a);
            float db = Depth(b);
            if (da < Near && db < Near)
            {
                pa = pb = default(Vector2);
                return false;
            }
            if (da < Near)
                a = Vector3.Lerp(a, b, (Near - da) / (db - da));
            else if (db < Near)
                b = Vector3.Lerp(b, a, (Near - db) / (da - db));
            return TryProject(a, out pa) & TryProject(b, out pb);
        }

        // Apparent size of an object at point relative to the target at the default zoom.
        public float Perspective(Vector3 point)
        {
            return MathHelper.Clamp(ReferenceDistance / Math.Max(Near, Depth(point)), .35f, 4f);
        }

    }

    /// <summary>Rasterizes the 3D radar plane, rings and contact stems; contact icons are returned as 2D billboards.</summary>
    internal static class RadarRenderer
    {
        const int CIRCLE_SEGMENTS = 96;
        const float QUADRANT_COVERAGE = .8f;
        const float LOCK_ANIM_DISTANCE = 20f;
        const float ICON_SIZE = 16f;
        // A contact's icon takes clicks this far around its centre, in pixels at the default zoom.
        const float HIT_RADIUS = 14f;
        const float MISSILE_SIZE = .8f;

        static readonly List<Vector2> PolygonBuffer = new List<Vector2>(CIRCLE_SEGMENTS);
        static readonly List<float> Crossings = new List<float>(8);
        static readonly List<Vector3> WorldPolygon = new List<Vector3>(CIRCLE_SEGMENTS);

        public static float PixelScale(Vector2I canvas)
        {
            return Math.Max(1f, Math.Min(canvas.X, canvas.Y) / 400f);
        }

        public static void Render(PixelCanvas canvas, RadarScene scene)
        {
            canvas.Clear(Color.Transparent);
            var size = new Vector2I(canvas.Width, canvas.Height);
            if (size.X <= 0 || size.Y <= 0)
                return;
            RadarCamera camera = RadarCamera.Create(scene, size);
            float px = PixelScale(size);
            float range = Math.Max(1f, scene.WorldRange);
            Color line = scene.LineColor;

            FillDisc(canvas, camera, Vector3.Zero, range, new Color(line, .12f));
            float[] rings = scene.RingDistances;
            for (int i = 0; i < rings.Length; i++)
                if (rings[i] > 0f && rings[i] < range)
                    StrokeCircle(canvas, camera, rings[i], 2.5f * px, new Color(line, .65f));
            StrokeCircle(canvas, camera, range, 4f * px, line);

            var quadrant = new Color(line, .5f);
            float inner = range * (1f - QUADRANT_COVERAGE);
            DrawLine3D(canvas, camera, new Vector3(-range, 0f, 0f), new Vector3(-inner, 0f, 0f), 2f * px, quadrant);
            DrawLine3D(canvas, camera, new Vector3(inner, 0f, 0f), new Vector3(range, 0f, 0f), 2f * px, quadrant);
            DrawLine3D(canvas, camera, new Vector3(0f, -range, 0f), new Vector3(0f, -inner, 0f), 2f * px, quadrant);
            DrawLine3D(canvas, camera, new Vector3(0f, inner, 0f), new Vector3(0f, range, 0f), 2f * px, quadrant);

            float ship = 16f * px * camera.ReferenceDistance / camera.Focal;
            FillPolygon3D(canvas, camera, line,
                new Vector3(0f, -ship * .8f, 0f), new Vector3(ship * .5f, ship * .5f, 0f), new Vector3(-ship * .5f, ship * .5f, 0f));

            for (int i = 0; i < scene.Missiles.Count; i++)
                DrawMissileGeometry(canvas, camera, scene.Missiles[i], range, px);
            for (int i = 0; i < scene.Targets.Count; i++)
                DrawTargetGeometry(canvas, camera, scene.Targets[i], range, px, line);
        }

        // The trail fades out behind the missile; the line ahead is fainter than the trail's head.
        static void DrawMissileGeometry(PixelCanvas canvas, RadarCamera camera, RadarMissile missile, float range, float px)
        {
            Vector3 top = missile.Position * range;
            var foot = new Vector3(top.X, top.Y, 0f);
            if (Math.Abs(top.Z) > 1e-3f)
                DrawLine3D(canvas, camera, foot, top, 1.5f * px, new Color(missile.Color, .45f));
            int count = missile.Trail.Count;
            for (int i = 1; i < count; i++)
            {
                float alpha = .15f + .6f * i / count;
                DrawLine3D(canvas, camera, missile.Trail[i - 1] * range, missile.Trail[i] * range, 2f * px,
                    new Color(missile.Color, alpha));
            }
            if (count > 0)
                DrawLine3D(canvas, camera, missile.Trail[count - 1] * range, top, 2f * px, new Color(missile.Color, .75f));
            DrawLine3D(canvas, camera, top, missile.Ahead * range, 1.5f * px, new Color(missile.Color, .5f));
        }

        // The contact whose icon holds the pixel, nearest the camera first; 0 for none.
        public static long HitTest(RadarScene scene, Vector2I canvas, Vector2 pixel)
        {
            if (canvas.X <= 0 || canvas.Y <= 0)
                return 0;
            RadarCamera camera = RadarCamera.Create(scene, canvas);
            float range = Math.Max(1f, scene.WorldRange);
            float px = PixelScale(canvas);
            long hit = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < scene.Targets.Count; i++)
            {
                RadarTarget target = scene.Targets[i];
                if (target.Marker == RadarMarker.OutOfRange || target.EntityId == 0)
                    continue;
                Vector3 point = target.Position * range;
                Vector2 icon;
                if (!camera.TryProject(point, out icon))
                    continue;
                float radius = HIT_RADIUS * px * camera.Perspective(point);
                float depth = camera.Depth(point);
                if (Vector2.DistanceSquared(icon, pixel) > radius * radius || depth >= nearest)
                    continue;
                hit = target.EntityId;
                nearest = depth;
            }
            return hit;
        }

        static void DrawTargetGeometry(PixelCanvas canvas, RadarCamera camera, RadarTarget target, float range, float px,
            Color line)
        {
            Vector3 top = target.Position * range;
            var foot = new Vector3(top.X, top.Y, 0f);
            if (target.Marker != RadarMarker.OutOfRange && Math.Abs(top.Z) > 1e-3f)
            {
                float footScale = camera.Perspective(foot);
                DrawLine3D(canvas, camera, foot, top, 2f * px * Math.Min(1.5f, footScale), target.ElevationColor);
                FillDisc(canvas, camera, foot, 6f * px * camera.ReferenceDistance / camera.Focal, target.ElevationColor);
            }

            Vector2 icon;
            if (!(target.TargetLock || target.LockCandidate) || !camera.TryProject(top, out icon))
                return;
            float scale = px * camera.Perspective(top);
            if (!target.TargetLock)
            {
                BoxCorners(canvas, icon, (ICON_SIZE + 24f) * scale + LOCK_ANIM_DISTANCE * 2f * scale, 8f * scale,
                    2f * scale, line);
                return;
            }
            float lockOffset = (1f - MathHelper.Clamp(target.TargetLockPercent, 0f, 1f)) * LOCK_ANIM_DISTANCE * scale;
            float box = (ICON_SIZE + 24f) * scale + lockOffset * 2f;
            BoxCorners(canvas, icon, box, 10f * scale, 3f * scale, target.IconColor);
            if (target.TargetLockPercent >= .999f)
                Diamond(canvas, icon, (ICON_SIZE + 10f) * scale, 2f * scale, target.IconColor);
        }

        // Contact icons and plane labels as billboards, far to near, positioned in canvas pixels.
        public static void BuildBillboards(RadarScene scene, Vector2I canvas, List<MapMarker> markers)
        {
            if (canvas.X <= 0 || canvas.Y <= 0)
                return;
            RadarCamera camera = RadarCamera.Create(scene, canvas);
            float range = Math.Max(1f, scene.WorldRange);
            var labelColor = new Color(scene.LineColor, .72f);
            AddLabel(markers, camera, "0º", new Vector3(0f, -range * 1.08f, 0f), labelColor);
            AddLabel(markers, camera, "90º", new Vector3(range * 1.1f, 0f, 0f), labelColor);
            AddLabel(markers, camera, "180º", new Vector3(0f, range * 1.08f, 0f), labelColor);
            AddLabel(markers, camera, "270º", new Vector3(-range * 1.1f, 0f, 0f), labelColor);

            int start = markers.Count;
            for (int i = 0; i < scene.Targets.Count; i++)
            {
                RadarTarget target = scene.Targets[i];
                Vector3 point = target.Position * range;
                Vector2 pixel;
                if (!camera.TryProject(point, out pixel))
                    continue;
                var marker = new MapMarker
                {
                    Kind = MapMarkerKind.Radio,
                    Position = pixel,
                    Color = target.IconColor,
                    Depth = camera.Depth(point)
                };
                float perspective = camera.Perspective(point);
                if (target.Marker == RadarMarker.OutOfRange)
                {
                    Vector2 origin;
                    camera.TryProject(Vector3.Zero, out origin);
                    Vector2 direction = pixel - origin;
                    marker.Icon = "Triangle";
                    marker.Size = perspective;
                    marker.Rotation = (float)Math.Atan2(direction.Y, direction.X) + MathHelper.PiOver2;
                }
                else if (!string.IsNullOrEmpty(target.IconTexture))
                {
                    marker.Icon = target.IconTexture;
                    marker.Size = 1.7f * perspective;
                }
                else
                {
                    marker.Icon = MarkerTexture(target.Marker);
                    marker.Size = 1.15f * perspective;
                }
                markers.Add(marker);
            }
            for (int i = 0; i < scene.Missiles.Count; i++)
            {
                RadarMissile missile = scene.Missiles[i];
                Vector3 point = missile.Position * range;
                Vector2 pixel, ahead;
                if (!camera.TryProject(point, out pixel))
                    continue;
                float rotation = 0f;
                if (camera.TryProject(point + missile.Heading * range * .05f, out ahead) &&
                    Vector2.DistanceSquared(ahead, pixel) > 1e-4f)
                    rotation = (float)Math.Atan2(ahead.Y - pixel.Y, ahead.X - pixel.X) + MathHelper.PiOver2;
                markers.Add(new MapMarker
                {
                    Kind = MapMarkerKind.Radio,
                    Position = pixel,
                    Color = missile.Color,
                    Depth = camera.Depth(point),
                    Icon = "Triangle",
                    Size = MISSILE_SIZE * camera.Perspective(point),
                    Rotation = rotation
                });
            }
            markers.Sort(start, markers.Count - start, FarToNear.Instance);
        }

        sealed class FarToNear : IComparer<MapMarker>
        {
            public static readonly FarToNear Instance = new FarToNear();

            public int Compare(MapMarker x, MapMarker y)
            {
                return y.Depth.CompareTo(x.Depth);
            }
        }

        static void AddLabel(List<MapMarker> markers, RadarCamera camera, string text, Vector3 point, Color color)
        {
            Vector2 pixel;
            if (camera.TryProject(point, out pixel))
                markers.Add(new MapMarker { Position = pixel, Label = text, Color = color, CenterLabel = true, Shadow = false });
        }

        static string MarkerTexture(RadarMarker marker)
        {
            switch (marker)
            {
                case RadarMarker.Square:
                    return "SquareSimple";
                case RadarMarker.Circle:
                    return "Circle";
                default:
                    return "Triangle";
            }
        }

        static void FillDisc(PixelCanvas canvas, RadarCamera camera, Vector3 center, float radius, Color color)
        {
            WorldPolygon.Clear();
            for (int i = 0; i < CIRCLE_SEGMENTS; i++)
            {
                double angle = i * MathHelper.TwoPi / CIRCLE_SEGMENTS;
                WorldPolygon.Add(center + new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0f));
            }
            FillWorldPolygon(canvas, camera, WorldPolygon, color);
        }

        // Clips the polygon against the camera near plane, then fills its projection.
        internal static void FillWorldPolygon(PixelCanvas canvas, RadarCamera camera, List<Vector3> points, Color color)
        {
            List<Vector2> polygon = Polygon();
            int count = points.Count;
            for (int i = 0; i < count; i++)
            {
                Vector3 current = points[i];
                Vector3 next = points[(i + 1) % count];
                float currentDepth = camera.Depth(current) - camera.Near;
                float nextDepth = camera.Depth(next) - camera.Near;
                Vector2 pixel;
                if (currentDepth >= 0f && camera.TryProject(current, out pixel))
                    polygon.Add(pixel);
                if (currentDepth >= 0f != nextDepth >= 0f &&
                    camera.TryProject(Vector3.Lerp(current, next, currentDepth / (currentDepth - nextDepth)) + camera.Forward * 1e-3f, out pixel))
                    polygon.Add(pixel);
            }
            FillPolygon(canvas, polygon, color);
        }

        static void StrokeCircle(PixelCanvas canvas, RadarCamera camera, float radius, float width, Color color)
        {
            Vector3 previous = new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= CIRCLE_SEGMENTS; i++)
            {
                double angle = i * MathHelper.TwoPi / CIRCLE_SEGMENTS;
                var next = new Vector3((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius, 0f);
                DrawLine3D(canvas, camera, previous, next, width, color);
                previous = next;
            }
        }

        static void FillPolygon3D(PixelCanvas canvas, RadarCamera camera, Color color, params Vector3[] points)
        {
            WorldPolygon.Clear();
            WorldPolygon.AddRange(points);
            FillWorldPolygon(canvas, camera, WorldPolygon, color);
        }

        static void DrawLine3D(PixelCanvas canvas, RadarCamera camera, Vector3 a, Vector3 b, float width, Color color)
        {
            Vector2 pa;
            Vector2 pb;
            if (camera.TryProjectSegment(a, b, out pa, out pb))
                DrawLine(canvas, pa, pb, width, color);
        }

        static List<Vector2> Polygon()
        {
            PolygonBuffer.Clear();
            return PolygonBuffer;
        }

        // Even-odd scanline fill with fractional coverage at the span ends.
        internal static void FillPolygon(PixelCanvas canvas, List<Vector2> polygon, Color color)
        {
            int count = polygon.Count;
            if (count < 3 || color.A == 0)
                return;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                minY = Math.Min(minY, polygon[i].Y);
                maxY = Math.Max(maxY, polygon[i].Y);
            }
            int top = Math.Max(0, (int)Math.Floor(minY));
            int bottom = Math.Min(canvas.Height - 1, (int)Math.Ceiling(maxY));
            List<float> crossings = Crossings;
            for (int y = top; y <= bottom; y++)
            {
                float sampleY = y + .5f;
                crossings.Clear();
                for (int i = 0; i < count; i++)
                {
                    Vector2 a = polygon[i];
                    Vector2 b = polygon[(i + 1) % count];
                    if (a.Y <= sampleY == b.Y <= sampleY)
                        continue;
                    crossings.Add(a.X + (sampleY - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }
                crossings.Sort();
                for (int i = 0; i + 1 < crossings.Count; i += 2)
                    FillSpan(canvas, y, crossings[i], crossings[i + 1], color);
            }
        }

        static void FillSpan(PixelCanvas canvas, int y, float left, float right, Color color)
        {
            int first = Math.Max(0, (int)Math.Floor(left));
            int last = Math.Min(canvas.Width - 1, (int)Math.Floor(right));
            for (int x = first; x <= last; x++)
            {
                float coverage = Math.Min(x + 1f, right) - Math.Max(x, left);
                if (coverage > 0f)
                    canvas.BlendPixel(x, y, color, coverage);
            }
        }

        // Antialiased thick segment: coverage falls off over one pixel past half the width.
        internal static void DrawLine(PixelCanvas canvas, Vector2 a, Vector2 b, float width, Color color)
        {
            float half = Math.Max(.5f, width * .5f);
            int left = Math.Max(0, (int)Math.Floor(Math.Min(a.X, b.X) - half - 1f));
            int right = Math.Min(canvas.Width - 1, (int)Math.Ceiling(Math.Max(a.X, b.X) + half + 1f));
            int top = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, b.Y) - half - 1f));
            int bottom = Math.Min(canvas.Height - 1, (int)Math.Ceiling(Math.Max(a.Y, b.Y) + half + 1f));
            Vector2 segment = b - a;
            float lengthSquared = segment.LengthSquared();
            for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                var p = new Vector2(x + .5f, y + .5f);
                float t = lengthSquared <= 1e-6f ? 0f : MathHelper.Clamp(Vector2.Dot(p - a, segment) / lengthSquared, 0f, 1f);
                float distance = Vector2.Distance(p, a + segment * t);
                float coverage = half + .5f - distance;
                if (coverage > 0f)
                    canvas.BlendPixel(x, y, color, Math.Min(1f, coverage));
            }
        }

        static void BoxCorners(PixelCanvas canvas, Vector2 center, float box, float length, float width, Color color)
        {
            float h = box * .5f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                var corner = center + new Vector2(sx * h, sy * h);
                DrawLine(canvas, corner, corner - new Vector2(sx * length, 0f), width, color);
                DrawLine(canvas, corner, corner - new Vector2(0f, sy * length), width, color);
            }
        }

        static void Diamond(PixelCanvas canvas, Vector2 center, float size, float width, Color color)
        {
            float h = size * .5f;
            var top = center - new Vector2(0f, h);
            var rightPoint = center + new Vector2(h, 0f);
            var bottomPoint = center + new Vector2(0f, h);
            var leftPoint = center - new Vector2(h, 0f);
            DrawLine(canvas, top, rightPoint, width, color);
            DrawLine(canvas, rightPoint, bottomPoint, width, color);
            DrawLine(canvas, bottomPoint, leftPoint, width, color);
            DrawLine(canvas, leftPoint, top, width, color);
        }
    }
}
