using Acrux.Core.Input;
using Acrux.Styling;
using Acrux.Controls;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using LcdMod.Client.Extensions;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    /// <summary>A styled action-pad button with independent title, icon, and live status.</summary>
    internal sealed class ActionButton : Button
    {
        public static readonly StyledProperty<string> TitleProperty =
            StyledProperty.Register<ActionButton, string>(
                nameof(Title), string.Empty, UiInvalidation.Measure);

        public static readonly StyledProperty<BackgroundBrush> ActionIconMaskProperty =
            StyledProperty.Register<ActionButton, BackgroundBrush>(
                nameof(ActionIconMask), new ImageBrush("MissingIcon") { Stretch = Stretch.Uniform }, UiInvalidation.Render);

        public static readonly StyledProperty<string> ActionIconProperty =
            StyledProperty.Register<ActionButton, string>(
                nameof(ActionIcon), "MissingIcon", UiInvalidation.Measure | UiInvalidation.Render);

        public static readonly StyledProperty<string> StatusProperty =
            StyledProperty.Register<ActionButton, string>(
                nameof(Status), string.Empty, UiInvalidation.Measure);
        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<ActionButton, string>(
                nameof(FontFamily), "White", UiInvalidation.Measure);

        public string Title
        {
            get { return GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value ?? string.Empty); }
        }

        public string ActionIcon
        {
            get { return GetValue(ActionIconProperty); }
            set
            {
                SetValue(ActionIconProperty, string.IsNullOrWhiteSpace(value) ? "MissingIcon" : value);
                ActionIconMask = new ImageBrush(GetValue(ActionIconProperty)) { Stretch = Stretch.Uniform };
            }
        }

        public BackgroundBrush ActionIconMask
        {
            get { return GetValue(ActionIconMaskProperty); }
            set { SetValue(ActionIconMaskProperty, value); }
        }

        public string Status
        {
            get { return GetValue(StatusProperty); }
            set { SetValue(StatusProperty, value ?? string.Empty); }
        }

        public string FontFamily
        {
            get { return GetValue(FontFamilyProperty); }
            set { SetValue(FontFamilyProperty, string.IsNullOrWhiteSpace(value) ? "White" : value); }
        }

        public static readonly StyledProperty<string> VariantProperty =
            StyledProperty.Register<ActionButton, string>(nameof(Variant), string.Empty, UiInvalidation.Style);

        // Visual variant exposed as the :classic, :transparent or :border pseudo-class.
        public string Variant
        {
            get { return GetValue(VariantProperty); }
            set { SetValue(VariantProperty, value ?? string.Empty); }
        }

        public static readonly StyledProperty<bool> IsConfiguredProperty =
            StyledProperty.Register<ActionButton, bool>(nameof(IsConfigured), true, UiInvalidation.Style);

        // False for an empty slot, exposed as the :unconfigured pseudo-class.
        public bool IsConfigured
        {
            get { return GetValue(IsConfiguredProperty); }
            set { SetValue(IsConfiguredProperty, value); }
        }

        public static readonly StyledProperty<ICommand> SecondaryCommandProperty =
            StyledProperty.Register<ActionButton, ICommand>(nameof(SecondaryCommand), null);

        public ICommand SecondaryCommand
        {
            get { return GetValue(SecondaryCommandProperty); }
            set { SetValue(SecondaryCommandProperty, value); }
        }

        public static readonly StyledProperty<Color> PanelColorProperty =
            StyledProperty.Register<ActionButton, Color>(nameof(PanelColor), Color.Transparent, UiInvalidation.Render);

        // The slot's configured color, or transparent for the theme's. It fills the panel, or colors the outline of the
        // border and transparent styles, and lightens or darkens while hovered like the pre-migration pad.
        public Color PanelColor
        {
            get { return GetValue(PanelColorProperty); }
            set { SetValue(PanelColorProperty, value); }
        }

        Color? _appliedColor;
        bool _appliedOutline;

        public ActionButton()
        {
            SecondaryClicked = () =>
            {
                ICommand command = SecondaryCommand;
                if (command != null && command.CanExecute(null))
                    command.Execute(null);
            };
        }

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            string variant = Variant;
            PseudoClasses.Set(":classic", variant == "classic");
            PseudoClasses.Set(":transparent", variant == "transparent");
            PseudoClasses.Set(":border", variant == "border");
            PseudoClasses.Set(":unconfigured", !IsConfigured);
            ApplyPanelColor();
        }

        protected override void OnPropertyChanged(IStyledProperty property)
        {
            base.OnPropertyChanged(property);
            if (property == PanelColorProperty || property == VariantProperty || property == IsConfiguredProperty)
                ApplyPanelColor();
        }

        void ApplyPanelColor()
        {
            Color color = PanelColor;
            string variant = Variant;
            bool outline = variant == "border" || variant == "transparent";
            Color? shown = color.A > 0 && IsConfigured
                ? IsPointerOver || IsPressed ? HoverColor(color) : color
                : (Color?)null;
            if (shown == _appliedColor && outline == _appliedOutline)
                return;
            _appliedColor = shown;
            _appliedOutline = outline;
            ClearValue(BackgroundProperty);
            ClearValue(BorderBrushProperty);
            ClearValue(ForegroundProperty);
            if (!shown.HasValue)
                return;
            if (outline)
                BorderBrush = shown.Value;
            else
            {
                Background = new SolidColorBrush(shown.Value);
                Foreground = Color.White.ContrastRatio(shown.Value) >= Color.Black.ContrastRatio(shown.Value)
                    ? Color.White
                    : Color.Black;
            }
        }

        static Color HoverColor(Color color)
        {
            return Color.White.ContrastRatio(color) >= Color.Black.ContrastRatio(color)
                ? color.MulValue(1.18)
                : color.MulValue(.82);
        }
    }
}
