using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Styling;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.NpcMarket
{
    internal sealed class NpcMarketSortHeader : Button
    {
        public static readonly StyledProperty<MarketSortHeaderModel> HeaderProperty =
            StyledProperty.Register<NpcMarketSortHeader, MarketSortHeaderModel>(nameof(Header), null, UiInvalidation.Render);
        public MarketSortHeaderModel Header { get { return GetValue(HeaderProperty); } set { SetValue(HeaderProperty, value); } }

        public static readonly StyledProperty<string> LabelProperty =
            StyledProperty.Register<NpcMarketSortHeader, string>(nameof(Label), string.Empty, UiInvalidation.Measure);
        public string Label { get { return GetValue(LabelProperty); } set { SetValue(LabelProperty, value); } }

        public static readonly StyledProperty<bool> IsActiveProperty =
            StyledProperty.Register<NpcMarketSortHeader, bool>(nameof(IsActive), false, UiInvalidation.Measure | UiInvalidation.Style);
        public bool IsActive { get { return GetValue(IsActiveProperty); } set { SetValue(IsActiveProperty, value); } }

        public static readonly StyledProperty<Transform> IndicatorTransformProperty =
            StyledProperty.Register<NpcMarketSortHeader, Transform>(nameof(IndicatorTransform), null, UiInvalidation.Render);
        public Transform IndicatorTransform { get { return GetValue(IndicatorTransformProperty); } set { SetValue(IndicatorTransformProperty, value); } }

        public static readonly StyledProperty<bool> IsRightAlignedProperty =
            StyledProperty.Register<NpcMarketSortHeader, bool>(nameof(IsRightAligned), true, UiInvalidation.Measure);
        public bool IsRightAligned { get { return GetValue(IsRightAlignedProperty); } set { SetValue(IsRightAlignedProperty, value); } }

        public static readonly StyledProperty<bool> IsLeftAlignedProperty =
            StyledProperty.Register<NpcMarketSortHeader, bool>(nameof(IsLeftAligned), false, UiInvalidation.Measure);
        public bool IsLeftAligned { get { return GetValue(IsLeftAlignedProperty); } set { SetValue(IsLeftAlignedProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<NpcMarketSortHeader, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<NpcMarketSortHeader, float>(nameof(FontSize), 1f, UiInvalidation.Measure);
        public float FontSize { get { return GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }

        MarketSortHeaderModel _observed;

        protected override void OnPropertyChanged(IStyledProperty property)
        {
            base.OnPropertyChanged(property);
            if (property == HeaderProperty)
                Observe(Header);
            else if (property == IsRightAlignedProperty)
                IsLeftAligned = !IsRightAligned;
        }

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(":active", IsActive);
        }

        void Observe(MarketSortHeaderModel header)
        {
            if (_observed != null)
                _observed.PropertyChanged -= OnHeaderChanged;
            _observed = header;
            if (header != null)
                header.PropertyChanged += OnHeaderChanged;
            Sync();
        }

        void OnHeaderChanged(ObservableObject sender, string property)
        {
            Sync();
        }

        void Sync()
        {
            var header = _observed;
            Label = header == null ? string.Empty : header.Label;
            IsActive = header != null && header.IsActive;
            IndicatorTransform = header == null ? null : header.IndicatorTransform;
            Command = header == null ? null : header.Command;
        }

        public override void Dispose()
        {
            Observe(null);
            base.Dispose();
        }
    }

    internal sealed class NpcMarketTrend : TemplatedControl
    {
        public static readonly StyledProperty<MarketTrendModel> TrendProperty =
            StyledProperty.Register<NpcMarketTrend, MarketTrendModel>(nameof(Trend), null, UiInvalidation.Render);
        public MarketTrendModel Trend { get { return GetValue(TrendProperty); } set { SetValue(TrendProperty, value); } }

        public static readonly StyledProperty<string> TextProperty =
            StyledProperty.Register<NpcMarketTrend, string>(nameof(Text), string.Empty, UiInvalidation.Measure);
        public string Text { get { return GetValue(TextProperty); } set { SetValue(TextProperty, value); } }

        public static readonly StyledProperty<Color> TrendColorProperty =
            StyledProperty.Register<NpcMarketTrend, Color>(nameof(TrendColor), Color.White, UiInvalidation.Render);
        public Color TrendColor { get { return GetValue(TrendColorProperty); } set { SetValue(TrendColorProperty, value); } }

        public static readonly StyledProperty<BackgroundBrush> IconMaskProperty =
            StyledProperty.Register<NpcMarketTrend, BackgroundBrush>(nameof(IconMask), null, UiInvalidation.Render);
        public BackgroundBrush IconMask { get { return GetValue(IconMaskProperty); } set { SetValue(IconMaskProperty, value); } }

        public static readonly StyledProperty<Transform> IconTransformProperty =
            StyledProperty.Register<NpcMarketTrend, Transform>(nameof(IconTransform), null, UiInvalidation.Render);
        public Transform IconTransform { get { return GetValue(IconTransformProperty); } set { SetValue(IconTransformProperty, value); } }

        public static readonly StyledProperty<bool> HasValueProperty =
            StyledProperty.Register<NpcMarketTrend, bool>(nameof(HasValue), true, UiInvalidation.Measure);
        public bool HasValue { get { return GetValue(HasValueProperty); } set { SetValue(HasValueProperty, value); } }

        public static readonly StyledProperty<bool> IsPlaceholderProperty =
            StyledProperty.Register<NpcMarketTrend, bool>(nameof(IsPlaceholder), false, UiInvalidation.Measure);
        public bool IsPlaceholder { get { return GetValue(IsPlaceholderProperty); } set { SetValue(IsPlaceholderProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<NpcMarketTrend, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<NpcMarketTrend, float>(nameof(FontSize), 1f, UiInvalidation.Measure);
        public float FontSize { get { return GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }

        MarketTrendModel _observed;

        protected override void OnPropertyChanged(IStyledProperty property)
        {
            base.OnPropertyChanged(property);
            if (property != TrendProperty)
                return;
            if (_observed != null)
                _observed.PropertyChanged -= OnTrendChanged;
            _observed = Trend;
            if (_observed != null)
                _observed.PropertyChanged += OnTrendChanged;
            Sync();
        }

        void OnTrendChanged(ObservableObject sender, string property)
        {
            Sync();
        }

        void Sync()
        {
            var trend = _observed;
            Text = trend == null ? string.Empty : trend.Text;
            TrendColor = trend == null ? Color.White : trend.Color;
            IconMask = trend == null ? null : trend.IconMask;
            IconTransform = trend == null ? null : trend.IconTransform;
            HasValue = trend != null && trend.HasValue;
            IsPlaceholder = trend == null || trend.IsPlaceholder;
        }

        public override void Dispose()
        {
            if (_observed != null)
                _observed.PropertyChanged -= OnTrendChanged;
            _observed = null;
            base.Dispose();
        }
    }
}
