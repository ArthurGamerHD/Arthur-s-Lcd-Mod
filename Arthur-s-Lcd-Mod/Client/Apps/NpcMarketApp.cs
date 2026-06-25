using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Custom.Market;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
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
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;

namespace LcdMod.Client.Apps
{
    [LcdApp(19)]
    [ConfigComponent(APP, typeof(NpcMarketConfigComponent), PropertyName = "NpcMarketComponent")]
    internal sealed partial class NpcMarketApp : App, IApp
    {
        public const string TITLE = MOD_PREFIX + "MarketApp";
        const string LOC_REFRESH = MOD_PREFIX + "MarketApp_Refresh";
        const string LOC_EMPTY = MOD_PREFIX + "MarketApp_Empty";
        const string LOC_NO_TRADER = MOD_PREFIX + "MarketApp_NoTrader";
        const string LOC_UPDATED = MOD_PREFIX + "MarketApp_Updated";
        const string LOC_NEXT_RESTOCK = "StoreBlockView_TimeRemainingEconomyUpdate";
        const string LOC_COLUMN_NAME = "StoreBlock_Column_Name";
        const string LOC_COLUMN_PRICE = "StoreBlock_Column_PricePerUnit";
        const string LOC_COLUMN_TREND = "StoreBlock_Column_Trend";
        const string LOC_SEARCH = "MarketWatchTab_Button_Search";
        const float SINGLE_SIDE_LIST_MIN_WIDTH = 480f;
        const float BOTH_LIST_MIN_WIDTH = 720f;
        static readonly NpcMarketMode[] MarketModes = { NpcMarketMode.Buy, NpcMarketMode.Sell, NpcMarketMode.Both };

        readonly List<Control> _children = new List<Control>();
        readonly NpcMarketAggregator _aggregator = new NpcMarketAggregator();
        readonly List<NpcMarketRow> _rows = new List<NpcMarketRow>();
        readonly StringBuilder _text = new StringBuilder();
        readonly PagesPanel _pagesPanel;
        readonly NpcMarketListStripPanel _listStripPanel;
        readonly ComboBox<NpcMarketMode> _modeComboBox;
        readonly List<Button> _sortHeaderButtons = new List<Button>();
        readonly TextInputModel _searchInputModel;
        readonly TextInput _searchInput;
        readonly Button _searchButton;
        readonly Button _clearSearchButton;
        readonly Grid _footerGrid;
        readonly TextBlock _footerUpdatedText;
        readonly TextBlock _footerNextTickText;
        readonly Button _refreshButton;
        NpcMarketMode _mode;
        NpcMarketSortColumn _sortColumn = NpcMarketSortColumn.Price;
        bool _sortDescending;
        string _searchQuery = string.Empty;
        NpcMarketAggregationResult _aggregation = new NpcMarketAggregationResult();

        NpcMarketClientCacheKey CacheKey =>
            new NpcMarketClientCacheKey(
                Host.Block?.EntityId ?? 0L,
                Host.SurfaceIndex);

        public override IReadOnlyList<Control> Children => _children;
        internal NpcMarketMode Mode => _mode;
        internal IAppHost AppHost => Host;
        bool _restoredPageIndex;
        int _lastAutoPageStep = -1;

