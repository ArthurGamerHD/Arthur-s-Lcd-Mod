using System;
using Adk.Gui.Rendering;
using VRageMath;

namespace Cartography.Maps
{
    internal static class ScreenSpaceRoadStroke
    {
        public static void Draw(PixelCanvas canvas, Vector2 start, Vector2 end,
            Color color, int width, int firstRow = 0, int endRow = int.MaxValue)
        {
            // Rasterize only visible pixels, even when unrestricted zoom puts
            // endpoints billions of pixels away or makes the road fill the view.
            // Rows are limited to [firstRow, endRow) so bands can run in parallel.
            double radius = Math.Max(Math.Sqrt(0.5), width / 2.0);
            int top = (int)Math.Max(Math.Max(0, firstRow), Math.Min(canvas.Height,
                Math.Floor(Math.Min(start.Y, end.Y) - radius)));
            int bottom = (int)Math.Max(-1, Math.Min(Math.Min(canvas.Height, endRow) - 1,
                Math.Ceiling(Math.Max(start.Y, end.Y) + radius)));
            double dx = (double)end.X - start.X;
            double dy = (double)end.Y - start.Y;
            double lengthSquared = dx * dx + dy * dy;
            for (int y = top; y <= bottom; y++)
            {
                double spanLeft;
                double spanRight;
                if (!TryGetCapsuleRowSpan(start, end, radius, y, out spanLeft, out spanRight))
                    continue;
                int left = (int)Math.Max(0, Math.Min(canvas.Width, Math.Floor(spanLeft)));
                int right = (int)Math.Max(-1, Math.Min(canvas.Width - 1, Math.Ceiling(spanRight)));
                for (int x = left; x <= right; x++)
                {
                    double px = x - (double)start.X;
                    double py = y - (double)start.Y;
                    double t = lengthSquared > 0
                        ? Math.Max(0, Math.Min(1, (px * dx + py * dy) / lengthSquared)) : 0;
                    px -= t * dx;
                    py -= t * dy;
                    // Half a pixel's diagonal keeps even subpixel/thin curves connected.
                    if (px * px + py * py <= radius * radius)
                        canvas.SetPixel(x, y, color);
                }
            }
        }

        /// <summary>
        /// Writes <paramref name="value"/> into every mask pixel covered by the
        /// capsule, keeping the larger value. The mask covers canvas rows
        /// [maskTop, maskEnd), <paramref name="width"/> pixels each.
        /// </summary>
        public static void Mark(ushort[] mask, int width, int maskTop, int maskEnd,
            Vector2 start, Vector2 end, double radius, ushort value)
        {
            int top = (int)Math.Max(maskTop, Math.Min(maskEnd,
                Math.Floor(Math.Min(start.Y, end.Y) - radius)));
            int bottom = (int)Math.Max(maskTop - 1, Math.Min(maskEnd - 1,
                Math.Ceiling(Math.Max(start.Y, end.Y) + radius)));
            double dx = (double)end.X - start.X;
            double dy = (double)end.Y - start.Y;
            double lengthSquared = dx * dx + dy * dy;
            for (int y = top; y <= bottom; y++)
            {
                double spanLeft;
                double spanRight;
                if (!TryGetCapsuleRowSpan(start, end, radius, y, out spanLeft, out spanRight))
                    continue;
                int left = (int)Math.Max(0, Math.Min(width, Math.Floor(spanLeft)));
                int right = (int)Math.Max(-1, Math.Min(width - 1, Math.Ceiling(spanRight)));
                int row = (y - maskTop) * width;
                for (int x = left; x <= right; x++)
                {
                    double px = x - (double)start.X;
                    double py = y - (double)start.Y;
                    double t = lengthSquared > 0
                        ? Math.Max(0, Math.Min(1, (px * dx + py * dy) / lengthSquared)) : 0;
                    px -= t * dx;
                    py -= t * dy;
                    if (px * px + py * py <= radius * radius && mask[row + x] < value)
                        mask[row + x] = value;
                }
            }
        }

        /// <summary>
        /// Horizontal extent of the capsule (segment widened by radius) on row y.
        /// The capsule is convex, so its slice is the union of the slices of its
        /// two end discs and its side rectangle.
        /// </summary>
        public static bool TryGetCapsuleRowSpan(Vector2 start, Vector2 end, double radius,
            double y, out double left, out double right)
        {
            left = double.MaxValue;
            right = double.MinValue;
            AddDiscSpan(start, radius, y, ref left, ref right);
            AddDiscSpan(end, radius, y, ref left, ref right);
            double dx = (double)end.X - start.X;
            double dy = (double)end.Y - start.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length > 0)
            {
                double nx = -dy / length * radius;
                double ny = dx / length * radius;
                double ax = start.X + nx, ay = start.Y + ny;
                double bx = end.X + nx, by = end.Y + ny;
                double cx = end.X - nx, cy = end.Y - ny;
                double qx = start.X - nx, qy = start.Y - ny;
                AddEdgeSpan(ax, ay, bx, by, y, ref left, ref right);
                AddEdgeSpan(bx, by, cx, cy, y, ref left, ref right);
                AddEdgeSpan(cx, cy, qx, qy, y, ref left, ref right);
                AddEdgeSpan(qx, qy, ax, ay, y, ref left, ref right);
            }
            return left <= right;
        }

        static void AddDiscSpan(Vector2 center, double radius, double y,
            ref double left, ref double right)
        {
            double offset = y - center.Y;
            double squared = radius * radius - offset * offset;
            if (squared < 0) return;
            double half = Math.Sqrt(squared);
            left = Math.Min(left, center.X - half);
            right = Math.Max(right, center.X + half);
        }

        static void AddEdgeSpan(double ax, double ay, double bx, double by, double y,
            ref double left, ref double right)
        {
            if ((ay - y) * (by - y) > 0) return;
            if (ay == by)
            {
                left = Math.Min(left, Math.Min(ax, bx));
                right = Math.Max(right, Math.Max(ax, bx));
                return;
            }
            double x = ax + (y - ay) * (bx - ax) / (by - ay);
            left = Math.Min(left, x);
            right = Math.Max(right, x);
        }
    }
}
