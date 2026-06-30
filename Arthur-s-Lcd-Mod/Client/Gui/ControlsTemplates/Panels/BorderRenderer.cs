using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    public static class BorderRenderer
    {
        public const float DEFAULT_RADIUS_PIXELS = 6f;

        public static List<MySprite> SpritesBuffer = new List<MySprite>(16);

        public static float ScaleRadius(float radiusPixels, float scale)
        {
            var size = radiusPixels * Math.Max(0f, scale);
            return size < 1f ? 0f : (int)size;
        }

        public static void CreateSpritesFromRect(
            RectangleF rect,
            List<MySprite> sprites,
            Color? color = null,
            float radiusPixels = DEFAULT_RADIUS_PIXELS,
            float radiusScale = 1f,
            Color? strokeColor = null,
            float strokeThicknessPixels = 0f)
        {
            radiusPixels = ScaleRadius(radiusPixels, radiusScale);

            Color fill = color ?? Color.Gray;
            float stroke = Math.Max(0f, strokeThicknessPixels * Math.Max(0f, radiusScale));
            if (stroke > 0f)
            {
                Color outline = strokeColor ?? fill;
                if (fill.A < 255 || outline.A < 255)
                {
                    throw new NotSupportedException(
                        "Rounded rectangle stroke with alpha is not supported by the text-surface sprite API. " +
                        "Use an opaque fill/stroke or render a single translucent fill without stroke.");
                }

                DrawSingleRect(rect, sprites, outline, radiusPixels);

                RectangleF inner = Inset(rect, stroke);
                if (inner.Width > 0f && inner.Height > 0f)
                    DrawSingleRect(inner, sprites, fill, Math.Max(0f, radiusPixels - stroke));
                return;
            }

            DrawSingleRect(rect, sprites, fill, radiusPixels);
        }

        static void DrawSingleRect(
            RectangleF rect,
            List<MySprite> sprites,
            Color color,
            float radiusPixels)
        {
            if (color.A == 0 || rect.Width <= 0f || rect.Height <= 0f)
                return;

            if (radiusPixels <= 0f)
                sprites.Add(new MySprite(0, "SquareSimple", rect.Center, rect.Size, color));
            else
                sprites.AddRange(DrawRectangle(rect, color, 1f, radiusPixels));
        }

        static RectangleF Inset(RectangleF rect, float amount)
        {
            float inset = Math.Max(0f, amount);
            return new RectangleF(
                rect.X + inset,
                rect.Y + inset,
                Math.Max(0f, rect.Width - inset * 2f),
                Math.Max(0f, rect.Height - inset * 2f));
        }

        public static void CreateBorderSpritesFromRect(
            RectangleF rect,
            List<MySprite> sprites,
            Color backgroundColor,
            Color borderColor,
            float radiusPixels = DEFAULT_RADIUS_PIXELS,
            float radiusScale = 1f,
            float thicknessPixels = 1f)
        {
            if (sprites == null || rect.Width <= 0f || rect.Height <= 0f)
                return;

            float scale = Math.Max(0f, radiusScale);
            float thickness = Math.Max(0f, thicknessPixels * scale);
            if (thickness <= 0f || borderColor.A == 0)
                return;

            if (backgroundColor.A != byte.MaxValue)
            {
                throw new NotSupportedException(
                    "Inner rounded borders require a fully opaque background. " +
                    "Any background alpha would let the border layer show through the inset background.");
            }

            CreateSpritesFromRect(
                rect,
                sprites,
                borderColor,
                radiusPixels: radiusPixels,
                radiusScale: scale);
        }

        public static MySprite[] DrawRectangle(RectangleF rectangle, Color color, float finalScale = 1f,
            float radiusPixels = DEFAULT_RADIUS_PIXELS)
        {
            if (color.A == 0)
                return Array.Empty<MySprite>();

            SpritesBuffer.Clear();
            Vector2 fullSize = rectangle.Size * finalScale;

            float r = Math.Min(radiusPixels * finalScale, Math.Min(fullSize.X, fullSize.Y) * 0.5f);
            if (r <= 0f)
            {
                SpritesBuffer.Add(new MySprite(0, "SquareSimple", rectangle.Center, fullSize, color));
                return SpritesBuffer.ToArray();
            }

            if (color.A < 255)
                DrawTranslucentRectangle(rectangle.Center, fullSize, color, r, SpritesBuffer);
            else
                DrawOpaqueRectangle(rectangle.Center, fullSize, color, r, SpritesBuffer);

#if LAYOUT_DEBUG
            SpritesBuffer.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareHollow",
                Position = rectangle.Center,
                Size = fullSize,
                Color = Color.Red,
                Alignment = TextAlignment.CENTER
            });
