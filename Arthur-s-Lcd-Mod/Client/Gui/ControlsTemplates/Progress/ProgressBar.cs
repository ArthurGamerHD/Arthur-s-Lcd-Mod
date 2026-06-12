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
            BarPanel.CreateBackgroundSprites(
                sprites,
                new Vector2(rect.X, rect.Y),
                rect.Size,
                background,
                Fraction,
                CornerRadius,
                ProgressBarStyle);

            var fillWidth = rect.Width * (Fraction > .99f ? 1f : MathHelper.Clamp(Fraction, 0f, 1f));
            if (fillWidth <= 0.001f)
                return;

            var fillClip = new RectangleF(rect.X, rect.Y, fillWidth, rect.Height);
            if (!BeginContentClip(sprites, fillClip))
                return;

            BarPanel.CreateFillSprites(
                sprites,
                new Vector2(rect.X, rect.Y),
                rect.Size,
                FillColorOverride ?? fill,
                CornerRadius,
                ProgressBarStyle);
            EndContentClip(sprites);
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
            CreateBackgroundSprites(sprites, posTopLeft, size, bgColor, f, cornerRadius, style);
            if (f <= 0f)
                return;

            var renderFillColor = fillColorOverride ?? fillColor;
            CreateFillSprites(sprites, posTopLeft, new Vector2(size.X * f, size.Y), renderFillColor, cornerRadius, style);
        }

        public static void CreateBackgroundSprites(
            List<MySprite> sprites,
            Vector2 posTopLeft,
            Vector2 size,
            Color bgColor,
            float fraction,
            float cornerRadius = -1f,
            ProgressBarStyle style = ProgressBarStyle.PillBleed)
        {
            var f = fraction > .99f ? 1 : MathHelper.Clamp(fraction, 0f, 1f);
            if (f >= 1f)
                return;

            var normalizedSize = new Vector2(MathHelper.Max(1f, size.X), MathHelper.Max(1f, size.Y));
            var normalizedPosition = new Vector2(posTopLeft.X, posTopLeft.Y + (normalizedSize.Y / 2f));
            var maxR = normalizedSize.Y * 0.5f;
            var radius = cornerRadius > 0f ? MathHelper.Min(cornerRadius, maxR) : maxR;

            if (style == ProgressBarStyle.Ellipse)
            {
                sprites.Add(MakeTex("Circle", normalizedPosition, normalizedSize, bgColor));
                return;
            }

            AddPill(sprites, normalizedPosition, normalizedSize, radius, normalizedSize.X, bgColor);
        }

        public static void CreateFillSprites(
            List<MySprite> sprites,
            Vector2 posTopLeft,
            Vector2 size,
            Color fillColor,
            float cornerRadius = -1f,
            ProgressBarStyle style = ProgressBarStyle.PillBleed)
        {
            var normalizedSize = new Vector2(MathHelper.Max(1f, size.X), MathHelper.Max(1f, size.Y));
            var normalizedPosition = new Vector2(posTopLeft.X, posTopLeft.Y + (normalizedSize.Y / 2f));
            var maxR = normalizedSize.Y * 0.5f;
            var radius = cornerRadius > 0f ? MathHelper.Min(cornerRadius, maxR) : maxR;

            if (style == ProgressBarStyle.Ellipse)
            {
                sprites.Add(MakeTex("Circle", normalizedPosition, normalizedSize, fillColor));
                return;
            }

            AddPill(sprites, normalizedPosition, normalizedSize, radius, normalizedSize.X, fillColor);
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
