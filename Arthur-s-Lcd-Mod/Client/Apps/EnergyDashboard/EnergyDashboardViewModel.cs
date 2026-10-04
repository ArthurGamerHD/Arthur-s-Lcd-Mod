using System;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using Adk.Gui.Styling;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.EnergyDashboard
{
    internal sealed class EnergyDashboardViewModel : ObservableObject
    {
        public const int TIER_COUNT = 6;
        public const float ROW_HEIGHT = 44f;
        const float HEADER_HEIGHT = 72f;
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

        public EnergyDashboardViewModel(string[] tierLabels, Action<int> selectTier)
        {
            for (int i = 0; i < TIER_COUNT; i++)
            {
                int tier = i;
                _tiers[i] = new EnergyTierModel(tierLabels[i], new DelegateCommand(_ =>
                {
                    if (selectTier != null) selectTier(tier);
                    SetSelectedTier(SelectedTier, true);
                }));
            }
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
        public int SelectedPage => Normalize(_pageTarget);
        public int PageTarget => _pageTarget;
        public int BarCount => _bars.Count;
        public int TierColumns => TIER_COUNT;
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

        public void SetSelectedTier(int tier, bool force = false)
        {
            if (!force && SelectedTier == tier) return;
            SelectedTier = tier;
            for (int i = 0; i < _tiers.Length; i++)
                _tiers[i].SetChecked(i == tier, force);
        }

        public void RefreshColors(Color foreground, Color fontColor, Color accent, Color secondaryContainer)
        {
            Foreground = foreground;
            AccentColor = accent;
            SecondaryContainerColor = secondaryContainer;
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
            float width = Math.Max(0f, _viewport.X);
            float pagesHeight = Math.Max(0f, _viewport.Y - HEADER_HEIGHT);
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

    internal sealed class EnergyTierModel : ObservableObject
    {
        bool _isChecked;

        public EnergyTierModel(string label, ICommand command)
        {
            Label = label;
            Command = command;
        }

        public string Label { get; }
        public bool IsChecked => _isChecked;
        public ICommand Command { get; }

        internal void SetChecked(bool value, bool force)
        {
            if (!RaiseAndSetIfChanged(ref _isChecked, value, nameof(IsChecked)) && force)
                RaisePropertyChanged<bool>(nameof(IsChecked));
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
        public string Glyph => _glyph;
        public string Amount => _amount;
        public string Label => _label;
        public string ValueText => _value;
        public float Ratio => _ratio;
        public Color Color => _color;
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
