using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.Thrust
{
    /// <summary>Reusable presentation whose complete visual tree is supplied by its runtime style.</summary>
    internal sealed class ThrustLegendCellControl : TemplatedControl
    {
        public static readonly StyledProperty<string> LabelProperty =
            StyledProperty.Register<ThrustLegendCellControl, string>(nameof(Label), string.Empty, UiInvalidation.Measure);
        public string Label
        {
            get { return GetValue(LabelProperty); }
            set { SetValue(LabelProperty, value); }
        }

        public static readonly StyledProperty<string> ValueProperty =
            StyledProperty.Register<ThrustLegendCellControl, string>(nameof(Value), string.Empty, UiInvalidation.Measure);
        public string Value
        {
            get { return GetValue(ValueProperty); }
            set { SetValue(ValueProperty, value); }
        }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<ThrustLegendCellControl, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily
        {
            get { return GetValue(FontFamilyProperty); }
            set { SetValue(FontFamilyProperty, value); }
        }

        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<ThrustLegendCellControl, Color>(nameof(Foreground), Color.White, UiInvalidation.Render);
        public Color Foreground
        {
            get { return GetValue(ForegroundProperty); }
            set { SetValue(ForegroundProperty, value); }
        }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<ThrustLegendCellControl, float>(nameof(FontSize), 1f, UiInvalidation.Measure);
        public float FontSize
        {
            get { return GetValue(FontSizeProperty); }
            set { SetValue(FontSizeProperty, value); }
        }

        public static readonly StyledProperty<BackgroundBrush> MarkerProperty =
            StyledProperty.Register<ThrustLegendCellControl, BackgroundBrush>(nameof(Marker), new SolidColorBrush(Color.Blue), UiInvalidation.Render);
        public BackgroundBrush Marker
        {
            get { return GetValue(MarkerProperty); }
            set { SetValue(MarkerProperty, value); }
        }

        public static readonly StyledProperty<float> MarkerSizeProperty =
            StyledProperty.Register<ThrustLegendCellControl, float>(nameof(MarkerSize), 8f, UiInvalidation.Measure);
        public float MarkerSize
        {
            get { return GetValue(MarkerSizeProperty); }
            set { SetValue(MarkerSizeProperty, value); }
        }

        public static readonly StyledProperty<Vector4> MarkerMarginProperty =
            StyledProperty.Register<ThrustLegendCellControl, Vector4>(nameof(MarkerMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 MarkerMargin
        {
            get { return GetValue(MarkerMarginProperty); }
            set { SetValue(MarkerMarginProperty, value); }
        }

        public static readonly StyledProperty<Vector4> LabelMarginProperty =
            StyledProperty.Register<ThrustLegendCellControl, Vector4>(nameof(LabelMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 LabelMargin
        {
            get { return GetValue(LabelMarginProperty); }
            set { SetValue(LabelMarginProperty, value); }
        }

        public static readonly StyledProperty<Vector4> ValueMarginProperty =
            StyledProperty.Register<ThrustLegendCellControl, Vector4>(nameof(ValueMargin), Vector4.Zero, UiInvalidation.Measure);
        public Vector4 ValueMargin
        {
            get { return GetValue(ValueMarginProperty); }
            set { SetValue(ValueMarginProperty, value); }
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return TemplateRoot == null ? Vector2.Zero : TemplateRoot.Measure(availableSize);
        }
    }
}