#endif

            return SpritesBuffer.ToArray();
        }

        static void DrawOpaqueRectangle(
            Vector2 center,
            Vector2 fullSize,
            Color color,
            float radius,
            List<MySprite> sprites)
        {
            Vector2 half = fullSize * 0.5f;
            Vector2 coreSize = new Vector2(
                fullSize.X - 2f * radius,
                fullSize.Y - 2f * radius);

            MySprite tx = new MySprite(0, "SquareSimple", center, coreSize, color);
            sprites.Add(tx);

            Vector2 cornerSize = new Vector2(radius * 2f, radius * 2f);
            MySprite corner = tx;
            corner.Data = "Circle";
            corner.Size = cornerSize;

            corner.Position = center + new Vector2(-half.X + radius, -half.Y + radius);
            sprites.Add(corner);

            corner.Position = center + new Vector2(half.X - radius, -half.Y + radius);
            sprites.Add(corner);

            corner.Position = center + new Vector2(-half.X + radius, half.Y - radius);
            sprites.Add(corner);

            corner.Position = center + new Vector2(half.X - radius, half.Y - radius);
            sprites.Add(corner);

            MySprite edge = tx;
            Vector2 horizontalEdgeSize = new Vector2(fullSize.X - 2f * radius, 2f * radius);
            Vector2 verticalEdgeSize = new Vector2(2f * radius, fullSize.Y - 2f * radius);

            edge.Size = horizontalEdgeSize;
            edge.Position = center + new Vector2(0f, -half.Y + radius);
            sprites.Add(edge);

            edge.Position = center + new Vector2(0f, half.Y - radius);
            sprites.Add(edge);

            edge.Size = verticalEdgeSize;
            edge.Position = center + new Vector2(-half.X + radius, 0f);
            sprites.Add(edge);

            edge.Position = center + new Vector2(half.X - radius, 0f);
            sprites.Add(edge);
        }
        
        static void DrawTranslucentRectangle(
            Vector2 center,
            Vector2 fullSize,
            Color color,
            float radius,
            List<MySprite> sprites)
        {
            int left = (int)Math.Floor(center.X - fullSize.X * 0.5f);
            int top = (int)Math.Floor(center.Y - fullSize.Y * 0.5f);
            int right = (int)Math.Ceiling(center.X + fullSize.X * 0.5f);
            int bottom = (int)Math.Ceiling(center.Y + fullSize.Y * 0.5f);

            int width = Math.Max(0, right - left);
            int height = Math.Max(0, bottom - top);
            if (width <= 0 || height <= 0)
                return;

            int sliceRadius = Math.Max(1, (int)Math.Round(radius));
            sliceRadius = Math.Min(sliceRadius, Math.Min(width, height) / 2);
            if (sliceRadius <= 0)
            {
                AddRectangleSprite(sprites, left, top, width, height, color);
                return;
            }

            int centerWidth = Math.Max(0, width - sliceRadius * 2);
            int centerHeight = Math.Max(0, height - sliceRadius * 2);

            // 2, 4 and 6. The middle slice spans all three center-row cells.
            AddRectangleSprite(sprites, left + sliceRadius, top,
                centerWidth, sliceRadius, color);
            AddRectangleSprite(sprites, left, top + sliceRadius,
                width, centerHeight, color);
            AddRectangleSprite(sprites, left + sliceRadius, bottom - sliceRadius,
                centerWidth, sliceRadius, color);

            float diameter = sliceRadius * 2f;

            // 1, 3, 5 and 7. A new clip replaces the previous corner clip, so no
            // clear-clip sprites are needed between corners.
            AddClippedCorner(sprites,
                new Rectangle(left, top, sliceRadius, sliceRadius),
                new Vector2(left + sliceRadius, top + sliceRadius), diameter, color);
            AddClippedCorner(sprites,
                new Rectangle(right - sliceRadius, top, sliceRadius, sliceRadius),
                new Vector2(right - sliceRadius, top + sliceRadius), diameter, color);
            AddClippedCorner(sprites,
                new Rectangle(left, bottom - sliceRadius, sliceRadius, sliceRadius),
                new Vector2(left + sliceRadius, bottom - sliceRadius), diameter, color);
            AddClippedCorner(sprites,
                new Rectangle(right - sliceRadius, bottom - sliceRadius, sliceRadius, sliceRadius),
                new Vector2(right - sliceRadius, bottom - sliceRadius), diameter, color);

            // Restore the owning element's rectangular clip. Parent controls can
            // further intersect this through their existing clipping behavior.
            sprites.Add(MySprite.CreateClipRect(new Rectangle(left, top, width, height)));
        }

        static void AddRectangleSprite(
            List<MySprite> sprites,
            float x,
            float y,
            float width,
            float height,
            Color color)
        {
            if (width <= 0f || height <= 0f)
                return;

            sprites.Add(new MySprite(
                SpriteType.TEXTURE,
                "SquareSimple",
                new Vector2(x + width * 0.5f, y + height * 0.5f),
                new Vector2(width, height),
                color));
        }

        static void AddRectangleSprite(
            List<MySprite> sprites,
            int x,
            int y,
            int width,
            int height,
            Color color)
        {
            if (width <= 0 || height <= 0)
                return;

            sprites.Add(new MySprite(
                SpriteType.TEXTURE,
                "SquareSimple",
                new Vector2(x + width * 0.5f, y + height * 0.5f),
                new Vector2(width, height),
                color));
        }

        static void AddClippedCorner(
            List<MySprite> sprites,
            Rectangle clip,
            Vector2 circleCenter,
            float diameter,
            Color color)
        {
            if (clip.Width <= 0 || clip.Height <= 0)
                return;

            sprites.Add(MySprite.CreateClipRect(clip));
            sprites.Add(new MySprite(
                SpriteType.TEXTURE,
                "Circle",
                circleCenter,
                new Vector2(diameter, diameter),
                color));
        }
    }
}
