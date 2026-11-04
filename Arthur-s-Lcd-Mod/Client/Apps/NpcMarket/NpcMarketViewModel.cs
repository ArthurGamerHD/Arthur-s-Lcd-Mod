using Acrux.Core.Input;
using Acrux.Styling;
using System;
using System.Collections.Generic;
using LcdMod.Client.Helpers;
using LcdMod.Client.Market;
using Adk.Mvvm;
using VRage;
using VRageMath;

namespace LcdMod.Client.Apps.NpcMarket
{
    /// <summary>What the market view model reads from and asks of its app (<see cref="NpcMarketApp"/>; a design-time host in the preview).</summary>
    internal interface INpcMarketHost
    {
        NpcMarketMode Mode { get; }
        IReadOnlyList<NpcMarketRow> NativeRows { get; }
        string NativeUpdatedText { get; }
        string NativeNextTickText { get; }
        string NativeRefreshText { get; }
        string NativeSearchQuery { get; }
        string NativeEmptyText { get; }
        string NativeNoTraderText { get; }
        bool NativeRefreshEnabled { get; }
        bool NativeModeEnabled { get; }
        NpcMarketSortColumn SortColumn { get; }
        bool SortDescending { get; }
        NpcMarketApp.MarketContentState ContentState { get; }
        string NativeSearchPlaceholder { get; }
        float NativeFontScale { get; }
        Color NativeForeground { get; }
        Color ResolveMarketColor(Gui.Styling.ResourceKey<Color> key, Color fallback);
        void SetNativeSearch(string value);
        void ClearNativeSearch();
        void SortNative(NpcMarketSortColumn column);
        void OpenNativeRow(string itemKey, NpcMarketMode mode);
        void OnNativePageChanged(int pageIndex);
        void RefreshNativeMarket();
        void SetMode(NpcMarketMode mode);
    }

    internal sealed class NpcMarketViewModel : ObservableObject
    {
        public const float HEADER_HEIGHT = 28f;
        public const float ROW_HEIGHT = 34f;
        const float SINGLE_SIDE_LIST_MIN_WIDTH = 480f;
        const float BOTH_LIST_MIN_WIDTH = 720f;
        const float PAGE_GAP = 12f;
        const float NAVIGATION_HEIGHT = 42f;
        const float ARROW_WIDTH = 42f;
        const float INDICATOR_WIDTH = 136f;
        const float NAVIGATION_GAP = 8f;
        const float INDICATOR_DOT_SIZE = 8f;
        const float INDICATOR_DOT_GAP = 8f;
        const float FOOTER_HEIGHT = 58f;

        readonly INpcMarketHost _app;
        readonly Dictionary<string, MarketRowModel> _rowsByKey = new Dictionary<string, MarketRowModel>(StringComparer.Ordinal);
        readonly List<MarketRowModel> _rows = new List<MarketRowModel>();
        readonly List<MarketPageModel> _pagePool = new List<MarketPageModel>();
        IReadOnlyList<MarketPageModel> _pages = new MarketPageModel[0];
        readonly MarketSortHeaderModel[] _headers;
        Vector2 _viewport;
        int _rowsPerPage = 1;
        int _selectedPage;
        float _viewportFraction = 1f;
        bool _showNavigation;
        float _arrowButtonWidth = ARROW_WIDTH;
        float _indicatorButtonWidth = INDICATOR_WIDTH;
        bool _isBoth;
        string _searchQuery = string.Empty;
        string _messageText = string.Empty;
        string _messageIcon = "Warning";
        Color _messageColor = Color.White;
        bool _showMessage;
        bool _showTable;
        bool _searchOpen;
        Color _foreground = Color.White;
        Color _accent = Color.White;
        Color _secondaryContainer = Color.Gray;
        Color _arrowColor = Color.White;