        public NpcMarketApp(IAppHost host) : base(host)
        {
            _mode = NormalizeConfiguredMode(NpcMarketComponent.SelectedMode);
            NpcMarketComponent.SelectedMode = (int)_mode;
            _searchQuery = NpcMarketComponent.SearchQuery ?? string.Empty;
            NpcMarketComponent.SearchQuery = _searchQuery;
            LoadSortStateForMode(_mode);
            _pagesPanel = AddChild(new PagesPanel());
            _pagesPanel.SetVisible(false);
            _pagesPanel.PageChanged = OnPageChanged;
            _listStripPanel = new NpcMarketListStripPanel(Host)
            {
                HorizontalGap = 12f * GetLayoutScale(),
                SortClicked = OnSortColumnClicked,
                SearchClicked = OpenSearch,
                RowClicked = OnMarketRowClicked
            };
            _modeComboBox = new ComboBox<NpcMarketMode>(MarketModes, GetModeLabel, OnModeChanged, Host.RenderSprites)
            {
                OpenDirection = ComboBoxOpenDirection.Up
            };
            _modeComboBox.SetSelectedValue(_mode);
            _modeComboBox.SetVisible(false);
            _searchInputModel = new TextInputModel
            {
                Title = MyTexts.GetString(LOC_SEARCH),
                Placeholder = MyTexts.GetString(LOC_SEARCH),
                ValueChanged = OnSearchChanged
            };
            _searchInput = AddChild(new TextInput(default(RectangleF), _searchInputModel));
            _searchInput.CustomRender = RenderSearchInput;
            _searchInput.SetVisible(false);
            _searchButton = AddChild(new Button(default(RectangleF), new ButtonModel { Clicked = OnSearchClicked }));
            _searchButton.CustomRender = RenderSearchButton;
            _searchButton.SetVisible(false);
            _clearSearchButton = AddChild(new Button(default(RectangleF), new ButtonModel { Clicked = OnClearSearchClicked }));
            _clearSearchButton.CustomRender = RenderClearSearchButton;
            _clearSearchButton.SetVisible(false);
            _refreshButton = new Button(default(RectangleF), new ButtonModel { Clicked = OnRefreshClicked });
            _refreshButton.CustomRender = RenderRefreshButton;
            _refreshButton.SetVisible(false);
            _footerGrid = AddChild(new Grid());
            _footerGrid.SetClass("ControlBase NpcMarketFooter");
            _footerGrid.BackgroundTexture = null;
            _footerGrid.SetVisible(false);
            _footerGrid.SetColumns(1f, 1f, 1f, 1f);
            _footerGrid.SetRows(1f, 1f, 1f);
            var footerTextGrid = new Grid();
            footerTextGrid.SetClass("ControlBase NpcMarketFooterText");
            footerTextGrid.BackgroundTexture = null;
            footerTextGrid.SetColumns(1f);
            footerTextGrid.SetRows(1f, 1f);
            _footerUpdatedText = new TextBlock(default(RectangleF))
            {
                FontScale = 0.63f,
                HorizontalAlignment = TextAlignment.LEFT,
                VerticalAlignment = TextBlockVerticalAlignment.Center
            };
            _footerNextTickText = new TextBlock(default(RectangleF))
            {
                FontScale = 0.63f,
                HorizontalAlignment = TextAlignment.LEFT,
                VerticalAlignment = TextBlockVerticalAlignment.Center
            };
            footerTextGrid.Set(_footerUpdatedText, 0, 0);
            footerTextGrid.Set(_footerNextTickText, 0, 1);
            _footerGrid.Set(footerTextGrid, 0, 0, 1, 3);
            _footerGrid.Set(_modeComboBox, 1, 1);
            _footerGrid.Set(_refreshButton, 3, 1);
            EnsureSortHeaderButtons();
            NpcMarketClientCache.Updated += HandleUpdated;
        }

        public override void Close()
        {
            NpcMarketClientCache.Updated -= HandleUpdated;
        }

        public override void Update()
        {
            var key = CacheKey;
            if (IsLocallyAccessDenied())
            {
                NpcMarketClientCache.MarkAccessDenied(key);
                RefreshRows();
                return;
            }

            NpcMarketClientCache.EnsureRequested(key);
            NpcMarketClientCache.Update(key);
            RefreshRows();
        }

        public override void LayoutChanged()
        {
            RefreshRows();
        }

        public override List<MySprite> GetSprites()
        {
            ClearInteractiveTree();
            var sprites = new List<MySprite>();
            var view = Host.ViewBox;
            var scale = GetLayoutScale();
            var fontScale = Host.Surface.FontSize;
            var textScale = 0.72f * scale * fontScale;
            var muted = ResolveResource(ThemeResources.MutedTextColor, new Color(Host.ForegroundColor, 0.68f));
            var footerHeight = GetFooterHeight(textScale);

            var key = CacheKey;
            var snapshot = NpcMarketClientCache.GetSnapshot(key);
            var headerTop = view.Y + (Host.TitleVisible ? 48f * GeneralComponent.GetScale() * fontScale : 12f * scale);
            
            if (IsLocallyAccessDenied() || (snapshot != null && IsAccessDenied(snapshot)))
            {
                DrawNativeMessage(sprites, MyTexts.GetString("AccessDenied"), "Lock",
                    ResolveResource(ThemeResources.ErrorColor, ColorComponent.ResolveErrorColor()));
            }
            else if (snapshot == null)
            {
                Host.DrawLoading(sprites, GeneralComponent.GetScale());
            }
            else if (ShouldShowNoTraderWarning(snapshot))
            {
                DrawNativeMessage(sprites, MyTexts.GetString(LOC_NO_TRADER));
            }
            else if (_rows.Count == 0 && string.IsNullOrWhiteSpace(_searchQuery))
            {
                DrawNativeMessage(sprites, MyTexts.GetString(LOC_EMPTY));
            }
            else
            {
                var headerHeight = 28f * scale;
                var searchHeight = string.IsNullOrWhiteSpace(_searchQuery) ? 0f : 34f * scale;
                var rowTop = headerTop + searchHeight;
                var rowHeight = 34f * scale;
                var contentRight = view.Right;
                if (searchHeight > 0f)
                    DrawSearchInput(sprites, headerTop + 2f * scale, searchHeight - 4f * scale,
                        contentRight);

                if (_rows.Count > 0)
                {
                    ConfigurePagesPanel(rowTop, footerHeight, headerHeight, rowHeight, textScale, muted);
                    _pagesPanel.Render(sprites);
                }
            }

            DrawFooter(sprites, textScale, muted);
            ClearDirtyAfterRender();
            return sprites;
        }

