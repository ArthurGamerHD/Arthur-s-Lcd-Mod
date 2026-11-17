using System;
using System.Collections.Generic;
using System.Globalization;
using Acrux.Core.Input;
using Acrux.Styling;
using LcdMod.Client.Apps.Dialogs;
using LcdMod.Client.Market;
using Adk.Mvvm;

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

    /// <summary>Delivery ordering for the dialog's Buy offers; null when no delivery provider is installed.</summary>
    internal sealed class MarketDelivery
    {
        public Func<MarketQuote, bool> CanDeliver;
        // done(destinations or null, error or null)
        public Action<Action<IList<MarketDestination>, string>> LoadDestinations;
        // (padId, done(destinations or null, error or null))
        public Action<long, Action<IList<MarketDestination>, string>> SelectDestination;
        // (quote, quantity, padId, done(result))
        public Action<MarketQuote, int, long, Action<MarketDeliveryQuote>> RequestQuote;
        // (quoteId, done(error or null))
        public Action<string, Action<string>> Submit;
        // done(cart lines or null, error or null)
        public Action<Action<IList<MarketCartLine>, string>> LoadCart;
        // (quote, quantity, done(cart lines or null, error or null))
        public Action<MarketQuote, int, Action<IList<MarketCartLine>, string>> AddToCart;
        // (line, quantity, done(cart lines or null, error or null)); quantity 0 removes the line
        public Action<MarketCartLine, int, Action<IList<MarketCartLine>, string>> SetCartQuantity;
        // (stationId, done(cart lines or null, error or null)); stationId 0 empties the cart
        public Action<long, Action<IList<MarketCartLine>, string>> ClearCart;
        // (stationId, padId, done(result)): prices one store's branch of the cart as one order
        public Action<long, long, Action<MarketDeliveryQuote>> QuoteCart;
        public Action OpenCart;
        public Func<long, string> FormatCredits;
        public Func<int, string> FormatDuration;
        // Seconds on the game clock; quotes expire on the host after 90 of them.
        public Func<double> Now;
        public Func<bool> IsGpsModifierPressed;
    }

    /// <summary>A priced delivery the host holds for a short time; Error is set instead when it could not be priced.</summary>
    internal sealed class MarketDeliveryQuote
    {
        public string Id;
        public string Error;
        public long Subtotal;
        public long Fee;
        public long Total;
        public int EtaSeconds;
    }

    /// <summary>A delivery destination (landing Pad); the label is what the destination picker shows.</summary>
    internal sealed class MarketDestination
    {
        public long PadId;
        public string Label;
        public bool IsSelected;

        public override string ToString() => Label ?? string.Empty;
    }

    /// <summary>
    /// Stations quoting one market item, sortable by column; clicking a station marks it with a GPS, or, with a
    /// delivery provider in Buy mode, selects it for a delivery order (GPS with the modifier key).
    /// </summary>
    [PreviewWith("Title", "Platinum Ingot")]
    [PreviewWith("SmallFontSize", 0.5525f)]
    [PreviewWith("IsRegular", true)]
    [PreviewWith("IsCompact", false)]
    [PreviewWith("FontSize", 0.65f)]
    [PreviewWith("ShowDelivery", true)]
    [PreviewWith("DeliveryQuantity", "100")]
    [PreviewWith("PriceText", "287,500 SC")]
    [PreviewWith("DeliveryText", "Delivery 25,250 SC, 00:02:10")]
    [PreviewWith("CanOrder", true)]
    [PreviewWith("HasError", true)]
    [PreviewWith("ErrorText", "This Pad has an active order.")]
    [PreviewWith("LargeFontSize", 0.8125f)]
    [PreviewWith("CanAddToCart", true)]
    [PreviewWith("ShowCart", true)]
    [PreviewWith("CartLabel", "Cart: 350")]
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
        readonly MarketDelivery _delivery;
        string _selectedKey;
        string _deliveryQuantity = "1";
        int _destinationGeneration;
        IList<MarketDestination> _destinations = new MarketDestination[0];
        MarketDestination _selectedDestination;
        string _destinationError;
        MarketDeliveryQuote _deliveryQuote;
        double _quotedAt;
        bool _quoting;
        bool _sending;
        int _quoteGeneration;
        string _error;
        string _info;
        const double CartRefreshSeconds = 5;
        int _cartCount;
        double _cartLoadedAt = double.MinValue;
        int _cartGeneration;
        NpcMarketMode _mode;
        MarketQuoteColumn _sortColumn = MarketQuoteColumn.Price;
        bool _descending;

        public MarketItemDialogViewModel(string itemName, string icon, NpcMarketMode mode, string[] headerLabels,
            string emptyText, Func<NpcMarketMode, IList<MarketQuote>> load, Action<MarketQuote> markStation,
            Action<NpcMarketMode> modeChanged, MarketDelivery delivery = null)
        {
            _delivery = delivery;
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
            AddToCartCommand = new DelegateCommand(_ => AddToCart());
            OpenCartCommand = new DelegateCommand(_ => { if (_delivery != null && _delivery.OpenCart != null) _delivery.OpenCart(); });
            BuyCommand = new DelegateCommand(_ => Buy());
            if (_delivery != null)
                ReloadDestinations();
            Refresh();
        }

        [PreviewWith("MyObjectBuilder_Ingot/Platinum")]
        public string Icon { get; }
        [PreviewWith("No results")]
        public string EmptyText { get; }
        public Func<NpcMarketMode, string> ModeLabel { get; set; } = mode => mode.ToString();
        [PreviewWith("Buy")]
        public NpcMarketMode Mode => _mode;
        [PreviewWith(3)]
        public IReadOnlyList<MarketQuoteRow> Rows => _rows;
        [PreviewWith("Distance")]
        public string DistanceLabel => _headerLabels[0];
        [PreviewWith("Name")]
        public string StationLabel => _headerLabels[1];
        [PreviewWith("Price per unit")]
        public string PriceLabel => _headerLabels[2];
        [PreviewWith("Trend")]
        public string TrendLabel => _headerLabels[3];
        public ICommand SortDistanceCommand { get; }
        public ICommand SortStationCommand { get; }
        public ICommand SortPriceCommand { get; }
        public ICommand SortTrendCommand { get; }
        public bool IsDistanceSorted => _sortColumn == MarketQuoteColumn.Distance;
        public bool IsStationSorted => _sortColumn == MarketQuoteColumn.Station;
        [PreviewWith(true)]
        public bool IsPriceSorted => _sortColumn == MarketQuoteColumn.Price;
        public bool IsTrendSorted => _sortColumn == MarketQuoteColumn.Trend;
        public Transform SortDirection => _descending ? Flipped : Upright;
        public bool IsEmpty => _rows.Count == 0;

        public bool ShowDelivery => _delivery != null && _mode == NpcMarketMode.Buy;
        public ICommand AddToCartCommand { get; }
        public ICommand BuyCommand { get; }
        public bool CanOrder => CanAddToCart && _selectedDestination != null;
        public bool CanAddToCart => !_sending && SelectedQuote() != null && ParseQuantity() > 0;
        public bool ShowCart => _delivery != null;
        public string CartLabel => string.Format(CultureInfo.CurrentCulture, Loc("LcdMod_Market_Cart_Button"), _cartCount);
        public ICommand OpenCartCommand { get; }

        /// <summary>The cart dialog reports the new item count after each change.</summary>
        public void SetCartCount(int count)
        {
            _cartLoadedAt = _delivery == null ? 0 : _delivery.Now();
            _cartGeneration++;
            if (_cartCount == count)
                return;
            _cartCount = count;
            RaisePropertyChanged<string>(nameof(CartLabel));
        }
        public float LargeFontSize => FontSize * 1.25f;
        public IList<MarketDestination> Destinations => _destinations;
        public string ErrorText => _error ?? _destinationError ?? string.Empty;
        public bool HasError => ErrorText.Length > 0;
        public string InfoText => HasError ? string.Empty : _info ?? string.Empty;
        public bool HasInfo => InfoText.Length > 0;

        public string PriceText
        {
            get
            {
                var quote = SelectedQuote();
                if (quote == null)
                    return Loc("LcdMod_Market_Delivery_PickStation");
                if (_deliveryQuote != null)
                    return Credits(_deliveryQuote.Subtotal);
                int quantity = ParseQuantity();
                return quantity > 0 ? Credits((long)quote.Price * quantity) : "-";
            }
        }

        public string DeliveryText
        {
            get
            {
                if (_selectedDestination == null)
                    return Loc("LcdMod_Market_Delivery_ChooseDestination");
                if (_quoting)
                    return "\u2026";
                if (_deliveryQuote == null)
                    return "-";
                string eta = _delivery.FormatDuration == null ? _deliveryQuote.EtaSeconds + " s" : _delivery.FormatDuration(_deliveryQuote.EtaSeconds);
                return string.Format(CultureInfo.CurrentCulture, Loc("LcdMod_Market_Delivery_Summary"), FormatNumber(_deliveryQuote.Fee), eta);
            }
        }

        // Picking a Pad makes it the Shop's destination on the host, like Dropline's own Pads page.
        public int SelectedDestinationIndex
        {
            get { return _selectedDestination == null ? -1 : _destinations.IndexOf(_selectedDestination); }
            set
            {
                if (_delivery == null || value < 0 || value >= _destinations.Count)
                    return;
                var destination = _destinations[value];
                if (_selectedDestination != null && _selectedDestination.PadId == destination.PadId)
                    return;
                _selectedDestination = destination;
                RaisePropertyChanged<int>(nameof(SelectedDestinationIndex));
                _error = null;
                var generation = ++_destinationGeneration;
                _delivery.SelectDestination(destination.PadId, (destinations, error) =>
                {
                    if (generation != _destinationGeneration)
                        return;
                    if (error == null)
                    {
                        ApplyDestinations(generation, destinations, null);
                        return;
                    }
                    _error = error;
                    ReloadDestinations();
                });
                Requote();
            }
        }

        public string DeliveryQuantity
        {
            get { return _deliveryQuantity; }
            set
            {
                if (!RaiseAndSetIfChanged(ref _deliveryQuantity, value ?? string.Empty, nameof(DeliveryQuantity)))
                    return;
                Requote();
            }
        }

        public void SelectMode(NpcMarketMode mode)
        {
            mode = mode == NpcMarketMode.Sell ? NpcMarketMode.Sell : NpcMarketMode.Buy;
            if (!RaiseAndSetIfChanged(ref _mode, mode, nameof(Mode)))
                return;
            if (_sortColumn == MarketQuoteColumn.Price)
                _descending = mode == NpcMarketMode.Sell;
            if (_modeChanged != null)
                _modeChanged(mode);
            RaisePropertyChanged<bool>(nameof(ShowDelivery));
            Refresh();
        }

        /// <summary>The pointer reached Buy now: a quote that expired (or was never made) is renewed before the click.</summary>
        public void PrepareBuy()
        {
            if (CanOrder && !QuoteValid && !_quoting)
                Requote();
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
            // The cart may also change elsewhere (another screen, a purchase settling), so the count is polled.
            if (_delivery != null && _delivery.LoadCart != null && _delivery.Now() - _cartLoadedAt >= CartRefreshSeconds)
                ReloadCart();
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
            if (_selectedKey != null && !_rowsByKey.ContainsKey(_selectedKey))
                SelectForDelivery(null);

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
            RaisePropertyChanged<float>(nameof(LargeFontSize));
            foreach (var row in _rowsByKey.Values)
                row.RaisePropertyChanged<float>(nameof(MarketQuoteRow.FontSize));
        }

        internal void Activate(MarketQuote quote)
        {
            if (ShowDelivery && (_delivery.IsGpsModifierPressed == null || !_delivery.IsGpsModifierPressed()))
                SelectForDelivery(quote);
            else if (_markStation != null)
                _markStation(quote);
        }

        internal bool IsSelected(MarketQuote quote) => quote != null && _selectedKey == quote.Key;

        void SelectForDelivery(MarketQuote quote)
        {
            if (quote != null && _delivery.CanDeliver != null && !_delivery.CanDeliver(quote))
            {
                _error = Loc("LcdMod_Market_Delivery_NotDeliverable");
                RaiseDeliveryState();
                return;
            }
            var key = quote == null ? null : quote.Key;
            if (_selectedKey == key)
                return;
            var previous = _selectedKey;
            _selectedKey = key;
            MarketQuoteRow row;
            if (previous != null && _rowsByKey.TryGetValue(previous, out row))
                row.RaisePropertyChanged<bool>(nameof(MarketQuoteRow.IsSelected));
            if (key != null && _rowsByKey.TryGetValue(key, out row))
                row.RaisePropertyChanged<bool>(nameof(MarketQuoteRow.IsSelected));
            Requote();
        }

        // The host keeps a quote 90 s; renewing a little earlier leaves time for the click to arrive.
        bool QuoteValid => _deliveryQuote != null && _delivery.Now() - _quotedAt < 80;

        // Every edit replaces the quote; the generation drops replies that arrive for an older edit.
        void Requote()
        {
            if (_delivery == null)
                return;
            var generation = ++_quoteGeneration;
            _deliveryQuote = null;
            _quoting = false;
            _error = null;
            _info = null;
            var quote = SelectedQuote();
            int quantity = ParseQuantity();
            if (quote != null && quantity > 0 && _selectedDestination != null)
            {
                _quoting = true;
                _delivery.RequestQuote(quote, quantity, _selectedDestination.PadId, result =>
                {
                    if (generation != _quoteGeneration)
                        return;
                    _quoting = false;
                    if (result.Error != null)
                        _error = result.Error;
                    else
                    {
                        _deliveryQuote = result;
                        _quotedAt = _delivery.Now();
                    }
                    RaiseDeliveryState();
                });
            }
            RaiseDeliveryState();
        }

        void Buy()
        {
            if (!CanOrder)
                return;
            if (!QuoteValid)
            {
                if (!_quoting)
                    Requote();
                return;
            }
            string quoteId = _deliveryQuote.Id;
            var generation = ++_quoteGeneration;
            _deliveryQuote = null;
            _sending = true;
            _error = null;
            _info = null;
            RaiseDeliveryState();
            _delivery.Submit(quoteId, error => Sent(generation, error, "LcdMod_Market_Delivery_Ordered"));
        }

        void AddToCart()
        {
            if (!CanAddToCart)
                return;
            var generation = _quoteGeneration;
            _sending = true;
            _error = null;
            _info = null;
            RaiseDeliveryState();
            _delivery.AddToCart(SelectedQuote(), ParseQuantity(), (lines, error) =>
            {
                _sending = false;
                if (generation == _quoteGeneration)
                {
                    _error = error;
                    _info = error == null ? Loc("LcdMod_Market_Delivery_AddedToCart") : null;
                }
                if (lines != null)
                    SetCartCount(Count(lines));
                RaiseDeliveryState();
            });
        }

        void ReloadCart()
        {
            _cartLoadedAt = _delivery.Now();
            var generation = ++_cartGeneration;
            _delivery.LoadCart((lines, error) =>
            {
                if (generation == _cartGeneration && lines != null)
                    SetCartCount(Count(lines));
            });
        }

        static int Count(IList<MarketCartLine> lines)
        {
            int count = 0;
            foreach (var line in lines)
                count += line.Quantity;
            return count;
        }

        void Sent(int generation, string error, string successKey)
        {
            _sending = false;
            if (generation == _quoteGeneration)
            {
                _error = error;
                _info = error == null ? Loc(successKey) : null;
            }
            RaiseDeliveryState();
            ReloadDestinations();
        }

        void ReloadDestinations()
        {
            var generation = ++_destinationGeneration;
            _delivery.LoadDestinations((destinations, error) => ApplyDestinations(generation, destinations, error));
        }

        void ApplyDestinations(int generation, IList<MarketDestination> destinations, string error)
        {
            if (generation != _destinationGeneration)
                return;
            _destinations = error == null && destinations != null ? destinations : new MarketDestination[0];
            _destinationError = error ?? (_destinations.Count == 0 ? Loc("LcdMod_Market_Delivery_NoPads") : null);
            long previous = _selectedDestination == null ? 0 : _selectedDestination.PadId;
            MarketDestination selected = null;
            foreach (var destination in _destinations)
                if (destination.IsSelected)
                    selected = destination;
            _selectedDestination = selected;
            RaisePropertyChanged<IList<MarketDestination>>(nameof(Destinations));
            RaisePropertyChanged<int>(nameof(SelectedDestinationIndex));
            if ((selected == null ? 0 : selected.PadId) != previous)
                Requote();
            else
                RaiseDeliveryState();
        }

        void RaiseDeliveryState()
        {
            RaisePropertyChanged<string>(nameof(PriceText));
            RaisePropertyChanged<string>(nameof(DeliveryText));
            RaisePropertyChanged<string>(nameof(ErrorText));
            RaisePropertyChanged<bool>(nameof(HasError));
            RaisePropertyChanged<string>(nameof(InfoText));
            RaisePropertyChanged<bool>(nameof(HasInfo));
            RaisePropertyChanged<bool>(nameof(CanOrder));
            RaisePropertyChanged<bool>(nameof(CanAddToCart));
        }

        string Credits(long value) => FormatNumber(value) + " SC";

        string FormatNumber(long value) =>
            _delivery != null && _delivery.FormatCredits != null ? _delivery.FormatCredits(value) : value.ToString(CultureInfo.CurrentCulture);

        MarketQuote SelectedQuote()
        {
            MarketQuoteRow row;
            return _selectedKey != null && _rowsByKey.TryGetValue(_selectedKey, out row) ? row.Quote : null;
        }

        int ParseQuantity()
        {
            int quantity;
            return int.TryParse(_deliveryQuantity, NumberStyles.None, CultureInfo.InvariantCulture, out quantity) ? quantity : 0;
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
            Command = new DelegateCommand(_ => _owner.Activate(_quote));
        }

        public ICommand Command { get; }
        internal MarketQuote Quote => _quote;
        [PreviewWith(true, false, false)]
        public bool IsSelected => _owner.IsSelected(_quote);
        [PreviewWith("210 km", "48.0 km", "12.4 km")]
        public string Distance => _distance;
        [PreviewWith("Trade Hub Sigma", "Mining Station Kappa", "Outpost Theta")]
        public string Station => _station;
        [PreviewWith("2,875 SC", "2,980 SC", "3,120 SC")]
        public string Price => _price;
        [PreviewWith("0%")]
        public string Trend => _trend;
        [PreviewWith("Steady1")]
        public string TrendIcon => _trendIcon;
        public Transform TrendRotation => _trendRotation;
        [PreviewWith(0.5525f)]
        public float FontSize => _owner.SmallFontSize;

        internal void Update(MarketQuote quote, NpcMarketMode mode)
        {
            _quote = quote;
            RaisePropertyChanged<bool>(nameof(IsSelected));
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