        public NpcMarketViewModel(INpcMarketHost app)
        {
            _app = app;
            _headers = new[]
            {
                new MarketSortHeaderModel(NpcMarketSortColumn.Name, app),
                new MarketSortHeaderModel(NpcMarketSortColumn.Price, app),
                new MarketSortHeaderModel(NpcMarketSortColumn.Trend, app),
                new MarketSortHeaderModel(NpcMarketSortColumn.BuyPrice, app),
                new MarketSortHeaderModel(NpcMarketSortColumn.SellPrice, app),
                new MarketSortHeaderModel(NpcMarketSortColumn.BuyTrend, app),
                new MarketSortHeaderModel(NpcMarketSortColumn.SellTrend, app)
            };
            SearchCommand = new DelegateCommand(_ => IsSearchOpen = true);
            ClearSearchCommand = new DelegateCommand(_ => _app.ClearNativeSearch());
            RefreshMarketCommand = new DelegateCommand(_ => _app.RefreshNativeMarket());
            PreviousPageCommand = new DelegateCommand(_ => MovePage(-1));
            NextPageCommand = new DelegateCommand(_ => MovePage(1));
        }

        [PreviewWith(1)]
        public IReadOnlyList<MarketPageModel> Pages => _pages;
        public int SelectedPage => _selectedPage;
        [PreviewWith(1f)]
        public float ViewportFraction => _viewportFraction;
        public bool ShowNavigation => _showNavigation;
        [PreviewWith(42f)]
        public float ArrowButtonWidth => _arrowButtonWidth;
        [PreviewWith(136f)]
        public float IndicatorButtonWidth => _indicatorButtonWidth;
        [PreviewWith("#B4C5EDFF")]
        public Color ArrowColor => _arrowColor;
        public bool HasSearch => _searchQuery.Length > 0;
        public string SearchQuery => _searchQuery;

        // The header's search box is expanded: opened by the user, or holding a query.
        public bool IsSearchOpen
        {
            get { return _searchOpen || HasSearch; }
            set
            {
                if (_searchOpen == value)
                    return;
                _searchOpen = value;
                NotifySearchState();
            }
        }

        public string SearchText
        {
            get { return _searchQuery; }
            set { _app.SetNativeSearch(value ?? string.Empty); }
        }

        [PreviewWith("Search")]
        public string SearchPlaceholder => _app.NativeSearchPlaceholder;
        [PreviewWith(true)]
        public bool ShowTable => _showTable;
        public bool ShowMessage => _showMessage;
        public string MessageText => _messageText;
        [PreviewWith("Warning")]
        public string MessageIcon => _messageIcon;
        public BackgroundBrush MessageIconMask { get; private set; } = new ImageBrush("Warning") { Stretch = Stretch.Uniform };
        [PreviewWith("#E0A010")]
        public Color MessageColor => _messageColor;
        [PreviewWith(166.62401f)]
        public float MessageIconSize { get; private set; } = 64f;
        [PreviewWith("2 min ago")]
        public string UpdatedText => _app.NativeUpdatedText;
        [PreviewWith("8 min")]
        public string NextTickText => _app.NativeNextTickText;
        [PreviewWith("Refresh")]
        public string RefreshText => _app.NativeRefreshText;
        [PreviewWith(true)]
        public bool RefreshEnabled => _app.NativeRefreshEnabled;
        [PreviewWith(true)]
        public bool ModeEnabled => _app.NativeModeEnabled;
        public bool CanNavigate => _pages.Count > 1 && _showNavigation;
        public ICommand SearchCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand RefreshMarketCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public INpcMarketHost App => _app;
        [PreviewWith(1f)]
        public float FontScale => _app.NativeFontScale;

        internal MarketSortHeaderModel Header(NpcMarketSortColumn column) => _headers[(int)column];

        public void SetViewport(Vector2 size)
        {
            if (_viewport == size)
                return;
            _viewport = size;
            UpdateLayout();
        }

        public void MovePage(int step)
        {
            if (_pages.Count > 1)
                SelectPage(_selectedPage + step);
        }

        public void SelectPage(int index)
        {
            int count = _pages.Count;
            int page = count == 0 ? 0 : (index % count + count) % count;
            if (RaiseAndSetIfChanged(ref _selectedPage, page, nameof(SelectedPage)))
            {
                RefreshIndicators();
                _app.OnNativePageChanged(page);
            }
        }

        internal void SelectPage(MarketPageModel page)
        {
            int index = IndexOfPage(page);
            if (index >= 0)
                SelectPage(index);
        }