        public override bool HasVisibleItems()
        {
            return NpcMarketClientCache.GetSnapshot(CacheKey) != null;
        }

        void IApp.OnMouseScroll(int delta, ref bool handled)
        {
            if (handled)
                return;

            if (!_pagesPanel.Visible || !_pagesPanel.CanNavigate)
                return;

            _pagesPanel.FirstVisiblePage += delta > 0 ? -1 : 1;
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
            var localPlayer = MyAPIGateway.Session != null ? MyAPIGateway.Session.LocalHumanPlayer : null;
            return terminalBlock != null &&
                   localPlayer != null &&
                   !terminalBlock.HasPlayerAccess(localPlayer.IdentityId);
        }

        void RefreshRows()
        {
            _rows.Clear();
            var snapshot = NpcMarketClientCache.GetSnapshot(CacheKey);
            if (snapshot == null)
                return;

            _aggregation = _aggregator.Build(snapshot, Host.Surface, _mode, _sortColumn, _sortDescending,
                GetMaxDistanceMeters());
            foreach (var t in _aggregation.Rows)
            {
                if (MatchesSearch(t))
                    _rows.Add(t);
            }
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

            var createGps = MyAPIGateway.Input != null && MyAPIGateway.Input.IsAnyCtrlKeyPressed();

            if (createGps)
            {
                CreateTemporaryGps(GetQuoteForMode(group.Summary, target.Mode), group.DisplayName);
                return;
            }

            var interactiveHost = Host as InteractiveSurfaceScript;
            if (interactiveHost != null)
                interactiveHost.ShowDialog(new NpcMarketItemDialog(this, group.ItemKey, target.Mode));
        }

