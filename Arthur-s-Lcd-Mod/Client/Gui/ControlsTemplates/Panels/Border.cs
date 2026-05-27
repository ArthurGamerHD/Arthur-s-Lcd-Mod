using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    public static class Border
    {
        public const float DEFAULT_RADIUS_PIXELS = 6f;

        public static List<MySprite> SpritesBuffer = new List<MySprite>(16);

        public static float ScaleRadius(float radiusPixels, float scale)
        {
            var size = radiusPixels * Math.Max(0f, scale);
            return size < 1f ? 0f : (int)size;
        }

        public static void CreateSpritesFromRect(RectangleF rect, List<MySprite> sprites, Color? color = null, float radiusPixels = DEFAULT_RADIUS_PIXELS, float radiusScale = 1f)
        {
            radiusPixels = ScaleRadius(radiusPixels, radiusScale);

            if (color == null)
                color = Color.Gray;

            if (radiusPixels <= 0)
                sprites.Add(new MySprite(0, "SquareSimple", rect.Center, rect.Size, color));
            else
                sprites.AddRange(DrawRectangle(rect, color.Value, 1f, radiusPixels));
        }

        public static MySprite[] DrawRectangle(RectangleF rectangle, Color color, float finalScale = 1f,
            float radiusPixels = DEFAULT_RADIUS_PIXELS)
        {
            SpritesBuffer.Clear();
            Vector2 fullSize = rectangle.Size * finalScale;
            Vector2 half = fullSize * 0.5f;

            float r = Math.Min(radiusPixels * finalScale, Math.Min(fullSize.X, fullSize.Y) * 0.5f);
            Vector2 coreSize = new Vector2(
                fullSize.X - 2f * r,
                fullSize.Y - 2f * r
            );

            MySprite tx = new MySprite(0, "SquareSimple", rectangle.Center, coreSize, color);

            SpritesBuffer.Add(tx);

            Vector2 cornerSize = new Vector2(r * 2f, r * 2f);

            Vector2 center = rectangle.Center;

            MySprite corner = tx;
            corner.Data = "Circle";
            corner.Size = cornerSize;

            // corners
            corner.Position = center + new Vector2(-half.X + r, -half.Y + r);
            SpritesBuffer.Add(corner);

            corner.Position = center + new Vector2(half.X - r, -half.Y + r);
            SpritesBuffer.Add(corner);

            corner.Position = center + new Vector2(-half.X + r, half.Y - r);
            SpritesBuffer.Add(corner);

            corner.Position = center + new Vector2(half.X - r, half.Y - r);
            SpritesBuffer.Add(corner);

            // edges
            MySprite edge = tx;
            edge.Data = tx.Data;

            Vector2 horizontalEdgeSize = new Vector2(fullSize.X - 2f * r, 2f * r);
            Vector2 verticalEdgeSize = new Vector2(2f * r, fullSize.Y - 2f * r);

            // top
            edge.Size = horizontalEdgeSize;
            edge.Position = center + new Vector2(0, -half.Y + r);
            SpritesBuffer.Add(edge);

            // bottom
            edge.Position = center + new Vector2(0, half.Y - r);
            SpritesBuffer.Add(edge);

            // left
            edge.Size = verticalEdgeSize;
            edge.Position = center + new Vector2(-half.X + r, 0);
            SpritesBuffer.Add(edge);

            // Right
            edge.Position = center + new Vector2(half.X - r, 0);
            SpritesBuffer.Add(edge);

#if LAYOUT_DEBUG
            // debug draw
            SpritesBuffer.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareHollow",
                Position = center,
                Size = fullSize,
                Color = Color.Red,
                Alignment = TextAlignment.CENTER
            });
#endif

            return SpritesBuffer.ToArray();
        }
    }
}
