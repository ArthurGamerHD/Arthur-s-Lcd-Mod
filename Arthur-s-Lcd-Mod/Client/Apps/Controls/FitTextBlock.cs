using System;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    internal sealed class FitTextBlock : VisualElement
    {
        const float WIDTH_FILL = 0.94f;
        const float HEIGHT_FILL = 0.88f;

        public static readonly StyledProperty<string> TextProperty =
            StyledProperty.Register<FitTextBlock, string>(nameof(Text), string.Empty, UiInvalidation.Render);
        public string Text { get { return GetValue(TextProperty); } set { SetValue(TextProperty, value ?? string.Empty); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<FitTextBlock, string>(nameof(FontFamily), "White", UiInvalidation.Render);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<FitTextBlock, Color>(nameof(Foreground), Color.White, UiInvalidation.Render);
        public Color Foreground { get { return GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }

        public static readonly StyledProperty<float> MinFontSizeProperty =
            StyledProperty.Register<FitTextBlock, float>(nameof(MinFontSize), 0.6f, UiInvalidation.Render);
        public float MinFontSize { get { return GetValue(MinFontSizeProperty); } set { SetValue(MinFontSizeProperty, value); } }

        public static readonly StyledProperty<float> MaxFontSizeProperty =
            StyledProperty.Register<FitTextBlock, float>(nameof(MaxFontSize), 6f, UiInvalidation.Render);
        public float MaxFontSize { get { return GetValue(MaxFontSizeProperty); } set { SetValue(MaxFontSizeProperty, value); } }

        public static readonly StyledProperty<HorizontalAlignment> TextAlignmentProperty =
            StyledProperty.Register<FitTextBlock, HorizontalAlignment>(nameof(TextAlignment), HorizontalAlignment.Center, UiInvalidation.Render);
        public HorizontalAlignment TextAlignment { get { return GetValue(TextAlignmentProperty); } set { SetValue(TextAlignmentProperty, value); } }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        protected override void DrawSelf(RenderContext context)
        {
            string text = Text;
            if (string.IsNullOrEmpty(text) || TextMetrics == null || Bounds.Width <= 0f || Bounds.Height <= 0f)
                return;
            Vector2 unit = TextMetrics.Measure(FontFamily, text, 1f);
            if (unit.X <= 0f || unit.Y <= 0f)
                return;
            float fit = Math.Min(Bounds.Width * WIDTH_FILL / unit.X, Bounds.Height * HEIGHT_FILL / unit.Y);
            float size = MathHelper.Clamp(fit, Math.Max(0.01f, MinFontSize), Math.Max(0.01f, MaxFontSize));
            HorizontalAlignment alignment = TextAlignment;
            float x = alignment == HorizontalAlignment.Left
                ? Bounds.X
                : alignment == HorizontalAlignment.Right ? Bounds.Right : Bounds.Center.X;
            context.Text(FontFamily, text, new Vector2(x, Bounds.Center.Y), size, Foreground, alignment,
                VerticalAlignment.Center, false);
        }
    }
}