        internal void CreateTemporaryGps(NpcMarketStationQuote quote, string itemName)
        {
            var session = MyAPIGateway.Session;
            var gpsCollection = session?.GPS;
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
            if (session == null || snapshot == null || snapshot.NextEconomyTickWorldElapsedTicks <= 0)
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

        void ClearInteractiveTree()
        {
            _children.Clear();
            _pagesPanel.SetVisible(false);
            foreach (var t in _sortHeaderButtons)
                t.SetVisible(false);

            _searchButton.SetVisible(false);
            _searchInput.SetVisible(false);
            _clearSearchButton.SetVisible(false);
            _footerGrid.SetVisible(false);
            _refreshButton.SetVisible(false);
            _modeComboBox.SetVisible(false);
        }

        void EnsureSortHeaderButtons()
        {
            if (_sortHeaderButtons.Count > 0)
                return;

            AddSortHeaderButton(NpcMarketSortColumn.Name, LOC_COLUMN_NAME);
            AddSortHeaderButton(NpcMarketSortColumn.Price, LOC_COLUMN_PRICE);
            AddSortHeaderButton(NpcMarketSortColumn.Trend, LOC_COLUMN_TREND);
            AddSortHeaderButton(NpcMarketSortColumn.BuyPrice, null);
            AddSortHeaderButton(NpcMarketSortColumn.SellPrice, null);
            AddSortHeaderButton(NpcMarketSortColumn.BuyTrend, null);
            AddSortHeaderButton(NpcMarketSortColumn.SellTrend, null);
        }

        void AddSortHeaderButton(NpcMarketSortColumn column, string localizationKey)
        {
            var button = AddChild(new Button(default(RectangleF), new SortHeaderButtonModel
            {
                Column = column,
                LocalizationKey = localizationKey,
                Clicked = OnSortHeaderClicked
            }));
            button.CustomRender = RenderSortHeaderButton;
            button.SetClass("ControlBase Button Sort");
            button.SetVisible(false);
            _sortHeaderButtons.Add(button);
        }

        void DrawSearchInput(List<MySprite> sprites, float top, float height, float contentRight)
        {
            var scale = GetLayoutScale();
            var rect = new RectangleF(
                Host.ViewBox.X + 12f * scale,
                top,
                Math.Max(0f, contentRight - Host.ViewBox.X - 12f * scale),
                height);
            _searchInputModel.Title = MyTexts.GetString(LOC_SEARCH);
            _searchInputModel.Placeholder = MyTexts.GetString(LOC_SEARCH);
            _searchInputModel.Value = _searchQuery;
            _searchInputModel.Enabled = true;
            _searchInputModel.ValueChanged = OnSearchChanged;
            _searchInput.SetRect(rect);
            _searchInput.SetStyleId("Primary");
            _searchInput.SetVisible(rect.Width > 0f && rect.Height > 0f);
            if (!_children.Contains(_searchInput))
                _children.Add(_searchInput);
            _searchInput.Render(sprites);

            var clearSize = Math.Min(rect.Height, 32f * scale);
            _clearSearchButton.SetRect(new RectangleF(rect.Right - clearSize, rect.Y, clearSize, rect.Height));
            _clearSearchButton.SetStyleId("Primary");
            _clearSearchButton.SetVisible(true);
            if (!_children.Contains(_clearSearchButton))
                _children.Add(_clearSearchButton);
            _clearSearchButton.Render(sprites);
        }

        void ConfigurePagesPanel(float contentTop, float footerHeight, float headerHeight, float rowHeight, float textScale, Color muted)
        {
            if (_rows.Count <= 0)
                return;

            var bounds = new RectangleF(
                Host.ViewBox.X,
                contentTop,
                Host.ViewBox.Width,
                Math.Max(0f, Host.ViewBox.Bottom - contentTop - Math.Max(0f, footerHeight)));

            _listStripPanel.Rows = _rows;
            _listStripPanel.Mode = _mode;
            _listStripPanel.SortColumn = _sortColumn;
            _listStripPanel.SortDescending = _sortDescending;
            var scale = GetLayoutScale();
            _listStripPanel.LogicalMinimumListWidth = (_mode == NpcMarketMode.Both ? BOTH_LIST_MIN_WIDTH : SINGLE_SIDE_LIST_MIN_WIDTH) * scale;
            _listStripPanel.HorizontalGap = 12f * scale;
            _listStripPanel.RepeatedHeaderHeight = headerHeight;
            _listStripPanel.RowHeight = rowHeight;
            _listStripPanel.TextScale = textScale;
            _listStripPanel.LayoutScale = scale;
            _listStripPanel.MutedColor = muted;
            _pagesPanel.LayoutScale = scale;
            _pagesPanel.PageProvider = viewport => _listStripPanel.ConfigurePages(_pagesPanel, viewport);
            RestorePageIndex();
            _pagesPanel.SetRect(bounds);
            AdvancePageFromTimer();
            SavePageIndex();
            _pagesPanel.SetVisible(_rows.Count > 0);
            if (_rows.Count > 0 && !_children.Contains(_pagesPanel))
                _children.Add(_pagesPanel);
        }

        void DrawFooter(List<MySprite> sprites, float textScale, Color muted)
        {
            GetLayoutScale();
            var buttonText = MyTexts.GetString(LOC_REFRESH);
            var buttonSize = GetRefreshButtonSize(buttonText, textScale);
            var modeSize = GetModeButtonSize(textScale);
            var key = CacheKey;
            var enabled = NpcMarketClientCache.CanForceRefresh(key);
            var snapshot = NpcMarketClientCache.GetSnapshot(key);
            var modeEnabled = !IsLocallyAccessDenied() && !IsAccessDenied(snapshot);

            ConfigureFooterGrid(buttonSize, modeSize);

            ConfigureRefreshButton(enabled, buttonText);
            _modeComboBox.SetOptions(GetAvailableMarketModes());
            _modeComboBox.SetSelectedValue(_mode);
            _modeComboBox.SetEnabled(modeEnabled);
            _modeComboBox.SetStyleId(modeEnabled ? "Primary" : "Disabled");
            _modeComboBox.SetVisible(true);

            var now = WorldTime.NowElapsedTicks();
            _footerUpdatedText.Text = snapshot == null ? string.Empty : string.Format(MyTexts.GetString(LOC_UPDATED),
                FormatDuration(now - snapshot.CacheBuiltAtWorldElapsedTicks));
            _footerNextTickText.Text = snapshot == null ? string.Empty : MyTexts.GetString(LOC_NEXT_RESTOCK) + " " +
                FormatDuration(snapshot.NextEconomyTickWorldElapsedTicks - now);
            _footerUpdatedText.TextColor = muted;
            _footerNextTickText.TextColor = muted;

            _footerGrid.SetVisible(true);
            if (!_children.Contains(_footerGrid))
                _children.Add(_footerGrid);

            _footerGrid.Render(sprites);
        }

        void ConfigureFooterGrid(Vector2 refreshButtonSize, Vector2 modeButtonSize)
        {
            var view = Host.ViewBox;
            var scale = GetLayoutScale();
            var footerHeight = GetFooterHeight(0f);
            var horizontalPadding = 14f * scale;
            var gap = 8f * scale;
            var footerBounds = new RectangleF(
                view.X + horizontalPadding,
                view.Bottom - footerHeight,
                Math.Max(0f, view.Width - horizontalPadding * 2f),
                footerHeight);
            var topPadding = Math.Max(0f, footerHeight - refreshButtonSize.Y - 10f * scale);
            var bottomPadding = Math.Max(0f, footerHeight - topPadding - refreshButtonSize.Y);
            var controlsWidth = modeButtonSize.X + gap + refreshButtonSize.X;
            var textWidth = Math.Max(1f, footerBounds.Width - controlsWidth);
            var buttonHeight = Math.Max(1f, refreshButtonSize.Y);

            _footerGrid.SetColumns(
                textWidth,
                Math.Max(1f, modeButtonSize.X),
                Math.Max(1f, gap),
                Math.Max(1f, refreshButtonSize.X));
            _footerGrid.SetRows(
                Math.Max(1f, topPadding),
                buttonHeight,
                Math.Max(1f, bottomPadding));
            _footerGrid.SetRect(footerBounds);
        }

        float GetFooterHeight(float textScale)
        {
            var buttonSize = GetRefreshButtonSize(MyTexts.GetString(LOC_REFRESH), textScale);
            return Math.Max(58f * GetLayoutScale(), buttonSize.Y + 20f * GetLayoutScale());
        }

        void DrawNativeMessage(List<MySprite> sprites, string message, string icon = "Warning", Color? color = null)
        {
            color = color ?? ResolveResource(ThemeResources.WarningColor, ColorComponent.ResolveWarningColor());
            Host.DrawMessage(
                sprites,
                message,
                icon,
                color.Value,
                GeneralComponent.GetScale());
        }

        void ConfigureRefreshButton(bool enabled, string text)
        {
            var model = _refreshButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = text;
                model.Enabled = enabled;
            }

            _refreshButton.SetVisible(true);
            _refreshButton.SetCursor(enabled ? CursorType.Hand : CursorType.Default);
            _refreshButton.SetStyleId(enabled ? "Primary" : "Disabled");
            _refreshButton.SetEnabled(enabled);
        }

