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
using LcdMod.Client.Helpers;
using LcdMod.Client.Market;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using LcdMod.Common.Market;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using Sandbox.ModAPI;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Apps
{
    internal sealed class NpcMarketApp : AppBase, IAppInteractive
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
        static readonly NpcMarketMode[] MarketModes = { NpcMarketMode.Buy, NpcMarketMode.Sell };

        readonly List<ControlBase> _interactiveList = new List<ControlBase>();
        readonly NpcMarketAggregator _aggregator = new NpcMarketAggregator();
        readonly List<NpcMarketRow> _rows = new List<NpcMarketRow>();
        readonly StringBuilder _text = new StringBuilder();
        readonly ScrollPanel _scrollPanel;
        readonly ComboBox<NpcMarketMode> _modeComboBox;
        readonly List<Button> _sortHeaderButtons = new List<Button>();
        readonly Dictionary<string, Button> _rowButtonsByItemKey =
            new Dictionary<string, Button>(StringComparer.Ordinal);
        readonly TextInputModel _searchInputModel;
        readonly TextInput _searchInput;
        readonly Button _searchButton;
        readonly Button _clearSearchButton;
        Button _refreshButton;
        ControlStyle _refreshButtonStyle;
        ControlStyle _disabledButtonStyle;
        ControlStyle _modeButtonStyle;
        ControlStyle _sortHeaderStyle;
        ControlStyle _searchInputStyle;
        NpcMarketMode _mode = NpcMarketMode.Buy;
        NpcMarketSortColumn _sortColumn = NpcMarketSortColumn.Price;
        bool _sortDescending;
        string _searchQuery = string.Empty;
        NpcMarketAggregationResult _aggregation = new NpcMarketAggregationResult();

        NpcMarketClientCacheKey CacheKey =>
            new NpcMarketClientCacheKey(
                Host.Block?.EntityId ?? 0L,
                AppConfig?.ScreenIndex ?? 0);

        public List<ControlBase> InteractiveList { get { return _interactiveList; } }
        internal NpcMarketMode Mode { get { return _mode; } }
        internal IAppHost AppHost { get { return Host; } }

        public NpcMarketApp(ScreenConfigInteractive config, IAppHost host) : base(config, host)
        {
            _scrollPanel = new ScrollPanel(CursorType.Default, this);
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _scrollPanel.SetVisible(false);
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
            _searchInput = new TextInput(default(RectangleF), _searchInputModel);
            _searchInput.CustomRender = RenderSearchInput;
            _searchInput.SetVisible(false);
            _searchButton = new Button(default(RectangleF), new ButtonModel { Clicked = OnSearchClicked });
            _searchButton.CustomRender = RenderSearchButton;
            _searchButton.SetVisible(false);
            _clearSearchButton = new Button(default(RectangleF), new ButtonModel { Clicked = OnClearSearchClicked });
            _clearSearchButton.CustomRender = RenderClearSearchButton;
            _clearSearchButton.SetVisible(false);
            EnsureSortHeaderButtons();
            NpcMarketClientCache.Updated += HandleUpdated;
        }

        public void Close()
        {
            NpcMarketClientCache.Updated -= HandleUpdated;
        }

        public override void Update()
        {
            var key = CacheKey;
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
            var scale = Host.Scale;
            var fontScale = Host.Surface.FontSize;
            var textScale = 0.72f * scale * fontScale;
            var muted = new Color(Host.ForegroundColor, 0.68f);
            var footerHeight = GetFooterHeight(textScale);

            var key = CacheKey;
            var snapshot = NpcMarketClientCache.GetSnapshot(key);
            var headerTop = view.Y + (Host.TitleVisible ? 48f * scale * fontScale : 12f * scale);
            if (snapshot == null)
            {
                Host.DrawLoading(sprites, AppConfig.Scale);
            }
            else if (ShouldShowNoTraderWarning(snapshot))
            {
                DrawNativeWarning(sprites, MyTexts.GetString(LOC_NO_TRADER));
            }
            else if (_rows.Count == 0 && string.IsNullOrWhiteSpace(_searchQuery))
            {
                DrawNativeWarning(sprites, MyTexts.GetString(LOC_EMPTY));
            }
            else
            {
                var headerHeight = 28f * scale;
                var searchHeight = string.IsNullOrWhiteSpace(_searchQuery) ? 0f : 34f * scale;
                var rowTop = headerTop + headerHeight + searchHeight;
                var rowHeight = 34f * scale;
                ConfigureScrollPanel(rowTop, footerHeight, rowHeight);
                DrawTableHeader(sprites, headerTop, headerHeight, muted,
                    _scrollPanel.ContentViewportBounds.Right);
                if (searchHeight > 0f)
                    DrawSearchInput(sprites, headerTop + headerHeight + 2f * scale, searchHeight - 4f * scale,
                        _scrollPanel.ContentViewportBounds.Right);
                BeginClip(sprites, _scrollPanel.ContentViewportBounds);
                var start = _scrollPanel.StartRow;
                var end = Math.Min(_rows.Count, start + _scrollPanel.RenderRows);
                ConfigureVisibleRowButtons(start, end, rowHeight, _scrollPanel.ContentViewportBounds.Right);
                for (var i = start; i < end; i++)
                {
                    var top = _scrollPanel.ContentBounds.Y + (i - start) * rowHeight;
                    DrawRowHoverBackground(sprites, _rows[i].ItemKey);
                    DrawRow(sprites, _rows[i], top, rowHeight, textScale, muted, _scrollPanel.ContentViewportBounds.Right);
                }
                sprites.Add(MySprite.CreateClearClipRect());
                _scrollPanel.Render(CreateRenderContext(), sprites);
            }

            DrawFooter(sprites, textScale, muted);
            return sprites;
        }

        public bool HasVisibleItems()
        {
            return NpcMarketClientCache.GetSnapshot(CacheKey) != null;
        }

        public void OnMouseScroll(int delta, ref bool handled)
        {
            if (handled)
                return;

            if (!_scrollPanel.Scroll(this, delta))
                return;

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
                case NpcMarketScopeMode.AccessDenied:
                    return true;
            }

            return snapshot.Scope.KnownStationCount <= 0;
        }

        void RefreshRows()
        {
            _rows.Clear();
            var snapshot = NpcMarketClientCache.GetSnapshot(CacheKey);
            if (snapshot == null)
                return;

            _aggregation = _aggregator.Build(snapshot, Host.Surface, _mode, _sortColumn, _sortDescending);
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
                var itemKey = _rows[rowIndex].ItemKey;
                if (string.IsNullOrEmpty(itemKey))
                    continue;

                var rect = new RectangleF(
                    Host.ViewBox.X,
                    _scrollPanel.ContentBounds.Y + (rowIndex - start) * rowHeight,
                    Math.Max(0f, contentRight - Host.ViewBox.X),
                    rowHeight);
                Button button;
                if (!_rowButtonsByItemKey.TryGetValue(itemKey, out button))
                {
                    button = new Button(rect, CursorType.Hand, itemKey, OnMarketRowClicked);
                    _rowButtonsByItemKey[itemKey] = button;
                    _scrollPanel.AddChild(button);
                }
                else
                {
                    button.SetRect(rect);
                }

                button.SetVisible(true);
            }
        }

        void DrawRowHoverBackground(List<MySprite> sprites, string itemKey)
        {
            Button button;
            if (string.IsNullOrEmpty(itemKey) || !_rowButtonsByItemKey.TryGetValue(itemKey, out button) ||
                !button.IsPointerOver)
                return;

            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
            {
                Position = button.Bounds.Center,
                Size = button.Bounds.Size,
                Color = new Color(Host.ForegroundColor, 0.10f)
            });
        }

        void OnMarketRowClicked(object dataContext, object sender)
        {
            var itemKey = dataContext as string;
            var group = GetItemGroup(itemKey);
            if (group == null)
                return;

            Button button;
            _rowButtonsByItemKey.TryGetValue(itemKey ?? string.Empty, out button);
            var createGps = MyAPIGateway.Input != null && MyAPIGateway.Input.IsAnyCtrlKeyPressed();
            if (button != null)
                button.ClickSound = createGps ? AudioHelper.HudGps3 : AudioHelper.HudClick;

            if (createGps)
            {
                CreateTemporaryGps(group.Summary?.BestQuote, group.DisplayName);
                return;
            }

            var interactiveHost = Host as InteractiveSurfaceScript;
            if (interactiveHost != null)
                interactiveHost.ShowDialog(new NpcMarketItemDialog(this, group.ItemKey));
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
            var modeLabel = MyTexts.GetString(_mode == NpcMarketMode.Buy
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

        static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void DrawRow(List<MySprite> sprites, NpcMarketRow row, float top, float rowHeight, float textScale, Color muted, float contentRight)
        {
            var view = Host.ViewBox;
            var scale = Host.Scale;
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
            var text = FormatDelta(delta);
            var color = GetDeltaColor(delta, muted);
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, Host.Surface);
            var iconSize = Math.Max(8f * Host.Scale, textSize.Y * 0.82f);
            var gap = 3f * Host.Scale;
            float rotation;
            var sprite = GetTrendSprite(delta, _mode, out rotation);

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

        void ClearInteractiveTree()
        {
            _interactiveList.Clear();
            _scrollPanel.SetVisible(false);
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
        }

        void AddSortHeaderButton(NpcMarketSortColumn column, string localizationKey)
        {
            var button = new Button(default(RectangleF), new SortHeaderButtonModel
            {
                Column = column,
                LocalizationKey = localizationKey,
                Clicked = OnSortHeaderClicked
            });
            button.CustomRender = RenderSortHeaderButton;
            button.SetVisible(false);
            _sortHeaderButtons.Add(button);
        }

        void DrawTableHeader(List<MySprite> sprites, float top, float height, Color muted, float contentRight)
        {
            var view = Host.ViewBox;
            var scale = Host.Scale;
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

        void ConfigureSearchButton(RectangleF rect)
        {
            _searchButton.SetRect(rect);
            _searchButton.SetStyle(GetSortHeaderStyle());
            _searchButton.SetVisible(rect.Width > 0f && rect.Height > 0f);
            if (!_interactiveList.Contains(_searchButton))
                _interactiveList.Add(_searchButton);
        }

        void DrawSearchInput(List<MySprite> sprites, float top, float height, float contentRight)
        {
            var scale = Host.Scale;
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
            _searchInput.SetStyle(GetSearchInputStyle());
            _searchInput.SetVisible(rect.Width > 0f && rect.Height > 0f);
            if (!_interactiveList.Contains(_searchInput))
                _interactiveList.Add(_searchInput);
            _searchInput.Render(CreateButtonRenderContext(), sprites);

            var clearSize = Math.Min(rect.Height, 32f * scale);
            _clearSearchButton.SetRect(new RectangleF(rect.Right - clearSize, rect.Y, clearSize, rect.Height));
            _clearSearchButton.SetStyle(GetSearchInputStyle());
            _clearSearchButton.SetVisible(true);
            if (!_interactiveList.Contains(_clearSearchButton))
                _interactiveList.Add(_clearSearchButton);
            _clearSearchButton.Render(CreateButtonRenderContext(), sprites);
        }

        void ConfigureSortHeaderButton(int index, RectangleF rect)
        {
            if (index < 0 || index >= _sortHeaderButtons.Count)
                return;

            var button = _sortHeaderButtons[index];
            button.SetRect(rect);
            button.SetStyle(GetSortHeaderStyle());
            button.SetVisible(rect.Width > 0f && rect.Height > 0f);
            if (!_interactiveList.Contains(button))
                _interactiveList.Add(button);
        }

        void ConfigureScrollPanel(float contentTop, float footerHeight, float rowHeight)
        {
            _scrollPanel.Configure(
                Host.ViewBox,
                contentTop,
                footerHeight,
                rowHeight,
                _rows.Count,
                ScrollPanel.DefaultScrollerWidthPixels * Host.Scale,
                0f);
            var colorable = AppConfig as ScreenConfigColorable;
            var thumbColor = colorable?.HeaderColor ?? Host.ForegroundColor;
            _scrollPanel.SetScrollBarColors(
                new Color(Host.Surface.ScriptForegroundColor.R, Host.Surface.ScriptForegroundColor.G, Host.Surface.ScriptForegroundColor.B, 127),
                new Color(thumbColor.R, thumbColor.G, thumbColor.B, 250));
            _scrollPanel.SetVisible(_rows.Count > 0);
            if (_rows.Count > 0 && !_interactiveList.Contains(_scrollPanel))
                _interactiveList.Add(_scrollPanel);
        }

        void DrawFooter(List<MySprite> sprites, float textScale, Color muted)
        {
            var view = Host.ViewBox;
            var scale = Host.Scale;
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
            _modeComboBox.Configure(modeRect, Host.Scale, GetModeButtonStyle());
            _modeComboBox.SetSelectedValue(_mode);
            if (!_interactiveList.Contains(_refreshButton))
                _interactiveList.Add(_refreshButton);
            if (!_interactiveList.Contains(_modeComboBox))
                _interactiveList.Add(_modeComboBox);

            var snapshot = NpcMarketClientCache.GetSnapshot(key);
            var now = WorldTime.NowElapsedTicks();
            var updated = snapshot == null ? string.Empty : string.Format(MyTexts.GetString(LOC_UPDATED),
                FormatDuration(now - snapshot.CacheBuiltAtWorldElapsedTicks));
            var nextTick = snapshot == null ? string.Empty : MyTexts.GetString(LOC_NEXT_RESTOCK) + " " +
                FormatDuration(snapshot.NextEconomyTickWorldElapsedTicks - now);
            var range = GetRangeText();

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
                Data = string.IsNullOrEmpty(range) ? nextTick : nextTick + " - " + range,
                Position = new Vector2(view.X + 14f * scale, view.Bottom - 22f * scale),
                RotationOrScale = textScale * 0.88f,
                Color = muted,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            _refreshButton.Render(CreateButtonRenderContext(), sprites);
            _modeComboBox.Render(CreateButtonRenderContext(), sprites);
        }

        string GetRangeText()
        {
            if (_rows.Count <= 0 || _scrollPanel.VisibleRows <= 0)
                return string.Empty;

            var start = Math.Min(_rows.Count, _scrollPanel.StartRow + 1);
            var end = Math.Min(_rows.Count, _scrollPanel.StartRow + _scrollPanel.VisibleRows);
            if (_rows.Count <= _scrollPanel.VisibleRows)
                return string.Empty;

            return start + "-" + end + " / " + _rows.Count;
        }

        float GetFooterHeight(float textScale)
        {
            var buttonSize = GetRefreshButtonSize(MyTexts.GetString(LOC_REFRESH), textScale);
            return Math.Max(58f * Host.Scale, buttonSize.Y + 20f * Host.Scale);
        }

        Color GetDeltaColor(float delta, Color muted)
        {
            if (Math.Abs(delta) < 0.05f)
                return muted;

            var favorable = _mode == NpcMarketMode.Buy ? delta < 0f : delta > 0f;
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

        void DrawNativeWarning(List<MySprite> sprites, string message)
        {
            var colorable = AppConfig as ScreenConfigColorable;
            Host.DrawMessage(
                sprites,
                message,
                "Warning",
                colorable != null ? colorable.WarningColor : Color.Red,
                AppConfig.Scale);
        }

        void EnsureRefreshButton(RectangleF rect, bool enabled, string text)
        {
            if (_refreshButton == null)
                _refreshButton = new Button(rect, new ButtonModel { Text = text, Clicked = OnRefreshClicked });
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
            _refreshButton.SetStyle(GetRefreshButtonStyle(enabled));
            _refreshButton.CustomRender = RenderRefreshButton;
        }

        ControlStyle GetRefreshButtonStyle(bool enabled)
        {
            if (enabled)
            {
                if (_refreshButtonStyle == null)
                    _refreshButtonStyle = Button.CreatePrimaryButtonStyle(Theme);
                else
                    _refreshButtonStyle.ThemeColors = Theme;
                return _refreshButtonStyle;
            }

            if (_disabledButtonStyle == null)
                _disabledButtonStyle = Button.CreateDisabledButtonStyle(Theme);
            else
                _disabledButtonStyle.ThemeColors = Theme;
            return _disabledButtonStyle;
        }

        ControlStyle GetModeButtonStyle()
        {
            if (_modeButtonStyle == null)
                _modeButtonStyle = Button.CreatePrimaryButtonStyle(Theme);
            else
                _modeButtonStyle.ThemeColors = Theme;
            return _modeButtonStyle;
        }

        ControlStyle GetSortHeaderStyle()
        {
            var hoverColor = new Color(Host.ForegroundColor, 0.12f);
            if (_sortHeaderStyle == null)
            {
                _sortHeaderStyle = new ControlStyle(Host.ForegroundColor, Color.Transparent)
                {
                    HoverPanelColor = hoverColor,
                    HoverTextColor = Host.ForegroundColor,
                    BorderRadiusPixels = 0f
                };
            }
            else
            {
                _sortHeaderStyle.SetColors(Host.ForegroundColor, Color.Transparent);
                _sortHeaderStyle.HoverPanelColor = hoverColor;
                _sortHeaderStyle.HoverTextColor = Host.ForegroundColor;
            }

            return _sortHeaderStyle;
        }

        ControlStyle GetSearchInputStyle()
        {
            if (_searchInputStyle == null)
                _searchInputStyle = Button.CreatePrimaryButtonStyle(Theme);
            else
                _searchInputStyle.ThemeColors = Theme;
            return _searchInputStyle;
        }

        void RenderSearchButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var hovered = control.IsPointerOver;
            sprites.Add(new MySprite(SpriteType.TEXTURE, "Search")
            {
                Position = control.Bounds.Center,
                Size = new Vector2(18f * context.Scale),
                Color = context.Style.GetTextColor(hovered)
            });
        }

        void RenderSearchInput(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var hovered = control.IsPointerOver;
            var innerPadding = Math.Max(2f, Math.Min(rect.Width, rect.Height) * 0.08f);
            var innerRect = Inset(rect, innerPadding);
            var textColor = context.GetThemeColor(Constants.ON_SECONDARY_CONTAINER);
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

            Border.CreateSpritesFromRect(rect, sprites, context.Style.GetPanelColor(hovered),
                radiusScale: context.Scale);
            Border.CreateSpritesFromRect(innerRect, sprites, context.GetThemeColor(
                    hovered ? Constants.SECONDARY_CONTAINER + Constants.HOVER : Constants.SECONDARY_CONTAINER),
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

        void RenderClearSearchButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var hovered = control.IsPointerOver;
            var color = context.GetThemeColor(Constants.ON_SECONDARY_CONTAINER);
            if (hovered)
                color = context.Style.GetTextColor(true);

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

        void RenderSortHeaderButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var model = control.DataContext as SortHeaderButtonModel;
            if (model == null)
                return;

            var rect = control.Bounds;
            var active = model.Column == _sortColumn;
            var hovered = control.IsPointerOver;
            var text = MyTexts.GetString(model.LocalizationKey);
            var textScale = 0.58f * context.Scale * context.FontScale;
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, context.Surface);
            var textY = rect.Center.Y - textSize.Y * 0.5f;
            var textX = GetSortHeaderTextX(model.Column, rect, context.Scale);
            var alignment = model.Column == NpcMarketSortColumn.Name ? TextAlignment.LEFT : TextAlignment.RIGHT;
            var textColor = context.Style.GetTextColor(hovered || active);

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

        void RenderRefreshButton(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var model = control.DataContext as ButtonModel;
            var enabled = model == null || model.Enabled;
            var rect = control.Bounds;
            var hover = enabled && control.IsPointerOver;
            var color = context.Style.GetPanelColor(hover);
            var textColor = context.Style.GetTextColor(hover);
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
            _searchInputModel.Title = MyTexts.GetString(LOC_SEARCH);
            _searchInputModel.Placeholder = MyTexts.GetString(LOC_SEARCH);
            _searchInputModel.Value = _searchQuery;
            _searchInputModel.ValueChanged = OnSearchChanged;
            _searchInputModel.Click(sender);
        }

        void OnSearchChanged(string value)
        {
            _searchQuery = value ?? string.Empty;
            _scrollPanel.ResetScroll(false);
            RefreshRows();
            Host.RenderSprites();
        }

        void OnClearSearchClicked(ButtonModel model, object sender)
        {
            OnSearchChanged(string.Empty);
        }

        void OnModeChanged(NpcMarketMode mode)
        {
            if (_mode == mode)
                return;

            _mode = mode;
            if (_sortColumn == NpcMarketSortColumn.Price)
                _sortDescending = mode == NpcMarketMode.Sell;
            _scrollPanel.ResetScroll(false);
            RefreshRows();
            Host.RenderSprites();
        }

        void OnSortHeaderClicked(ButtonModel model, object sender)
        {
            var header = model as SortHeaderButtonModel;
            if (header == null)
                return;

            if (_sortColumn == header.Column)
                _sortDescending = !_sortDescending;
            else
            {
                _sortColumn = header.Column;
                _sortDescending = header.Column == NpcMarketSortColumn.Price && _mode == NpcMarketMode.Sell;
            }

            _scrollPanel.ResetScroll(false);
            RefreshRows();
            Host.RenderSprites();
        }

        void HandleUpdated()
        {
            RefreshRows();
            Host.RenderSprites();
        }

        void OnScrollPanelChanged(ScrollPanel panel)
        {
            Host.RenderSprites();
        }

        ControlRenderContext CreateRenderContext()
        {
            return CreateControlRenderContext(
                Host.Surface,
                Host.Scale,
                Host.Surface.FontSize,
                new Vector2(float.NaN, float.NaN));
        }

        ControlRenderContext CreateButtonRenderContext()
        {
            return CreateControlRenderContext(Host.Surface, Host.Scale, Host.Surface.FontSize, Vector2.Zero);
        }

        Vector2 GetRefreshButtonSize(string text, float textScale)
        {
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, Host.Surface);
            return new Vector2(Math.Max(96f * Host.Scale, textSize.X + 24f * Host.Scale),
                Math.Max(28f * Host.Scale, textSize.Y + 10f * Host.Scale));
        }

        Vector2 GetModeButtonSize(float textScale)
        {
            var buy = FormatingHelper.GetSizeInPixel(GetModeLabel(NpcMarketMode.Buy), "White", textScale, Host.Surface);
            var sell = FormatingHelper.GetSizeInPixel(GetModeLabel(NpcMarketMode.Sell), "White", textScale, Host.Surface);
            return new Vector2(Math.Max(82f * Host.Scale, Math.Max(buy.X, sell.X) + 34f * Host.Scale),
                Math.Max(28f * Host.Scale, Math.Max(buy.Y, sell.Y) + 10f * Host.Scale));
        }

        static string GetModeLabel(NpcMarketMode mode)
        {
            return MyTexts.GetString(mode == NpcMarketMode.Buy ? "StoreScreenBuyHeader" : "StoreScreenSellHeader");
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

        static void BeginClip(List<MySprite> sprites, RectangleF bounds)
        {
            if (sprites == null)
                return;

            int x = (int)Math.Floor(bounds.X);
            int y = (int)Math.Floor(bounds.Y);
            int right = (int)Math.Ceiling(bounds.Right);
            int bottom = (int)Math.Ceiling(bounds.Bottom);
            sprites.Add(MySprite.CreateClipRect(new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y))));
        }

        sealed class SortHeaderButtonModel : ButtonModel
        {
            public NpcMarketSortColumn Column { get; set; }
            public string LocalizationKey { get; set; }
        }

    }
}
