using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Market;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Common.Market;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using Sandbox.ModAPI;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;

namespace LcdMod.Client.Apps
{
    [LcdApp(19)]
    [MultiFunction(TITLE, "MyObjectBuilder_StoreBlock/StoreBlock")]
    [ConfigComponent(APP, typeof(NpcMarketConfigComponent), PropertyName = "NpcMarketComponent")]
    internal sealed partial class NpcMarketApp : App, IApp, NpcMarket.INpcMarketHost
    {
        public const string TITLE = MOD_PREFIX + "MarketApp";
        const string LOC_REFRESH = MOD_PREFIX + "MarketApp_Refresh";
        const string LOC_EMPTY = MOD_PREFIX + "MarketApp_Empty";
        const string LOC_NO_TRADER = MOD_PREFIX + "MarketApp_NoTrader";
        const string LOC_UPDATED = MOD_PREFIX + "MarketApp_Updated";
        const string LOC_NEXT_RESTOCK = "StoreBlockView_TimeRemainingEconomyUpdate";
        const string LOC_SEARCH = "MarketWatchTab_Button_Search";
        const int FOOTER_CLOCK_UPDATE_INTERVAL_RUNS = 6;
        internal static readonly NpcMarketMode[] MarketModes = { NpcMarketMode.Buy, NpcMarketMode.Sell, NpcMarketMode.Both };

        readonly NpcMarketAggregator _aggregator = new NpcMarketAggregator();
        readonly List<NpcMarketRow> _rows = new List<NpcMarketRow>();
        NpcMarketMode _mode;
        NpcMarketSortColumn _sortColumn = NpcMarketSortColumn.Price;
        bool _sortDescending;
        string _searchQuery;
        NpcMarketAggregationResult _aggregation = new NpcMarketAggregationResult();
        bool _aggregationDirty = true;
        bool _rowsDirty = true;
        bool _hasMaxDistance;
        double _lastMaxDistanceMeters;
        bool _hasContentState;
        MarketContentState _contentState;
        string _footerUpdatedValue = string.Empty;
        string _footerNextTickValue = string.Empty;
        bool _footerRefreshEnabled;
        bool _footerModeEnabled;
        int _footerClockUpdateRunCount = FOOTER_CLOCK_UPDATE_INTERVAL_RUNS - 1;
        readonly NpcMarket.NpcMarketViewModel _viewModel;

        NpcMarketClientCacheKey CacheKey =>
            new NpcMarketClientCacheKey(
                Host.Block?.EntityId ?? 0L,
                Host.SurfaceIndex);

        internal NpcMarketMode Mode => _mode;
        internal override float WindowRenderScaling => GetLayoutScale() > 0f ? GetLayoutScale() : 1f;
        internal IAppHost AppHost => Host;
        internal float NativeFontScale => Host.Surface.FontSize > 0f ? Host.Surface.FontSize : 1f;
        internal Color NativeForeground => Host.ForegroundColor;
        NpcMarketMode NpcMarket.INpcMarketHost.Mode => Mode;
        IReadOnlyList<NpcMarketRow> NpcMarket.INpcMarketHost.NativeRows => NativeRows;
        string NpcMarket.INpcMarketHost.NativeUpdatedText => NativeUpdatedText;
        string NpcMarket.INpcMarketHost.NativeNextTickText => NativeNextTickText;
        string NpcMarket.INpcMarketHost.NativeRefreshText => NativeRefreshText;
        string NpcMarket.INpcMarketHost.NativeSearchQuery => NativeSearchQuery;
        string NpcMarket.INpcMarketHost.NativeEmptyText => NativeEmptyText;
        string NpcMarket.INpcMarketHost.NativeNoTraderText => NativeNoTraderText;
        bool NpcMarket.INpcMarketHost.NativeRefreshEnabled => NativeRefreshEnabled;
        bool NpcMarket.INpcMarketHost.NativeModeEnabled => NativeModeEnabled;
        NpcMarketSortColumn NpcMarket.INpcMarketHost.SortColumn => SortColumn;
        bool NpcMarket.INpcMarketHost.SortDescending => SortDescending;
        MarketContentState NpcMarket.INpcMarketHost.ContentState => ContentState;
        string NpcMarket.INpcMarketHost.NativeSearchPlaceholder => NativeSearchPlaceholder;
        float NpcMarket.INpcMarketHost.NativeFontScale => NativeFontScale;
        Color NpcMarket.INpcMarketHost.NativeForeground => NativeForeground;
        Color NpcMarket.INpcMarketHost.ResolveMarketColor(ResourceKey<Color> key, Color fallback) => ResolveMarketColor(key, fallback);
        void NpcMarket.INpcMarketHost.SetNativeSearch(string value) { SetNativeSearch(value); }
        void NpcMarket.INpcMarketHost.ClearNativeSearch() { ClearNativeSearch(); }
        void NpcMarket.INpcMarketHost.SortNative(NpcMarketSortColumn column) { SortNative(column); }
        void NpcMarket.INpcMarketHost.OpenNativeRow(string itemKey, NpcMarketMode mode) { OpenNativeRow(itemKey, mode); }
        void NpcMarket.INpcMarketHost.OnNativePageChanged(int pageIndex) { OnNativePageChanged(pageIndex); }
        void NpcMarket.INpcMarketHost.RefreshNativeMarket() { RefreshNativeMarket(); }
        void NpcMarket.INpcMarketHost.SetMode(NpcMarketMode mode) { SetMode(mode); }
        NpcMarket.MarketItemDialogViewModel _itemDialog;
        NpcMarket.DroplineCartViewModel _cartDialog;
        internal IReadOnlyList<NpcMarketRow> NativeRows => _rows;
        internal string NativeModeText => GetModeLabel(_mode);
        internal string NativeUpdatedText => _footerUpdatedValue;
        internal string NativeNextTickText => _footerNextTickValue;
        internal string NativeSearchText => string.IsNullOrEmpty(_searchQuery) ? MyTexts.GetString(LOC_SEARCH) : _searchQuery;
        internal string NativeRefreshText => MyTexts.GetString(LOC_REFRESH);
        internal string NativeSearchQuery => _searchQuery;
        internal string NativeEmptyText => MyTexts.GetString(LOC_EMPTY);
        internal string NativeNoTraderText => MyTexts.GetString(LOC_NO_TRADER);
        internal bool NativeRefreshEnabled => _footerRefreshEnabled;
        internal bool NativeModeEnabled => _footerModeEnabled;
        internal NpcMarketSortColumn SortColumn => _sortColumn;
        internal bool SortDescending => _sortDescending;
        internal MarketContentState ContentState => _contentState;

