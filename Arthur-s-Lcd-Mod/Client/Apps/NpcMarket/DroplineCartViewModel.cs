using System;
using System.Collections.Generic;
using System.Globalization;
using Acrux.Core.Input;
using LcdMod.Client.Apps.Dialogs;
using Adk.Mvvm;

namespace LcdMod.Client.Apps.NpcMarket
{
    /// <summary>One line of the player's delivery cart, as the market app resolved it (icon, names).</summary>
    internal sealed class MarketCartLine
    {
        public long StationId;
        public string StationName;
        public string TypeId;
        public string SubtypeId;
        public string Icon;
        public string Name;
        public int Quantity;
        // -1 when the station no longer lists the item.
        public long UnitPrice;
    }

    /// <summary>
    /// The player's delivery cart as a tree of stores and their items. Dropline delivers one store per order, so the
    /// selected store (branch) is priced and bought; the other branches stay in the cart.
    /// </summary>
    [PreviewWith("Title", "Delivery cart")]
    [PreviewWith("FontSize", 0.65f)]
    [PreviewWith("SmallFontSize", 0.5525f)]
    [PreviewWith("LargeFontSize", 0.8125f)]
    [PreviewWith("TotalText", "312,750 SC")]
    [PreviewWith("DeliveryText", "Delivery 25,250 SC, 00:02:10")]
    [PreviewWith("SelectedText", "Trade Hub Sigma")]
    [PreviewWith("CanBuy", true)]
    [PreviewWith("CanRemoveBranch", true)]
    internal sealed class DroplineCartViewModel : DialogViewModel
    {
        const double ReloadSeconds = 2;
        const int MaximumReloads = 10;

        readonly MarketDelivery _delivery;
        readonly Action<int> _countChanged;
        readonly List<DroplineCartBranchModel> _branches = new List<DroplineCartBranchModel>();
        readonly Dictionary<long, DroplineCartBranchModel> _branchesByStation = new Dictionary<long, DroplineCartBranchModel>();
        IList<MarketDestination> _destinations = new MarketDestination[0];
        MarketDestination _selectedDestination;
        string _destinationError;
        int _destinationGeneration;
        long _selectedStation;
        MarketDeliveryQuote _quote;
        double _quotedAt;
        bool _loaded;
        bool _quoting;
        bool _sending;
        int _generation;
        string _error;
        string _info;
        long _boughtStation;
        double _reloadAt;
        int _reloads;

        public DroplineCartViewModel(MarketDelivery delivery, Action<int> countChanged)
        {
            _delivery = delivery;
            _countChanged = countChanged;
            Title = Loc("LcdMod_Market_Cart_Title");
            BuyCommand = new DelegateCommand(_ => Buy());
            RemoveBranchCommand = new DelegateCommand(_ => RemoveBranch());
            ReloadDestinations();
            Reload();
        }

        [PreviewWith(2)]
        public IReadOnlyList<DroplineCartBranchModel> Branches => _branches;
        public bool IsEmpty => _loaded && _branches.Count == 0;
        [PreviewWith("Your cart is empty.")]
        public string EmptyText => Loc("LcdMod_Market_Cart_Empty");
        public float LargeFontSize => FontSize * 1.25f;
        public ICommand BuyCommand { get; }
        public ICommand RemoveBranchCommand { get; }
        public IList<MarketDestination> Destinations => _destinations;
        public bool CanBuy => !_sending && Selected != null && _selectedDestination != null;
        public bool CanRemoveBranch => !_sending && Selected != null;
        public string ErrorText => _error ?? _destinationError ?? string.Empty;
        public bool HasError => ErrorText.Length > 0;
        public string InfoText => HasError ? string.Empty : _info ?? string.Empty;
        public bool HasInfo => InfoText.Length > 0;
        public string SelectedText => Selected == null ? Loc("LcdMod_Market_Cart_PickStore") : Selected.StationName;

        public string TotalText
        {
            get
            {
                if (Selected == null)
                    return "-";
                return _quote != null ? Credits(_quote.Total) : Selected.SubtotalText;
            }
        }