        void RenderSearchButton(ControlTemplate control, List<MySprite> sprites)
        {
            sprites.Add(new MySprite(SpriteType.TEXTURE, "Search")
            {
                Position = control.Bounds.Center,
                Size = new Vector2(18f * control.LayoutScale),
                Color = control.TextColor
            });
        }

        void RenderSearchInput(ControlTemplate control, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var hovered = control.IsPointerOver;
            var innerPadding = Math.Max(2f, Math.Min(rect.Width, rect.Height) * 0.08f);
            var innerRect = Inset(rect, innerPadding);
            var textColor = control.TextColor;
            var textScale = 0.58f * control.LayoutScale * control.FontScale;
            var horizontalPadding = 10f * control.LayoutScale;
            var iconSize = 16f * control.LayoutScale;
            var iconCenter = new Vector2(innerRect.X + horizontalPadding + iconSize * 0.5f, innerRect.Center.Y);
            var textLeft = iconCenter.X + iconSize * 0.5f + horizontalPadding;
            var clearSpace = 28f * control.LayoutScale;
            var availableTextWidth = Math.Max(0f,
                innerRect.Right - clearSpace - horizontalPadding - textLeft);
            var text = Trim(_searchInputModel.ToString(), availableTextWidth, textScale);
            var textSize = FormatingHelper.GetSizeInPixel(text, control, textScale, control.TextSurface);

            var button = control as Button;
            var outerColor = button?.BackgroundColor ?? control.BackgroundColor;
            BorderRenderer.CreateSpritesFromRect(rect, sprites, outerColor,
                radiusScale: control.LayoutScale);
            BorderRenderer.CreateSpritesFromRect(innerRect, sprites,
                hovered
                    ? control.GetResourceColor(ThemeResources.AccentColor, outerColor)
                    : control.GetResourceColor(ThemeResources.SurfaceColor, outerColor),
                radiusScale: control.LayoutScale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(textLeft, innerRect.Center.Y - textSize.Y * 0.5f),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = TextAlignment.LEFT,
                FontId = control.TextFont
            });
            sprites.Add(new MySprite(SpriteType.TEXTURE, "Search")
            {
                Position = iconCenter,
                Size = new Vector2(iconSize),
                Color = textColor
            });
        }