        internal Color ResolveMarketColor(ResourceKey<Color> key, Color fallback)
        {
            return ResolveResource(key, fallback);
        }

        internal string NativeSearchPlaceholder => MyTexts.GetString(LOC_SEARCH);

        internal void SetNativeSearch(string value)
        {
            if (string.Equals(value, _searchQuery, StringComparison.Ordinal))
                return;
            OnSearchChanged(value);
            _viewModel.Refresh();
        }

        internal void ClearNativeSearch()
        {
            OnSearchChanged(string.Empty);
            _viewModel.Refresh();
        }

        internal void SortNative(NpcMarketSortColumn column)
        {
            OnSortColumnClicked(column);
            _viewModel.Refresh();
        }

        internal void OpenNativeRow(string itemKey, NpcMarketMode mode)
        {
            if (!string.IsNullOrEmpty(itemKey))
                OnMarketRowClicked(new NpcMarketRowClickTarget(itemKey, mode));
        }

        internal void OnNativePageChanged(int pageIndex)
        {
            SavePageIndex(pageIndex);
            ResetAutoPageTimer();
            Host.RenderSprites();
        }
        bool _restoredPageIndex;
        int _lastAutoPageStep = -1;
        long _autoPagePauseStartedFrame = long.MinValue;
        long _autoPagePausedFrames;

        public NpcMarketApp(IAppHost host) : base(host)
        {
            _viewModel = new NpcMarket.NpcMarketViewModel(this);
            SetContent(new NpcMarket.NpcMarketView { DataContext = _viewModel });
            _mode = NormalizeConfiguredMode(NpcMarketComponent.SelectedMode);
            NpcMarketComponent.SelectedMode = (int)_mode;
            _searchQuery = NpcMarketComponent.SearchQuery ?? string.Empty;
            NpcMarketComponent.SearchQuery = _searchQuery;
            LoadSortStateForMode(_mode);
            NpcMarketClientCache.Updated += HandleUpdated;
            TextureHelper.TextureIconCacheChanged += HandleTextureIconCacheChanged;
        }

        public override void Close()
        {
            NpcMarketClientCache.Updated -= HandleUpdated;
            TextureHelper.TextureIconCacheChanged -= HandleTextureIconCacheChanged;
        }

        public override void Update()
        {
            if (_itemDialog != null)
                _itemDialog.Refresh();
            if (_cartDialog != null)
                _cartDialog.Tick();
            var key = CacheKey;
            if (IsLocallyAccessDenied())
            {
                NpcMarketClientCache.MarkAccessDenied(key);
                ApplyPendingRowChanges();
                UpdateContentState();
                UpdateFooterPresentation();
                _viewModel.Refresh();
                return;
            }

            var maxDistanceMeters = GetMaxDistanceMeters();
            if (!_hasMaxDistance || !AreEqualDistance(_lastMaxDistanceMeters, maxDistanceMeters))
            {
                _hasMaxDistance = true;
                _lastMaxDistanceMeters = maxDistanceMeters;
                MarkAggregationDirty();
            }

            NpcMarketClientCache.EnsureRequested(key);
            NpcMarketClientCache.Update(key);
            ApplyPendingRowChanges();
            UpdateContentState();
            UpdateFooterPresentation();
            _viewModel.Refresh();
            RestorePageIndex();
            AdvancePageFromTimer();
        }

