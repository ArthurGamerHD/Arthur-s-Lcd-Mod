using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Templates;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Market;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Market
{
    internal sealed class NpcMarketListPanel : Panel
    {
        const float ITEM_NAME_MAX_WIDTH = 240f;
        readonly List<Button> _sortHeaderButtons = new List<Button>();
        readonly List<NpcMarketRowHitSlot> _rowHitSlots = new List<NpcMarketRowHitSlot>();
        readonly StringBuilder _text = new StringBuilder();
        readonly IAppHost _host;
        readonly Grid _templateGrid;
        readonly Panel _headerPanel;
        readonly Repeater<NpcMarketRowHitSlot> _rowHitRepeater;
        readonly Button _searchButton;
        IList<NpcMarketRow> _rows = new List<NpcMarketRow>();
        NpcMarketListPage _page;
        NpcMarketMode _mode;
        NpcMarketSortColumn _sortColumn;
        bool _sortDescending;
        float _headerHeight;
        float _rowHeight;
        float _textScale;
        float _layoutScale = 1f;
        Color _muted;

        public NpcMarketListPanel(IAppHost host)
        {
            _host = host;
            _templateGrid = new Grid(default(RectangleF), new[] { 1f }, new[] { 1f, 1f });
            _headerPanel = new Panel();
            _rowHitRepeater = new Repeater<NpcMarketRowHitSlot>()
                .ItemTemplate(Template.For<NpcMarketRowHitSlot>(CreateRowHitControl))
                .Bind(BindRowHitControl)
                .ArrangeItem(ArrangeRowHitControl);
            _rowHitRepeater.SetClass("ControlBase NpcMarketRows");

            _templateGrid.Set(_headerPanel, 0, 0);
            _templateGrid.Set(_rowHitRepeater, 0, 1);
            AddChild(_templateGrid);

            _searchButton = new Button(default(RectangleF), new ButtonModel { Clicked = OnSearchClicked });
            _searchButton.CustomRender = RenderSearchButton;
            _searchButton.SetClass("ControlBase Button Sort");
            _searchButton.SetVisible(false);
            _headerPanel.AddChild(_searchButton);
            EnsureSortHeaderButtons();
        }

        public Action<NpcMarketSortColumn> SortClicked { get; set; }
        public Action SearchClicked { get; set; }
        public Action<NpcMarketRowClickTarget> RowClicked { get; set; }

        public override void Arrange(RectangleF bounds)
        {
            SetRect(bounds);
        }

        public void Configure(NpcMarketListPageContext context)
        {
            if (context == null)
                return;

            _rows = context.Rows ?? new List<NpcMarketRow>();
            _page = context.Page;
            _mode = context.Mode;
            _sortColumn = context.SortColumn;
            _sortDescending = context.SortDescending;
            _headerHeight = context.HeaderHeight;
            _rowHeight = context.RowHeight;
            _textScale = context.TextScale;
            _layoutScale = Math.Max(0.01f, context.LayoutScale);
            _muted = context.MutedColor;
            SortClicked = context.SortClicked;
            SearchClicked = context.SearchClicked;
            RowClicked = context.RowClicked;
            SetRect(new RectangleF(Bounds.X, Bounds.Y, _page != null ? _page.Width : Bounds.Width, _page != null ? _page.Height : Bounds.Height));
        }

        void EnsureSortHeaderButtons()
        {
            if (_sortHeaderButtons.Count > 0)
                return;

            AddSortHeaderButton(NpcMarketSortColumn.Name, "StoreBlock_Column_Name");
            AddSortHeaderButton(NpcMarketSortColumn.Price, "StoreBlock_Column_PricePerUnit");
            AddSortHeaderButton(NpcMarketSortColumn.Trend, "StoreBlock_Column_Trend");
            AddSortHeaderButton(NpcMarketSortColumn.BuyPrice, null);
            AddSortHeaderButton(NpcMarketSortColumn.SellPrice, null);
            AddSortHeaderButton(NpcMarketSortColumn.BuyTrend, null);
            AddSortHeaderButton(NpcMarketSortColumn.SellTrend, null);
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
            button.SetClass("ControlBase Button Sort");
            _sortHeaderButtons.Add(button);
            _headerPanel.AddChild(button);
        }

        protected override void ArrangeChildren()
        {
            float safeHeaderHeight = Math.Max(1f, _headerHeight);
            float safeBodyHeight = Math.Max(1f, Bounds.Height - safeHeaderHeight);
            _templateGrid.SetRows(safeHeaderHeight, safeBodyHeight);
            _templateGrid.SetRect(Bounds);
            ConfigureHeaderButtons();
            ConfigureRowButtons();
        }

        void ConfigureHeaderButtons()
        {
            _searchButton.SetVisible(false);
            foreach (var button in _sortHeaderButtons)
                button.SetVisible(false);

            if (_mode == NpcMarketMode.Both)
            {
                var layout = GetBothLayout(Bounds, _layoutScale);
                ConfigureSearchButton(new RectangleF(
                    Bounds.X + 12f * _layoutScale,
                    Bounds.Y,
                    Math.Max(0f, layout.NameLeft - Bounds.X - 12f * _layoutScale),
                    _headerHeight));
                ConfigureSortHeaderButton(0, new RectangleF(layout.NameLeft, Bounds.Y, Math.Max(0f, layout.BuyPriceRect.X - layout.NameLeft), _headerHeight));
                ConfigureSortHeaderButton(3, WithRow(layout.BuyPriceRect, Bounds.Y, _headerHeight));
                ConfigureSortHeaderButton(5, WithRow(layout.BuyTrendRect, Bounds.Y, _headerHeight));
                ConfigureSortHeaderButton(4, WithRow(layout.SellPriceRect, Bounds.Y, _headerHeight));
                ConfigureSortHeaderButton(6, WithRow(layout.SellTrendRect, Bounds.Y, _headerHeight));
                return;
            }

            var scale = _layoutScale;
            var contentRight = Bounds.Right;
            var nameLeft = Bounds.X + 48f * scale;
            var priceLeft = contentRight - 204f * scale;
            var trendLeft = contentRight - 96f * scale;
            priceLeft = Math.Max(nameLeft, Math.Min(priceLeft, contentRight));
            trendLeft = Math.Max(priceLeft, Math.Min(trendLeft, contentRight));
            ConfigureSearchButton(new RectangleF(
                Bounds.X + 12f * scale,
                Bounds.Y,
                Math.Max(0f, nameLeft - Bounds.X - 12f * scale),
                _headerHeight));
            ConfigureSortHeaderButton(0, new RectangleF(nameLeft, Bounds.Y, priceLeft - nameLeft, _headerHeight));
            ConfigureSortHeaderButton(1, new RectangleF(priceLeft, Bounds.Y, trendLeft - priceLeft, _headerHeight));
            ConfigureSortHeaderButton(2, new RectangleF(trendLeft, Bounds.Y, contentRight - trendLeft, _headerHeight));
        }

        void ConfigureSearchButton(RectangleF rect)
        {
            _searchButton.SetRect(rect);
            _searchButton.SetClass("ControlBase Button Sort");
            _searchButton.SetVisible(rect.Width > 0f && rect.Height > 0f);
        }

        void ConfigureSortHeaderButton(int index, RectangleF rect)
        {
            if (index < 0 || index >= _sortHeaderButtons.Count)
                return;

            var button = _sortHeaderButtons[index];
            button.SetRect(rect);
            ApplySortClass(button);
            button.SetVisible(rect.Width > 0f && rect.Height > 0f);
        }

        void ConfigureRowButtons()
        {
            _rowHitSlots.Clear();

            if (_page == null)
            {
                _rowHitRepeater.Items(_rowHitSlots);
                return;
            }

            for (var i = 0; i < _page.RowCount; i++)
            {
                var rowIndex = _page.StartRowIndex + i;
                if (rowIndex < 0 || rowIndex >= _rows.Count)
                    continue;

                var row = _rows[rowIndex];
                var top = Bounds.Y + _headerHeight + i * _rowHeight;
                if (_mode == NpcMarketMode.Both)
                {
                    var layout = GetBothLayout(Bounds, _layoutScale);
                    AddRowHitSlot(row, NpcMarketMode.Buy, _page.PageIndex, rowIndex, i, WithRow(Union(layout.BuyPriceRect, layout.BuyTrendRect), top, _rowHeight));
                    AddRowHitSlot(row, NpcMarketMode.Sell, _page.PageIndex, rowIndex, i, WithRow(Union(layout.SellPriceRect, layout.SellTrendRect), top, _rowHeight));
                    continue;
                }

                AddRowHitSlot(row, _mode, _page.PageIndex, rowIndex, i, new RectangleF(Bounds.X, top, Bounds.Width, _rowHeight));
            }

            _rowHitRepeater.Items(_rowHitSlots);
        }

        void AddRowHitSlot(NpcMarketRow row, NpcMarketMode mode, int pageIndex, int rowIndex, int visibleIndex, RectangleF rect)
        {
            if (row == null || !HasQuoteForMode(row, mode) || rect.Width <= 0f || rect.Height <= 0f)
                return;

            _rowHitSlots.Add(new NpcMarketRowHitSlot
            {
                Row = row,
                Mode = mode,
                PageIndex = pageIndex,
                RowIndex = rowIndex,
                VisibleIndex = visibleIndex,
                Bounds = rect,
                Target = new NpcMarketRowClickTarget(row.ItemKey, mode)
            });
        }

        ControlTemplate CreateRowHitControl(NpcMarketRowHitSlot slot, int index)
        {
            var button = new Button(default(RectangleF), new RowHitButtonModel { Clicked = OnMarketRowClicked });
            button.SetClass("ControlBase Button NpcMarketRowHit");
            button.CustomRender = RenderRowHitButton;
            return button;
        }

        void BindRowHitControl(ControlTemplate control, NpcMarketRowHitSlot slot, int index)
        {
            if (control == null || slot == null)
                return;

            var model = control.DataContext as RowHitButtonModel;
            if (model == null)
            {
                model = new RowHitButtonModel();
                control.SetDataContext(model);
            }

            model.Slot = slot;
            model.Clicked = OnMarketRowClicked;
        }

        void ArrangeRowHitControl(ControlTemplate control, RectangleF bounds, NpcMarketRowHitSlot slot, int index)
        {
            var button = control as Button;
            if (button == null || slot == null)
                return;

            button.SetRect(slot.Bounds);
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var muted = GetResourceColor(ThemeResources.MutedTextColor, _muted);
            var textColor = GetResourceColor(ThemeResources.OnSurfaceColor, _host.ForegroundColor);
            var dividerColor = GetResourceColor(ThemeResources.DividerColor, muted);

            DrawHeaderDivider(sprites, dividerColor);
            _rowHitRepeater.Render(sprites);

            for (var i = 0; _page != null && i < _page.RowCount; i++)
            {
                var rowIndex = _page.StartRowIndex + i;
                if (rowIndex < 0 || rowIndex >= _rows.Count)
                    continue;

                var rect = new RectangleF(Bounds.X, Bounds.Y + _headerHeight + i * _rowHeight, Bounds.Width, _rowHeight);
                DrawRow(sprites, _rows[rowIndex], rect, _textScale, muted, textColor);
            }

            _headerPanel.Render(sprites);
        }

        void DrawHeaderDivider(List<MySprite> sprites, Color dividerColor)
        {
            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
            {
                Position = new Vector2(Bounds.Center.X, Bounds.Y + _headerHeight - _layoutScale),
                Size = new Vector2(Bounds.Width, _layoutScale),
                Color = dividerColor
            });
        }

        void DrawRow(List<MySprite> sprites, NpcMarketRow row, RectangleF rect, float textScale, Color muted, Color textColor)
        {
            if (_mode == NpcMarketMode.Both)
            {
                DrawBothRow(sprites, row, rect, textScale, muted, textColor);
                return;
            }

            var scale = _layoutScale;
            var centerY = rect.Center.Y;
            var iconSize = 24f * scale;
            var iconCenter = new Vector2(rect.X + 28f * scale, centerY);
            sprites.Add(new MySprite(SpriteType.TEXTURE, row.SpriteName)
            {
                Position = iconCenter,
                Size = new Vector2(iconSize),
                Color = Color.White
            });

            var nameLeft = rect.X + 48f * scale;
            var priceLeft = rect.Right - 204f * scale;
            var priceRight = rect.Right - 106f * scale;
            var deltaRight = rect.Right - 14f * scale;
            var nameWidth = Math.Max(20f, Math.Min(ITEM_NAME_MAX_WIDTH * scale, priceLeft - nameLeft - 12f * scale));
            var secondary = row.GetSecondaryLabel();
            var displayName = string.IsNullOrEmpty(secondary) ? row.DisplayName : row.DisplayName + " (" + secondary + ")";
            DrawText(sprites, Trim(displayName, nameWidth, textScale), nameLeft, centerY, textScale, TextAlignment.LEFT, textColor);
            DrawText(sprites, FormatingHelper.FormatSpaceCredits(row.PricePerUnit) + " SC", priceRight, centerY, textScale, TextAlignment.RIGHT, textColor);
            DrawTrendForMode(sprites, row.DeltaPercent, _mode, deltaRight, centerY, textScale, muted);
        }

        void DrawBothRow(List<MySprite> sprites, NpcMarketRow row, RectangleF rect, float textScale, Color muted, Color textColor)
        {
            var scale = _layoutScale;
            var centerY = rect.Center.Y;
            var layout = GetBothLayout(rect, scale);
            sprites.Add(new MySprite(SpriteType.TEXTURE, row.SpriteName)
            {
                Position = new Vector2(rect.X + 28f * scale, centerY),
                Size = new Vector2(24f * scale),
                Color = Color.White
            });

            var secondary = row.GetSecondaryLabel();
            var displayName = string.IsNullOrEmpty(secondary) ? row.DisplayName : row.DisplayName + " (" + secondary + ")";
            DrawText(sprites, Trim(displayName, layout.NameWidth, textScale), layout.NameLeft, centerY, textScale, TextAlignment.LEFT, textColor);
            DrawOptionalPrice(sprites, row.BestBuyQuote, layout.BuyPriceRect.Right - 8f * scale, centerY, textScale, textColor);
            DrawOptionalTrend(sprites, row.BestBuyQuote, NpcMarketMode.Buy, layout.BuyTrendRect.Right - 10f * scale, centerY, textScale, muted);
            DrawOptionalPrice(sprites, row.BestSellQuote, layout.SellPriceRect.Right - 8f * scale, centerY, textScale, textColor);
            DrawOptionalTrend(sprites, row.BestSellQuote, NpcMarketMode.Sell, layout.SellTrendRect.Right - 10f * scale, centerY, textScale, muted);
        }

        void DrawOptionalPrice(List<MySprite> sprites, NpcMarketStationQuote quote, float right, float centerY, float textScale, Color textColor)
        {
            if (quote == null)
            {
                DrawSteadyPlaceholder(sprites, right - 18f * _layoutScale, centerY, textScale);
                return;
            }

            DrawText(sprites, FormatingHelper.FormatSpaceCredits(quote.PersonalizedCurrentPricePerUnit) + " SC", right, centerY, textScale, TextAlignment.RIGHT, textColor);
        }

        void DrawOptionalTrend(List<MySprite> sprites, NpcMarketStationQuote quote, NpcMarketMode mode, float right, float centerY, float textScale, Color muted)
        {
            if (quote == null)
            {
                DrawSteadyPlaceholder(sprites, right - 18f * _layoutScale, centerY, textScale);
                return;
            }

            DrawTrendForMode(sprites, quote.EffectiveViewerChangePercent, mode, right, centerY, textScale, muted);
        }

        void DrawSteadyPlaceholder(List<MySprite> sprites, float centerX, float centerY, float textScale)
        {
            var size = Math.Max(10f * _layoutScale, 14f * textScale);
            sprites.Add(new MySprite(SpriteType.TEXTURE, "Steady1")
            {
                Position = new Vector2(centerX, centerY),
                Size = new Vector2(size),
                Color = Color.White,
                Alignment = TextAlignment.CENTER
            });
        }

        void DrawTrendForMode(List<MySprite> sprites, float delta, NpcMarketMode mode, float right, float centerY, float textScale, Color muted)
        {
            var text = FormatDelta(delta);
            var color = GetDeltaColor(delta, mode, muted);
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, _host.Surface);
            var iconSize = Math.Max(8f * _layoutScale, textSize.Y * 0.82f);
            var gap = 3f * _layoutScale;
            float rotation;
            var sprite = GetTrendSprite(delta, mode, out rotation);
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = sprite, Position = new Vector2(right - textSize.X - gap - iconSize * 0.5f, centerY), Size = new Vector2(iconSize), RotationOrScale = rotation, Color = Color.White, Alignment = TextAlignment.CENTER });
            DrawText(sprites, text, right, centerY, textScale, TextAlignment.RIGHT, color);
        }

        void DrawText(List<MySprite> sprites, string text, float x, float centerY, float scale, TextAlignment alignment, Color color)
        {
            var size = FormatingHelper.GetSizeInPixel(text ?? string.Empty, "White", scale, _host.Surface);
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = text ?? string.Empty, Position = new Vector2(x, centerY - size.Y * 0.5f), RotationOrScale = scale, Color = color, Alignment = alignment, FontId = "White" });
        }

        void ApplySortClass(Button button)
        {
            if (button == null)
                return;

            var model = button.DataContext as SortHeaderButtonModel;
            bool active = model != null && model.Column == _sortColumn;
            if (!active)
            {
                button.SetClass("ControlBase Button Sort");
                return;
            }

            button.SetClass(_sortDescending
                ? "ControlBase Button Sort SortDescending"
                : "ControlBase Button Sort SortAscending");
        }

        void RenderRowHitButton(ControlTemplate control, List<MySprite> sprites)
        {
            if (!control.IsPointerOver)
                return;

            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
            {
                Position = control.Bounds.Center,
                Size = control.Bounds.Size,
                Color = GetResourceColor(ThemeResources.SurfaceContainerColor, new Color(_host.ForegroundColor, 0.10f))
            });
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
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, control.TextSurface);
            var textY = rect.Center.Y - textSize.Y * 0.5f;
            var textX = GetSortHeaderTextX(model.Column, rect, control.LayoutScale);
            var alignment = model.Column == NpcMarketSortColumn.Name ? TextAlignment.LEFT : TextAlignment.RIGHT;
            var textColor = control.TextColor;
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = text, Position = new Vector2(textX, textY), RotationOrScale = textScale, Color = textColor, Alignment = alignment, FontId = "White" });
            if (active)
            {
                sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = text, Position = new Vector2(textX + 0.7f * control.LayoutScale, textY), RotationOrScale = textScale, Color = textColor, Alignment = alignment, FontId = "White" });
                var triangleX = alignment == TextAlignment.LEFT ? textX + textSize.X + 8f * control.LayoutScale : textX - textSize.X - 8f * control.LayoutScale;
                sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Triangle", Position = new Vector2(triangleX, rect.Center.Y), Size = new Vector2(8f * control.LayoutScale, 6f * control.LayoutScale), RotationOrScale = sortDescending ? MathHelper.Pi : 0f, Color = textColor, Alignment = TextAlignment.CENTER });
            }
        }

        void RenderSearchButton(ControlTemplate control, List<MySprite> sprites)
        {
            var hovered = control.IsPointerOver;
            sprites.Add(new MySprite(SpriteType.TEXTURE, "Search")
            {
                Position = control.Bounds.Center,
                Size = new Vector2(18f * control.LayoutScale),
                Color = hovered ? control.GetResourceColor(ThemeResources.AccentColor, control.TextColor) : control.TextColor
            });
        }

        void OnSearchClicked(ButtonModel model, object sender)
        {
            var clicked = SearchClicked;
            if (clicked != null)
                clicked();
        }

        void OnSortHeaderClicked(ButtonModel model, object sender)
        {
            var header = model as SortHeaderButtonModel;
            if (header != null && SortClicked != null)
                SortClicked(header.Column);
        }

        void OnMarketRowClicked(ButtonModel model, object sender)
        {
            var rowHit = model as RowHitButtonModel;
            var target = rowHit != null && rowHit.Slot != null ? rowHit.Slot.Target : null;
            if (target != null && RowClicked != null)
                RowClicked(target);
        }

        string Trim(string value, float width, float scale)
        {
            _text.Clear().Append(value ?? string.Empty);
            if (width <= 0f)
                return string.Empty;
            if (FormatingHelper.GetSizeInPixel(_text.ToString(), "White", scale, _host.Surface).X <= width)
                return _text.ToString();
            while (_text.Length > 0)
            {
                _text.Length--;
                var contentLength = _text.Length;
                _text.Append(FormatingHelper.ELLIPSIS);
                if (FormatingHelper.GetSizeInPixel(_text.ToString(), "White", scale, _host.Surface).X <= width)
                    return _text.ToString();
                _text.Length = contentLength;
            }
            return _text.ToString();
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

        static BothLayout GetBothLayout(RectangleF rect, float scale)
        {
            var nameLeft = rect.X + 48f * scale;
            var right = Math.Max(nameLeft, rect.Right);
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

        static string GetSortHeaderLabel(SortHeaderButtonModel model)
        {
            if (model == null)
                return string.Empty;
            if (!string.IsNullOrEmpty(model.LocalizationKey))
                return MyTexts.GetString(model.LocalizationKey);
            switch (model.Column)
            {
                case NpcMarketSortColumn.BuyPrice:
                    return MyTexts.GetString("StoreScreenBuyHeader") + " " + MyTexts.GetString("StoreBlock_Column_PricePerUnit");
                case NpcMarketSortColumn.SellPrice:
                    return MyTexts.GetString("StoreScreenSellHeader") + " " + MyTexts.GetString("StoreBlock_Column_PricePerUnit");
                case NpcMarketSortColumn.BuyTrend:
                    return MyTexts.GetString("StoreScreenBuyHeader") + " " + MyTexts.GetString("StoreBlock_Column_Trend");
                case NpcMarketSortColumn.SellTrend:
                    return MyTexts.GetString("StoreScreenSellHeader") + " " + MyTexts.GetString("StoreBlock_Column_Trend");
                default:
                    return string.Empty;
            }
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

        Color GetDeltaColor(float delta, NpcMarketMode mode, Color muted)
        {
            if (Math.Abs(delta) < 0.05f)
                return GetResourceColor(MarketThemeResources.PriceTrendNeutralColor, muted);

            var favorable = mode == NpcMarketMode.Buy ? delta < 0f : delta > 0f;
            return favorable
                ? GetResourceColor(MarketThemeResources.PriceTrendUpColor, Color.LightGreen)
                : GetResourceColor(MarketThemeResources.PriceTrendDownColor, Color.IndianRed);
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

        static string FormatDelta(float delta)
        {
            if (Math.Abs(delta) < 0.05f)
                return "0%";
            return (delta > 0f ? "+" : string.Empty) + delta.ToString("0.#") + "%";
        }

        sealed class SortHeaderButtonModel : ButtonModel
        {
            public NpcMarketSortColumn Column { get; set; }
            public string LocalizationKey { get; set; }
        }

        sealed class RowHitButtonModel : ButtonModel
        {
            public NpcMarketRowHitSlot Slot { get; set; }
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
