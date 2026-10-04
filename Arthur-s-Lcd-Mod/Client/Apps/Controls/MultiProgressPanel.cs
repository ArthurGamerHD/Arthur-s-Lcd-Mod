using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    /// <summary>Overlapping completion values and their legend, supplied by a replaceable template.</summary>
    internal sealed class MultiProgressPanel : TemplatedControl
    {
        public static readonly StyledProperty<float> FirstValueProperty =
            StyledProperty.Register<MultiProgressPanel, float>(nameof(FirstValue), 0f, UiInvalidation.Render);
        public float FirstValue
        {
            get { return GetValue(FirstValueProperty); }
            set { SetValue(FirstValueProperty, float.IsNaN(value) ? 0f : MathHelper.Clamp(value, 0f, 1f)); }
        }

        public static readonly StyledProperty<float> SecondValueProperty =
            StyledProperty.Register<MultiProgressPanel, float>(nameof(SecondValue), 0f, UiInvalidation.Render);
        public float SecondValue
        {
            get { return GetValue(SecondValueProperty); }
            set { SetValue(SecondValueProperty, float.IsNaN(value) ? 0f : MathHelper.Clamp(value, 0f, 1f)); }
        }

        public static readonly StyledProperty<float> ThirdValueProperty =
            StyledProperty.Register<MultiProgressPanel, float>(nameof(ThirdValue), 0f, UiInvalidation.Render);
        public float ThirdValue
        {
            get { return GetValue(ThirdValueProperty); }
            set { SetValue(ThirdValueProperty, float.IsNaN(value) ? 0f : MathHelper.Clamp(value, 0f, 1f)); }
        }

        public static readonly StyledProperty<string> FirstTextProperty =
            StyledProperty.Register<MultiProgressPanel, string>(nameof(FirstText), string.Empty, UiInvalidation.Measure);
        public string FirstText
        {
            get { return GetValue(FirstTextProperty); }
            set { SetValue(FirstTextProperty, value); }
        }

        public static readonly StyledProperty<string> SecondTextProperty =
            StyledProperty.Register<MultiProgressPanel, string>(nameof(SecondText), string.Empty, UiInvalidation.Measure);
        public string SecondText
        {
            get { return GetValue(SecondTextProperty); }
            set { SetValue(SecondTextProperty, value); }
        }

        public static readonly StyledProperty<string> ThirdTextProperty =
            StyledProperty.Register<MultiProgressPanel, string>(nameof(ThirdText), string.Empty, UiInvalidation.Measure);
        public string ThirdText
        {
            get { return GetValue(ThirdTextProperty); }
            set { SetValue(ThirdTextProperty, value); }
        }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<MultiProgressPanel, float>(nameof(FontSize), 1f, UiInvalidation.Measure);
        public float FontSize
        {
            get { return GetValue(FontSizeProperty); }
            set { SetValue(FontSizeProperty, value); }
        }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<MultiProgressPanel, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily
        {
            get { return GetValue(FontFamilyProperty); }
            set { SetValue(FontFamilyProperty, value); }
        }

        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<MultiProgressPanel, Color>(nameof(Foreground), Color.White, UiInvalidation.Render);
        public Color Foreground
        {
            get { return GetValue(ForegroundProperty); }
            set { SetValue(ForegroundProperty, value); }
        }

        public static readonly StyledProperty<BackgroundBrush> FirstBrushProperty =
            StyledProperty.Register<MultiProgressPanel, BackgroundBrush>(nameof(FirstBrush), new SolidColorBrush(Color.Blue), UiInvalidation.Render);
        public BackgroundBrush FirstBrush
        {
            get { return GetValue(FirstBrushProperty); }
            set { SetValue(FirstBrushProperty, value); }
        }

        public static readonly StyledProperty<BackgroundBrush> SecondBrushProperty =
            StyledProperty.Register<MultiProgressPanel, BackgroundBrush>(nameof(SecondBrush), new SolidColorBrush(Color.White), UiInvalidation.Render);
        public BackgroundBrush SecondBrush
        {
            get { return GetValue(SecondBrushProperty); }
            set { SetValue(SecondBrushProperty, value); }
        }

        public static readonly StyledProperty<BackgroundBrush> ThirdBrushProperty =
            StyledProperty.Register<MultiProgressPanel, BackgroundBrush>(nameof(ThirdBrush), new SolidColorBrush(Color.Yellow), UiInvalidation.Render);
        public BackgroundBrush ThirdBrush
        {
            get { return GetValue(ThirdBrushProperty); }
            set { SetValue(ThirdBrushProperty, value); }
        }

        public static readonly StyledProperty<BackgroundBrush> TrackProperty =
            StyledProperty.Register<MultiProgressPanel, BackgroundBrush>(nameof(Track), new SolidColorBrush(new Color(40, 40, 40)), UiInvalidation.Render);
        public BackgroundBrush Track
        {
            get { return GetValue(TrackProperty); }
            set { SetValue(TrackProperty, value); }
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize) =>
            TemplateRoot == null ? Vector2.Zero : TemplateRoot.Measure(availableSize);
    }
}
