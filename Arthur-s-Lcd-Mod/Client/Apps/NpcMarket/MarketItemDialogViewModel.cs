using System;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Dialogs;
using LcdMod.Client.Market;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.NpcMarket
{
    internal enum MarketQuoteColumn
    {
        Distance,
        Station,
        Price,
        Trend
    }

    /// <summary>One station's quote, already formatted by the market app.</summary>
    internal sealed class MarketQuote
    {
        public object Source;
        public string Key;
        public double Distance;
        public string DistanceText;
        public string Station;
        public int Price;
        public string PriceText;
        public float Trend;
    }

    /// <summary>Stations quoting one market item, sortable by column; clicking a station marks it with a GPS.</summary>
    internal sealed class MarketItemDialogViewModel : DialogViewModel
    {
        static readonly RotateTransform Flipped = new RotateTransform(180f);
        static readonly RotateTransform Upright = new RotateTransform(0f);

        readonly Func<NpcMarketMode, IList<MarketQuote>> _load;
        readonly Action<MarketQuote> _markStation;
        readonly Action<NpcMarketMode> _modeChanged;
        readonly List<MarketQuoteRow> _rows = new List<MarketQuoteRow>();
        readonly Dictionary<string, MarketQuoteRow> _rowsByKey = new Dictionary<string, MarketQuoteRow>(StringComparer.Ordinal);
        readonly List<MarketQuote> _quotes = new List<MarketQuote>();
        readonly string[] _headerLabels;
        NpcMarketMode _mode;
        MarketQuoteColumn _sortColumn = MarketQuoteColumn.Price;
        bool _descending;

        public MarketItemDialogViewModel(string itemName, string icon, NpcMarketMode mode, string[] headerLabels,
            string emptyText, Func<NpcMarketMode, IList<MarketQuote>> load, Action<MarketQuote> markStation,
            Action<NpcMarketMode> modeChanged)
        {
            Title = itemName;
            Icon = string.IsNullOrEmpty(icon) ? "MissingIcon" : icon;
            EmptyText = emptyText;
            _load = load;
            _markStation = markStation;
            _modeChanged = modeChanged;
            _mode = mode == NpcMarketMode.Sell ? NpcMarketMode.Sell : NpcMarketMode.Buy;
            _descending = _mode == NpcMarketMode.Sell;
            _headerLabels = headerLabels;
            SortDistanceCommand = new DelegateCommand(_ => Sort(MarketQuoteColumn.Distance));
            SortStationCommand = new DelegateCommand(_ => Sort(MarketQuoteColumn.Station));
            SortPriceCommand = new DelegateCommand(_ => Sort(MarketQuoteColumn.Price));
            SortTrendCommand = new DelegateCommand(_ => Sort(MarketQuoteColumn.Trend));
            Refresh();
        }

        public string Icon { get; }
        public string EmptyText { get; }
        public Func<NpcMarketMode, string> ModeLabel { get; set; } = mode => mode.ToString();
        public NpcMarketMode Mode => _mode;
        public IReadOnlyList<MarketQuoteRow> Rows => _rows;
        public string DistanceLabel => _headerLabels[0];
        public string StationLabel => _headerLabels[1];
        public string PriceLabel => _headerLabels[2];
        public string TrendLabel => _headerLabels[3];
        public ICommand SortDistanceCommand { get; }
        public ICommand SortStationCommand { get; }
        public ICommand SortPriceCommand { get; }
        public ICommand SortTrendCommand { get; }
        public bool IsDistanceSorted => _sortColumn == MarketQuoteColumn.Distance;
        public bool IsStationSorted => _sortColumn == MarketQuoteColumn.Station;
        public bool IsPriceSorted => _sortColumn == MarketQuoteColumn.Price;
        public bool IsTrendSorted => _sortColumn == MarketQuoteColumn.Trend;
        public Transform SortDirection => _descending ? Flipped : Upright;
        public bool IsEmpty => _rows.Count == 0;

        public void SelectMode(NpcMarketMode mode)
        {
            mode = mode == NpcMarketMode.Sell ? NpcMarketMode.Sell : NpcMarketMode.Buy;
            if (!RaiseAndSetIfChanged(ref _mode, mode, nameof(Mode)))
                return;
            if (_sortColumn == MarketQuoteColumn.Price)
                _descending = mode == NpcMarketMode.Sell;
            if (_modeChanged != null)
                _modeChanged(mode);
            Refresh();
        }

        internal void Sort(MarketQuoteColumn column)
        {
            if (_sortColumn == column)
                _descending = !_descending;
            else
            {
                _sortColumn = column;
                _descending = column == MarketQuoteColumn.Price && _mode == NpcMarketMode.Sell;
            }
            Refresh();
        }

        public void Refresh()
        {
            _quotes.Clear();
            var loaded = _load(_mode);
            if (loaded != null)
                _quotes.AddRange(loaded);
            _quotes.Sort(Compare);

            bool changed = _quotes.Count != _rows.Count;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _quotes.Count; i++)
            {
                var quote = _quotes[i];
                seen.Add(quote.Key);
                MarketQuoteRow row;
                if (!_rowsByKey.TryGetValue(quote.Key, out row))
                    _rowsByKey[quote.Key] = row = new MarketQuoteRow(this);
                row.Update(quote, _mode);
                if (i >= _rows.Count)
                    _rows.Add(row);
                else if (!ReferenceEquals(_rows[i], row))
                {
                    _rows[i] = row;
                    changed = true;
                }
            }
            if (_rows.Count > _quotes.Count)
                _rows.RemoveRange(_quotes.Count, _rows.Count - _quotes.Count);
            var stale = new List<string>();
            foreach (var key in _rowsByKey.Keys)
                if (!seen.Contains(key))
                    stale.Add(key);
            foreach (var key in stale)
                _rowsByKey.Remove(key);

            RaisePropertyChanged<bool>(nameof(IsDistanceSorted));
            RaisePropertyChanged<bool>(nameof(IsStationSorted));
            RaisePropertyChanged<bool>(nameof(IsPriceSorted));
            RaisePropertyChanged<bool>(nameof(IsTrendSorted));
            RaisePropertyChanged<Transform>(nameof(SortDirection));
            if (changed)
            {
                RaisePropertyChanged<IReadOnlyList<MarketQuoteRow>>(nameof(Rows));
                RaisePropertyChanged<bool>(nameof(IsEmpty));
            }
        }

        protected override void OnFontSizeChanged()
        {
            foreach (var row in _rowsByKey.Values)
                row.RaisePropertyChanged<float>(nameof(MarketQuoteRow.FontSize));
        }

        internal void Mark(MarketQuote quote)
        {
            if (_markStation != null)
                _markStation(quote);
        }

        int Compare(MarketQuote a, MarketQuote b)
        {
            var left = _descending ? b : a;
            var right = _descending ? a : b;
            int result;
            switch (_sortColumn)
            {
                case MarketQuoteColumn.Distance:
                    result = left.Distance.CompareTo(right.Distance);
                    break;
                case MarketQuoteColumn.Station:
                    result = string.Compare(left.Station, right.Station, StringComparison.OrdinalIgnoreCase);
                    break;
                case MarketQuoteColumn.Trend:
                    result = left.Trend.CompareTo(right.Trend);
                    break;
                default:
                    result = left.Price.CompareTo(right.Price);
                    break;
            }
            if (result != 0)
                return result;
            result = a.Distance.CompareTo(b.Distance);
            return result != 0 ? result : string.CompareOrdinal(a.Key, b.Key);
        }

        internal static string FormatTrend(float trend)
        {
            if (Math.Abs(trend) < 0.05f)
                return "0%";
            return (trend > 0f ? "+" : string.Empty) + trend.ToString("0.#") + "%";
        }

        internal static string TrendIcon(float trend, NpcMarketMode mode, out Transform rotation)
        {
            if (Math.Abs(trend) < 0.05f)
            {
                rotation = Upright;
                return mode == NpcMarketMode.Buy ? "Steady1" : "Steady2";
            }
            rotation = mode == NpcMarketMode.Sell ? Flipped : Upright;
            bool favorable = mode == NpcMarketMode.Buy ? trend < 0f : trend > 0f;
            return favorable ? "ArrowGreenDown" : "ArrowRedUp";
        }
    }

    internal sealed class MarketQuoteRow : ObservableObject
    {
        readonly MarketItemDialogViewModel _owner;
        MarketQuote _quote;
        string _distance = string.Empty;
        string _station = string.Empty;
        string _price = string.Empty;
        string _trend = string.Empty;
        string _trendIcon = string.Empty;
        Transform _trendRotation;

        public MarketQuoteRow(MarketItemDialogViewModel owner)
        {
            _owner = owner;
            Command = new DelegateCommand(_ => _owner.Mark(_quote));
        }

        public ICommand Command { get; }
        public string Distance => _distance;
        public string Station => _station;
        public string Price => _price;
        public string Trend => _trend;
        public string TrendIcon => _trendIcon;
        public Transform TrendRotation => _trendRotation;
        public float FontSize => _owner.SmallFontSize;

        internal void Update(MarketQuote quote, NpcMarketMode mode)
        {
            _quote = quote;
            RaiseAndSetIfChanged(ref _distance, quote.DistanceText ?? string.Empty, nameof(Distance));
            RaiseAndSetIfChanged(ref _station, quote.Station ?? string.Empty, nameof(Station));
            RaiseAndSetIfChanged(ref _price, quote.PriceText ?? string.Empty, nameof(Price));
            RaiseAndSetIfChanged(ref _trend, MarketItemDialogViewModel.FormatTrend(quote.Trend), nameof(Trend));
            Transform rotation;
            RaiseAndSetIfChanged(ref _trendIcon, MarketItemDialogViewModel.TrendIcon(quote.Trend, mode, out rotation), nameof(TrendIcon));
            RaiseAndSetIfChanged(ref _trendRotation, rotation, nameof(TrendRotation));
        }
    }
}
