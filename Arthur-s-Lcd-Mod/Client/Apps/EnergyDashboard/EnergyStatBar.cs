using System;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Rendering;
using Acrux.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.EnergyDashboard
{
    internal sealed class EnergyStatBar : TemplatedControl
    {
        public static readonly StyledProperty<string> LabelProperty =
            StyledProperty.Register<EnergyStatBar, string>(nameof(Label), string.Empty, UiInvalidation.Measure);
        public string Label { get { return GetValue(LabelProperty); } set { SetValue(LabelProperty, value); } }

        public static readonly StyledProperty<string> ValueTextProperty =
            StyledProperty.Register<EnergyStatBar, string>(nameof(ValueText), string.Empty, UiInvalidation.Measure);
        public string ValueText { get { return GetValue(ValueTextProperty); } set { SetValue(ValueTextProperty, value); } }

        public static readonly StyledProperty<float> RatioProperty =
            StyledProperty.Register<EnergyStatBar, float>(nameof(Ratio), 0f, UiInvalidation.Measure);
        public float Ratio { get { return GetValue(RatioProperty); } set { SetValue(RatioProperty, value); } }

        public static readonly StyledProperty<Color> FillColorProperty =
            StyledProperty.Register<EnergyStatBar, Color>(nameof(FillColor), Color.White, UiInvalidation.Measure);
        public Color FillColor { get { return GetValue(FillColorProperty); } set { SetValue(FillColorProperty, value); } }

        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<EnergyStatBar, Color>(nameof(Foreground), Color.White, UiInvalidation.Measure);
        public Color Foreground { get { return GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<EnergyStatBar, float>(nameof(FontSize), 1f, UiInvalidation.Measure);
        public float FontSize { get { return GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<EnergyStatBar, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<string> DisplayLabelProperty =
            StyledProperty.Register<EnergyStatBar, string>(nameof(DisplayLabel), string.Empty, UiInvalidation.Measure);
        public string DisplayLabel { get { return GetValue(DisplayLabelProperty); } set { SetValue(DisplayLabelProperty, value); } }

        public static readonly StyledProperty<float> LabelFontSizeProperty =
            StyledProperty.Register<EnergyStatBar, float>(nameof(LabelFontSize), 1f, UiInvalidation.Measure);
        public float LabelFontSize { get { return GetValue(LabelFontSizeProperty); } set { SetValue(LabelFontSizeProperty, value); } }

        public static readonly StyledProperty<float> ValueFontSizeProperty =
            StyledProperty.Register<EnergyStatBar, float>(nameof(ValueFontSize), 1f, UiInvalidation.Measure);
        public float ValueFontSize { get { return GetValue(ValueFontSizeProperty); } set { SetValue(ValueFontSizeProperty, value); } }

        public static readonly StyledProperty<Color> LabelForegroundProperty =
            StyledProperty.Register<EnergyStatBar, Color>(nameof(LabelForeground), Color.White, UiInvalidation.Render);
        public Color LabelForeground { get { return GetValue(LabelForegroundProperty); } set { SetValue(LabelForegroundProperty, value); } }

        public static readonly StyledProperty<BackgroundBrush> TrackBrushProperty =
            StyledProperty.Register<EnergyStatBar, BackgroundBrush>(nameof(TrackBrush), new SolidColorBrush(Color.Transparent), UiInvalidation.Render);
        public BackgroundBrush TrackBrush { get { return GetValue(TrackBrushProperty); } set { SetValue(TrackBrushProperty, value); } }

        public static readonly StyledProperty<BackgroundBrush> FillBrushProperty =
            StyledProperty.Register<EnergyStatBar, BackgroundBrush>(nameof(FillBrush), new SolidColorBrush(Color.Transparent), UiInvalidation.Render);
        public BackgroundBrush FillBrush { get { return GetValue(FillBrushProperty); } set { SetValue(FillBrushProperty, value); } }

        public static readonly StyledProperty<float> FillWidthProperty =
            StyledProperty.Register<EnergyStatBar, float>(nameof(FillWidth), 0f, UiInvalidation.Measure);
        public float FillWidth { get { return GetValue(FillWidthProperty); } set { SetValue(FillWidthProperty, value); } }

        public static readonly StyledProperty<bool> HasFillProperty =
            StyledProperty.Register<EnergyStatBar, bool>(nameof(HasFill), false, UiInvalidation.Measure);
        public bool HasFill { get { return GetValue(HasFillProperty); } set { SetValue(HasFillProperty, value); } }

        public static readonly StyledProperty<Vector4> BarMarginProperty =
            StyledProperty.Register<EnergyStatBar, Vector4>(nameof(BarMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 BarMargin { get { return GetValue(BarMarginProperty); } set { SetValue(BarMarginProperty, value); } }

        Vector2 _measuredSize;
        string _measuredLabel, _measuredValue, _measuredFamily;
        float _measuredFont = -1f, _measuredRatio = -1f;
        Color _measuredForeground, _measuredFill;
        ITextMetrics _measuredMetrics;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_measuredSize != availableSize || _measuredLabel != Label || _measuredValue != ValueText ||
                _measuredFamily != FontFamily || _measuredFont != FontSize || _measuredRatio != Ratio ||
                _measuredForeground != Foreground || _measuredFill != FillColor ||
                !ReferenceEquals(_measuredMetrics, TextMetrics))
            {
                _measuredSize = availableSize; _measuredLabel = Label; _measuredValue = ValueText;
                _measuredFamily = FontFamily; _measuredFont = FontSize; _measuredRatio = Ratio;
                _measuredForeground = Foreground; _measuredFill = FillColor; _measuredMetrics = TextMetrics;
                UpdateLayout(availableSize);
            }
            return TemplateRoot == null ? Vector2.Zero : TemplateRoot.Measure(availableSize);
        }

        void UpdateLayout(Vector2 size)
        {
            Color fg = Foreground;
            DisplayLabel = string.IsNullOrEmpty(Label) ? string.Empty : Label + ":";
            LabelFontSize = .52f * FontSize;
            float labelHeight = EnergyText.Measure(TextMetrics, FontFamily,
                string.IsNullOrEmpty(DisplayLabel) ? " " : DisplayLabel, LabelFontSize).Y;
            float barTop = Math.Min(size.Y, labelHeight + 2f);
            float barHeight = Math.Max(1f, size.Y - barTop);
            BarMargin = new Vector4(0f, barTop, 0f, 0f);
            ValueFontSize = EnergyText.Fit(TextMetrics, FontFamily, ValueText, .56f * FontSize,
                Math.Max(1f, size.X - 8f), Math.Max(1f, barHeight - 2f));
            float ratio = MathHelper.Clamp(Ratio, 0f, 1f);
            HasFill = ratio > .004f;
            FillWidth = size.X * ratio;
            LabelForeground = new Color(fg.R, fg.G, fg.B, 180);
            TrackBrush = new SolidColorBrush(new Color(fg.R, fg.G, fg.B, 24));
            FillBrush = new SolidColorBrush(new Color(FillColor.R, FillColor.G, FillColor.B, 180));
        }
    }

    internal static class EnergyText
    {
        public static Vector2 Measure(ITextMetrics metrics, string font, string text, float size)
        {
            return metrics == null || string.IsNullOrEmpty(text) ? Vector2.Zero : metrics.Measure(font, text, size);
        }

        public static float Fit(ITextMetrics metrics, string font, string text, float size, float width, float height)
        {
            Vector2 measured = Measure(metrics, font, text ?? string.Empty, size);
            if (measured.X <= 0f || measured.Y <= 0f || (measured.X <= width && measured.Y <= height))
                return size;
            return Math.Max(.01f, size * Math.Min(width / measured.X, height / measured.Y));
        }

        public static float FitWidth(ITextMetrics metrics, string font, float size, float width, params string[] texts)
        {
            float widest = 0f;
            for (int i = 0; i < texts.Length; i++)
                widest = Math.Max(widest, Measure(metrics, font, texts[i], size).X);
            return widest <= 0f || widest <= width ? size : Math.Max(.01f, size * width / widest);
        }

        public static float RenderScaling(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.Parent)
            {
                var window = current as Window;
                if (window != null) return Math.Max(.01f, (float)window.RenderScaling);
            }
            return 1f;
        }
    }
}
