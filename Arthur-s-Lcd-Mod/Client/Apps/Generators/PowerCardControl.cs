using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.Generators
{
    internal sealed class PowerCardControl : TemplatedControl
    {
        public static readonly StyledProperty<string> TitleProperty =
            StyledProperty.Register<PowerCardControl, string>(nameof(Title), string.Empty, UiInvalidation.Measure);
        public string Title { get { return GetValue(TitleProperty); } set { SetValue(TitleProperty, value ?? string.Empty); } }

        public static readonly StyledProperty<string> InfoTextProperty =
            StyledProperty.Register<PowerCardControl, string>(nameof(InfoText), string.Empty, UiInvalidation.Measure);
        public string InfoText { get { return GetValue(InfoTextProperty); } set { SetValue(InfoTextProperty, value ?? string.Empty); } }

        public static readonly StyledProperty<float> UsageProperty =
            StyledProperty.Register<PowerCardControl, float>(nameof(Usage), 0f, UiInvalidation.Render);
        public float Usage { get { return GetValue(UsageProperty); } set { SetValue(UsageProperty, value); } }

        public static readonly StyledProperty<BackgroundBrush> UsageBrushProperty =
            StyledProperty.Register<PowerCardControl, BackgroundBrush>(nameof(UsageBrush), null, UiInvalidation.Render);
        public BackgroundBrush UsageBrush { get { return GetValue(UsageBrushProperty); } set { SetValue(UsageBrushProperty, value); } }

        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<PowerCardControl, Color>(nameof(Foreground), Color.White, UiInvalidation.Render);
        public Color Foreground { get { return GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }

        public static readonly StyledProperty<Color> CardBackgroundProperty =
            StyledProperty.Register<PowerCardControl, Color>(nameof(CardBackground), Color.Transparent, UiInvalidation.Render);
        public Color CardBackground { get { return GetValue(CardBackgroundProperty); } set { SetValue(CardBackgroundProperty, value); } }

        public static readonly StyledProperty<Color> CardShadowProperty =
            StyledProperty.Register<PowerCardControl, Color>(nameof(CardShadow), Color.Transparent, UiInvalidation.Render);
        public Color CardShadow { get { return GetValue(CardShadowProperty); } set { SetValue(CardShadowProperty, value); } }

        public static readonly StyledProperty<Color> LineColorProperty =
            StyledProperty.Register<PowerCardControl, Color>(nameof(LineColor), Color.Transparent, UiInvalidation.Render);
        public Color LineColor { get { return GetValue(LineColorProperty); } set { SetValue(LineColorProperty, value); } }

        public static readonly StyledProperty<Vector4> LineThicknessProperty =
            StyledProperty.Register<PowerCardControl, Vector4>(nameof(LineThickness), Vector4.Zero, UiInvalidation.Render);
        public Vector4 LineThickness { get { return GetValue(LineThicknessProperty); } set { SetValue(LineThicknessProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<PowerCardControl, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<PowerCardControl, float>(nameof(FontSize), 1f, UiInvalidation.Measure);
        public float FontSize { get { return GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }
    }
}