        public void Refresh()
        {
            Color muted = _app.ResolveMarketColor(Gui.Styling.ThemeResources.MutedTextColor, new Color(_app.NativeForeground, 0.68f));
            Color trendUp = _app.ResolveMarketColor(MarketThemeResources.PriceTrendUpColor, Color.LightGreen);
            Color trendDown = _app.ResolveMarketColor(MarketThemeResources.PriceTrendDownColor, Color.IndianRed);
            Color trendNeutral = _app.ResolveMarketColor(MarketThemeResources.PriceTrendNeutralColor, muted);
            _foreground = _app.ResolveMarketColor(Gui.Styling.ThemeResources.OnSurfaceColor, _app.NativeForeground);
            _accent = _app.ResolveMarketColor(Gui.Styling.ThemeResources.AccentColor, _foreground);
            _secondaryContainer = _app.ResolveMarketColor(Gui.Styling.ThemeResources.SecondaryContainerColor, Color.Gray);
            Color font = _app.ResolveMarketColor(Gui.Styling.ThemeResources.FontColor, _foreground);
            RaiseAndSetIfChanged(ref _arrowColor, new Color(font.R, font.G, font.B, (byte)(font.A * 180 / 255)), nameof(ArrowColor));

            NpcMarketMode mode = _app.Mode;
            bool both = mode == NpcMarketMode.Both;
            RaiseAndSetIfChanged(ref _isBoth, both, nameof(_isBoth));
            for (int i = 0; i < _headers.Length; i++)
                _headers[i].Update(_app.SortColumn, _app.SortDescending);

            _rows.Clear();
            IReadOnlyList<NpcMarketRow> source = _app.NativeRows;
            for (int i = 0; i < source.Count; i++)
            {
                NpcMarketRow row = source[i];
                MarketRowModel model;
                if (!_rowsByKey.TryGetValue(row.ItemKey, out model))
                    _rowsByKey[row.ItemKey] = model = new MarketRowModel(row.ItemKey, _app);
                model.Update(row, mode, trendUp, trendDown, trendNeutral);
                _rows.Add(model);
            }

            if (RaiseAndSetIfChanged(ref _searchQuery, _app.NativeSearchQuery ?? string.Empty, nameof(SearchQuery)))
                NotifySearchState();
            RaisePropertyChanged<bool>(nameof(HasSearch));
            UpdateContentState();
            RaisePropertyChanged<string>(nameof(UpdatedText));
            RaisePropertyChanged<string>(nameof(NextTickText));
            RaisePropertyChanged<string>(nameof(RefreshText));
            RaisePropertyChanged<bool>(nameof(RefreshEnabled));
            RaisePropertyChanged<bool>(nameof(ModeEnabled));
            UpdateLayout();
        }

        void NotifySearchState()
        {
            RaisePropertyChanged<bool>(nameof(IsSearchOpen));
            RaisePropertyChanged<string>(nameof(SearchText));
            for (int i = 0; i < _pages.Count; i++)
                _pages[i].NotifySearchState();
            UpdateLayout();
        }

        void UpdateContentState()
        {
            string text, icon = "Warning";
            Color color = _app.ResolveMarketColor(Gui.Styling.ThemeResources.WarningColor, Color.Yellow);
            bool table = false;
            switch (_app.ContentState)
            {
                case NpcMarketApp.MarketContentState.AccessDenied:
                    text = MyTexts.GetString("AccessDenied");
                    icon = "Lock";
                    color = _app.ResolveMarketColor(Gui.Styling.ThemeResources.ErrorColor, Color.Red);
                    break;
                case NpcMarketApp.MarketContentState.NoTrader:
                    text = _app.NativeNoTraderText;
                    break;
                case NpcMarketApp.MarketContentState.Empty:
                    text = _app.NativeEmptyText;
                    break;
                case NpcMarketApp.MarketContentState.Loading:
                    text = string.Empty;
                    break;
                default:
                    text = string.Empty;
                    table = true;
                    break;
            }

            // A search that matches nothing keeps the table, so its header and search box stay to change the query.
            RaiseAndSetIfChanged(ref _showTable, table && (_rows.Count > 0 || _searchQuery.Length > 0), nameof(ShowTable));
            RaiseAndSetIfChanged(ref _showMessage, text.Length > 0, nameof(ShowMessage));
            RaiseAndSetIfChanged(ref _messageText, text, nameof(MessageText));
            RaiseAndSetIfChanged(ref _messageColor, color, nameof(MessageColor));
            if (RaiseAndSetIfChanged(ref _messageIcon, icon, nameof(MessageIcon)))
            {
                MessageIconMask = new ImageBrush(icon) { Stretch = Stretch.Uniform };
                RaisePropertyChanged<BackgroundBrush>(nameof(MessageIconMask));
            }
        }

