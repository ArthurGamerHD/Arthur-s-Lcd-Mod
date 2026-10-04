using Adk.Gui.Core.Input;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.NpcMarket;
using LcdMod.Client.Market;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps
{
    // Test-only stand-in for the market app: the real NpcMarketView XAML and code-behind
    // compile against these members, without the game's market services.
    internal sealed class NpcMarketApp
    {
        internal static readonly NpcMarketMode[] MarketModes = { NpcMarketMode.Buy, NpcMarketMode.Sell, NpcMarketMode.Both };
        internal static string GetModeLabel(NpcMarketMode mode) => mode.ToString();
        internal NpcMarketMode Mode { get; private set; }
        internal void SetMode(NpcMarketMode mode) => Mode = mode;
    }
}

namespace LcdMod.Client.Apps.NpcMarket
{
    internal sealed class NpcMarketViewModel : ObservableObject
    {
        public NpcMarketViewModel()
        {
            RefreshMarketCommand = new DelegateCommand(_ => RefreshCount++);
            SearchCommand = new DelegateCommand(_ => { });
            ClearSearchCommand = new DelegateCommand(_ => { });
            PreviousPageCommand = new DelegateCommand(_ => { });
            NextPageCommand = new DelegateCommand(_ => { });
            Pages = new[] { new MarketPageModel(this) };
        }

        public int RefreshCount { get; private set; }
        public NpcMarketApp App { get; } = new NpcMarketApp();
        public float FontScale => 1f;
        bool _modeEnabled;
        public bool ModeEnabled => _modeEnabled;
        public void EnableMode()
        {
            _modeEnabled = true;
            RaisePropertyChanged<bool>(nameof(ModeEnabled));
        }
        public void SetViewport(Vector2 size) { }
        public ICommand SearchCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand RefreshMarketCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public bool HasSearch => false;
        public string SearchQuery => string.Empty;
        public bool ShowTable => true;
        public IReadOnlyList<MarketPageModel> Pages { get; }
        public int SelectedPage => 0;
        public float ViewportFraction => 1f;
        public bool ShowNavigation => false;
        public float ArrowButtonWidth => 42f;
        public float IndicatorButtonWidth => 136f;
        public Color ArrowColor => Color.White;
        public bool ShowMessage => false;
        public Color MessageColor => Color.White;
        public BackgroundBrush MessageIconMask => new ImageBrush("Warning");
        public float MessageIconSize => 64f;
        public string MessageText => string.Empty;
        public string UpdatedText => "Updated";
        public string NextTickText => "Next";
        public string RefreshText => "Refresh";
        public bool RefreshEnabled => true;
    }

    internal sealed class MarketPageModel : ObservableObject
    {
        readonly NpcMarketViewModel _owner;
        public MarketPageModel(NpcMarketViewModel owner) { _owner = owner; }
        public IReadOnlyList<MarketRowModel> Rows { get; } = new[] { new MarketRowModel() };
        public bool IsBoth => false;
        public bool IsSingle => true;
        public ICommand SearchCommand => _owner.SearchCommand;
        bool _isSearchOpen;
        string _searchText = string.Empty;
        public bool IsSearchOpen { get => _isSearchOpen; set => RaiseAndSetIfChanged(ref _isSearchOpen, value, nameof(IsSearchOpen)); }
        public string SearchText { get => _searchText; set => RaiseAndSetIfChanged(ref _searchText, value ?? string.Empty, nameof(SearchText)); }
        public string SearchPlaceholder => "Search";
        public MarketSortHeaderModel NameHeader { get; } = new MarketSortHeaderModel("Name");
        public MarketSortHeaderModel PriceHeader { get; } = new MarketSortHeaderModel("Price");
        public MarketSortHeaderModel TrendHeader { get; } = new MarketSortHeaderModel("Trend");
        public MarketSortHeaderModel BuyPriceHeader { get; } = new MarketSortHeaderModel("Buy");
        public MarketSortHeaderModel BuyTrendHeader { get; } = new MarketSortHeaderModel("Buy trend");
        public MarketSortHeaderModel SellPriceHeader { get; } = new MarketSortHeaderModel("Sell");
        public MarketSortHeaderModel SellTrendHeader { get; } = new MarketSortHeaderModel("Sell trend");
        public bool IsCurrent => true;
        public BackgroundBrush DotMask => new ImageBrush("Circle");
        public Color DotColor => Color.White;
        public Color DotInnerColor => Color.Gray;
        public float DotSize => 8f;
        public float InnerDotSize => 6f;
        public float DotCellWidth => 16f;
        public ICommand SelectCommand { get; } = new DelegateCommand(_ => { });
    }

    internal sealed class MarketSortHeaderModel : ObservableObject
    {
        public MarketSortHeaderModel(string label) { Label = label; }
        public string Label { get; }
        public bool IsActive => false;
        public Transform IndicatorTransform => null;
        public ICommand Command { get; } = new DelegateCommand(_ => { });
    }

    internal sealed class MarketTrendModel : ObservableObject
    {
        public string Text => "+1%";
        public Color Color => Color.Green;
        public BackgroundBrush IconMask => new ImageBrush("ArrowGreenDown");
        public Transform IconTransform => null;
        public bool HasValue => true;
        public bool IsPlaceholder => false;
    }

    internal sealed class MarketRowModel : ObservableObject
    {
        public string Icon => "MissingIcon";
        public string Name => "Iron Ingot";
        public string Price => "10 SC";
        public string BuyPrice => "10 SC";
        public string SellPrice => "8 SC";
        public bool HasQuote => true;
        public bool HasBuy => true;
        public bool HasSell => true;
        public bool NoBuy => false;
        public bool NoSell => false;
        public bool IsBoth => false;
        public bool IsSingle => true;
        public MarketTrendModel Trend { get; } = new MarketTrendModel();
        public MarketTrendModel BuyTrend { get; } = new MarketTrendModel();
        public MarketTrendModel SellTrend { get; } = new MarketTrendModel();
        public ICommand Command { get; } = new DelegateCommand(_ => { });
        public ICommand BuyCommand { get; } = new DelegateCommand(_ => { });
        public ICommand SellCommand { get; } = new DelegateCommand(_ => { });
    }
}