        public string DeliveryText
        {
            get
            {
                if (_selectedDestination == null)
                    return Loc("LcdMod_Market_Delivery_ChooseDestination");
                if (_quoting)
                    return "…";
                if (_quote == null)
                    return "-";
                string eta = _delivery.FormatDuration == null ? _quote.EtaSeconds + " s" : _delivery.FormatDuration(_quote.EtaSeconds);
                return string.Format(CultureInfo.CurrentCulture, Loc("LcdMod_Market_Delivery_Summary"), FormatNumber(_quote.Fee), eta);
            }
        }

        // Picking a Pad makes it the Shop's destination on the host, like Dropline's own Pads page.
        public int SelectedDestinationIndex
        {
            get { return _selectedDestination == null ? -1 : _destinations.IndexOf(_selectedDestination); }
            set
            {
                if (value < 0 || value >= _destinations.Count)
                    return;
                var destination = _destinations[value];
                if (_selectedDestination != null && _selectedDestination.PadId == destination.PadId)
                    return;
                _selectedDestination = destination;
                RaisePropertyChanged<int>(nameof(SelectedDestinationIndex));
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

        DroplineCartBranchModel Selected
        {
            get
            {
                DroplineCartBranchModel branch;
                return _branchesByStation.TryGetValue(_selectedStation, out branch) ? branch : null;
            }
        }

        /// <summary>The pointer reached Buy now: a quote that expired (or was never made) is renewed before the click.</summary>
        public void PrepareBuy()
        {
            if (CanBuy && !QuoteValid && !_quoting)
                Requote();
        }

        /// <summary>Called by the app every update: after a purchase, reloads until Dropline has charged the branch.</summary>
        public void Tick()
        {
            if (_boughtStation == 0 || _delivery.Now() < _reloadAt)
                return;
            if (++_reloads > MaximumReloads)
            {
                _boughtStation = 0;
                return;
            }
            _reloadAt = _delivery.Now() + ReloadSeconds;
            Reload();
        }

        protected override void OnFontSizeChanged()
        {
            RaisePropertyChanged<float>(nameof(LargeFontSize));
            foreach (var branch in _branches)
                branch.RaiseFontSize();
        }

        internal bool IsSelected(DroplineCartBranchModel branch) => branch != null && branch.StationId == _selectedStation;

        internal void Select(DroplineCartBranchModel branch)
        {
            if (branch == null || branch.StationId == _selectedStation)
                return;
            var previous = Selected;
            _selectedStation = branch.StationId;
            if (previous != null)
                previous.RaiseSelected();
            branch.RaiseSelected();
            Requote();
        }

        internal void SetQuantity(DroplineCartLineModel line, int quantity)
        {
            var generation = ++_generation;
            _quote = null;
            _error = null;
            _info = null;
            RaiseState();
            _delivery.SetCartQuantity(line.Line, quantity, (lines, error) => Apply(generation, lines, error));
        }

        void Reload()
        {
            var generation = ++_generation;
            _delivery.LoadCart((lines, error) => Apply(generation, lines, error));
        }

        void RemoveBranch()
        {
            if (!CanRemoveBranch)
                return;
            var generation = ++_generation;
            _quote = null;
            _error = null;
            _info = null;
            RaiseState();
            _delivery.ClearCart(_selectedStation, (lines, error) => Apply(generation, lines, error));
        }

        void Apply(int generation, IList<MarketCartLine> lines, string error)
        {
            if (generation != _generation)
                return;
            _loaded = true;
            if (error != null)
            {
                _error = error;
                RaiseState();
                return;
            }
            UpdateBranches(lines ?? new MarketCartLine[0]);
            int count = 0;
            foreach (var line in lines ?? new MarketCartLine[0])
                count += line.Quantity;
            if (_countChanged != null)
                _countChanged(count);
            if (_boughtStation != 0 && !_branchesByStation.ContainsKey(_boughtStation))
                _boughtStation = 0;
            Requote();
        }

        // Branches and lines keep their models (and a quantity being typed) across replies.
        void UpdateBranches(IList<MarketCartLine> lines)
        {
            var grouped = new Dictionary<long, List<MarketCartLine>>();
            var order = new List<long>();
            foreach (var line in lines)
            {
                List<MarketCartLine> group;
                if (!grouped.TryGetValue(line.StationId, out group))
                {
                    grouped[line.StationId] = group = new List<MarketCartLine>();
                    order.Add(line.StationId);
                }
                group.Add(line);
            }
            order.Sort((a, b) => string.Compare(grouped[a][0].StationName, grouped[b][0].StationName, StringComparison.OrdinalIgnoreCase));

            _branches.Clear();
            var stale = new HashSet<long>(_branchesByStation.Keys);
            foreach (long station in order)
            {
                DroplineCartBranchModel branch;
                if (!_branchesByStation.TryGetValue(station, out branch))
                    _branchesByStation[station] = branch = new DroplineCartBranchModel(this, station);
                branch.Update(grouped[station]);
                _branches.Add(branch);
                stale.Remove(station);
            }
            foreach (long station in stale)
                _branchesByStation.Remove(station);
            if (Selected == null)
                _selectedStation = _branches.Count == 0 ? 0 : _branches[0].StationId;
            foreach (var branch in _branches)
                branch.RaiseSelected();
            RaisePropertyChanged<IReadOnlyList<DroplineCartBranchModel>>(nameof(Branches));
            RaisePropertyChanged<bool>(nameof(IsEmpty));
        }

        // The host keeps a quote 90 s; renewing a little earlier leaves time for the click to arrive.
        bool QuoteValid => _quote != null && _delivery.Now() - _quotedAt < 80;

        void Requote()
        {
            var generation = ++_generation;
            _quote = null;
            _quoting = Selected != null && _selectedDestination != null;
            if (_quoting)
                _delivery.QuoteCart(_selectedStation, _selectedDestination.PadId, result =>
                {
                    if (generation != _generation)
                        return;
                    _quoting = false;
                    if (result.Error != null)
                        _error = result.Error;
                    else
                    {
                        _quote = result;
                        _quotedAt = _delivery.Now();
                    }
                    RaiseState();
                });
            RaiseState();
        }

        void Buy()
        {
            if (!CanBuy)
                return;
            if (!QuoteValid)
            {
                if (!_quoting)
                    Requote();
                return;
            }
            string quoteId = _quote.Id;
            long station = _selectedStation;
            var generation = ++_generation;
            _quote = null;
            _sending = true;
            _error = null;
            _info = null;
            RaiseState();
            _delivery.Submit(quoteId, error =>
            {
                _sending = false;
                if (generation == _generation)
                {
                    _error = error;
                    _info = error == null ? Loc("LcdMod_Market_Delivery_Ordered") : null;
                }
                if (error == null)
                {
                    _boughtStation = station;
                    _reloads = 0;
                    _reloadAt = _delivery.Now() + ReloadSeconds;
                }
                RaiseState();
                ReloadDestinations();
            });
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
                RaiseState();
        }

        void RaiseState()
        {
            RaisePropertyChanged<string>(nameof(SelectedText));
            RaisePropertyChanged<string>(nameof(TotalText));
            RaisePropertyChanged<string>(nameof(DeliveryText));
            RaisePropertyChanged<string>(nameof(ErrorText));
            RaisePropertyChanged<bool>(nameof(HasError));
            RaisePropertyChanged<string>(nameof(InfoText));
            RaisePropertyChanged<bool>(nameof(HasInfo));
            RaisePropertyChanged<bool>(nameof(CanBuy));
            RaisePropertyChanged<bool>(nameof(CanRemoveBranch));
        }

        internal string Credits(long value) => FormatNumber(value) + " SC";

        string FormatNumber(long value) =>
            _delivery.FormatCredits != null ? _delivery.FormatCredits(value) : value.ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>One store of the cart and its items; selecting it makes it the order to price and buy.</summary>
    internal sealed class DroplineCartBranchModel : ObservableObject
    {
        readonly DroplineCartViewModel _owner;
        readonly List<DroplineCartLineModel> _lines = new List<DroplineCartLineModel>();
        readonly Dictionary<string, DroplineCartLineModel> _linesByItem = new Dictionary<string, DroplineCartLineModel>(StringComparer.Ordinal);
        string _stationName = string.Empty;
        string _subtotal = "-";

        public DroplineCartBranchModel(DroplineCartViewModel owner, long stationId)
        {
            _owner = owner;
            StationId = stationId;
            SelectCommand = new DelegateCommand(_ => _owner.Select(this));
        }

        internal long StationId { get; }
        public ICommand SelectCommand { get; }
        [PreviewWith(true, false)]
        public bool IsSelected => _owner.IsSelected(this);
        [PreviewWith("Trade Hub Sigma", "Outpost Theta")]
        public string StationName => _stationName;
        [PreviewWith("287,500 SC", "45,000 SC")]
        public string SubtotalText => _subtotal;
        [PreviewWith(2)]
        public IReadOnlyList<DroplineCartLineModel> Lines => _lines;
        [PreviewWith(0.65f)]
        public float FontSize => _owner.FontSize;

        internal void Update(List<MarketCartLine> lines)
        {
            string name = lines[0].StationName;
            RaiseAndSetIfChanged(ref _stationName, string.IsNullOrEmpty(name) ? lines[0].StationId.ToString(CultureInfo.InvariantCulture) : name,
                nameof(StationName));
            _lines.Clear();
            var stale = new HashSet<string>(_linesByItem.Keys);
            long subtotal = 0;
            bool priced = true;
            foreach (var line in lines)
            {
                string key = line.TypeId + "/" + line.SubtypeId;
                DroplineCartLineModel model;
                if (!_linesByItem.TryGetValue(key, out model))
                    _linesByItem[key] = model = new DroplineCartLineModel(_owner);
                model.Update(line);
                _lines.Add(model);
                stale.Remove(key);
                if (line.UnitPrice < 0)
                    priced = false;
                else
                    subtotal += line.UnitPrice * line.Quantity;
            }
            foreach (var key in stale)
                _linesByItem.Remove(key);
            RaiseAndSetIfChanged(ref _subtotal, priced ? _owner.Credits(subtotal) : "-", nameof(SubtotalText));
            RaisePropertyChanged<IReadOnlyList<DroplineCartLineModel>>(nameof(Lines));
        }

        internal void RaiseSelected() => RaisePropertyChanged<bool>(nameof(IsSelected));

        internal void RaiseFontSize()
        {
            RaisePropertyChanged<float>(nameof(FontSize));
            foreach (var line in _lines)
                line.RaisePropertyChanged<float>(nameof(DroplineCartLineModel.FontSize));
        }
    }

    internal sealed class DroplineCartLineModel : ObservableObject
    {
        readonly DroplineCartViewModel _owner;
        string _quantity = string.Empty;

        public DroplineCartLineModel(DroplineCartViewModel owner)
        {
            _owner = owner;
            RemoveCommand = new DelegateCommand(_ => _owner.SetQuantity(this, 0));
        }

        internal MarketCartLine Line { get; private set; } = new MarketCartLine();
        public ICommand RemoveCommand { get; }
        [PreviewWith("MyObjectBuilder_Ingot/Platinum", "MyObjectBuilder_Component/SteelPlate")]
        public string Icon => string.IsNullOrEmpty(Line.Icon) ? "MissingIcon" : Line.Icon;
        [PreviewWith("Platinum Ingot", "Steel Plate")]
        public string Name => Line.Name ?? string.Empty;
        [PreviewWith("100", "250")]
        public string Quantity
        {
            get { return _quantity; }
            set
            {
                if (!RaiseAndSetIfChanged(ref _quantity, value ?? string.Empty, nameof(Quantity)))
                    return;
                int quantity;
                if (int.TryParse(_quantity, NumberStyles.None, CultureInfo.InvariantCulture, out quantity) && quantity > 0 &&
                    quantity != Line.Quantity)
                    _owner.SetQuantity(this, quantity);
            }
        }
        [PreviewWith("287,500 SC", "45,000 SC")]
        public string SubtotalText => Line.UnitPrice < 0 ? "-" : _owner.Credits(Line.UnitPrice * Line.Quantity);
        [PreviewWith(0.5525f)]
        public float FontSize => _owner.SmallFontSize;

        internal void Update(MarketCartLine line)
        {
            Line = line;
            int typed;
            if (!int.TryParse(_quantity, NumberStyles.None, CultureInfo.InvariantCulture, out typed) || typed != line.Quantity)
                RaiseAndSetIfChanged(ref _quantity, line.Quantity.ToString(CultureInfo.InvariantCulture), nameof(Quantity));
            RaisePropertyChanged<string>(nameof(Icon));
            RaisePropertyChanged<string>(nameof(Name));
            RaisePropertyChanged<string>(nameof(SubtotalText));
        }
    }
}
