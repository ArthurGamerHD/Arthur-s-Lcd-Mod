using System;
using System.Collections.Generic;
using Acrux.Core.Input;
using Acrux.Styling;
using Adk.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.EnergyDashboard
{
    [PreviewWith("VisiblePageCount", 1)]
    [PreviewWith("ViewportFraction", 0.9716599f)]
    [PreviewWith("Tiers", 6)]
    [PreviewWith("TierFontSize", 0.58f)]
    [PreviewWith("TierColumns", 6)]
    [PreviewWith("ShowNavigation", true)]
    [PreviewWith("SelectedTierIndex", 4)]
    [PreviewWith("SelectedTier", 4)]
    [PreviewWith("SelectedPage", 0)]
    [PreviewWith("SecondaryContainerColor", "#352937")]
    [PreviewWith("Production.ValueText", "9.10 MW/15.4 MW")]
    [PreviewWith("Production.Ratio", 0.59f)]
    [PreviewWith("Production.Label", "Production")]
    [PreviewWith("Production.Foreground", "#C5EDFF")]
    [PreviewWith("Production.FillColor", "#3A203F")]
    [PreviewWith("ProducerPage.Title", "Production")]
    [PreviewWith("ProducerPage.Rows", 2)]
    [PreviewWith("ProducerPage.RowColumns", 2)]
    [PreviewWith("ProducerPage.RightTime", "now")]
    [PreviewWith("ProducerPage.MiddleTime", "2m 30s")]
    [PreviewWith("ProducerPage.LeftTime", "5m")]
    [PreviewWith("ProducerPage.IsOnScreen", false)]
    [PreviewWith("ProducerPage.IsEmpty", false)]
    [PreviewWith("ProducerPage.IsCurrent", false)]
    [PreviewWith("ProducerPage.HasTimeScale", true)]
    [PreviewWith("ProducerPage.Foreground", "#C5EDFF")]
    [PreviewWith("ProducerPage.EmptyText", "No data")]
    [PreviewWith("ProducerPage.DotTexture", "CircleHollow")]
    [PreviewWith("ProducerPage.DotInnerColor", "#352937")]
    [PreviewWith("ProducerPage.DotColor", "#C5EDFF")]
    [PreviewWith("ProducerPage.AxisLabel3", "15.4 MW")]
    [PreviewWith("ProducerPage.AxisLabel2", "10.3 MW")]
    [PreviewWith("ProducerPage.AxisLabel1", "5.1 MW")]
    [PreviewWith("ProducerPage.AxisLabel0", "0 MW")]
    [PreviewWith("PanelCornerRadius", "8 8 8 8")]
    [PreviewWith("PanelBackground", "#40352937")]
    [PreviewWith("Pages.Title", "Consumption", "Production", "Charge")]
    [PreviewWith("Pages.Rows", 2)]
    [PreviewWith("Pages.RowColumns", 2, 2, 1)]
    [PreviewWith("Pages.RightTime", "now")]
    [PreviewWith("Pages.MiddleTime", "2m 30s")]
    [PreviewWith("Pages.LeftTime", "5m")]
    [PreviewWith("Pages.IsOnScreen", true, false, false)]
    [PreviewWith("Pages.IsEmpty", false)]
    [PreviewWith("Pages.IsCurrent", true, false, false)]
    [PreviewWith("Pages.HasTimeScale", true)]
    [PreviewWith("Pages.Foreground", "#C5EDFF")]
    [PreviewWith("Pages.EmptyText", "No data")]
    [PreviewWith("Pages.DotTexture", "Circle", "CircleHollow", "CircleHollow")]
    [PreviewWith("Pages.DotInnerColor", "#352937")]
    [PreviewWith("Pages.DotColor", "#C5EDFF")]
    [PreviewWith("Pages.AxisLabel3", "12 MW", "15.4 MW", "12 MWh")]
    [PreviewWith("Pages.AxisLabel2", "8 MW", "10.3 MW", "8 MWh")]
    [PreviewWith("Pages.AxisLabel1", "4 MW", "5.1 MW", "4 MWh")]
    [PreviewWith("Pages.AxisLabel0", "0 MW", "0 MW", "0 MWh")]
    [PreviewWith("Pages", 3)]
    [PreviewWith("PageWidth", 480f)]
    [PreviewWith("PageTarget", 0)]
    [PreviewWith("IndicatorButtonWidth", 136f)]
    [PreviewWith("HasCharge", true)]
    [PreviewWith("Foreground", "#C5EDFF")]
    [PreviewWith("Consumption.ValueText", "8.42 MW/12.0 MW")]
    [PreviewWith("Consumption.Ratio", 0.7f)]
    [PreviewWith("Consumption.Label", "Consumption")]
    [PreviewWith("Consumption.Foreground", "#C5EDFF")]
    [PreviewWith("Consumption.FillColor", "#E0A010")]
    [PreviewWith("ConsumerPage.Title", "Consumption")]
    [PreviewWith("ConsumerPage.Rows", 2)]
    [PreviewWith("ConsumerPage.RowColumns", 2)]
    [PreviewWith("ConsumerPage.RightTime", "now")]
    [PreviewWith("ConsumerPage.MiddleTime", "2m 30s")]
    [PreviewWith("ConsumerPage.LeftTime", "5m")]
    [PreviewWith("ConsumerPage.IsOnScreen", true)]
    [PreviewWith("ConsumerPage.IsEmpty", false)]
    [PreviewWith("ConsumerPage.IsCurrent", true)]
    [PreviewWith("ConsumerPage.HasTimeScale", true)]
    [PreviewWith("ConsumerPage.Foreground", "#C5EDFF")]
    [PreviewWith("ConsumerPage.EmptyText", "No data")]
    [PreviewWith("ConsumerPage.DotTexture", "Circle")]
    [PreviewWith("ConsumerPage.DotInnerColor", "#352937")]
    [PreviewWith("ConsumerPage.DotColor", "#C5EDFF")]
    [PreviewWith("ConsumerPage.AxisLabel3", "12 MW")]
    [PreviewWith("ConsumerPage.AxisLabel2", "8 MW")]
    [PreviewWith("ConsumerPage.AxisLabel1", "4 MW")]
    [PreviewWith("ConsumerPage.AxisLabel0", "0 MW")]
    [PreviewWith("ChargePage.Title", "Charge")]
    [PreviewWith("ChargePage.Rows", 1)]
    [PreviewWith("ChargePage.RowColumns", 1)]
    [PreviewWith("ChargePage.RightTime", "now")]
    [PreviewWith("ChargePage.MiddleTime", "2m 30s")]
    [PreviewWith("ChargePage.LeftTime", "5m")]
    [PreviewWith("ChargePage.IsOnScreen", false)]
    [PreviewWith("ChargePage.IsEmpty", false)]
    [PreviewWith("ChargePage.IsCurrent", false)]
    [PreviewWith("ChargePage.HasTimeScale", true)]
    [PreviewWith("ChargePage.Foreground", "#C5EDFF")]
    [PreviewWith("ChargePage.EmptyText", "No data")]
    [PreviewWith("ChargePage.DotTexture", "CircleHollow")]
    [PreviewWith("ChargePage.DotInnerColor", "#352937")]
    [PreviewWith("ChargePage.DotColor", "#C5EDFF")]
    [PreviewWith("ChargePage.AxisLabel3", "12 MWh")]
    [PreviewWith("ChargePage.AxisLabel2", "8 MWh")]
    [PreviewWith("ChargePage.AxisLabel1", "4 MWh")]
    [PreviewWith("ChargePage.AxisLabel0", "0 MWh")]
    [PreviewWith("Charge.ValueText", "9.6 MWh/12.0 MWh (80%)")]
    [PreviewWith("Charge.Ratio", 0.8f)]
    [PreviewWith("Charge.Label", "Charge")]
    [PreviewWith("Charge.Foreground", "#C5EDFF")]
    [PreviewWith("Charge.FillColor", "#6ED282")]
    [PreviewWith("Bars.ValueText", "8.42 MW/12.0 MW", "9.10 MW/15.4 MW", "9.6 MWh/12.0 MWh (80%)")]
    [PreviewWith("Bars.Ratio", 0.7f, 0.59f, 0.8f)]
    [PreviewWith("Bars.Label", "Consumption", "Production", "Charge")]
    [PreviewWith("Bars.Foreground", "#C5EDFF")]
    [PreviewWith("Bars.FillColor", "#E0A010", "#3A203F", "#6ED282")]
    [PreviewWith("Bars", 3)]
    [PreviewWith("BarCount", 3)]
    [PreviewWith("ArrowColor", "#B4C5EDFF")]
    [PreviewWith("ArrowButtonWidth", 42f)]
    [PreviewWith("AccentColor", "#D1B1D7")]
    internal sealed class EnergyDashboardViewModel : ObservableObject
    {
        public const int TIER_COUNT = 6;
        public const float ROW_HEIGHT = 44f;
        const float HEADER_HEIGHT = 76f;
        const float PANEL_INSET = 9f;
        const float PANEL_RADIUS = 8f;
        const byte PANEL_ALPHA = 64;
        const float TIER_FONT_SCALE = .58f;
        const float PAGE_TARGET_WIDTH = 480f;
        const float NAVIGATION_HEIGHT = 42f;
        const float ARROW_WIDTH = 42f;
        const float INDICATOR_WIDTH = 136f;
        const float NAVIGATION_GAP = 8f;
        const float GRAPH_WEIGHT = 90f;
        const float LIST_WEIGHT = 140f;

        readonly EnergyTierModel[] _tiers = new EnergyTierModel[TIER_COUNT];
        IReadOnlyList<EnergyStatBarModel> _bars;
        IReadOnlyList<EnergyPageModel> _pages;
        Vector2 _viewport;
        int _pageTarget;
        bool _hasCharge = true;

        readonly Action<int> _selectTier;

        public EnergyDashboardViewModel(string[] tierLabels, Action<int> selectTier)
        {
            _selectTier = selectTier;
            for (int i = 0; i < TIER_COUNT; i++)
                _tiers[i] = new EnergyTierModel(tierLabels[i], this);
            ConsumerPage = new EnergyPageModel(this);
            ProducerPage = new EnergyPageModel(this);
            ChargePage = new EnergyPageModel(this);
            PreviousPageCommand = new DelegateCommand(_ => MovePage(-1));
            NextPageCommand = new DelegateCommand(_ => MovePage(1));
            UpdateMembership();
        }

        public EnergyStatBarModel Consumption { get; } = new EnergyStatBarModel();
        public EnergyStatBarModel Production { get; } = new EnergyStatBarModel();
        public EnergyStatBarModel Charge { get; } = new EnergyStatBarModel();
        public EnergyPageModel ConsumerPage { get; }
        public EnergyPageModel ProducerPage { get; }
        public EnergyPageModel ChargePage { get; }
        public IReadOnlyList<EnergyStatBarModel> Bars => _bars;
        public IReadOnlyList<EnergyTierModel> Tiers => _tiers;
        public IReadOnlyList<EnergyPageModel> Pages => _pages;
        public bool HasCharge => _hasCharge;
        public int SelectedTier { get; private set; }

        public int SelectedTierIndex
        {
            get { return SelectedTier; }
            set
            {
                if (value == SelectedTier || value < 0 || value >= TIER_COUNT)
                    return;
                if (_selectTier != null)
                    _selectTier(value);
            }
        }

        public int SelectedPage => Normalize(_pageTarget);
        public int PageTarget => _pageTarget;
        public int BarCount => _bars.Count;
        public int TierColumns => TIER_COUNT;
        public Color PanelBackground { get; private set; } = new Color(255, 255, 255, PANEL_ALPHA);
        public float TierFontSize { get; private set; } = TIER_FONT_SCALE;

        // The panel's top corner under the first or last tab is square while that tab is selected, so the tab joins the
        // panel without a notch.
        public Vector4 PanelCornerRadius => new Vector4(
            SelectedTier == 0 ? 0f : PANEL_RADIUS,
            SelectedTier == TIER_COUNT - 1 ? 0f : PANEL_RADIUS,
            PANEL_RADIUS,
            PANEL_RADIUS);
        public float PageWidth { get; private set; }
        public float ViewportFraction { get; private set; } = 1f;
        public int VisiblePageCount { get; private set; } = 1;
        public bool ShowNavigation { get; private set; }
        public float ArrowButtonWidth { get; private set; } = ARROW_WIDTH;
        public float IndicatorButtonWidth { get; private set; } = INDICATOR_WIDTH;
        public Color ArrowColor { get; private set; } = new Color(255, 255, 255, 180);
        public Color Foreground { get; private set; } = Color.White;
        public Color AccentColor { get; private set; } = Color.White;
        public Color SecondaryContainerColor { get; private set; } = Color.White;
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }

        public void SetViewport(Vector2 size)
        {
            if (_viewport == size) return;
            _viewport = size;
            UpdateLayout();
        }

        public void SetHasCharge(bool hasCharge)
        {
            if (_hasCharge == hasCharge) return;
            _hasCharge = hasCharge;
            UpdateMembership();
        }

        public void SetSelectedTier(int tier)
        {
            if (SelectedTier == tier) return;
            SelectedTier = tier;
            RaisePropertyChanged<int>(nameof(SelectedTierIndex));
            RaisePropertyChanged<Vector4>(nameof(PanelCornerRadius));
        }

        public void SetFontScale(float fontScale)
        {
            float size = fontScale * TIER_FONT_SCALE;
            if (TierFontSize == size) return;
            TierFontSize = size;
            RaisePropertyChanged<float>(nameof(TierFontSize));
            for (int i = 0; i < _tiers.Length; i++)
                _tiers[i].RaiseFontSizeChanged();
        }

        public void RefreshColors(Color foreground, Color fontColor, Color accent, Color secondaryContainer)
        {
            Foreground = foreground;
            AccentColor = accent;
            SecondaryContainerColor = secondaryContainer;
            PanelBackground = new Color(secondaryContainer.R, secondaryContainer.G, secondaryContainer.B, PANEL_ALPHA);
            ArrowColor = new Color(fontColor.R, fontColor.G, fontColor.B, (byte)(fontColor.A * 180 / 255));
            RefreshPageIndicators();
            RaisePropertyChanged<object>(string.Empty);
        }

        public void SelectPage(int index)
        {
            int count = _pages.Count;
            if (count == 0 || Normalize(index) == SelectedPage) return;
            int lower = index + (int)Math.Floor((_pageTarget - index) / (float)count) * count;
            int upper = lower + count;
            SetPageTarget(_pageTarget - lower < upper - _pageTarget ? lower : upper);
        }

        void MovePage(int step)
        {
            if (_pages.Count > 1) SetPageTarget(_pageTarget + step);
        }

        void SetPageTarget(int target)
        {
            _pageTarget = target;
            RefreshPageIndicators();
            RaisePropertyChanged<int>(nameof(PageTarget));
            RaisePropertyChanged<int>(nameof(SelectedPage));
        }

        int Normalize(int index)
        {
            int count = _pages.Count;
            return count == 0 ? 0 : (index % count + count) % count;
        }

        internal void SelectPage(EnergyPageModel page)
        {
            for (int i = 0; i < _pages.Count; i++)
                if (ReferenceEquals(_pages[i], page)) SelectPage(i);
        }

        void UpdateMembership()
        {
            _bars = _hasCharge
                ? new[] { Consumption, Production, Charge }
                : new[] { Consumption, Production };
            _pages = _hasCharge
                ? new[] { ConsumerPage, ProducerPage, ChargePage }
                : new[] { ConsumerPage, ProducerPage };
            int selected = SelectedPage;
            _pageTarget = selected;
            UpdateLayout();
        }

        void UpdateLayout()
        {
            float width = Math.Max(0f, _viewport.X - PANEL_INSET * 2f);
            float pagesHeight = Math.Max(0f, _viewport.Y - HEADER_HEIGHT - PANEL_INSET * 2f);
            int count = _pages.Count;
            VisiblePageCount = Math.Max(1, Math.Min(count, (int)Math.Floor(width / PAGE_TARGET_WIDTH)));
            bool allPagesFit = VisiblePageCount >= count;
            ShowNavigation = count > 1 && !allPagesFit && pagesHeight > NAVIGATION_HEIGHT;
            float contentHeight = ShowNavigation ? Math.Max(1f, pagesHeight - NAVIGATION_HEIGHT) : pagesHeight;
            PageWidth = allPagesFit
                ? width / VisiblePageCount
                : Math.Min(PAGE_TARGET_WIDTH, width / VisiblePageCount);
            ViewportFraction = width > 0f ? PageWidth / width : 1f;

            float arrow = ARROW_WIDTH, indicator = INDICATOR_WIDTH;
            if (arrow * 2f + indicator + NAVIGATION_GAP * 2f > width)
            {
                arrow = Math.Max(18f, Math.Min(arrow, width * .22f));
                indicator = Math.Max(1f, width - arrow * 2f - NAVIGATION_GAP * 2f);
            }
            ArrowButtonWidth = arrow;
            IndicatorButtonWidth = indicator;

            float listHeight = contentHeight * LIST_WEIGHT / (GRAPH_WEIGHT + LIST_WEIGHT);
            ConsumerPage.SetListViewport(PageWidth, listHeight);
            ProducerPage.SetListViewport(PageWidth, listHeight);
            ChargePage.SetListViewport(PageWidth, listHeight);
            RefreshPageIndicators();
            RaisePropertyChanged<object>(string.Empty);
        }

        void RefreshPageIndicators()
        {
            int count = _pages.Count;
            for (int i = 0; i < count; i++)
            {
                int selected = SelectedPage;
                int offset = ((i - selected) % count + count) % count;
                _pages[i].SetIndicator(i == selected, offset < VisiblePageCount,
                    Foreground, AccentColor, SecondaryContainerColor);
            }
        }
    }

    internal sealed class EnergyStatBarModel : ObservableObject
    {
        string _label = string.Empty, _valueText = string.Empty;
        float _ratio;
        Color _fillColor = Color.White, _foreground = Color.White;
        public string Label => _label;
        public string ValueText => _valueText;
        public float Ratio => _ratio;
        public Color FillColor => _fillColor;
        public Color Foreground => _foreground;

        public void Update(string label, string valueText, float ratio, Color fillColor, Color foreground)
        {
            RaiseAndSetIfChanged(ref _label, label ?? string.Empty, nameof(Label));
            RaiseAndSetIfChanged(ref _valueText, valueText ?? string.Empty, nameof(ValueText));
            RaiseAndSetIfChanged(ref _ratio, ratio, nameof(Ratio));
            RaiseAndSetIfChanged(ref _fillColor, fillColor, nameof(FillColor));
            RaiseAndSetIfChanged(ref _foreground, foreground, nameof(Foreground));
        }
    }

    /// <summary>A time-range tab; its content is the shared dashboard pages, reached through <see cref="Owner"/>.</summary>
    internal sealed class EnergyTierModel : ObservableObject
    {
        public EnergyTierModel(string label, EnergyDashboardViewModel owner)
        {
            Label = label;
            Owner = owner;
        }

        [PreviewWith("1s", "5s", "30s", "1m", "5m", "All")]
        public string Label { get; }
        public EnergyDashboardViewModel Owner { get; }
        [PreviewWith(0.58f)]
        public float FontSize => Owner.TierFontSize;

        internal void RaiseFontSizeChanged()
        {
            RaisePropertyChanged<float>(nameof(FontSize));
        }
    }

    internal sealed class EnergyGraphSeries
    {
        public EnergyGraphSeries(Vector2[] points, Color color)
        {
            Points = points ?? new Vector2[0];
            Color = color;
        }

        public Vector2[] Points { get; }
        public Color Color { get; }
    }

    internal sealed class EnergyPageModel : ObservableObject
    {
        static readonly EnergyPowerRowModel[] NoRows = new EnergyPowerRowModel[0];
        static readonly EnergyGraphSeries[] NoSeries = new EnergyGraphSeries[0];
        readonly EnergyDashboardViewModel _owner;
        IReadOnlyList<EnergyPowerRowModel> _rows = NoRows;
        IReadOnlyList<EnergyGraphSeries> _series = NoSeries;
        float _pageWidth, _listHeight;
        int _rowColumns = 1;
        string _title = string.Empty, _left = string.Empty, _middle = string.Empty, _right = string.Empty;
        string _axis0 = string.Empty, _axis1 = string.Empty, _axis2 = string.Empty, _axis3 = string.Empty;
        string _emptyText = string.Empty;
        bool _hasTimeScale, _isEmpty = true, _isCurrent, _isOnScreen;
        Color _foreground = Color.White, _dotColor = Color.White, _dotInnerColor = Color.White;
        string _dotTexture = "CircleHollow";

        internal EnergyPageModel(EnergyDashboardViewModel owner)
        {
            _owner = owner;
            SelectCommand = new DelegateCommand(_ => _owner.SelectPage(this));
        }

        public IReadOnlyList<EnergyPowerRowModel> Rows => _rows;
        public IReadOnlyList<EnergyGraphSeries> Series => _series;
        public int RowColumns => _rowColumns;
        public string Title => _title;
        public string AxisLabel0 => _axis0;
        public string AxisLabel1 => _axis1;
        public string AxisLabel2 => _axis2;
        public string AxisLabel3 => _axis3;
        public string LeftTime => _left;
        public string MiddleTime => _middle;
        public string RightTime => _right;
        public bool HasTimeScale => _hasTimeScale;
        public bool IsEmpty => _isEmpty;
        public string EmptyText => _emptyText;
        public Color Foreground => _foreground;
        public bool IsCurrent => _isCurrent;
        public bool IsOnScreen => _isOnScreen;
        public string DotTexture => _dotTexture;
        public BackgroundBrush DotMask { get; private set; } = new ImageBrush("CircleHollow");
        public Color DotColor => _dotColor;
        public Color DotInnerColor => _dotInnerColor;
        public ICommand SelectCommand { get; }

        public void SetRows(IReadOnlyList<EnergyPowerRowModel> rows)
        {
            rows = rows ?? NoRows;
            bool changed = rows.Count != _rows.Count;
            for (int i = 0; !changed && i < rows.Count; i++)
                changed = !ReferenceEquals(rows[i], _rows[i]);
            if (!changed) return;
            var snapshot = new EnergyPowerRowModel[rows.Count];
            for (int i = 0; i < rows.Count; i++) snapshot[i] = rows[i];
            _rows = snapshot;
            UpdateRowWidth();
            RaisePropertyChanged<IReadOnlyList<EnergyPowerRowModel>>(nameof(Rows));
        }

        public void UpdateGraph(string title, string[] axisLabels, string left, string middle, string right,
            bool hasTimeScale, bool isEmpty, string emptyText, IReadOnlyList<EnergyGraphSeries> series,
            Color foreground)
        {
            RaiseAndSetIfChanged(ref _title, title ?? string.Empty, nameof(Title));
            RaiseAndSetIfChanged(ref _axis0, axisLabels[0], nameof(AxisLabel0));
            RaiseAndSetIfChanged(ref _axis1, axisLabels[1], nameof(AxisLabel1));
            RaiseAndSetIfChanged(ref _axis2, axisLabels[2], nameof(AxisLabel2));
            RaiseAndSetIfChanged(ref _axis3, axisLabels[3], nameof(AxisLabel3));
            RaiseAndSetIfChanged(ref _left, left ?? string.Empty, nameof(LeftTime));
            RaiseAndSetIfChanged(ref _middle, middle ?? string.Empty, nameof(MiddleTime));
            RaiseAndSetIfChanged(ref _right, right ?? string.Empty, nameof(RightTime));
            RaiseAndSetIfChanged(ref _hasTimeScale, hasTimeScale, nameof(HasTimeScale));
            RaiseAndSetIfChanged(ref _isEmpty, isEmpty, nameof(IsEmpty));
            RaiseAndSetIfChanged(ref _emptyText, emptyText ?? string.Empty, nameof(EmptyText));
            RaiseAndSetIfChanged(ref _foreground, foreground, nameof(Foreground));
            series = series ?? NoSeries;
            if (SameSeries(_series, series)) return;
            _series = series;
            RaisePropertyChanged<IReadOnlyList<EnergyGraphSeries>>(nameof(Series));
        }

        static bool SameSeries(IReadOnlyList<EnergyGraphSeries> left, IReadOnlyList<EnergyGraphSeries> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i].Color != right[i].Color) return false;
                Vector2[] a = left[i].Points, b = right[i].Points;
                if (a.Length != b.Length) return false;
                for (int p = 0; p < a.Length; p++)
                    if (a[p] != b[p]) return false;
            }
            return true;
        }

        internal void SetListViewport(float pageWidth, float listHeight)
        {
            _pageWidth = pageWidth;
            _listHeight = listHeight;
            UpdateRowWidth();
        }

        internal void SetIndicator(bool current, bool onScreen, Color foreground, Color accent, Color secondary)
        {
            RaiseAndSetIfChanged(ref _isCurrent, current, nameof(IsCurrent));
            RaiseAndSetIfChanged(ref _isOnScreen, onScreen, nameof(IsOnScreen));
            if (RaiseAndSetIfChanged(ref _dotTexture, current || onScreen ? "Circle" : "CircleHollow", nameof(DotTexture)))
            {
                DotMask = new ImageBrush(_dotTexture);
                RaisePropertyChanged<BackgroundBrush>(nameof(DotMask));
            }
            RaiseAndSetIfChanged(ref _dotColor, current ? foreground : onScreen ? accent : foreground, nameof(DotColor));
            RaiseAndSetIfChanged(ref _dotInnerColor, secondary, nameof(DotInnerColor));
        }

        void UpdateRowWidth()
        {
            float width = Math.Max(0f, _pageWidth);
            int columns = Columns(width);
            if (Math.Ceiling(_rows.Count / (float)columns) * EnergyDashboardViewModel.ROW_HEIGHT > _listHeight + .001f)
            {
                width = Math.Max(0f, width - 7.5f);
                columns = Columns(width);
            }
            RaiseAndSetIfChanged(ref _rowColumns, columns, nameof(RowColumns));
        }

        int Columns(float width)
        {
            int columns = Math.Max(1, (int)Math.Floor(width / 220f));
            if (_rows.Count > 0) columns = Math.Min(columns, _rows.Count);
            return Math.Max(1, columns);
        }
    }

    internal sealed class EnergyPowerRowModel : ObservableObject
    {
        string _icon = string.Empty, _glyph = string.Empty, _amount = string.Empty, _label = string.Empty, _value = string.Empty;
        float _ratio;
        Color _color = Color.White, _foreground = Color.White;
        bool _isSelected;

        public EnergyPowerRowModel(Action clicked, Action<bool> hovered)
        {
            Command = new DelegateCommand(_ => { if (clicked != null) clicked(); });
            HoverCommand = new DelegateCommand(value => { if (hovered != null) hovered(value is bool && (bool)value); });
        }

        public string Icon => _icon;
        [PreviewWith("R", "A")]
        public string Glyph => _glyph;
        [PreviewWith("4", "6")]
        public string Amount => _amount;
        [PreviewWith("Refinery", "Assembler")]
        public string Label => _label;
        [PreviewWith("4.80 MW", "2.10 MW")]
        public string ValueText => _value;
        [PreviewWith(0.57f, 0.25f)]
        public float Ratio => _ratio;
        [PreviewWith("#E6785A", "#B482DC")]
        public Color Color => _color;
        [PreviewWith("#C5EDFF")]
        public Color Foreground => _foreground;
        public bool IsSelected => _isSelected;
        public ICommand Command { get; }
        public ICommand HoverCommand { get; }

        public void Update(string icon, string glyph, string amount, string label, string value, float ratio)
        {
            RaiseAndSetIfChanged(ref _icon, icon ?? string.Empty, nameof(Icon));
            RaiseAndSetIfChanged(ref _glyph, glyph ?? string.Empty, nameof(Glyph));
            RaiseAndSetIfChanged(ref _amount, amount ?? string.Empty, nameof(Amount));
            RaiseAndSetIfChanged(ref _label, label ?? string.Empty, nameof(Label));
            RaiseAndSetIfChanged(ref _value, value ?? string.Empty, nameof(ValueText));
            RaiseAndSetIfChanged(ref _ratio, ratio, nameof(Ratio));
        }

        public void UpdateState(Color color, Color foreground, bool selected)
        {
            RaiseAndSetIfChanged(ref _color, color, nameof(Color));
            RaiseAndSetIfChanged(ref _foreground, foreground, nameof(Foreground));
            RaiseAndSetIfChanged(ref _isSelected, selected, nameof(IsSelected));
        }
    }
}
