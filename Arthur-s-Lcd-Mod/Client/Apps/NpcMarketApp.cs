using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Market;
using LcdMod.Client.Market.Gui;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using LcdMod.Common.Market;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using Sandbox.ModAPI;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Apps
{
    internal sealed class NpcMarketApp : App, IApp
    {
        public const string TITLE = "LcdMod_MarketApp";
        const float ITEM_NAME_MAX_WIDTH = 240f;
        const string LOC_REFRESH = "LcdMod_MarketApp_Refresh";
        const string LOC_EMPTY = "LcdMod_MarketApp_Empty";
        const string LOC_NO_TRADER = "LcdMod_MarketApp_NoTrader";
        const string LOC_UPDATED = "LcdMod_MarketApp_Updated";
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
        readonly Dictionary<string, Button> _rowButtonsByItemKey =
            new Dictionary<string, Button>(StringComparer.Ordinal);
        readonly TextInputModel _searchInputModel;
        readonly TextInput _searchInput;
        readonly Button _searchButton;
        readonly Button _clearSearchButton;
        Button _refreshButton;
        NpcMarketMode _mode;
        NpcMarketSortColumn _sortColumn = NpcMarketSortColumn.Price;
        bool _sortDescending;
        string _searchQuery = string.Empty;
        NpcMarketAggregationResult _aggregation = new NpcMarketAggregationResult();

        NpcMarketClientCacheKey CacheKey =>
            new NpcMarketClientCacheKey(
                Host.Block?.EntityId ?? 0L,
                AppConfig?.ScreenIndex ?? 0);

        public override IReadOnlyList<Control> Children => _children;
        internal NpcMarketMode Mode => _mode;
        internal IAppHost AppHost => Host;
        readonly ScreenConfigNpcMarket _config;
        bool _restoredPageIndex;
        int _lastAutoPageStep = -1;

        public NpcMarketApp(ScreenConfigNpcMarket config, IAppHost host) : base(config, host)
        {
            _config = config;
            _mode = NormalizeConfiguredMode(_config.SelectedMode);
            _config.SelectedMode = (int)_mode;
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
            _modeComboBox = AddChild(new ComboBox<NpcMarketMode>(MarketModes, GetModeLabel, OnModeChanged, Host.RenderSprites)
            {
                OpenDirection = ComboBoxOpenDirection.Up
            });
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
            var muted = new Color(Host.ForegroundColor, 0.68f);
            var footerHeight = GetFooterHeight(textScale);

            var key = CacheKey;
            var snapshot = NpcMarketClientCache.GetSnapshot(key);
            var headerTop = view.Y + (Host.TitleVisible ? 48f * AppConfig.Scale * fontScale : 12f * scale);
            
            if (IsLocallyAccessDenied() || (snapshot != null && IsAccessDenied(snapshot)))
            {
                DrawNativeMessage(sprites, MyTexts.GetString("AccessDenied"), "Lock", _config.ErrorColor);
            }
            else if (snapshot == null)
            {
                Host.DrawLoading(sprites, _config.Scale);
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
                    _pagesPanel.Render(CreateRenderContext(), sprites);
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
            for (var i = 0; i < _aggregation.Rows.Count; i++)
            {
                if (MatchesSearch(_aggregation.Rows[i]))
                    _rows.Add(_aggregation.Rows[i]);
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

        void ConfigureVisibleRowButtons(int start, int end, float rowHeight, float contentRight)
        {
            foreach (var button in _rowButtonsByItemKey.Values)
                button.SetVisible(false);

            for (var rowIndex = start; rowIndex < end; rowIndex++)
            {
                var row = _rows[rowIndex];
                var itemKey = row.ItemKey;
                if (string.IsNullOrEmpty(itemKey))
                    continue;

                var top = _pagesPanel.ContentViewportBounds.Y + (rowIndex - start) * rowHeight;
                if (_mode == NpcMarketMode.Both)
                {
                    var layout = GetBothLayout(contentRight, GetLayoutScale());
                    ConfigureRowButton(row, NpcMarketMode.Buy, WithRow(Union(layout.BuyPriceRect, layout.BuyTrendRect), top, rowHeight));
                    ConfigureRowButton(row, NpcMarketMode.Sell, WithRow(Union(layout.SellPriceRect, layout.SellTrendRect), top, rowHeight));
                    continue;
                }

                ConfigureRowButton(row, _mode, new RectangleF(
                    Host.ViewBox.X,
                    top,
                    Math.Max(0f, contentRight - Host.ViewBox.X),
                    rowHeight));
            }
        }

        void ConfigureRowButton(NpcMarketRow row, NpcMarketMode mode, RectangleF rect)
        {
            if (row == null || !HasQuoteForMode(row, mode) || rect.Width <= 0f || rect.Height <= 0f)
                return;

            var target = new NpcMarketRowClickTarget(row.ItemKey, mode);
            var key = target.Key;
            Button button;
            if (!_rowButtonsByItemKey.TryGetValue(key, out button))
            {
                button = new Button(rect, CursorType.Hand, target, OnMarketRowClicked);
                _rowButtonsByItemKey[key] = button;
                _pagesPanel.AddChild(button);
            }
            else
            {
                button.SetRect(rect);
                button.SetDataContext(target);
            }

            button.SetVisible(true);
        }

        void DrawRowHoverBackground(List<MySprite> sprites, NpcMarketRow row)
        {
            if (row == null || string.IsNullOrEmpty(row.ItemKey))
                return;

            foreach (var entry in _rowButtonsByItemKey)
            {
                var target = entry.Value.DataContext as NpcMarketRowClickTarget;
                if (target == null || !entry.Value.Visible || !entry.Value.IsPointerOver ||
                    !string.Equals(target.ItemKey, row.ItemKey, StringComparison.Ordinal))
                    continue;

                sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
                {
                    Position = entry.Value.Bounds.Center,
                    Size = entry.Value.Bounds.Size,
                    Color = new Color(Host.ForegroundColor, 0.10f)
                });
            }
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

        void OnMarketRowClicked(object dataContext, object sender)
        {
            OnMarketRowClicked(dataContext as NpcMarketRowClickTarget);
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
            var modeLabel = MyTexts.GetString(quote.StoreItemType == VRage.Game.ObjectBuilders.Definitions.StoreItemTypes.Offer
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

            return Contains(row.DisplayName, query) ||
                   Contains(row.GetSecondaryLabel(), query) ||
                   Contains(row.BestStationName, query) ||
                   Contains(row.BestSellerFactionTag, query);
        }

        double GetMaxDistanceMeters()
        {
            var value = _config != null
                ? _config.MaxDistanceMeters
                : SliderNpcMarketMaxDistance.UNLIMITED_DISTANCE_METERS;
            if (SliderNpcMarketMaxDistance.IsUnlimited(value))
                return double.PositiveInfinity;

            return SliderNpcMarketMaxDistance.ClampDistanceMeters(value);
        }

        static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static RectangleF WithRow(RectangleF rect, float y, float height)
        {
            return new RectangleF(rect.X, y, rect.Width, height);
        }

        static RectangleF Union(RectangleF left, RectangleF right)
        {
            var x = Math.Min(left.X, right.X);
            var r = Math.Max(left.Right, right.Right);
            return new RectangleF(x, 0f, Math.Max(0f, r - x), 1f);
        }

        static bool HasQuoteForMode(NpcMarketRow row, NpcMarketMode mode)
        {
            return GetQuoteForMode(row, mode) != null;
        }

        static NpcMarketStationQuote GetQuoteForMode(NpcMarketRow row, NpcMarketMode mode)
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

        BothLayout GetBothLayout(float contentRight, float scale)
        {
            var view = Host.ViewBox;
            var nameLeft = view.X + 48f * scale;
            var right = Math.Max(nameLeft, contentRight);
            var trendWidth = 82f * scale;
            var priceWidth = 124f * scale;
            var sellTrendLeft = right - trendWidth;
            var sellPriceLeft = sellTrendLeft - priceWidth;
            var buyTrendLeft = sellPriceLeft - trendWidth;
            var buyPriceLeft = buyTrendLeft - priceWidth;
            var minNameWidth = 72f * scale;
            buyPriceLeft = Math.Max(nameLeft + minNameWidth, buyPriceLeft);

            return new BothLayout
            {
                NameLeft = nameLeft,
                NameWidth = Math.Max(20f, Math.Min(ITEM_NAME_MAX_WIDTH * scale, buyPriceLeft - nameLeft - 12f * scale)),
                BuyPriceRect = new RectangleF(buyPriceLeft, 0f, Math.Max(0f, buyTrendLeft - buyPriceLeft), 1f),
                BuyTrendRect = new RectangleF(buyTrendLeft, 0f, Math.Max(0f, sellPriceLeft - buyTrendLeft), 1f),
                SellPriceRect = new RectangleF(sellPriceLeft, 0f, Math.Max(0f, sellTrendLeft - sellPriceLeft), 1f),
                SellTrendRect = new RectangleF(sellTrendLeft, 0f, Math.Max(0f, right - sellTrendLeft), 1f)
            };
        }

        void DrawRow(List<MySprite> sprites, NpcMarketRow row, float top, float rowHeight, float textScale, Color muted, float contentRight)
        {
            if (_mode == NpcMarketMode.Both)
            {
                DrawBothRow(sprites, row, top, rowHeight, textScale, muted, contentRight);
                return;
            }

            var view = Host.ViewBox;
            var scale = GetLayoutScale();
            var centerY = top + rowHeight * 0.5f;
            var iconSize = 24f * scale;
            var iconCenter = new Vector2(view.X + 28f * scale, centerY);

            sprites.Add(new MySprite(SpriteType.TEXTURE, row.SpriteName)
            {
                Position = iconCenter,
                Size = new Vector2(iconSize),
                Color = Color.White
            });

            var nameLeft = view.X + 48f * scale;
            var priceLeft = contentRight - 204f * scale;
            var priceRight = contentRight - 106f * scale;
            var deltaRight = contentRight - 14f * scale;
            var availableNameWidth = priceLeft - nameLeft - 12f * scale;
            var nameWidth = Math.Max(20f, Math.Min(ITEM_NAME_MAX_WIDTH * scale, availableNameWidth));
            var secondary = row.GetSecondaryLabel();
            var displayName = string.IsNullOrEmpty(secondary)
                ? row.DisplayName
                : row.DisplayName + " (" + secondary + ")";
            var name = Trim(displayName, nameWidth, textScale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = name,
                Position = new Vector2(nameLeft, centerY - 10f * scale),
                RotationOrScale = textScale,
                Color = Host.ForegroundColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = FormatingHelper.FormatSpaceCredits(row.PricePerUnit) + " SC",
                Position = new Vector2(priceRight, centerY - 10f * scale),
                RotationOrScale = textScale,
                Color = Host.ForegroundColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });

            DrawTrend(sprites, row.DeltaPercent, deltaRight, centerY, textScale, muted);
        }

        void DrawTrend(List<MySprite> sprites, float delta, float right, float centerY, float textScale, Color muted)
        {
            DrawTrendForMode(sprites, delta, _mode, right, centerY, textScale, muted);
        }

        void DrawTrendForMode(List<MySprite> sprites, float delta, NpcMarketMode mode, float right, float centerY, float textScale, Color muted)
        {
            var text = FormatDelta(delta);
            var color = GetDeltaColor(delta, mode, muted);
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, Host.Surface);
            var iconSize = Math.Max(8f * GetLayoutScale(), textSize.Y * 0.82f);
            var gap = 3f * GetLayoutScale();
            float rotation;
            var sprite = GetTrendSprite(delta, mode, out rotation);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = sprite,
                Position = new Vector2(right - textSize.X - gap - iconSize * 0.5f, centerY),
                Size = new Vector2(iconSize),
                RotationOrScale = rotation,
                Color = Color.White,
                Alignment = TextAlignment.CENTER
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(right, centerY - textSize.Y * 0.5f),
                RotationOrScale = textScale,
                Color = color,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });
        }

        void DrawBothRow(List<MySprite> sprites, NpcMarketRow row, float top, float rowHeight, float textScale, Color muted, float contentRight)
        {
            var view = Host.ViewBox;
            var scale = GetLayoutScale();
            var centerY = top + rowHeight * 0.5f;
            var iconSize = 24f * scale;
            var iconCenter = new Vector2(view.X + 28f * scale, centerY);
            var layout = GetBothLayout(contentRight, scale);

            sprites.Add(new MySprite(SpriteType.TEXTURE, row.SpriteName)
            {
                Position = iconCenter,
                Size = new Vector2(iconSize),
                Color = Color.White
            });

            var secondary = row.GetSecondaryLabel();
            var displayName = string.IsNullOrEmpty(secondary)
                ? row.DisplayName
                : row.DisplayName + " (" + secondary + ")";
            DrawText(sprites, Trim(displayName, layout.NameWidth, textScale), layout.NameLeft,
                centerY, textScale, TextAlignment.LEFT, Host.ForegroundColor);

            DrawOptionalPrice(sprites, row.BestBuyQuote, layout.BuyPriceRect.Right - 8f * scale, centerY, textScale);
            DrawOptionalTrend(sprites, row.BestBuyQuote, NpcMarketMode.Buy, layout.BuyTrendRect.Right - 10f * scale,
                centerY, textScale, muted);
            DrawOptionalPrice(sprites, row.BestSellQuote, layout.SellPriceRect.Right - 8f * scale, centerY, textScale);
            DrawOptionalTrend(sprites, row.BestSellQuote, NpcMarketMode.Sell, layout.SellTrendRect.Right - 10f * scale,
                centerY, textScale, muted);
        }

        void DrawOptionalPrice(List<MySprite> sprites, NpcMarketStationQuote quote, float right, float centerY, float textScale)
        {
            if (quote == null)
            {
                DrawSteadyPlaceholder(sprites, right - 18f * GetLayoutScale(), centerY, textScale);
                return;
            }

            DrawText(sprites, FormatingHelper.FormatSpaceCredits(quote.PersonalizedCurrentPricePerUnit) + " SC",
                right, centerY, textScale, TextAlignment.RIGHT, Host.ForegroundColor);
        }

        void DrawOptionalTrend(List<MySprite> sprites, NpcMarketStationQuote quote, NpcMarketMode mode, float right,
            float centerY, float textScale, Color muted)
        {
            if (quote == null)
            {
                DrawSteadyPlaceholder(sprites, right - 18f * GetLayoutScale(), centerY, textScale);
                return;
            }

            DrawTrendForMode(sprites, quote.EffectiveViewerChangePercent, mode, right, centerY, textScale, muted);
        }

        void DrawSteadyPlaceholder(List<MySprite> sprites, float centerX, float centerY, float textScale)
        {
            var size = Math.Max(10f * GetLayoutScale(), 14f * textScale);
            sprites.Add(new MySprite(SpriteType.TEXTURE, "Steady1")
            {
                Position = new Vector2(centerX, centerY),
                Size = new Vector2(size),
                Color = Color.White,
                Alignment = TextAlignment.CENTER
            });
        }

        void DrawText(List<MySprite> sprites, string text, float x, float centerY, float scale, TextAlignment alignment, Color color)
        {
            var size = FormatingHelper.GetSizeInPixel(text ?? string.Empty, "White", scale, Host.Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text ?? string.Empty,
                Position = new Vector2(x, centerY - size.Y * 0.5f),
                RotationOrScale = scale,
                Color = color,
                Alignment = alignment,
                FontId = "White"
            });
        }

        void ClearInteractiveTree()
        {
            _children.Clear();
            _pagesPanel.SetVisible(false);
            for (var i = 0; i < _sortHeaderButtons.Count; i++)
                _sortHeaderButtons[i].SetVisible(false);
            _searchButton.SetVisible(false);
            _searchInput.SetVisible(false);
            _clearSearchButton.SetVisible(false);
            if (_refreshButton != null)
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
            button.SetVisible(false);
            _sortHeaderButtons.Add(button);
        }

        void DrawTableHeader(List<MySprite> sprites, float top, float height, Color muted, float contentRight)
        {
            if (_mode == NpcMarketMode.Both)
            {
                DrawBothTableHeader(sprites, top, height, muted, contentRight);
                return;
            }

            var view = Host.ViewBox;
            var scale = GetLayoutScale();
            var trendLeft = contentRight - 96f * scale;
            var priceLeft = contentRight - 204f * scale;
            var left = view.X + 12f * scale;
            var nameLeft = view.X + 48f * scale;
            var right = Math.Max(left, contentRight);
            priceLeft = Math.Max(nameLeft, Math.Min(priceLeft, right));
            trendLeft = Math.Max(priceLeft, Math.Min(trendLeft, right));

            ConfigureSearchButton(new RectangleF(left, top, nameLeft - left, height));
            ConfigureSortHeaderButton(0, new RectangleF(nameLeft, top, priceLeft - nameLeft, height));
            ConfigureSortHeaderButton(1, new RectangleF(priceLeft, top, trendLeft - priceLeft, height));
            ConfigureSortHeaderButton(2, new RectangleF(trendLeft, top, right - trendLeft, height));

            if(string.IsNullOrWhiteSpace(_searchQuery))
                sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
                {
                    Position = new Vector2((left + right) * 0.5f, top + height - scale),
                    Size = new Vector2(right - left, scale),
                    Color = muted
                });

            for (var i = 0; i < _sortHeaderButtons.Count; i++)
                _sortHeaderButtons[i].Render(CreateButtonRenderContext(), sprites);
            _searchButton.Render(CreateButtonRenderContext(), sprites);
        }

        void DrawBothTableHeader(List<MySprite> sprites, float top, float height, Color muted, float contentRight)
        {
            var scale = GetLayoutScale();
            var layout = GetBothLayout(contentRight, scale);
            var left = Host.ViewBox.X + 12f * scale;
            var right = Math.Max(left, contentRight);

            ConfigureSearchButton(new RectangleF(left, top, layout.NameLeft - left, height));
            ConfigureSortHeaderButton(0, new RectangleF(layout.NameLeft, top, Math.Max(0f, layout.BuyPriceRect.X - layout.NameLeft), height));
            ConfigureSortHeaderButton(3, WithRow(layout.BuyPriceRect, top, height));
            ConfigureSortHeaderButton(5, WithRow(layout.BuyTrendRect, top, height));
            ConfigureSortHeaderButton(4, WithRow(layout.SellPriceRect, top, height));
            ConfigureSortHeaderButton(6, WithRow(layout.SellTrendRect, top, height));

            if (string.IsNullOrWhiteSpace(_searchQuery))
                sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
                {
                    Position = new Vector2((left + right) * 0.5f, top + height - scale),
                    Size = new Vector2(right - left, scale),
                    Color = muted
                });

            for (var i = 0; i < _sortHeaderButtons.Count; i++)
                _sortHeaderButtons[i].Render(CreateButtonRenderContext(), sprites);
            _searchButton.Render(CreateButtonRenderContext(), sprites);
        }

        void ConfigureSearchButton(RectangleF rect)
        {
            _searchButton.SetRect(rect);
            _searchButton.SetStyleId("Primary");
            _searchButton.SetVisible(rect.Width > 0f && rect.Height > 0f);
            if (!_children.Contains(_searchButton))
                _children.Add(_searchButton);
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
            _searchInput.Render(CreateButtonRenderContext(), sprites);

            var clearSize = Math.Min(rect.Height, 32f * scale);
            _clearSearchButton.SetRect(new RectangleF(rect.Right - clearSize, rect.Y, clearSize, rect.Height));
            _clearSearchButton.SetStyleId("Primary");
            _clearSearchButton.SetVisible(true);
            if (!_children.Contains(_clearSearchButton))
                _children.Add(_clearSearchButton);
            _clearSearchButton.Render(CreateButtonRenderContext(), sprites);
        }

        void ConfigureSortHeaderButton(int index, RectangleF rect)
        {
            if (index < 0 || index >= _sortHeaderButtons.Count)
                return;

            var button = _sortHeaderButtons[index];
            button.SetRect(rect);
            button.SetStyleId("NpcMarketSortHeader");
            button.SetVisible(rect.Width > 0f && rect.Height > 0f);
            if (!_children.Contains(button))
                _children.Add(button);
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
            var view = Host.ViewBox;
            var scale = GetLayoutScale();
            var buttonText = MyTexts.GetString(LOC_REFRESH);
            var buttonSize = GetRefreshButtonSize(buttonText, textScale);
            var rect = new RectangleF(
                view.Right - buttonSize.X - 14f * scale,
                view.Bottom - buttonSize.Y - 10f * scale,
                buttonSize.X,
                buttonSize.Y);
            var modeSize = GetModeButtonSize(textScale);
            var modeRect = new RectangleF(
                rect.X - modeSize.X - 8f * scale,
                rect.Y,
                modeSize.X,
                modeSize.Y);
            var key = CacheKey;
            var enabled = NpcMarketClientCache.CanForceRefresh(key);
            EnsureRefreshButton(rect, enabled, buttonText);
            var snapshot = NpcMarketClientCache.GetSnapshot(key);
            var modeEnabled = !IsLocallyAccessDenied() && !IsAccessDenied(snapshot);
            _modeComboBox.SetOptions(GetAvailableMarketModes());
            _modeComboBox.Configure(modeRect, scale, null);
            _modeComboBox.SetSelectedValue(_mode);
            _modeComboBox.SetEnabled(modeEnabled);
            _modeComboBox.SetStyleId(modeEnabled ? "Primary" : "Disabled");
            if (!_children.Contains(_refreshButton))
                _children.Add(_refreshButton);
            if (!_children.Contains(_modeComboBox))
                _children.Add(_modeComboBox);

            var now = WorldTime.NowElapsedTicks();
            var updated = snapshot == null ? string.Empty : string.Format(MyTexts.GetString(LOC_UPDATED),
                FormatDuration(now - snapshot.CacheBuiltAtWorldElapsedTicks));
            var nextTick = snapshot == null ? string.Empty : MyTexts.GetString(LOC_NEXT_RESTOCK) + " " +
                FormatDuration(snapshot.NextEconomyTickWorldElapsedTicks - now);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = updated,
                Position = new Vector2(view.X + 14f * scale, view.Bottom - 44f * scale),
                RotationOrScale = textScale * 0.88f,
                Color = muted,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = nextTick,
                Position = new Vector2(view.X + 14f * scale, view.Bottom - 22f * scale),
                RotationOrScale = textScale * 0.88f,
                Color = muted,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            _refreshButton.Render(CreateButtonRenderContext(), sprites);
            _modeComboBox.Render(CreateButtonRenderContext(), sprites);
        }

        float GetFooterHeight(float textScale)
        {
            var buttonSize = GetRefreshButtonSize(MyTexts.GetString(LOC_REFRESH), textScale);
            return Math.Max(58f * GetLayoutScale(), buttonSize.Y + 20f * GetLayoutScale());
        }

        Color GetDeltaColor(float delta, Color muted)
        {
            return GetDeltaColor(delta, _mode, muted);
        }

        Color GetDeltaColor(float delta, NpcMarketMode mode, Color muted)
        {
            if (Math.Abs(delta) < 0.05f)
                return muted;

            var favorable = mode == NpcMarketMode.Buy ? delta < 0f : delta > 0f;
            return favorable ? Color.LightGreen : Color.IndianRed;
        }

        static string GetTrendSprite(float delta, NpcMarketMode mode, out float rotation)
        {
            rotation = mode == NpcMarketMode.Sell ? MathHelper.Pi : 0f;
            if (Math.Abs(delta) < 0.05f)
            {
                rotation = 0f;
                return mode == NpcMarketMode.Buy ? "Steady1" : "Steady2";
            }

            var favorable = mode == NpcMarketMode.Buy ? delta < 0f : delta > 0f;
            return favorable ? "ArrowGreenDown" : "ArrowRedUp";
        }

        void DrawNativeMessage(List<MySprite> sprites, string message, string icon = "Warning", Color? color = null)
        {
            color = color ?? _config.WarningColor;
            Host.DrawMessage(
                sprites,
                message,
                icon,
                color.Value,
                AppConfig.Scale);
        }

        void EnsureRefreshButton(RectangleF rect, bool enabled, string text)
        {
            if (_refreshButton == null)
                _refreshButton = AddChild(new Button(rect, new ButtonModel { Text = text, Clicked = OnRefreshClicked }));
            else
                _refreshButton.SetRect(rect);

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
            _refreshButton.CustomRender = RenderRefreshButton;
        }

        void RenderSearchButton(ControlTemplate control, ControlRenderContext context, List<MySprite> sprites)
        {
            var hovered = control.IsPointerOver;
            sprites.Add(new MySprite(SpriteType.TEXTURE, "Search")
            {
                Position = control.Bounds.Center,
                Size = new Vector2(18f * context.Scale),
                Color = control.TextColor
            });
        }

        void RenderSearchInput(ControlTemplate control, ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var hovered = control.IsPointerOver;
            var innerPadding = Math.Max(2f, Math.Min(rect.Width, rect.Height) * 0.08f);
            var innerRect = Inset(rect, innerPadding);
            var textColor = control.TextColor;
            var textScale = 0.58f * context.Scale * context.FontScale;
            var horizontalPadding = 10f * context.Scale;
            var iconSize = 16f * context.Scale;
            var iconCenter = new Vector2(innerRect.X + horizontalPadding + iconSize * 0.5f, innerRect.Center.Y);
            var textLeft = iconCenter.X + iconSize * 0.5f + horizontalPadding;
            var clearSpace = 28f * context.Scale;
            var availableTextWidth = Math.Max(0f,
                innerRect.Right - clearSpace - horizontalPadding - textLeft);
            var text = Trim(_searchInputModel.ToString(), availableTextWidth, textScale);
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, context.Surface);

            var button = control as Button;
            var outerColor = button != null ? button.BackgroundColor : control.BackgroundColor;
            Border.CreateSpritesFromRect(rect, sprites, outerColor,
                radiusScale: context.Scale);
            Border.CreateSpritesFromRect(innerRect, sprites,
                hovered
                    ? control.GetResourceColor(ThemeResources.AccentColor, outerColor)
                    : control.GetResourceColor(ThemeResources.SurfaceColor, outerColor),
                radiusScale: context.Scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(textLeft, innerRect.Center.Y - textSize.Y * 0.5f),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });
            sprites.Add(new MySprite(SpriteType.TEXTURE, "Search")
            {
                Position = iconCenter,
                Size = new Vector2(iconSize),
                Color = textColor
            });
        }

        void RenderClearSearchButton(ControlTemplate control, ControlRenderContext context, List<MySprite> sprites)
        {
            var hovered = control.IsPointerOver;
            var color = control.TextColor;

            var length = 11f * context.Scale;
            var thickness = Math.Max(1f, 1.5f * context.Scale);
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

        void RenderSortHeaderButton(ControlTemplate control, ControlRenderContext context, List<MySprite> sprites)
        {
            var model = control.DataContext as SortHeaderButtonModel;
            if (model == null)
                return;

            var rect = control.Bounds;
            var active = model.Column == _sortColumn;
            var hovered = control.IsPointerOver;
            var textScale = 0.58f * context.Scale * context.FontScale;
            var availableTextWidth = GetSortHeaderAvailableWidth(model.Column, rect, context.Scale, active);
            var text = Trim(GetSortHeaderLabel(model), availableTextWidth, textScale);
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, context.Surface);
            var textY = rect.Center.Y - textSize.Y * 0.5f;
            var textX = GetSortHeaderTextX(model.Column, rect, context.Scale);
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
                FontId = "White"
            });
            if (active)
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = text,
                    Position = new Vector2(textX + 0.7f * context.Scale, textY),
                    RotationOrScale = textScale,
                    Color = textColor,
                    Alignment = alignment,
                    FontId = "White"
                });
            }

            if (!active)
                return;

            var triangleX = alignment == TextAlignment.LEFT
                ? textX + textSize.X + 8f * context.Scale
                : textX - textSize.X - 8f * context.Scale;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Triangle",
                Position = new Vector2(triangleX, rect.Center.Y),
                Size = new Vector2(8f * context.Scale, 6f * context.Scale),
                RotationOrScale = _sortDescending ? MathHelper.Pi : 0f,
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

        void RenderRefreshButton(ControlTemplate control, ControlRenderContext context, List<MySprite> sprites)
        {
            var model = control.DataContext as ButtonModel;
            var enabled = model == null || model.Enabled;
            var rect = control.Bounds;
            var hover = enabled && control.IsPointerOver;
            var button = control as Button;
            var defaultColor = button != null ? button.BackgroundColor : control.BackgroundColor;
            var color = hover
                ? control.GetResourceColor(ThemeResources.AccentColor, defaultColor)
                : defaultColor;
            var textColor = control.TextColor;
            var text = model == null || string.IsNullOrEmpty(model.Text) ? MyTexts.GetString(LOC_REFRESH) : model.Text;
            var textScale = 0.58f * context.Scale * context.FontScale;

            Border.CreateSpritesFromRect(rect, sprites, color, radiusScale: context.Scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X,
                    rect.Center.Y - FormatingHelper.GetSizeInPixel(text, "White", textScale, context.Surface).Y * 0.5f),
                RotationOrScale = textScale,
                Color = textColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
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
            _config.SelectedMode = (int)_mode;
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
            _pagesPanel.FirstVisiblePage = Math.Max(0, (int)Math.Round(_config.HorizontalScrollOffsetPixels));
            SavePageIndex();
        }

        void ResetSavedPageIndex()
        {
            _config.ScrollOffsetPixels = 0f;
            _config.HorizontalScrollOffsetPixels = 0f;
            _config.VerticalScrollOffsetPixels = 0f;
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
            _config.HorizontalScrollOffsetPixels = Math.Max(0, pageIndex);
            _config.VerticalScrollOffsetPixels = 0f;
            _config.ScrollOffsetPixels = 0f;
        }

        void AdvancePageFromTimer()
        {
            if (!_pagesPanel.CanNavigate)
            {
                _lastAutoPageStep = -1;
                return;
            }

            var seconds = SliderNpcMarketPageSwitchDelay.ClampSeconds(_config.PageSwitchSeconds);
            if (seconds <= 0f)
            {
                _lastAutoPageStep = -1;
                return;
            }

            var session = MyAPIGateway.Session;
            if (session == null)
                return;

            var framesPerStep = Math.Max(1, (int)Math.Round(seconds * 60f));
            var step = (int)(session.GameplayFrameCounter / framesPerStep);
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
                _sortColumn = NormalizeSortColumn(_config.SellSortColumn, mode);
                _sortDescending = _config.SellSortDescending;
                _config.SellSortColumn = (int)_sortColumn;
                return;
            }

            if (mode == NpcMarketMode.Both)
            {
                _sortColumn = NormalizeSortColumn(_config.BothSortColumn, mode);
                _sortDescending = _config.BothSortDescending;
                _config.BothSortColumn = (int)_sortColumn;
                return;
            }

            _sortColumn = NormalizeSortColumn(_config.BuySortColumn, mode);
            _sortDescending = _config.BuySortDescending;
            _config.BuySortColumn = (int)_sortColumn;
        }

        void SaveSortStateForMode(NpcMarketMode mode)
        {
            if (mode == NpcMarketMode.Sell)
            {
                _config.SellSortColumn = (int)_sortColumn;
                _config.SellSortDescending = _sortDescending;
                return;
            }

            if (mode == NpcMarketMode.Both)
            {
                _config.BothSortColumn = (int)_sortColumn;
                _config.BothSortDescending = _sortDescending;
                return;
            }

            _config.BuySortColumn = (int)_sortColumn;
            _config.BuySortDescending = _sortDescending;
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

        ControlRenderContext CreateRenderContext()
        {
            return CreateControlRenderContext(
                Host.Surface,
                GetLayoutScale(),
                Host.Surface.FontSize,
                new Vector2(float.NaN, float.NaN));
        }

        ControlRenderContext CreateButtonRenderContext()
        {
            return CreateControlRenderContext(Host.Surface, GetLayoutScale(), Host.Surface.FontSize, Vector2.Zero);
        }

        Vector2 GetRefreshButtonSize(string text, float textScale)
        {
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, Host.Surface);
            var scale = GetLayoutScale();
            return new Vector2(Math.Max(96f * scale, textSize.X + 24f * scale),
                Math.Max(28f * scale, textSize.Y + 10f * scale));
        }

        Vector2 GetModeButtonSize(float textScale)
        {
            var buy = FormatingHelper.GetSizeInPixel(GetModeLabel(NpcMarketMode.Buy), "White", textScale, Host.Surface);
            var sell = FormatingHelper.GetSizeInPixel(GetModeLabel(NpcMarketMode.Sell), "White", textScale, Host.Surface);
            var both = FormatingHelper.GetSizeInPixel(GetModeLabel(NpcMarketMode.Both), "White", textScale, Host.Surface);
            var maxWidth = Math.Max(Math.Max(buy.X, sell.X), both.X);
            var maxHeight = Math.Max(Math.Max(buy.Y, sell.Y), both.Y);
            var scale = GetLayoutScale();
            return new Vector2(Math.Max(82f * scale, maxWidth + 34f * scale),
                Math.Max(28f * scale, maxHeight + 10f * scale));
        }

        float GetLayoutScale()
        {
            return _config != null && _config.Scale > 0f ? _config.Scale : 1f;
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

            if (FormatingHelper.GetSizeInPixel(_text.ToString(), "White", scale, Host.Surface).X <= width)
                return _text.ToString();

            while (_text.Length > 0)
            {
                _text.Length--;
                var contentLength = _text.Length;
                _text.Append(FormatingHelper.ELLIPSIS);
                if (FormatingHelper.GetSizeInPixel(_text.ToString(), "White", scale, Host.Surface).X <= width)
                    return _text.ToString();

                _text.Length = contentLength;
            }

            return _text.ToString();
        }

        static string FormatDelta(float delta)
        {
            if (Math.Abs(delta) < 0.05f)
                return "0%";

            return (delta > 0f ? "+" : string.Empty) + delta.ToString("0.#") + "%";
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

        struct BothLayout
        {
            public float NameLeft;
            public float NameWidth;
            public RectangleF BuyPriceRect;
            public RectangleF SellPriceRect;
            public RectangleF BuyTrendRect;
            public RectangleF SellTrendRect;
        }

    }
}
