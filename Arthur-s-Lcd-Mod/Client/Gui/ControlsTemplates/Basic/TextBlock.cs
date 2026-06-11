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
            FontId = "White";
            FontScale = 0.58f;
            LineSpacingPixels = 0f;
            Wrapping = TextBlockWrapping.NoWrap;
            Ellipsize = true;
            HorizontalAlignment = TextAlignment.LEFT;
            VerticalAlignment = TextBlockVerticalAlignment.Center;
        }

        public string Text { get; set; }
        public string FontId { get; set; }
        public float FontScale { get; set; }
        public float LineSpacingPixels { get; set; }
        public TextBlockWrapping Wrapping { get; set; }
        public bool Ellipsize { get; set; }
        public TextAlignment HorizontalAlignment { get; set; }
        public TextBlockVerticalAlignment VerticalAlignment { get; set; }
        public Color? TextColor { get; set; }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            if (string.IsNullOrEmpty(Text) || context == null || context.Surface == null)
                return;

            var rect = GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            string fontId = string.IsNullOrEmpty(FontId) ? "White" : FontId;
            float scale = Math.Max(0.01f, context.Scale * context.FontScale * FontScale);
            Color color = TextColor ?? context.Surface.ScriptForegroundColor;
            if (Wrapping == TextBlockWrapping.Wrap)
                RenderWrapped(rect, context, sprites, fontId, scale, color);
            else
                RenderSingleLine(rect, context, sprites, fontId, scale, color);
        }

        void RenderSingleLine(RectangleF rect, ControlRenderContext context, List<MySprite> sprites, string fontId, float scale, Color color)
        {
            string text = ResolveSingleLineText(rect, context, fontId, scale);

            if (string.IsNullOrEmpty(text))
                return;

            Vector2 size = MeasureText(context, text, fontId, scale);
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

        string ResolveSingleLineText(RectangleF rect, ControlRenderContext context, string fontId, float scale)
        {
            string text = Text ?? string.Empty;
            if (MeasureText(context, text, fontId, scale).X <= rect.Width)
                return text;

            return Ellipsize
                ? EllipsizeToWidth(text, context, fontId, scale, rect.Width)
                : TrimToWidth(text, context, fontId, scale, rect.Width);
        }

        static string EllipsizeToWidth(string text, ControlRenderContext context, string fontId, float scale, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
                return string.Empty;

            string suffix = FormatingHelper.ELLIPSIS.ToString();
            if (MeasureText(context, suffix, fontId, scale).X > maxWidth)
                return TrimToWidth(text, context, fontId, scale, maxWidth);

            string trimmed = text.TrimEnd();
            while (trimmed.Length > 0 && MeasureText(context, trimmed + suffix, fontId, scale).X > maxWidth)
                trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();

            return trimmed.Length > 0 ? trimmed + suffix : string.Empty;
        }

        static string TrimToWidth(string text, ControlRenderContext context, string fontId, float scale, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
                return string.Empty;

            string trimmed = text;
            while (trimmed.Length > 0 && MeasureText(context, trimmed, fontId, scale).X > maxWidth)
                trimmed = trimmed.Substring(0, trimmed.Length - 1);

            return trimmed;
        }

        static Vector2 MeasureText(ControlRenderContext context, string text, string fontId, float scale)
        {
            if (context == null || context.Surface == null || string.IsNullOrEmpty(text))
                return Vector2.Zero;

            var measured = FormatingHelper.GetSizeInPixel(text, fontId, scale, context.Surface);
            float height = measured.Y > 0f ? measured.Y : Math.Max(1f, 30f * Math.Max(0.01f, scale));
            return new Vector2(Math.Max(0f, measured.X), height);
        }

        void RenderWrapped(RectangleF rect, ControlRenderContext context, List<MySprite> sprites, string fontId, float scale, Color color)
        {
            var lines = TextWrappingHelper.WrapText(Text, context.Surface, fontId, scale, rect.Width, rect.Height, LineSpacingPixels * context.Scale, Ellipsize);
            if (lines == null || lines.Count == 0)
                return;

            float lineHeight = TextWrappingHelper.GetLineHeight(context.Surface, fontId, scale, LineSpacingPixels * context.Scale);
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