        void UpdateLayout()
        {
            float width = Math.Max(0f, _viewport.X);
            float height = Math.Max(0f, _viewport.Y - FOOTER_HEIGHT - (IsSearchOpen ? HEADER_HEIGHT : 0f));
            MessageIconSize = Math.Max(1f, Math.Min(width, height) * .4f);
            RaisePropertyChanged<float>(nameof(MessageIconSize));
            float minimum = _isBoth ? BOTH_LIST_MIN_WIDTH : SINGLE_SIDE_LIST_MIN_WIDTH;
            int visible = Math.Max(1, (int)Math.Floor((width + PAGE_GAP) / (minimum + PAGE_GAP)));
            float listWidth = Math.Max(minimum, (width - PAGE_GAP * (visible - 1)) / visible);

            int rowsPerPage = RowsPerPage(height);
            int pageCount = PageCount(rowsPerPage);
            bool navigation = pageCount > visible && height - NAVIGATION_HEIGHT > HEADER_HEIGHT + ROW_HEIGHT;
            if (navigation)
            {
                rowsPerPage = RowsPerPage(height - NAVIGATION_HEIGHT);
                pageCount = PageCount(rowsPerPage);
            }

            _rowsPerPage = rowsPerPage;
            RaiseAndSetIfChanged(ref _showNavigation, navigation, nameof(ShowNavigation));
            RaiseAndSetIfChanged(ref _viewportFraction, width > 0f ? Math.Min(1f, (listWidth + PAGE_GAP) / (width + PAGE_GAP)) : 1f,
                nameof(ViewportFraction));

            float arrow = ARROW_WIDTH, indicator = INDICATOR_WIDTH;
            if (arrow * 2f + indicator + NAVIGATION_GAP * 2f > width)
            {
                arrow = Math.Max(18f, Math.Min(arrow, width * .22f));
                indicator = Math.Max(1f, width - arrow * 2f - NAVIGATION_GAP * 2f);
            }
            RaiseAndSetIfChanged(ref _arrowButtonWidth, arrow, nameof(ArrowButtonWidth));
            RaiseAndSetIfChanged(ref _indicatorButtonWidth, indicator, nameof(IndicatorButtonWidth));
            RebuildPages();
        }

        int RowsPerPage(float height)
        {
            return Math.Max(1, (int)Math.Floor(Math.Max(0f, height - HEADER_HEIGHT) / ROW_HEIGHT));
        }

        int PageCount(int rowsPerPage)
        {
            if (_rows.Count == 0)
                return _searchQuery.Length > 0 ? 1 : 0;
            return (int)Math.Ceiling(_rows.Count / (float)rowsPerPage);
        }

        void RebuildPages()
        {
            int pageCount = PageCount(_rowsPerPage);
            while (_pagePool.Count < pageCount)
                _pagePool.Add(new MarketPageModel(this));

            bool changed = pageCount != _pages.Count;
            for (int i = 0; i < pageCount; i++)
                _pagePool[i].SetRows(_rows, i * _rowsPerPage, Math.Min(_rowsPerPage, _rows.Count - i * _rowsPerPage), _isBoth);

            if (changed)
            {
                var pages = new MarketPageModel[pageCount];
                for (int i = 0; i < pageCount; i++)
                    pages[i] = _pagePool[i];
                _pages = pages;
                RaisePropertyChanged<IReadOnlyList<MarketPageModel>>(nameof(Pages));
                if (_selectedPage >= pageCount)
                    SelectPage(_selectedPage);
            }
            RefreshIndicators();
        }

        void RefreshIndicators()
        {
            int count = _pages.Count;
            int visible = Math.Max(1, (int)Math.Floor(1f / Math.Max(0.01f, _viewportFraction) + 0.0001f));
            float diameter = INDICATOR_DOT_SIZE, gap = INDICATOR_DOT_GAP;
            float width = Math.Max(1f, _indicatorButtonWidth);
            if (count * diameter + Math.Max(0, count - 1) * gap > width)
            {
                float unit = width / Math.Max(1f, count + Math.Max(0, count - 1) * .75f);
                diameter = Math.Max(2f, unit);
                gap = Math.Max(1f, unit * .75f);
            }
            for (int i = 0; i < count; i++)
            {
                int offset = ((i - _selectedPage) % count + count) % count;
                _pages[i].SetIndicator(i == _selectedPage, offset < visible, _foreground, _accent, _secondaryContainer,
                    diameter, gap);
            }
        }

