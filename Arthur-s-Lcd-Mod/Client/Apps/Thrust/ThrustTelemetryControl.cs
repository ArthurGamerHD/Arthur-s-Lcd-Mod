using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.Thrust
{
    /// <summary>Reusable presentation whose complete visual tree is supplied by its runtime style.</summary>
    internal sealed class ThrustTelemetryControl : TemplatedControl
    {
        public static readonly StyledProperty<string> ValueProperty =
            StyledProperty.Register<ThrustTelemetryControl, string>(nameof(Value), string.Empty, UiInvalidation.Measure);
        public string Value
        {
            get { return GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<ThrustTelemetryControl, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily
        {
            get { return GetValue(FontFamilyProperty); }
            set { SetValue(FontFamilyProperty, value); }
        }

        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<ThrustTelemetryControl, Color>(nameof(Foreground), Color.White, UiInvalidation.Render);
        public Color Foreground
        {
            get { return GetValue(ForegroundProperty); }
            set { SetValue(ForegroundProperty, value); }
        }

        public static readonly StyledProperty<BackgroundBrush> BackgroundProperty =
            StyledProperty.Register<ThrustTelemetryControl, BackgroundBrush>(nameof(Background), new SolidColorBrush(Color.Transparent), UiInvalidation.Render);
        public BackgroundBrush Background
        {
            get { return GetValue(BackgroundProperty); }
            set { SetValue(BackgroundProperty, value); }
        }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<ThrustTelemetryControl, float>(nameof(FontSize), 1f, UiInvalidation.Measure);
        public float FontSize
        {
            get { return GetValue(FontSizeProperty); }
            set { SetValue(FontSizeProperty, value); }
        }

        public static readonly StyledProperty<float> BoxWidthProperty =
            StyledProperty.Register<ThrustTelemetryControl, float>(nameof(BoxWidth), 89f, UiInvalidation.Measure);
        public float BoxWidth
        {
            get { return GetValue(BoxWidthProperty); }
            set { SetValue(BoxWidthProperty, value); }
        }

        public static readonly StyledProperty<float> BoxHeightProperty =
            StyledProperty.Register<ThrustTelemetryControl, float>(nameof(BoxHeight), 32f, UiInvalidation.Measure);
        public float BoxHeight
        {
            get { return GetValue(BoxHeightProperty); }
            set { SetValue(BoxHeightProperty, value); }
        }

        public static readonly StyledProperty<Vector4> TextMarginProperty =
            StyledProperty.Register<ThrustTelemetryControl, Vector4>(nameof(TextMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 TextMargin
        {
            get { return GetValue(TextMarginProperty); }
            set { SetValue(TextMarginProperty, value); }
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return TemplateRoot == null ? Vector2.Zero : TemplateRoot.Measure(availableSize);
        }
    }
}
