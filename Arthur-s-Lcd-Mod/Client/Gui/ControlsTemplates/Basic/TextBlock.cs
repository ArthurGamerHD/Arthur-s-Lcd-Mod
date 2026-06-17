using System;
using System.Collections.Generic;
using LcdMod.Client.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Basic
{
    public enum TextBlockWrapping
    {
        NoWrap,
        Wrap
    }

    public enum TextBlockVerticalAlignment
    {
        Top,
        Center,
        Bottom
    }

    public sealed class TextBlock : RectangleControl
    {
        public TextBlock(RectangleF bounds) : base(bounds)
        {
            Text = string.Empty;
            FontId = null;
            FontScale = 0.58f;
            LineSpacingPixels = 0f;
            Wrapping = TextBlockWrapping.NoWrap;
            Ellipsize = true;
            HorizontalAlignment = TextAlignment.LEFT;
            VerticalAlignment = TextBlockVerticalAlignment.Center;
        }

        public string Text { get; set; }
        public string FontId { get; set; }
        public float LineSpacingPixels { get; set; }
        public TextBlockWrapping Wrapping { get; set; }
        public bool Ellipsize { get; set; }
        public TextAlignment HorizontalAlignment { get; set; }
        public TextBlockVerticalAlignment VerticalAlignment { get; set; }
        public new Color? TextColor { get; set; }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            if (string.IsNullOrEmpty(Text) || TextSurface == null)
                return;

            var rect = GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            string fontId = string.IsNullOrEmpty(FontId) ? TextFont : FontId;
            float styledFontScale = ResolveStyleValue(ControlTemplate.FontScaleProperty);
            float scale = Math.Max(0.01f, LayoutScale * styledFontScale * FontScale);
            Color color = TextColor ?? base.TextColor;
            if (Wrapping == TextBlockWrapping.Wrap)
                RenderWrapped(rect, sprites, fontId, scale, color);
            else
                RenderSingleLine(rect, sprites, fontId, scale, color);
        }

        void RenderSingleLine(RectangleF rect, List<MySprite> sprites, string fontId, float scale, Color color)
        {
            string text = ResolveSingleLineText(rect, fontId, scale);

            if (string.IsNullOrEmpty(text))
                return;

            Vector2 size = MeasureTextSafe(text, fontId, scale);
            float y = GetTextY(rect, size.Y, 0f);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(GetTextX(rect), y),
                RotationOrScale = scale,
                Color = color,
                Alignment = HorizontalAlignment,
                FontId = fontId
            });
        }

        string ResolveSingleLineText(RectangleF rect, string fontId, float scale)
        {
            string text = Text ?? string.Empty;
            if (MeasureTextSafe(text, fontId, scale).X <= rect.Width)
                return text;

            return Ellipsize
                ? EllipsizeToWidth(text, fontId, scale, rect.Width)
                : TrimToWidth(text, fontId, scale, rect.Width);
        }

        string EllipsizeToWidth(string text, string fontId, float scale, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
                return string.Empty;

            string suffix = FormatingHelper.ELLIPSIS.ToString();
            if (MeasureTextSafe(suffix, fontId, scale).X > maxWidth)
                return TrimToWidth(text, fontId, scale, maxWidth);

            string trimmed = text.TrimEnd();
            while (trimmed.Length > 0 && MeasureTextSafe(trimmed + suffix, fontId, scale).X > maxWidth)
                trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();

            return trimmed.Length > 0 ? trimmed + suffix : string.Empty;
        }

        string TrimToWidth(string text, string fontId, float scale, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
                return string.Empty;

            string trimmed = text;
            while (trimmed.Length > 0 && MeasureTextSafe(trimmed, fontId, scale).X > maxWidth)
                trimmed = trimmed.Substring(0, trimmed.Length - 1);

            return trimmed;
        }

        Vector2 MeasureTextSafe(string text, string fontId, float scale)
        {
            if (TextSurface == null || string.IsNullOrEmpty(text))
                return Vector2.Zero;

            var measured = FormatingHelper.GetSizeInPixel(text, fontId, scale, TextSurface);
            float height = measured.Y > 0f ? measured.Y : Math.Max(1f, 30f * Math.Max(0.01f, scale));
            return new Vector2(Math.Max(0f, measured.X), height);
        }

        void RenderWrapped(RectangleF rect, List<MySprite> sprites, string fontId, float scale, Color color)
        {
            var lines = TextWrappingHelper.WrapText(Text, TextSurface, fontId, scale, rect.Width, rect.Height, LineSpacingPixels * LayoutScale, Ellipsize);
            if (lines == null || lines.Count == 0)
                return;

            float lineHeight = TextWrappingHelper.GetLineHeight(TextSurface, fontId, scale, LineSpacingPixels * LayoutScale);
            float totalHeight = lineHeight * lines.Count;
            float y = GetTextY(rect, totalHeight, 0f);
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line))
                    continue;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = line,
                    Position = new Vector2(GetTextX(rect), y + i * lineHeight),
                    RotationOrScale = scale,
                    Color = color,
                    Alignment = HorizontalAlignment,
                    FontId = fontId
                });
            }
        }

        float GetTextX(RectangleF rect)
        {
            switch (HorizontalAlignment)
            {
                case TextAlignment.RIGHT:
                    return rect.Right;
                case TextAlignment.CENTER:
                    return rect.Center.X;
                default:
                    return rect.X;
            }
        }

        float GetTextY(RectangleF rect, float textHeight, float topPadding)
        {
            switch (VerticalAlignment)
            {
                case TextBlockVerticalAlignment.Top:
                    return rect.Y + topPadding;
                case TextBlockVerticalAlignment.Bottom:
                    return rect.Bottom - textHeight;
                default:
                    return rect.Center.Y - textHeight * 0.5f;
            }
        }
    }
}