        int IndexOfPage(MarketPageModel page)
        {
            for (int i = 0; i < _pages.Count; i++)
                if (ReferenceEquals(_pages[i], page))
                    return i;
            return -1;
        }

        internal static NpcMarketStationQuote GetQuoteForMode(NpcMarketRow row, NpcMarketMode mode)
        {
            if (row == null)
                return null;
            switch (mode)
            {
                case NpcMarketMode.Buy:
                    return row.BestBuyQuote ?? (row.StoreItemType == VRage.Game.ObjectBuilders.Definitions.StoreItemTypes.Offer ? row.BestQuote : null);
                case NpcMarketMode.Sell:
                    return row.BestSellQuote ?? (row.StoreItemType == VRage.Game.ObjectBuilders.Definitions.StoreItemTypes.Order ? row.BestQuote : null);
                default:
                    return row.BestQuote;
            }
        }
    }

    [PreviewWith("TrendHeader.Label", "Trend")]
    [PreviewWith("TrendHeader.IsActive", false)]
    [PreviewWith("SellTrendHeader.Label", "Sell Trend")]
    [PreviewWith("SellTrendHeader.IsActive", false)]
    [PreviewWith("SellPriceHeader.Label", "Sell Price per Unit")]
    [PreviewWith("SellPriceHeader.IsActive", false)]
    [PreviewWith("PriceHeader.Label", "Price per Unit")]
    [PreviewWith("PriceHeader.IsActive", false)]
    [PreviewWith("NameHeader.Label", "Name")]
    [PreviewWith("NameHeader.IsActive", true)]
    [PreviewWith("BuyTrendHeader.Label", "Buy Trend")]
    [PreviewWith("BuyTrendHeader.IsActive", false)]
    [PreviewWith("BuyPriceHeader.Label", "Buy Price per Unit")]
    [PreviewWith("BuyPriceHeader.IsActive", false)]
    internal sealed class MarketPageModel : ObservableObject
    {
        readonly NpcMarketViewModel _owner;
        IReadOnlyList<MarketRowModel> _rows = new MarketRowModel[0];
        bool _isBoth;
        bool _isCurrent;
        string _dotTexture = "CircleHollow";
        Color _dotColor;
        Color _dotInnerColor;

        public MarketPageModel(NpcMarketViewModel owner)
        {
            _owner = owner;
            SelectCommand = new DelegateCommand(_ => _owner.SelectPage(this));
        }

        [PreviewWith(5)]
        public IReadOnlyList<MarketRowModel> Rows => _rows;
        [PreviewWith(true)]
        public bool IsBoth => _isBoth;
        public bool IsSingle => !_isBoth;
        public ICommand SearchCommand => _owner.SearchCommand;

        public bool IsSearchOpen
        {
            get { return _owner.IsSearchOpen; }
            set { _owner.IsSearchOpen = value; }
        }

        public string SearchText
        {
            get { return _owner.SearchText; }
            set { _owner.SearchText = value; }
        }

        [PreviewWith("Search")]
        public string SearchPlaceholder => _owner.SearchPlaceholder;

        internal void NotifySearchState()
        {
            RaisePropertyChanged<bool>(nameof(IsSearchOpen));
            RaisePropertyChanged<string>(nameof(SearchText));
        }
        public MarketSortHeaderModel NameHeader => _owner.Header(NpcMarketSortColumn.Name);
        public MarketSortHeaderModel PriceHeader => _owner.Header(NpcMarketSortColumn.Price);
        public MarketSortHeaderModel TrendHeader => _owner.Header(NpcMarketSortColumn.Trend);
        public MarketSortHeaderModel BuyPriceHeader => _owner.Header(NpcMarketSortColumn.BuyPrice);
        public MarketSortHeaderModel BuyTrendHeader => _owner.Header(NpcMarketSortColumn.BuyTrend);
        public MarketSortHeaderModel SellPriceHeader => _owner.Header(NpcMarketSortColumn.SellPrice);
        public MarketSortHeaderModel SellTrendHeader => _owner.Header(NpcMarketSortColumn.SellTrend);
        [PreviewWith(true)]
        public bool IsCurrent => _isCurrent;
        public BackgroundBrush DotMask { get; private set; } = new ImageBrush("CircleHollow");
        [PreviewWith("#E0DDE0")]
        public Color DotColor => _dotColor;
        [PreviewWith("#352937")]
        public Color DotInnerColor => _dotInnerColor;
        public ICommand SelectCommand { get; }

