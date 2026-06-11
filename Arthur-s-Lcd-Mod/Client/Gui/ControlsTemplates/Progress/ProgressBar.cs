using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Progress
{
    public sealed class ProgressBar : RectangleControl
    {
        public ProgressBar(RectangleF bounds) : base(bounds, null, null)
        {
            Fraction = 0f;
            CornerRadius = -1f;
            ProgressBarStyle = ProgressBarStyle.PillBleed;
        }

        public float Fraction { get; set; }
        public Color? FillColor { get; set; }
        public Color? BackgroundColor { get; set; }
        public Color? FillColorOverride { get; set; }
        public float CornerRadius { get; set; }
        public ProgressBarStyle ProgressBarStyle { get; set; }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = GetViewBox();
            var fill = FillColor ?? context.Style.GetTextColor(false);
            var background = BackgroundColor ?? context.Style.GetPanelColor(false);
            BarPanel.CreateSprites(
                sprites,
                new Vector2(rect.X, rect.Y),
                rect.Size,
                fill,
                background,
                Fraction,
                FillColorOverride,
                CornerRadius,
                ProgressBarStyle);
        }
    }
    
    public enum ProgressBarStyle
    {
        PillBleed,
        Ellipse
    }
    
    public static class BarPanel
    {


        public static void CreateSprites(
            List<MySprite> sprites,
            Vector2 posTopLeft,
            Vector2 size,
            Color fillColor,
            Color bgColor,
            float fraction,
            Color? fillColorOverride = null,
            float cornerRadius = -1f,
            ProgressBarStyle style = ProgressBarStyle.PillBleed)
        {
            var f = fraction > .99f ? 1 : MathHelper.Clamp(fraction, 0f, 1f);
            var normalizedSize = new Vector2(MathHelper.Max(1f, size.X), MathHelper.Max(1f, size.Y));
            var normalizedPosition = new Vector2(posTopLeft.X, posTopLeft.Y + (normalizedSize.Y / 2f));
            var maxR = normalizedSize.Y * 0.5f;
            var radius = cornerRadius > 0f ? MathHelper.Min(cornerRadius, maxR) : maxR;
            var renderFillColor = fillColorOverride ?? fillColor;

            if (style == ProgressBarStyle.Ellipse)
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
                AddPillFill(sprites, normalizedPosition, normalizedSize, radius, fillW, renderFillColor);
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

        static void AddPillFill(
            List<MySprite> sprites,
            Vector2 position,
            Vector2 size,
            float radius,
            float width,
            Color color)
        {
            var w = MathHelper.Clamp(width, 0f, size.X);
            var h = size.Y;
            if (w <= 0f || h <= 0f)
                return;

            var r = radius;
            var d = r * 2f;
            var bleed = MathHelper.Clamp(h * 0.08f, 1f, 3f);
            AddClip(sprites, new RectangleF(position.X, position.Y - h * 0.5f, w, h));
            sprites.Add(MakeTex("Circle", position, new Vector2(d, h), color));

            if (w > r + 0.001f)
            {
                if (w > d + 0.001f)
                {
                    var rectX = r - bleed;
                    var rectW = w - d + 2f * bleed;
                    if (rectW > 0.25f)
                        sprites.Add(MakeTex("SquareSimple", position + new Vector2(rectX, 0f), new Vector2(rectW, h), color));
                }

                sprites.Add(MakeTex("Circle", position + new Vector2(w - d, 0f), new Vector2(d, h), color));
            }

            sprites.Add(MySprite.CreateClearClipRect());
        }

        static void AddClip(List<MySprite> sprites, RectangleF bounds)
        {
            int x = (int)System.Math.Floor(bounds.X);
            int y = (int)System.Math.Floor(bounds.Y);
            int right = (int)System.Math.Ceiling(bounds.Right);
            int bottom = (int)System.Math.Ceiling(bounds.Bottom);
            sprites.Add(MySprite.CreateClipRect(new Rectangle(x, y, System.Math.Max(0, right - x), System.Math.Max(0, bottom - y))));
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
