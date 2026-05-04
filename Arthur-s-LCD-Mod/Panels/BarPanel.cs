using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Graph.Panels
{
    public static class BarPanel
    {
        public enum Style
        {
            PillBleed,
            Ellipse
        }

        public static void CreateSprites(
            List<MySprite> sprites,
            Vector2 posTopLeft,
            Vector2 size,
            Color fillColor,
            Color bgColor,
            float fraction,
            Color? fillColorOverride = null,
            float cornerRadius = -1f,
            Style style = Style.PillBleed)
        {
            var f = fraction > .99f ? 1 : MathHelper.Clamp(fraction, 0f, 1f);
            var normalizedSize = new Vector2(MathHelper.Max(1f, size.X), MathHelper.Max(1f, size.Y));
            var normalizedPosition = new Vector2(posTopLeft.X, posTopLeft.Y + (normalizedSize.Y / 2f));
            var maxR = normalizedSize.Y * 0.5f;
            var radius = cornerRadius > 0f ? MathHelper.Min(cornerRadius, maxR) : maxR;
            var renderFillColor = fillColorOverride ?? fillColor;

            if (style == Style.Ellipse)
            {
                if (f < 1f)
                    sprites.Add(MakeTex("Circle", normalizedPosition, normalizedSize, bgColor));
                if (f > 0f)
                {
                    var w = normalizedSize.X * f;
                    sprites.Add(MakeTex("Circle", normalizedPosition, new Vector2(w, normalizedSize.Y), renderFillColor));
                }
                return;
            }

            if (f < 1f)
                AddPill(sprites, normalizedPosition, normalizedSize, radius, normalizedSize.X, bgColor);

            var fillW = normalizedSize.X * f;
            if (fillW > 0.001f)
                AddPill(sprites, normalizedPosition, normalizedSize, radius, fillW + 1f, renderFillColor, -1f);
        }

        static void AddPill(
            List<MySprite> sprites,
            Vector2 position,
            Vector2 size,
            float radius,
            float width,
            Color color,
            float xOffset = 0f)
        {
            var w = MathHelper.Clamp(width, 0f, size.X);
            var h = size.Y;
            if (w <= 0f || h <= 0f) return;

            var r = radius;
            var d = r * 2f;
            var bleed = MathHelper.Clamp(h * 0.08f, 1f, 3f);

            if (w <= d + 0.001f)
            {
                sprites.Add(MakeTex("Circle", position + new Vector2(xOffset, 0f), new Vector2(w, h), color));
                return;
            }

            sprites.Add(MakeTex("Circle", position + new Vector2(xOffset, 0f), new Vector2(d, h), color));
            sprites.Add(MakeTex("Circle", position + new Vector2(xOffset + (w - d), 0), new Vector2(d, h), color));

            var rectX = r - bleed;
            var rectW = w - 2f * r + 2f * bleed;
            if (rectW > 0.25f)
                sprites.Add(MakeTex("SquareSimple", position + new Vector2(xOffset + rectX, 0f), new Vector2(rectW, h), color));
        }

        static MySprite MakeTex(string name, Vector2 posTopLeft, Vector2 size, Color color)
        {
            return new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = name,
                Position = posTopLeft,
                Size = size,
                Color = color,
                Alignment = TextAlignment.LEFT
            };
        }
    }
}