        internal void SetRows(List<MarketRowModel> rows, int start, int count, bool isBoth)
        {
            bool changed = count != _rows.Count;
            for (int i = 0; !changed && i < count; i++)
                changed = !ReferenceEquals(_rows[i], rows[start + i]);
            if (changed)
            {
                var page = new MarketRowModel[count];
                for (int i = 0; i < count; i++)
                    page[i] = rows[start + i];
                _rows = page;
                RaisePropertyChanged<IReadOnlyList<MarketRowModel>>(nameof(Rows));
            }
            if (RaiseAndSetIfChanged(ref _isBoth, isBoth, nameof(IsBoth)))
                RaisePropertyChanged<bool>(nameof(IsSingle));
        }

        float _dotSize = 8f;
        float _dotCellWidth = 16f;
        [PreviewWith(8f)]
        public float DotSize => _dotSize;
        [PreviewWith(6f)]
        public float InnerDotSize => _dotSize * .75f;
        [PreviewWith(16f)]
        public float DotCellWidth => _dotCellWidth;

        internal void SetIndicator(bool current, bool onScreen, Color foreground, Color accent, Color secondary,
            float dotSize, float dotGap)
        {
            if (RaiseAndSetIfChanged(ref _dotSize, dotSize, nameof(DotSize)))
                RaisePropertyChanged<float>(nameof(InnerDotSize));
            RaiseAndSetIfChanged(ref _dotCellWidth, dotSize + dotGap, nameof(DotCellWidth));
            RaiseAndSetIfChanged(ref _isCurrent, current, nameof(IsCurrent));
            if (RaiseAndSetIfChanged(ref _dotTexture, current || onScreen ? "Circle" : "CircleHollow", nameof(DotMask)))
            {
                DotMask = new ImageBrush(_dotTexture);
                RaisePropertyChanged<BackgroundBrush>(nameof(DotMask));
            }
            RaiseAndSetIfChanged(ref _dotColor, current ? foreground : onScreen ? accent : foreground, nameof(DotColor));
            RaiseAndSetIfChanged(ref _dotInnerColor, secondary, nameof(DotInnerColor));
        }
    }

    internal sealed class MarketSortHeaderModel : ObservableObject
    {
        static readonly Transform Descending = new RotateTransform(180f);
        readonly NpcMarketSortColumn _column;
        string _label = string.Empty;
        bool _isActive;
        Transform _indicatorTransform;

        public MarketSortHeaderModel(NpcMarketSortColumn column, INpcMarketHost app)
        {
            _column = column;
            Command = new DelegateCommand(_ => app.SortNative(_column));
        }

        public string Label => _label;
        public bool IsActive => _isActive;
        public Transform IndicatorTransform => _indicatorTransform;
        public ICommand Command { get; }

        internal void Update(NpcMarketSortColumn active, bool descending)
        {
            RaiseAndSetIfChanged(ref _label, GetLabel(_column), nameof(Label));
            RaiseAndSetIfChanged(ref _isActive, active == _column, nameof(IsActive));
            RaiseAndSetIfChanged(ref _indicatorTransform, descending ? Descending : null, nameof(IndicatorTransform));
        }

        static string GetLabel(NpcMarketSortColumn column)
        {
            switch (column)
            {
                case NpcMarketSortColumn.Name:
                    return MyTexts.GetString("StoreBlock_Column_Name");
                case NpcMarketSortColumn.Price:
                    return MyTexts.GetString("StoreBlock_Column_PricePerUnit");
                case NpcMarketSortColumn.Trend:
                    return MyTexts.GetString("StoreBlock_Column_Trend");
                case NpcMarketSortColumn.BuyPrice:
                    return MyTexts.GetString("StoreScreenBuyHeader") + " " + MyTexts.GetString("StoreBlock_Column_PricePerUnit");
                case NpcMarketSortColumn.SellPrice:
                    return MyTexts.GetString("StoreScreenSellHeader") + " " + MyTexts.GetString("StoreBlock_Column_PricePerUnit");
                case NpcMarketSortColumn.BuyTrend:
                    return MyTexts.GetString("StoreScreenBuyHeader") + " " + MyTexts.GetString("StoreBlock_Column_Trend");
                default:
                    return MyTexts.GetString("StoreScreenSellHeader") + " " + MyTexts.GetString("StoreBlock_Column_Trend");
            }
        }
    }

