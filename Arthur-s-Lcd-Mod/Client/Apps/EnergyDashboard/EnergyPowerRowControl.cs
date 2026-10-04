using System;
using Adk.Gui.Controls;
using Adk.Gui.Core.Input;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.EnergyDashboard
{
    internal sealed class EnergyPowerRowControl : Button
    {
        public static readonly StyledProperty<string> RowIconProperty = RegisterText(nameof(RowIcon));
        public string RowIcon { get { return GetValue(RowIconProperty); } set { SetValue(RowIconProperty, value); } }
        public static readonly StyledProperty<string> GlyphProperty = RegisterText(nameof(Glyph));
        public string Glyph { get { return GetValue(GlyphProperty); } set { SetValue(GlyphProperty, value); } }
        public static readonly StyledProperty<string> AmountProperty = RegisterText(nameof(Amount));
        public string Amount { get { return GetValue(AmountProperty); } set { SetValue(AmountProperty, value); } }
        public static readonly StyledProperty<string> LabelProperty = RegisterText(nameof(Label));
        public string Label { get { return GetValue(LabelProperty); } set { SetValue(LabelProperty, value); } }
        public static readonly StyledProperty<string> ValueTextProperty = RegisterText(nameof(ValueText));
        public string ValueText { get { return GetValue(ValueTextProperty); } set { SetValue(ValueTextProperty, value); } }

        public static readonly StyledProperty<float> RatioProperty = RegisterSize(nameof(Ratio), 0f);
        public float Ratio { get { return GetValue(RatioProperty); } set { SetValue(RatioProperty, value); } }

        public static readonly StyledProperty<Color> AccentColorProperty =
            StyledProperty.Register<EnergyPowerRowControl, Color>(nameof(AccentColor), Color.White, UiInvalidation.Measure);
        public Color AccentColor { get { return GetValue(AccentColorProperty); } set { SetValue(AccentColorProperty, value); } }

        public static readonly StyledProperty<Color> AmountForegroundProperty =
            StyledProperty.Register<EnergyPowerRowControl, Color>(nameof(AmountForeground), Color.White, UiInvalidation.Render);
        public Color AmountForeground { get { return GetValue(AmountForegroundProperty); } set { SetValue(AmountForegroundProperty, value); } }

        public static readonly StyledProperty<bool> IsRowSelectedProperty =
            StyledProperty.Register<EnergyPowerRowControl, bool>(nameof(IsRowSelected), false, UiInvalidation.Style);
        public bool IsRowSelected { get { return GetValue(IsRowSelectedProperty); } set { SetValue(IsRowSelectedProperty, value); } }

        public static readonly StyledProperty<ICommand> HoverCommandProperty =
            StyledProperty.Register<EnergyPowerRowControl, ICommand>(nameof(HoverCommand), null);
        public ICommand HoverCommand { get { return GetValue(HoverCommandProperty); } set { SetValue(HoverCommandProperty, value); } }

        public static readonly StyledProperty<float> FontSizeProperty = RegisterSize(nameof(FontSize), 1f);
        public float FontSize { get { return GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<EnergyPowerRowControl, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<bool> HasIconProperty =
            StyledProperty.Register<EnergyPowerRowControl, bool>(nameof(HasIcon), false, UiInvalidation.Measure);
        public bool HasIcon { get { return GetValue(HasIconProperty); } set { SetValue(HasIconProperty, value); } }

        public static readonly StyledProperty<bool> HasGlyphProperty =
            StyledProperty.Register<EnergyPowerRowControl, bool>(nameof(HasGlyph), false, UiInvalidation.Measure);
        public bool HasGlyph { get { return GetValue(HasGlyphProperty); } set { SetValue(HasGlyphProperty, value); } }

        public static readonly StyledProperty<float> IconSizeProperty = RegisterSize(nameof(IconSize), 0f);
        public float IconSize { get { return GetValue(IconSizeProperty); } set { SetValue(IconSizeProperty, value); } }
        public static readonly StyledProperty<float> IconImageSizeProperty = RegisterSize(nameof(IconImageSize), 0f);
        public float IconImageSize { get { return GetValue(IconImageSizeProperty); } set { SetValue(IconImageSizeProperty, value); } }
        public static readonly StyledProperty<float> BarWidthProperty = RegisterSize(nameof(BarWidth), 0f);
        public float BarWidth { get { return GetValue(BarWidthProperty); } set { SetValue(BarWidthProperty, value); } }

        public static readonly StyledProperty<bool> HasTrackProperty =
            StyledProperty.Register<EnergyPowerRowControl, bool>(nameof(HasTrack), true, UiInvalidation.Measure);
        public bool HasTrack { get { return GetValue(HasTrackProperty); } set { SetValue(HasTrackProperty, value); } }

        public static readonly StyledProperty<bool> HasFillProperty =
            StyledProperty.Register<EnergyPowerRowControl, bool>(nameof(HasFill), false, UiInvalidation.Measure);
        public bool HasFill { get { return GetValue(HasFillProperty); } set { SetValue(HasFillProperty, value); } }

        public static readonly StyledProperty<Vector4> FillClipMarginProperty = RegisterThickness(nameof(FillClipMargin));
        public Vector4 FillClipMargin { get { return GetValue(FillClipMarginProperty); } set { SetValue(FillClipMarginProperty, value); } }
        public static readonly StyledProperty<Vector4> FillMarginProperty = RegisterThickness(nameof(FillMargin));
        public Vector4 FillMargin { get { return GetValue(FillMarginProperty); } set { SetValue(FillMarginProperty, value); } }

        public static readonly StyledProperty<BackgroundBrush> FillBrushProperty =
            StyledProperty.Register<EnergyPowerRowControl, BackgroundBrush>(nameof(FillBrush), new SolidColorBrush(Color.White), UiInvalidation.Render);
        public BackgroundBrush FillBrush { get { return GetValue(FillBrushProperty); } set { SetValue(FillBrushProperty, value); } }

        public static readonly StyledProperty<BackgroundBrush> TrackBrushProperty =
            StyledProperty.Register<EnergyPowerRowControl, BackgroundBrush>(nameof(TrackBrush), new SolidColorBrush(Color.Transparent), UiInvalidation.Render);
        public BackgroundBrush TrackBrush { get { return GetValue(TrackBrushProperty); } set { SetValue(TrackBrushProperty, value); } }

        static StyledProperty<string> RegisterText(string name)
        {
            return StyledProperty.Register<EnergyPowerRowControl, string>(name, string.Empty, UiInvalidation.Measure);
        }

        static StyledProperty<float> RegisterSize(string name, float value)
        {
            return StyledProperty.Register<EnergyPowerRowControl, float>(name, value, UiInvalidation.Measure);
        }

        static StyledProperty<Vector4> RegisterThickness(string name)
        {
            return StyledProperty.Register<EnergyPowerRowControl, Vector4>(name, Vector4.Zero, UiInvalidation.Measure);
        }

        bool _reportedHover;
        Vector2 _measuredSize;
        float _measuredRatio = -1f;
        string _measuredIcon;
        Color _measuredAccent;

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(":selected", IsRowSelected);
            if (_reportedHover == IsPointerOver) return;
            _reportedHover = IsPointerOver;
            ICommand command = HoverCommand;
            if (command != null && command.CanExecute(_reportedHover))
                command.Execute(_reportedHover);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_measuredSize != availableSize || _measuredRatio != Ratio || _measuredIcon != RowIcon ||
                _measuredAccent != AccentColor)
            {
                _measuredSize = availableSize; _measuredRatio = Ratio; _measuredIcon = RowIcon;
                _measuredAccent = AccentColor;
                UpdateLayout(availableSize);
            }
            return TemplateRoot == null ? Vector2.Zero : TemplateRoot.Measure(availableSize);
        }

        void UpdateLayout(Vector2 size)
        {
            float iconCell = size.X * .16f;
            IconSize = Math.Max(0f, Math.Min(size.Y * .78f, Math.Max(1f, iconCell - 2f)));
            IconImageSize = Math.Max(0f, IconSize - 3f);
            HasIcon = !string.IsNullOrEmpty(RowIcon);
            HasGlyph = !HasIcon;

            BarWidth = size.X * .52f * 24f / 26f;
            float fraction = Ratio > .99f ? 1f : MathHelper.Clamp(Ratio, 0f, 1f);
            float fill = BarWidth * fraction;
            HasTrack = fraction < 1f;
            HasFill = fill > .001f;
            FillClipMargin = new Vector4(0f, 0f, Math.Max(0f, BarWidth - fill), 0f);
            FillMargin = new Vector4(0f, 0f, -Math.Max(0f, BarWidth - fill), 0f);
            FillBrush = new SolidColorBrush(AccentColor);
        }
    }
}
