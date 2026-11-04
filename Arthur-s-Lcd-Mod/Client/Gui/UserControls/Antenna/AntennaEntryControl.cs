using Acrux.Styling;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using VRageMath;

namespace LcdMod.Client.Gui.UserControls.Antenna
{
    internal sealed class AntennaEntryControl : TemplatedControl
    {
        public static readonly StyledProperty<string> TitleProperty =
            StyledProperty.Register<AntennaEntryControl, string>(
                nameof(Title), string.Empty, UiInvalidation.Measure);

        public static readonly StyledProperty<BackgroundBrush> StatusIconMaskProperty =
            StyledProperty.Register<AntennaEntryControl, BackgroundBrush>(
                nameof(StatusIconMask), new ImageBrush("MissingIcon"), UiInvalidation.Render);

        public static readonly StyledProperty<string> StatusIconProperty =
            StyledProperty.Register<AntennaEntryControl, string>(
                nameof(StatusIcon), "MissingIcon", UiInvalidation.Render);

        public static readonly StyledProperty<string> StatusTextProperty =
            StyledProperty.Register<AntennaEntryControl, string>(
                nameof(StatusText), string.Empty, UiInvalidation.Measure);

        public static readonly StyledProperty<Color> StatusColorProperty =
            StyledProperty.Register<AntennaEntryControl, Color>(
                nameof(StatusColor), Color.Transparent, UiInvalidation.Render);

        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<AntennaEntryControl, Color>(
                nameof(Foreground), Color.White, UiInvalidation.Render);

        public static readonly StyledProperty<Color> CardBackgroundProperty =
            StyledProperty.Register<AntennaEntryControl, Color>(
                nameof(CardBackground), Color.Transparent, UiInvalidation.Render);

        public static readonly StyledProperty<Color> CardShadowProperty =
            StyledProperty.Register<AntennaEntryControl, Color>(
                nameof(CardShadow), Color.Transparent, UiInvalidation.Render);

        public static readonly StyledProperty<Color> LineColorProperty =
            StyledProperty.Register<AntennaEntryControl, Color>(
                nameof(LineColor), Color.Transparent, UiInvalidation.Render);

        public static readonly StyledProperty<Vector4> LineThicknessProperty =
            StyledProperty.Register<AntennaEntryControl, Vector4>(
                nameof(LineThickness), Vector4.Zero, UiInvalidation.Render);

        public static readonly StyledProperty<Vector4> IconMarginProperty =
            StyledProperty.Register<AntennaEntryControl, Vector4>(
                nameof(IconMargin), Vector4.Zero, UiInvalidation.Arrange);

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<AntennaEntryControl, string>(
                nameof(FontFamily), "White", UiInvalidation.Measure);

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<AntennaEntryControl, float>(
                nameof(FontSize), 1f, UiInvalidation.Measure);

        public string Title
        {
            get { return GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value ?? string.Empty); }
        }

        public string StatusIcon
        {
            get { return GetValue(StatusIconProperty); }
            set
            {
                SetValue(StatusIconProperty, string.IsNullOrWhiteSpace(value) ? "MissingIcon" : value);
                StatusIconMask = new ImageBrush(GetValue(StatusIconProperty)) { Stretch = Stretch.Uniform };
            }
        }

        public BackgroundBrush StatusIconMask
        {
            get { return GetValue(StatusIconMaskProperty); }
            set { SetValue(StatusIconMaskProperty, value); }
        }

        public string StatusText
        {
            get { return GetValue(StatusTextProperty); }
            set { SetValue(StatusTextProperty, value ?? string.Empty); }
        }

        public Color StatusColor
        {
            get { return GetValue(StatusColorProperty); }
            set { SetValue(StatusColorProperty, value); }
        }

        public Color Foreground
        {
            get { return GetValue(ForegroundProperty); }
            set { SetValue(ForegroundProperty, value); }
        }

        public Color CardBackground
        {
            get { return GetValue(CardBackgroundProperty); }
            set { SetValue(CardBackgroundProperty, value); }
        }

        public Color CardShadow
        {
            get { return GetValue(CardShadowProperty); }
            set { SetValue(CardShadowProperty, value); }
        }

        public Color LineColor
        {
            get { return GetValue(LineColorProperty); }
            set { SetValue(LineColorProperty, value); }
        }

        public Vector4 LineThickness
        {
            get { return GetValue(LineThicknessProperty); }
            set { SetValue(LineThicknessProperty, value); }
        }

        public Vector4 IconMargin
        {
            get { return GetValue(IconMarginProperty); }
            set { SetValue(IconMarginProperty, value); }
        }

        public string FontFamily
        {
            get { return GetValue(FontFamilyProperty); }
            set { SetValue(FontFamilyProperty, string.IsNullOrWhiteSpace(value) ? "White" : value); }
        }

        public float FontSize
        {
            get { return GetValue(FontSizeProperty); }
            set { SetValue(FontSizeProperty, value); }
        }
    }
}