    internal sealed class MarketTrendModel : ObservableObject
    {
        static readonly Transform Flipped = new RotateTransform(180f);
        string _text = string.Empty;
        Color _color;
        BackgroundBrush _iconMask = new ImageBrush("Steady1") { Stretch = Stretch.Uniform };
        Transform _iconTransform;
        bool _hasValue;

        public string Text => _text;
        public Color Color => _color;
        public BackgroundBrush IconMask => _iconMask;
        public Transform IconTransform => _iconTransform;
        public bool HasValue => _hasValue;
        public bool IsPlaceholder => !_hasValue;

        internal void Update(NpcMarketStationQuote quote, float delta, bool hasValue, NpcMarketMode mode, Color up, Color down,
            Color neutral)
        {
            if (RaiseAndSetIfChanged(ref _hasValue, hasValue, nameof(HasValue)))
                RaisePropertyChanged<bool>(nameof(IsPlaceholder));
            bool steady = Math.Abs(delta) < 0.05f;
            bool favorable = mode == NpcMarketMode.Buy ? delta < 0f : delta > 0f;
            RaiseAndSetIfChanged(ref _text, steady ? "0%" : (delta > 0f ? "+" : string.Empty) + delta.ToString("0.#") + "%",
                nameof(Text));
            RaiseAndSetIfChanged(ref _color, steady ? neutral : favorable ? up : down, nameof(Color));
            string sprite = !hasValue ? "Steady1" : steady ? (mode == NpcMarketMode.Buy ? "Steady1" : "Steady2")
                : favorable ? "ArrowGreenDown" : "ArrowRedUp";
            var mask = new ImageBrush(sprite) { Stretch = Stretch.Uniform };
            if (!mask.Equals(_iconMask))
            {
                _iconMask = mask;
                RaisePropertyChanged<BackgroundBrush>(nameof(IconMask));
            }
            RaiseAndSetIfChanged(ref _iconTransform, hasValue && !steady && mode == NpcMarketMode.Sell ? Flipped : null,
                nameof(IconTransform));
        }
    }

    [PreviewWith("Trend.Text", "+4.2%", "-1.8%", "0%", "+2.5%", "-6%")]
    [PreviewWith("Trend.IsPlaceholder", false)]
    [PreviewWith("Trend.HasValue", true)]
    [PreviewWith("Trend.Color", "#81C784", "#FAA39B", "#C1BBC2", "#81C784", "#FAA39B")]
    [PreviewWith("SellTrend.Text", "-4.2%", "+1.8%", "0%", "-2.5%", "+6%")]
    [PreviewWith("SellTrend.IsPlaceholder", false, false, true, false, false)]
    [PreviewWith("SellTrend.HasValue", true, true, false, true, true)]
    [PreviewWith("SellTrend.Color", "#FAA39B", "#81C784", "#C1BBC2", "#FAA39B", "#81C784")]
    [PreviewWith("BuyTrend.Text", "+4.2%", "-1.8%", "0%", "0%", "-6%")]
    [PreviewWith("BuyTrend.IsPlaceholder", false, false, false, true, false)]
    [PreviewWith("BuyTrend.HasValue", true, true, true, false, true)]
    [PreviewWith("BuyTrend.Color", "#FAA39B", "#81C784", "#C1BBC2", "#C1BBC2", "#81C784")]
    internal sealed class MarketRowModel : ObservableObject
    {
        readonly string _key;
        string _icon = "MissingIcon";
        string _name = string.Empty;
        string _price = string.Empty;
        string _buyPrice = string.Empty;
        string _sellPrice = string.Empty;
        bool _hasBuy;
        bool _hasSell;
        bool _hasQuote;
        bool _isBoth;

        public MarketRowModel(string key, INpcMarketHost app)
        {
            _key = key;
            Command = new DelegateCommand(_ => app.OpenNativeRow(_key, app.Mode));
            BuyCommand = new DelegateCommand(_ => app.OpenNativeRow(_key, NpcMarketMode.Buy));
            SellCommand = new DelegateCommand(_ => app.OpenNativeRow(_key, NpcMarketMode.Sell));
        }