        void RenderClearSearchButton(ControlTemplate control, List<MySprite> sprites)
        {
            var color = control.TextColor;

            var length = 11f * control.LayoutScale;
            var thickness = Math.Max(1f, 1.5f * control.LayoutScale);
            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
            {
                Position = control.Bounds.Center,
                Size = new Vector2(thickness, length),
                RotationOrScale = MathHelper.PiOver4,
                Color = color
            });
            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
            {
                Position = control.Bounds.Center,
                Size = new Vector2(thickness, length),
                RotationOrScale = -MathHelper.PiOver4,
                Color = color
            });
        }

        static RectangleF Inset(RectangleF rect, float amount)
        {
            return new RectangleF(
                rect.X + amount,
                rect.Y + amount,
                Math.Max(0f, rect.Width - amount * 2f),
                Math.Max(0f, rect.Height - amount * 2f));
        }

        void RenderSortHeaderButton(ControlTemplate control, List<MySprite> sprites)
        {
            var model = control.DataContext as SortHeaderButtonModel;
            if (model == null)
                return;

            var rect = control.Bounds;
            var sortAscending = control.HasStyleClass("SortAscending");
            var sortDescending = control.HasStyleClass("SortDescending");
            var active = sortAscending || sortDescending;
            var hovered = control.IsPointerOver;
            var textScale = 0.58f * control.LayoutScale * control.FontScale;
            var availableTextWidth = GetSortHeaderAvailableWidth(model.Column, rect, control.LayoutScale, active);
            var text = Trim(GetSortHeaderLabel(model), availableTextWidth, textScale);
            var textSize = FormatingHelper.GetSizeInPixel(text, control, textScale, control.TextSurface);
            var textY = rect.Center.Y - textSize.Y * 0.5f;
            var textX = GetSortHeaderTextX(model.Column, rect, control.LayoutScale);
            var alignment = model.Column == NpcMarketSortColumn.Name ? TextAlignment.LEFT : TextAlignment.RIGHT;
            var textColor = active || hovered
                ? control.GetResourceColor(ThemeResources.AccentColor, control.TextColor)
                : control.TextColor;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(textX, textY),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = alignment,
                FontId = control.TextFont
            });
            if (active)
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = text,
                    Position = new Vector2(textX + 0.7f * control.LayoutScale, textY),
                    RotationOrScale = textScale,
                    Color = textColor,
                    Alignment = alignment,
                    FontId = control.TextFont
                });
            }

            if (!active)
                return;

            var triangleX = alignment == TextAlignment.LEFT
                ? textX + textSize.X + 8f * control.LayoutScale
                : textX - textSize.X - 8f * control.LayoutScale;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Triangle",
                Position = new Vector2(triangleX, rect.Center.Y),
                Size = new Vector2(8f * control.LayoutScale, 6f * control.LayoutScale),
                RotationOrScale = sortDescending ? MathHelper.Pi : 0f,
                Color = textColor,
                Alignment = TextAlignment.CENTER
            });
        }

        static float GetSortHeaderTextX(NpcMarketSortColumn column, RectangleF rect, float scale)
        {
            switch (column)
            {
                case NpcMarketSortColumn.Name:
                    return rect.X;
                case NpcMarketSortColumn.Price:
                    return rect.Right - 10f * scale;
                default:
                    return rect.Right - 14f * scale;
            }
        }

        static float GetSortHeaderAvailableWidth(NpcMarketSortColumn column, RectangleF rect, float scale, bool active)
        {
            var horizontalPadding = column == NpcMarketSortColumn.Name ? 0f : 14f * scale;
            var sortIndicatorWidth = active ? 18f * scale : 0f;
            return Math.Max(0f, rect.Width - horizontalPadding - sortIndicatorWidth);
        }

        static string GetSortHeaderLabel(SortHeaderButtonModel model)
        {
            if (model == null)
                return string.Empty;

            if (!string.IsNullOrEmpty(model.LocalizationKey))
                return MyTexts.GetString(model.LocalizationKey);

            switch (model.Column)
            {
                case NpcMarketSortColumn.BuyPrice:
                    return MyTexts.GetString("StoreScreenBuyHeader") + " " + MyTexts.GetString(LOC_COLUMN_PRICE);
                case NpcMarketSortColumn.SellPrice:
                    return MyTexts.GetString("StoreScreenSellHeader") + " " + MyTexts.GetString(LOC_COLUMN_PRICE);
                case NpcMarketSortColumn.BuyTrend:
                    return MyTexts.GetString("StoreScreenBuyHeader") + " " + MyTexts.GetString(LOC_COLUMN_TREND);
                case NpcMarketSortColumn.SellTrend:
                    return MyTexts.GetString("StoreScreenSellHeader") + " " + MyTexts.GetString(LOC_COLUMN_TREND);
                default:
                    return string.Empty;
            }
        }

        void RenderRefreshButton(ControlTemplate control, List<MySprite> sprites)
        {
            var model = control.DataContext as ButtonModel;
            var enabled = model == null || model.Enabled;
            var rect = control.Bounds;
            var hover = enabled && control.IsPointerOver;
            var button = control as Button;
            var defaultColor = button?.BackgroundColor ?? control.BackgroundColor;
            var color = hover
                ? control.GetResourceColor(ThemeResources.AccentColor, defaultColor)
                : defaultColor;
            var textColor = control.TextColor;
            var text = model == null || string.IsNullOrEmpty(model.Text) ? MyTexts.GetString(LOC_REFRESH) : model.Text;
            var textScale = 0.58f * control.LayoutScale * control.FontScale;

            BorderRenderer.CreateSpritesFromRect(rect, sprites, color, radiusScale: control.LayoutScale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X,
                    rect.Center.Y - FormatingHelper.GetSizeInPixel(text, control, textScale, control.TextSurface).Y * 0.5f),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = TextAlignment.CENTER,
                FontId = control.TextFont
            });
        }

        void OnRefreshClicked(ButtonModel model, object sender)
        {
            if (model == null || !model.Enabled)
                return;

            NpcMarketClientCache.RequestRefresh(CacheKey, true);
            Host.RenderSprites();
        }

        void OnSearchClicked(ButtonModel model, object sender)
        {
            OpenSearch(sender);
        }

        void OpenSearch()
        {
            OpenSearch(null);
        }

        void OpenSearch(object sender)
        {
            _searchInputModel.Title = MyTexts.GetString(LOC_SEARCH);
            _searchInputModel.Placeholder = MyTexts.GetString(LOC_SEARCH);
            _searchInputModel.Value = _searchQuery;
            _searchInputModel.ValueChanged = OnSearchChanged;
            _searchInputModel.Click(sender);
        }

        void OnSearchChanged(string value)
        {
            _searchQuery = value ?? string.Empty;
            NpcMarketComponent.SearchQuery = _searchQuery;
            ResetSavedPageIndex();
            RefreshRows();
            Host.RenderSprites();
        }

        void OnClearSearchClicked(ButtonModel model, object sender)
        {
            OnSearchChanged(string.Empty);
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
            RefreshRows();
            Host.RenderSprites();
        }

        void OnSortHeaderClicked(ButtonModel model, object sender)
        {
            var header = model as SortHeaderButtonModel;
            if (header == null)
                return;

            OnSortColumnClicked(header.Column);
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
            ResetSavedPageIndex();
            RefreshRows();
            Host.RenderSprites();
        }

        void HandleUpdated()
        {
            RefreshRows();
            Host.RenderSprites();
        }

        void OnPageChanged(int pageIndex)
        {
            SavePageIndex(pageIndex);
            Host.RenderSprites();
        }

        void RestorePageIndex()
        {
            if (_restoredPageIndex)
                return;

            _restoredPageIndex = true;
            _pagesPanel.FirstVisiblePage = Math.Max(0, (int)Math.Round(NpcMarketComponent.HorizontalScrollOffsetPixels));
            SavePageIndex();
        }

        void ResetSavedPageIndex()
        {
            NpcMarketComponent.ScrollOffsetPixels = 0f;
            NpcMarketComponent.HorizontalScrollOffsetPixels = 0f;
            NpcMarketComponent.VerticalScrollOffsetPixels = 0f;
            _restoredPageIndex = true;
            _lastAutoPageStep = -1;
            _pagesPanel.FirstVisiblePage = 0;
        }

        void SavePageIndex()
        {
            SavePageIndex(_pagesPanel.FirstVisiblePage);
        }

        void SavePageIndex(int pageIndex)
        {
            NpcMarketComponent.HorizontalScrollOffsetPixels = Math.Max(0, pageIndex);
            NpcMarketComponent.VerticalScrollOffsetPixels = 0f;
            NpcMarketComponent.ScrollOffsetPixels = 0f;
        }

        void AdvancePageFromTimer()
        {
            if (!_pagesPanel.CanNavigate)
            {
                _lastAutoPageStep = -1;
                return;
            }

            var seconds = SliderNpcMarketPageSwitchDelay.ClampSeconds(NpcMarketComponent.PageSwitchSeconds);
            if (seconds <= 0f)
            {
                _lastAutoPageStep = -1;
                return;
            }

            var session = MyAPIGateway.Session;
            if (session == null)
                return;

            var framesPerStep = Math.Max(1, (int)Math.Round(seconds * 60f));
            var step = session.GameplayFrameCounter / framesPerStep;
            if (_lastAutoPageStep < 0)
            {
                _lastAutoPageStep = step;
                return;
            }

            if (step == _lastAutoPageStep)
                return;

            _lastAutoPageStep = step;
            _pagesPanel.FirstVisiblePage += 1;
        }

        NpcMarketMode NormalizeConfiguredMode(int value)
        {
            return NormalizeMode(value);
        }

        NpcMarketMode NormalizeAvailableMode(NpcMarketMode mode)
        {
            return mode;
        }

        NpcMarketMode[] GetAvailableMarketModes()
        {
            return MarketModes;
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


        Vector2 GetRefreshButtonSize(string text, float textScale)
        {
            var textSize = FormatingHelper.GetSizeInPixel(text, TextFont, textScale, Host.Surface);
            var scale = GetLayoutScale();
            return new Vector2(Math.Max(96f * scale, textSize.X + 24f * scale),
                Math.Max(28f * scale, textSize.Y + 10f * scale));
        }

        Vector2 GetModeButtonSize(float textScale)
        {
            var buy = FormatingHelper.GetSizeInPixel(GetModeLabel(NpcMarketMode.Buy), TextFont, textScale, Host.Surface);
            var sell = FormatingHelper.GetSizeInPixel(GetModeLabel(NpcMarketMode.Sell), TextFont, textScale, Host.Surface);
            var both = FormatingHelper.GetSizeInPixel(GetModeLabel(NpcMarketMode.Both), TextFont, textScale, Host.Surface);
            var maxWidth = Math.Max(Math.Max(buy.X, sell.X), both.X);
            var maxHeight = Math.Max(Math.Max(buy.Y, sell.Y), both.Y);
            var scale = GetLayoutScale();
            return new Vector2(Math.Max(82f * scale, maxWidth + 34f * scale),
                Math.Max(28f * scale, maxHeight + 10f * scale));
        }

        float GetLayoutScale()
        {
            return GeneralComponent.GetScale();
        }

        static string GetModeLabel(NpcMarketMode mode)
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

        string Trim(string value, float width, float scale)
        {
            _text.Clear().Append(value ?? string.Empty);
            if (width <= 0f)
                return string.Empty;

            if (FormatingHelper.GetSizeInPixel(_text.ToString(), TextFont, scale, Host.Surface).X <= width)
                return _text.ToString();

            while (_text.Length > 0)
            {
                _text.Length--;
                var contentLength = _text.Length;
                _text.Append(FormatingHelper.ELLIPSIS);
                if (FormatingHelper.GetSizeInPixel(_text.ToString(), TextFont, scale, Host.Surface).X <= width)
                    return _text.ToString();

                _text.Length = contentLength;
            }

            return _text.ToString();
        }

        static string FormatDuration(long ticks)
        {
            var seconds = Math.Max(0, (int)Math.Ceiling(WorldTime.ToSeconds(ticks)));
            if (seconds < 60)
                return seconds + "s";

            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        sealed class SortHeaderButtonModel : ButtonModel
        {
            public NpcMarketSortColumn Column { get; set; }
            public string LocalizationKey { get; set; }
        }
    }
}