        internal void RefreshNativeMarket()
        {
            NpcMarketClientCache.RequestRefresh(CacheKey, true);
            UpdateFooterPresentation(true);
            Host.RenderSprites();
        }
        internal void CycleNativeMode()
        {
            int index = Array.IndexOf(MarketModes, _mode);
            OnModeChanged(MarketModes[(index + 1) % MarketModes.Length]);
            _viewModel.Refresh();
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            InvalidateContentAndFooterSprites();
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        public override bool HasVisibleItems()
        {
            return NpcMarketClientCache.GetSnapshot(CacheKey) != null;
        }

        void IApp.OnMouseScroll(int delta, ref bool handled)
        {
            if (handled)
                return;

            if (!_viewModel.CanNavigate)
                return;

            _viewModel.MovePage(delta > 0 ? -1 : 1);
            handled = true;
            Host.RenderSprites();
        }

        static bool ShouldShowNoTraderWarning(Common.Networking.PacketSyncNpcMarket snapshot)
        {
            if (snapshot == null || snapshot.Scope == null)
                return false;

            switch (snapshot.Scope.Mode)
            {
                case NpcMarketScopeMode.UnownedHostBlock:
                case NpcMarketScopeMode.InvalidHostBlock:
                    return true;
            }

            return snapshot.Scope.KnownStationCount <= 0;
        }

        static bool IsAccessDenied(Common.Networking.PacketSyncNpcMarket snapshot)
        {
            return snapshot != null &&
                   snapshot.Scope != null &&
                   snapshot.Scope.Mode == NpcMarketScopeMode.AccessDenied;
        }

        bool IsLocallyAccessDenied()
        {
            var terminalBlock = Host.Block as IMyTerminalBlock;
            var localPlayer = MyAPIGateway.Session.LocalHumanPlayer;
            return terminalBlock != null &&
                   localPlayer != null &&
                   !terminalBlock.HasPlayerAccess(localPlayer.IdentityId);
        }

        void MarkAggregationDirty()
        {
            _aggregationDirty = true;
            _rowsDirty = true;
            InvalidateContentSprites();
        }

        void MarkRowsDirty()
        {
            _rowsDirty = true;
            InvalidateContentSprites();
        }

        void InvalidateContentSprites()
        {
            MarkDirty();
        }

        void InvalidateFooterSprites()
        {
            MarkDirty();
        }

        void InvalidateContentAndFooterSprites()
        {
            MarkDirty();
        }

        void UpdateContentState()
        {
            var snapshot = NpcMarketClientCache.GetSnapshot(CacheKey);
            MarketContentState state;
            if (IsLocallyAccessDenied() || IsAccessDenied(snapshot))
                state = MarketContentState.AccessDenied;
            else if (snapshot == null)
                state = MarketContentState.Loading;
            else if (ShouldShowNoTraderWarning(snapshot))
                state = MarketContentState.NoTrader;
            else if (_rows.Count == 0 && string.IsNullOrWhiteSpace(_searchQuery))
                state = MarketContentState.Empty;
            else
                state = MarketContentState.Rows;

            if (_hasContentState && _contentState == state)
                return;

            _hasContentState = true;
            _contentState = state;
            InvalidateContentSprites();
        }

        void UpdateFooterPresentation(bool forceClockRefresh = false)
        {
            var key = CacheKey;
            var snapshot = NpcMarketClientCache.GetSnapshot(key);
            var refreshEnabled = NpcMarketClientCache.CanForceRefresh(key);
            var modeEnabled = !IsLocallyAccessDenied() && !IsAccessDenied(snapshot);

            var updateClock = forceClockRefresh ||
                              ++_footerClockUpdateRunCount >= FOOTER_CLOCK_UPDATE_INTERVAL_RUNS;
            var updated = _footerUpdatedValue;
            var nextTick = _footerNextTickValue;
            if (updateClock)
            {
                _footerClockUpdateRunCount = 0;
                var now = WorldTime.NowElapsedTicks();
                updated = snapshot == null
                    ? string.Empty
                    : string.Format(MyTexts.GetString(LOC_UPDATED),
                        FormatDuration(now - snapshot.CacheBuiltAtWorldElapsedTicks));
                nextTick = snapshot == null
                    ? string.Empty
                    : MyTexts.GetString(LOC_NEXT_RESTOCK) + " " +
                      FormatDuration(snapshot.NextEconomyTickWorldElapsedTicks - now);
            }

            if (string.Equals(_footerUpdatedValue, updated, StringComparison.Ordinal) &&
                string.Equals(_footerNextTickValue, nextTick, StringComparison.Ordinal) &&
                _footerRefreshEnabled == refreshEnabled &&
                _footerModeEnabled == modeEnabled)
                return;

            _footerUpdatedValue = updated;
            _footerNextTickValue = nextTick;
            _footerRefreshEnabled = refreshEnabled;
            _footerModeEnabled = modeEnabled;
            InvalidateFooterSprites();
        }

        void ApplyPendingRowChanges()
        {
            if (_aggregationDirty)
                RebuildAggregation();

            if (_rowsDirty)
                RefreshVisibleRows();
        }

        void RebuildAggregation()
        {
            _aggregationDirty = false;
            var snapshot = NpcMarketClientCache.GetSnapshot(CacheKey);
            _aggregation = snapshot == null
                ? new NpcMarketAggregationResult()
                : _aggregator.Build(snapshot, Host.Surface, _mode, _sortColumn, _sortDescending,
                    GetMaxDistanceMeters());
            _rowsDirty = true;
        }

        void RefreshVisibleRows()
        {
            _rowsDirty = false;
            _rows.Clear();
            if (_aggregation == null)
                _aggregation = new NpcMarketAggregationResult();

            foreach (var row in _aggregation.Rows)
            {
                if (MatchesSearch(row))
                    _rows.Add(row);
            }

            unchecked
            {
            }
        }

        static bool AreEqualDistance(double left, double right)
        {
            if (double.IsPositiveInfinity(left) && double.IsPositiveInfinity(right))
                return true;

            return Math.Abs(left - right) < 0.001d;
        }

        internal NpcMarketItemGroup GetItemGroup(string itemKey)
        {
            NpcMarketItemGroup group;
            return !string.IsNullOrEmpty(itemKey) && _aggregation.GroupsByItemKey.TryGetValue(itemKey, out group)
                ? group
                : null;
        }

        internal void SetMode(NpcMarketMode mode)
        {
            OnModeChanged(mode);
        }

        void OnMarketRowClicked(NpcMarketRowClickTarget target)
        {
            if (target == null)
                return;

            var group = GetItemGroup(target.ItemKey);
            if (group == null)
                return;

            var createGps = MyAPIGateway.Input.IsAnyCtrlKeyPressed();

            if (createGps)
            {
                CreateTemporaryGps(GetQuoteForMode(group.Summary, target.Mode), group.DisplayName);
                return;
            }

            OpenItemDialog(group, target.Mode);
        }

        void OpenItemDialog(NpcMarketItemGroup group, NpcMarketMode mode)
        {
            string itemKey = group.ItemKey;
            string itemName = group.DisplayName;
            var model = new NpcMarket.MarketItemDialogViewModel(
                itemName,
                group.SpriteName,
                mode == NpcMarketMode.Sell ? NpcMarketMode.Sell : NpcMarketMode.Buy,
                new[]
                {
                    MyTexts.GetString("MarketWatchTab_Column_Distance"),
                    MyTexts.GetString("StoreBlock_Column_Name"),
                    MyTexts.GetString("StoreBlock_Column_PricePerUnit"),
                    MyTexts.GetString("StoreBlock_Column_Trend")
                },
                MyTexts.GetString("MarketWatchTab_SearchMessage_NoResults"),
                selected => LoadItemQuotes(itemKey, selected),
                quote => CreateTemporaryGps((NpcMarketStationQuote)quote.Source, itemName),
                selected => { if (Mode != NpcMarketMode.Both) SetMode(selected); },
                DroplineClient.IsPresent ? CreateDroplineDelivery() : null)
            {
                ModeLabel = selected => MyTexts.GetString(selected == NpcMarketMode.Buy ? "StoreScreenBuyHeader" : "StoreScreenSellHeader")
            };
            var dialog = new NpcMarket.MarketItemDialogView(model);
            dialog.Closed += () => { if (ReferenceEquals(_itemDialog, model)) _itemDialog = null; };
            if (ShowDialog(dialog))
                _itemDialog = model;
        }

        NpcMarket.MarketDelivery CreateDroplineDelivery()
        {
            var delivery = new NpcMarket.MarketDelivery
            {
                CanDeliver = quote =>
                {
                    var source = quote.Source as NpcMarketStationQuote;
                    return source != null && source.ItemType == ItemTypes.PhysicalItem && source.StoreItemType == StoreItemTypes.Offer;
                },
                LoadDestinations = done => DroplineClient.RequestDestinations(CacheKey.HostBlockEntityId,
                    reply => done(ToDestinations(reply), reply.Error)),
                SelectDestination = (padId, done) => DroplineClient.SelectDestination(CacheKey.HostBlockEntityId, padId,
                    reply => done(ToDestinations(reply), reply.Error)),
                RequestQuote = (quote, quantity, padId, done) =>
                {
                    var source = (NpcMarketStationQuote)quote.Source;
                    DroplineClient.RequestQuote(CacheKey.HostBlockEntityId, padId, source.StationId, source.TypeId, source.SubtypeId, quantity,
                        reply => done(ToQuote(reply)));
                },
                Submit = (quoteId, done) => DroplineClient.Submit(CacheKey.HostBlockEntityId, quoteId, reply => done(reply.Error)),
                LoadCart = done => DroplineClient.RequestCart(CacheKey.HostBlockEntityId, reply => done(ToCartLines(reply), reply.Error)),
                AddToCart = (quote, quantity, done) =>
                {
                    var source = (NpcMarketStationQuote)quote.Source;
                    DroplineClient.AddToCart(CacheKey.HostBlockEntityId, source.StationId, source.TypeId, source.SubtypeId, quantity,
                        reply => done(ToCartLines(reply), reply.Error));
                },
                SetCartQuantity = (line, quantity, done) => DroplineClient.SetCartQuantity(CacheKey.HostBlockEntityId, line.StationId,
                    line.TypeId, line.SubtypeId, quantity, reply => done(ToCartLines(reply), reply.Error)),
                ClearCart = (stationId, done) => DroplineClient.ClearCart(CacheKey.HostBlockEntityId, stationId,
                    reply => done(ToCartLines(reply), reply.Error)),
                QuoteCart = (stationId, padId, done) => DroplineClient.QuoteCart(CacheKey.HostBlockEntityId, stationId, padId,
                    reply => done(ToQuote(reply))),
                FormatCredits = FormatingHelper.FormatSpaceCredits,
                FormatDuration = FormatDuration,
                Now = () => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds,
                IsGpsModifierPressed = () => MyAPIGateway.Input.IsAnyCtrlKeyPressed()
            };
            delivery.OpenCart = () => OpenCart(delivery);
            return delivery;
        }

        void OpenCart(NpcMarket.MarketDelivery delivery)
        {
            if (_cartDialog != null)
                return;
            var model = new NpcMarket.DroplineCartViewModel(delivery, count =>
            {
                if (_itemDialog != null)
                    _itemDialog.SetCartCount(count);
            });
            var dialog = new NpcMarket.DroplineCartView(model);
            dialog.Closed += () => { if (ReferenceEquals(_cartDialog, model)) _cartDialog = null; };
            if (ShowDialog(dialog))
                _cartDialog = model;
        }

        static NpcMarket.MarketDeliveryQuote ToQuote(Common.Networking.PacketDroplineReply reply)
        {
            return new NpcMarket.MarketDeliveryQuote
            {
                Id = reply.QuoteId,
                Error = reply.Error,
                Subtotal = reply.Subtotal,
                Fee = reply.Fee,
                Total = reply.Total,
                EtaSeconds = reply.EtaSeconds
            };
        }

        IList<NpcMarket.MarketCartLine> ToCartLines(Common.Networking.PacketDroplineReply reply)
        {
            if (reply.Error != null || reply.CartLines == null)
                return null;
            var result = new List<NpcMarket.MarketCartLine>(reply.CartLines.Count);
            foreach (var line in reply.CartLines)
                result.Add(new NpcMarket.MarketCartLine
                {
                    StationId = line.StationId,
                    StationName = line.StationName,
                    TypeId = line.TypeId,
                    SubtypeId = line.SubtypeId,
                    Icon = TextureHelper.ResolveItemSprite(new VRage.Game.ModAPI.Ingame.MyItemType(line.TypeId, line.SubtypeId), Host.Surface),
                    Name = line.Name,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice
                });
            return result;
        }

        static IList<NpcMarket.MarketDestination> ToDestinations(Common.Networking.PacketDroplineReply reply)
        {
            if (reply.Error != null || reply.Destinations == null)
                return null;
            var result = new List<NpcMarket.MarketDestination>(reply.Destinations.Count);
            foreach (var pad in reply.Destinations)
            {
                var name = string.IsNullOrWhiteSpace(pad.Name) ? pad.PadId.ToString() : pad.Name;
                result.Add(new NpcMarket.MarketDestination
                {
                    PadId = pad.PadId,
                    Label = (pad.Flags & DroplineDestinationDto.BusyFlag) != 0
                        ? string.Format(MyTexts.GetString("LcdMod_Market_Delivery_PadBusy"), name)
                        : name,
                    IsSelected = (pad.Flags & DroplineDestinationDto.SelectedFlag) != 0
                });
            }
            return result;
        }

        static string FormatDuration(int seconds)
        {
            seconds = Math.Max(0, seconds);
            return (seconds / 3600).ToString("00") + ":" + (seconds / 60 % 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        List<NpcMarket.MarketQuote> LoadItemQuotes(string itemKey, NpcMarketMode mode)
        {
            var quotes = new List<NpcMarket.MarketQuote>();
            var group = GetItemGroup(itemKey);
            if (group == null)
                return quotes;
            var source = group.Mode == NpcMarketMode.Both
                ? (mode == NpcMarketMode.Buy ? group.BuyQuotes : group.SellQuotes)
                : group.Quotes;
            foreach (var quote in source)
                quotes.Add(new NpcMarket.MarketQuote
                {
                    Source = quote,
                    Key = quote.ItemKey + "|" + quote.SellerFactionId + "|" + quote.StationId,
                    Distance = quote.DistanceMeters,
                    DistanceText = FormatingHelper.DistanceToString((float)quote.DistanceMeters),
                    Station = quote.StationName,
                    Price = quote.PersonalizedCurrentPricePerUnit,
                    PriceText = FormatingHelper.FormatSpaceCredits(quote.PersonalizedCurrentPricePerUnit) + " SC",
                    Trend = quote.EffectiveViewerChangePercent
                });
            return quotes;
        }

        internal void CreateTemporaryGps(NpcMarketStationQuote quote, string itemName)
        {
            var session = MyAPIGateway.Session;
            var gpsCollection = session.GPS;
            if (quote == null || gpsCollection == null)
                return;

            var stationName = string.IsNullOrWhiteSpace(quote.StationName)
                ? MyTexts.GetString("TssTargetingInfo_StaticGrid")
                : quote.StationName;
            var modeLabel = MyTexts.GetString(quote.StoreItemType == StoreItemTypes.Offer
                ? "StoreScreenBuyHeader"
                : "StoreScreenSellHeader");
            var description = modeLabel + " " + (itemName ?? string.Empty) + " @ " +
                              FormatingHelper.FormatSpaceCredits(quote.PersonalizedCurrentPricePerUnit) + " SC";
            var gps = gpsCollection.Create(stationName, description, quote.StationPosition, true, true);
            if (gps == null)
                return;

            var discardAt = GetEconomyRefreshDiscardAt();
            if (discardAt.HasValue)
                gps.DiscardAt = discardAt.Value;

            gpsCollection.AddLocalGps(gps);
        }

        TimeSpan? GetEconomyRefreshDiscardAt()
        {
            var session = MyAPIGateway.Session;
            var snapshot = NpcMarketClientCache.GetSnapshot(CacheKey);
            if (snapshot == null || snapshot.NextEconomyTickWorldElapsedTicks <= 0)
                return null;

            var remainingTicks = snapshot.NextEconomyTickWorldElapsedTicks - WorldTime.NowElapsedTicks();
            return session.ElapsedPlayTime + TimeSpan.FromTicks(Math.Max(0L, remainingTicks));
        }

        bool MatchesSearch(NpcMarketRow row)
        {
            var query = (_searchQuery ?? string.Empty).Trim();
            if (query.Length == 0)
                return true;

            var clauses = query.Split(',');
            for (var i = 0; i < clauses.Length; i++)
            {
                if (MatchesSearchClause(row, clauses[i]))
                    return true;
            }

            return false;
        }

        double GetMaxDistanceMeters()
        {
            var value = NpcMarketComponent.MaxDistanceMeters;
            if (SliderNpcMarketMaxDistance.IsUnlimited(value))
                return double.PositiveInfinity;

            return SliderNpcMarketMaxDistance.ClampDistanceMeters(value);
        }

        static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool MatchesSearchClause(NpcMarketRow row, string clause)
        {
            clause = (clause ?? string.Empty).Trim();
            if (clause.Length == 0)
                return false;

            var terms = clause.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < terms.Length; i++)
            {
                if (!MatchesSearchTerm(row, terms[i]))
                    return false;
            }

            return true;
        }

        static bool MatchesSearchTerm(NpcMarketRow row, string term)
        {
            if (row == null || string.IsNullOrWhiteSpace(term))
                return false;

            term = term.Trim();
            if (term.Length > 1 && term[0] == '#')
                return MatchesSearchMacro(row, term.Substring(1));

            return Contains(row.DisplayName, term) ||
                   Contains(row.GetSecondaryLabel(), term) ||
                   Contains(row.BestStationName, term) ||
                   Contains(row.BestSellerFactionTag, term) ||
                   Contains(row.TypeId, term) ||
                   Contains(row.SubtypeId, term) ||
                   Contains(row.PrefabName, term);
        }

        static bool MatchesSearchMacro(NpcMarketRow row, string macro)
        {
            if (row == null || string.IsNullOrWhiteSpace(macro))
                return false;

            macro = macro.Trim();

            if (EqualsAnyIgnoreCase(macro, "ship", "ships", "prefab", "prefabs"))
                return row.ItemType == ItemTypes.Grid;

            return Contains(row.TypeId, macro) ||
                   Contains(row.SubtypeId, macro) ||
                   Contains(row.PrefabName, macro);
        }

        static bool EqualsAnyIgnoreCase(string value, params string[] candidates)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(value, candidates[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static NpcMarketStationQuote GetQuoteForMode(NpcMarketRow row, NpcMarketMode mode)
        {
            if (row == null)
                return null;

            switch (mode)
            {
                case NpcMarketMode.Buy:
                    return row.BestBuyQuote ?? (row.StoreItemType == StoreItemTypes.Offer ? row.BestQuote : null);
                case NpcMarketMode.Sell:
                    return row.BestSellQuote ?? (row.StoreItemType == StoreItemTypes.Order ? row.BestQuote : null);
                default:
                    return row.BestQuote;
            }
        }

        void OnSearchChanged(string value)
        {
            _searchQuery = value ?? string.Empty;
            NpcMarketComponent.SearchQuery = _searchQuery;
            ResetSavedPageIndex();
            MarkRowsDirty();
            ApplyPendingRowChanges();
            NotifySearchChanged();
            Host.RenderSprites();
        }

        void OnModeChanged(NpcMarketMode mode)
        {
            mode = NormalizeAvailableMode(mode);
            if (_mode == mode)
                return;

            SaveSortStateForMode(_mode);
            _mode = mode;
            NpcMarketComponent.SelectedMode = (int)_mode;
            LoadSortStateForMode(_mode);
            ResetSavedPageIndex();
            MarkAggregationDirty();
            InvalidateFooterSprites();
            ApplyPendingRowChanges();
            Host.RenderSprites();
        }

        void OnSortColumnClicked(NpcMarketSortColumn column)
        {
            if (_sortColumn == column)
                _sortDescending = !_sortDescending;
            else
            {
                _sortColumn = column;
                _sortDescending = column == NpcMarketSortColumn.Price && _mode == NpcMarketMode.Sell;
            }

            SaveSortStateForMode(_mode);
            _aggregator.SortRows(_aggregation.Rows, _sortColumn, _sortDescending);
            MarkRowsDirty();
            ApplyPendingRowChanges();
            Host.RenderSprites();
        }

        void HandleUpdated(NpcMarketClientCacheKey key, bool rowDataChanged)
        {
            if (!key.Equals(CacheKey))
                return;

            if (rowDataChanged)
                MarkAggregationDirty();

            ApplyPendingRowChanges();
            UpdateContentState();
            UpdateFooterPresentation(forceClockRefresh: true);
            if (IsDirty)
                Host.RenderSprites();
        }

        void HandleTextureIconCacheChanged()
        {
            MarkAggregationDirty();
            ApplyPendingRowChanges();
            Host.RenderSprites();
        }

        void RestorePageIndex()
        {
            if (_restoredPageIndex || _viewModel.Pages.Count == 0)
                return;

            _restoredPageIndex = true;
            _viewModel.SelectPage(Math.Max(0, (int)Math.Round(NpcMarketComponent.HorizontalScrollOffsetPixels)));
            SavePageIndex();
        }

        void ResetSavedPageIndex()
        {
            NpcMarketComponent.ScrollOffsetPixels = 0f;
            NpcMarketComponent.HorizontalScrollOffsetPixels = 0f;
            NpcMarketComponent.VerticalScrollOffsetPixels = 0f;
            _restoredPageIndex = true;
            ResetAutoPageTimer();
            _viewModel.SelectPage(0);
        }

        void SavePageIndex()
        {
            SavePageIndex(_viewModel.SelectedPage);
        }

        void SavePageIndex(int pageIndex)
        {
            NpcMarketComponent.HorizontalScrollOffsetPixels = Math.Max(0, pageIndex);
            NpcMarketComponent.VerticalScrollOffsetPixels = 0f;
            NpcMarketComponent.ScrollOffsetPixels = 0f;
        }

        void AdvancePageFromTimer()
        {
            if (!_viewModel.CanNavigate)
            {
                ResetAutoPageTimer();
                return;
            }

            var seconds = SliderNpcMarketPageSwitchDelay.ClampSeconds(NpcMarketComponent.PageSwitchSeconds);
            if (seconds <= 0f)
            {
                ResetAutoPageTimer();
                return;
            }

            var session = MyAPIGateway.Session;
            var framesPerStep = Math.Max(1, (int)Math.Round(seconds * 60f));
            var frame = session.GameplayFrameCounter;
            var content = Content as Acrux.Core.VisualElement;
            if (content != null && content.IsPointerOver)
            {
                if (_autoPagePauseStartedFrame == long.MinValue)
                    _autoPagePauseStartedFrame = frame;

                return;
            }

            if (_autoPagePauseStartedFrame != long.MinValue)
            {
                _autoPagePausedFrames += Math.Max(0L, frame - _autoPagePauseStartedFrame);
                _autoPagePauseStartedFrame = long.MinValue;
            }

            var step = Math.Max(0L, frame - _autoPagePausedFrames) / framesPerStep;
            if (_lastAutoPageStep < 0)
            {
                _lastAutoPageStep = (int)step;
                return;
            }

            if (step == _lastAutoPageStep)
                return;

            _lastAutoPageStep = (int)step;
            _viewModel.MovePage(1);
        }

        void ResetAutoPageTimer()
        {
            _lastAutoPageStep = -1;
            _autoPagePauseStartedFrame = long.MinValue;
            _autoPagePausedFrames = 0L;
        }

        NpcMarketMode NormalizeConfiguredMode(int value)
        {
            return NormalizeMode(value);
        }

        NpcMarketMode NormalizeAvailableMode(NpcMarketMode mode)
        {
            return mode;
        }

        static NpcMarketMode NormalizeMode(int value)
        {
            switch ((NpcMarketMode)value)
            {
                case NpcMarketMode.Sell:
                    return NpcMarketMode.Sell;
                case NpcMarketMode.Both:
                    return NpcMarketMode.Both;
                default:
                    return NpcMarketMode.Buy;
            }
        }

        void LoadSortStateForMode(NpcMarketMode mode)
        {
            if (mode == NpcMarketMode.Sell)
            {
                _sortColumn = NormalizeSortColumn(NpcMarketComponent.SellSortColumn, mode);
                _sortDescending = NpcMarketComponent.SellSortDescending;
                NpcMarketComponent.SellSortColumn = (int)_sortColumn;
                return;
            }

            if (mode == NpcMarketMode.Both)
            {
                _sortColumn = NormalizeSortColumn(NpcMarketComponent.BothSortColumn, mode);
                _sortDescending = NpcMarketComponent.BothSortDescending;
                NpcMarketComponent.BothSortColumn = (int)_sortColumn;
                return;
            }

            _sortColumn = NormalizeSortColumn(NpcMarketComponent.BuySortColumn, mode);
            _sortDescending = NpcMarketComponent.BuySortDescending;
            NpcMarketComponent.BuySortColumn = (int)_sortColumn;
        }

        void SaveSortStateForMode(NpcMarketMode mode)
        {
            if (mode == NpcMarketMode.Sell)
            {
                NpcMarketComponent.SellSortColumn = (int)_sortColumn;
                NpcMarketComponent.SellSortDescending = _sortDescending;
                return;
            }

            if (mode == NpcMarketMode.Both)
            {
                NpcMarketComponent.BothSortColumn = (int)_sortColumn;
                NpcMarketComponent.BothSortDescending = _sortDescending;
                return;
            }

            NpcMarketComponent.BuySortColumn = (int)_sortColumn;
            NpcMarketComponent.BuySortDescending = _sortDescending;
        }

        static NpcMarketSortColumn NormalizeSortColumn(int value, NpcMarketMode mode)
        {
            var column = (NpcMarketSortColumn)value;
            if (mode == NpcMarketMode.Both)
            {
                switch (column)
                {
                    case NpcMarketSortColumn.Name:
                    case NpcMarketSortColumn.BuyPrice:
                    case NpcMarketSortColumn.SellPrice:
                    case NpcMarketSortColumn.BuyTrend:
                    case NpcMarketSortColumn.SellTrend:
                        return column;
                    case NpcMarketSortColumn.Price:
                        return NpcMarketSortColumn.BuyPrice;
                    case NpcMarketSortColumn.Trend:
                        return NpcMarketSortColumn.BuyTrend;
                    default:
                        return NpcMarketSortColumn.Name;
                }
            }

            switch (column)
            {
                case NpcMarketSortColumn.Name:
                case NpcMarketSortColumn.Price:
                case NpcMarketSortColumn.Trend:
                    return column;
                case NpcMarketSortColumn.BuyPrice:
                case NpcMarketSortColumn.SellPrice:
                    return NpcMarketSortColumn.Price;
                case NpcMarketSortColumn.BuyTrend:
                case NpcMarketSortColumn.SellTrend:
                    return NpcMarketSortColumn.Trend;
                default:
                    return NpcMarketSortColumn.Price;
            }
        }


        float GetLayoutScale()
        {
            return GeneralComponent.GetScale();
        }

        internal static string GetModeLabel(NpcMarketMode mode)
        {
            switch (mode)
            {
                case NpcMarketMode.Buy:
                    return MyTexts.GetString("StoreScreenBuyHeader");
                case NpcMarketMode.Sell:
                    return MyTexts.GetString("StoreScreenSellHeader");
                default:
                    return "Both";
            }
        }

        static string FormatDuration(long ticks)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling(WorldTime.ToSeconds(ticks)));
            if (seconds < 60)
                return seconds + "s";

            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        internal enum MarketContentState
        {
            Loading,
            AccessDenied,
            NoTrader,
            Empty,
            Rows
        }
    }
}