        [PreviewWith("MyObjectBuilder_Ingot/Platinum", "MyObjectBuilder_Ingot/Uranium", "MyObjectBuilder_Ingot/Gold", "MyObjectBuilder_Component/SteelPlate", "MyObjectBuilder_Ore/Ice")]
        public string Icon => _icon;
        [PreviewWith("Platinum Ingot", "Uranium Ingot", "Gold Ingot", "Steel Plate", "Ice")]
        public string Name => _name;
        [PreviewWith("3.12K SC", "2.48K SC", "1.65K SC", "92 SC", "14 SC")]
        public string Price => _price;
        [PreviewWith("3.05K SC", "2.51K SC", "1.65K SC", "", "15 SC")]
        public string BuyPrice => _buyPrice;
        [PreviewWith("2.88K SC", "2.3K SC", "", "88 SC", "12 SC")]
        public string SellPrice => _sellPrice;
        public bool HasQuote => _hasQuote;
        [PreviewWith(true)]
        public bool IsBoth => _isBoth;
        public bool IsSingle => !_isBoth;
        [PreviewWith(true, true, true, false, true)]
        public bool HasBuy => _hasBuy;
        [PreviewWith(true, true, false, true, true)]
        public bool HasSell => _hasSell;
        [PreviewWith(false, false, false, true, false)]
        public bool NoBuy => !_hasBuy;
        [PreviewWith(false, false, true, false, false)]
        public bool NoSell => !_hasSell;
        public MarketTrendModel Trend { get; } = new MarketTrendModel();
        public MarketTrendModel BuyTrend { get; } = new MarketTrendModel();
        public MarketTrendModel SellTrend { get; } = new MarketTrendModel();
        public ICommand Command { get; }
        public ICommand BuyCommand { get; }
        public ICommand SellCommand { get; }

        internal void Update(NpcMarketRow row, NpcMarketMode mode, Color up, Color down, Color neutral)
        {
            if (RaiseAndSetIfChanged(ref _isBoth, mode == NpcMarketMode.Both, nameof(IsBoth)))
                RaisePropertyChanged<bool>(nameof(IsSingle));
            RaiseAndSetIfChanged(ref _icon, string.IsNullOrEmpty(row.SpriteName) ? "MissingIcon" : row.SpriteName, nameof(Icon));
            string secondary = row.GetSecondaryLabel();
            RaiseAndSetIfChanged(ref _name, string.IsNullOrEmpty(secondary) ? row.DisplayName : row.DisplayName + " (" + secondary + ")",
                nameof(Name));
            RaiseAndSetIfChanged(ref _price, FormatingHelper.FormatSpaceCredits(row.PricePerUnit) + " SC", nameof(Price));
            RaiseAndSetIfChanged(ref _hasQuote, NpcMarketViewModel.GetQuoteForMode(row, mode) != null, nameof(HasQuote));
            Trend.Update(null, row.DeltaPercent, true, mode, up, down, neutral);

            var buy = row.BestBuyQuote;
            var sell = row.BestSellQuote;
            if (RaiseAndSetIfChanged(ref _hasBuy, buy != null, nameof(HasBuy)))
                RaisePropertyChanged<bool>(nameof(NoBuy));
            if (RaiseAndSetIfChanged(ref _hasSell, sell != null, nameof(HasSell)))
                RaisePropertyChanged<bool>(nameof(NoSell));
            RaiseAndSetIfChanged(ref _buyPrice, buy == null ? string.Empty
                : FormatingHelper.FormatSpaceCredits(buy.PersonalizedCurrentPricePerUnit) + " SC", nameof(BuyPrice));
            RaiseAndSetIfChanged(ref _sellPrice, sell == null ? string.Empty
                : FormatingHelper.FormatSpaceCredits(sell.PersonalizedCurrentPricePerUnit) + " SC", nameof(SellPrice));
            BuyTrend.Update(buy, buy == null ? 0f : buy.EffectiveViewerChangePercent, buy != null, NpcMarketMode.Buy, up, down, neutral);
            SellTrend.Update(sell, sell == null ? 0f : sell.EffectiveViewerChangePercent, sell != null, NpcMarketMode.Sell, up, down, neutral);
        }
    }
}
