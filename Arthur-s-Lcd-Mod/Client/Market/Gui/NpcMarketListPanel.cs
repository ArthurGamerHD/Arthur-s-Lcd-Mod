using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using Sandbox.ModAPI;

namespace LcdMod.Client.Market.Gui
{
    internal sealed class NpcMarketListPanel : Panel
    {
        const float ITEM_NAME_MAX_WIDTH = 240f;
        static readonly Color CheckerWhite = new Color(255,255,255, 2);
        static readonly Color CheckerBlack = new Color(0,0,0, 2);
        readonly List<Button> _sortHeaderButtons = new List<Button>();
        readonly Dictionary<string, Button> _rowButtonsByKey = new Dictionary<string, Button>(StringComparer.Ordinal);
        readonly StringBuilder _text = new StringBuilder();
        readonly IAppHost _host;
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
        ControlStyle _sortHeaderStyle;

        public NpcMarketListPanel(IAppHost host)
        {
            _host = host;
            _searchButton = new Button(default(RectangleF), new ButtonModel { Clicked = OnSearchClicked });
            _searchButton.CustomRender = RenderSearchButton;
            _searchButton.SetVisible(false);
            AddChild(_searchButton);
            EnsureSortHeaderButtons();
        }

        public Action<NpcMarketSortColumn> SortClicked { get; set; }
        public Action SearchClicked { get; set; }
        public Action<NpcMarketRowClickTarget> RowClicked { get; set; }

        public override void Arrange(RectangleF bounds)
        {
            SetRect(bounds);
            ConfigureHeaderButtons();
            ConfigureRowButtons();
        }

        public void Configure(
            RectangleF bounds,
            IList<NpcMarketRow> rows,
            NpcMarketListPage page,
            NpcMarketMode mode,
            NpcMarketSortColumn sortColumn,
            bool sortDescending,
            float headerHeight,
            float rowHeight,
            float textScale,
            float layoutScale,
            Color muted,
            ControlStyle sortHeaderStyle)
        {
            _rows = rows ?? new List<NpcMarketRow>();
            _page = page;
            _mode = mode;
            _sortColumn = sortColumn;
            _sortDescending = sortDescending;
            _headerHeight = headerHeight;
            _rowHeight = rowHeight;
            _textScale = textScale;
            _layoutScale = Math.Max(0.01f, layoutScale);
            _muted = muted;
            _sortHeaderStyle = sortHeaderStyle;
            SetRect(bounds);
            ConfigureHeaderButtons();
            ConfigureRowButtons();
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
            _sortHeaderButtons.Add(button);
            AddChild(button);
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
            _searchButton.SetStyle(_sortHeaderStyle);
            _searchButton.SetVisible(rect.Width > 0f && rect.Height > 0f);
        }

        void ConfigureSortHeaderButton(int index, RectangleF rect)
        {
            if (index < 0 || index >= _sortHeaderButtons.Count)
                return;

            var button = _sortHeaderButtons[index];
            button.SetRect(rect);
            button.SetStyle(_sortHeaderStyle);
            button.SetVisible(rect.Width > 0f && rect.Height > 0f);
        }

        void ConfigureRowButtons()
        {
            foreach (var button in _rowButtonsByKey.Values)
                button.SetVisible(false);

            if (_page == null)
                return;

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
                    ConfigureRowButton(row, NpcMarketMode.Buy, WithRow(Union(layout.BuyPriceRect, layout.BuyTrendRect), top, _rowHeight));
                    ConfigureRowButton(row, NpcMarketMode.Sell, WithRow(Union(layout.SellPriceRect, layout.SellTrendRect), top, _rowHeight));
                    continue;
                }

                ConfigureRowButton(row, _mode, new RectangleF(Bounds.X, top, Bounds.Width, _rowHeight));
            }
        }

        void ConfigureRowButton(NpcMarketRow row, NpcMarketMode mode, RectangleF rect)
        {
            if (row == null || !HasQuoteForMode(row, mode) || rect.Width <= 0f || rect.Height <= 0f)
                return;

            var target = new NpcMarketRowClickTarget(row.ItemKey, mode);
            Button button;
            if (!_rowButtonsByKey.TryGetValue(target.Key, out button))
            {
                button = new Button(rect, CursorType.Hand, target, OnMarketRowClicked);
                _rowButtonsByKey[target.Key] = button;
                AddChild(button);
            }
            else
            {
                button.SetRect(rect);
                button.SetDataContext(target);
            }

            button.SetVisible(true);
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            DrawHeaderDivider(sprites);
            for (var i = 0; _page != null && i < _page.RowCount; i++)
            {
                var rowIndex = _page.StartRowIndex + i;
                if (rowIndex < 0 || rowIndex >= _rows.Count)
                    continue;

                var rect = new RectangleF(Bounds.X, Bounds.Y + _headerHeight + i * _rowHeight, Bounds.Width, _rowHeight);
                DrawRowBackground(sprites, rect, _page.PageIndex, rowIndex);
                DrawRowHoverBackground(sprites, _rows[rowIndex]);
                DrawRow(sprites, _rows[rowIndex], rect, _textScale, _muted);
            }

            _searchButton.Render(context, sprites);
            for (var i = 0; i < _sortHeaderButtons.Count; i++)
                _sortHeaderButtons[i].Render(context, sprites);
        }

        void DrawHeaderDivider(List<MySprite> sprites)
        {
            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
            {
                Position = new Vector2(Bounds.Center.X, Bounds.Y + _headerHeight - _layoutScale),
                Size = new Vector2(Bounds.Width, _layoutScale),
                Color = _muted
            });
        }

        void DrawRowHoverBackground(List<MySprite> sprites, NpcMarketRow row)
        {
            if (row == null || string.IsNullOrEmpty(row.ItemKey))
                return;

            foreach (var entry in _rowButtonsByKey)
            {
                var target = entry.Value.DataContext as NpcMarketRowClickTarget;
                if (target == null || !entry.Value.Visible || !entry.Value.IsPointerOver ||
                    !string.Equals(target.ItemKey, row.ItemKey, StringComparison.Ordinal))
                    continue;

                sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
                {
                    Position = entry.Value.Bounds.Center,
                    Size = entry.Value.Bounds.Size,
                    Color = new Color(_host.ForegroundColor, 0.10f)
                });
            }
        }

        void DrawRow(List<MySprite> sprites, NpcMarketRow row, RectangleF rect, float textScale, Color muted)
        {
            if (_mode == NpcMarketMode.Both)
            {
                DrawBothRow(sprites, row, rect, textScale, muted);
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
            DrawText(sprites, Trim(displayName, nameWidth, textScale), nameLeft, centerY, textScale, TextAlignment.LEFT, _host.ForegroundColor);
            DrawText(sprites, FormatingHelper.FormatSpaceCredits(row.PricePerUnit) + " SC", priceRight, centerY, textScale, TextAlignment.RIGHT, _host.ForegroundColor);
            DrawTrendForMode(sprites, row.DeltaPercent, _mode, deltaRight, centerY, textScale, muted);
        }

        void DrawBothRow(List<MySprite> sprites, NpcMarketRow row, RectangleF rect, float textScale, Color muted)
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
            DrawText(sprites, Trim(displayName, layout.NameWidth, textScale), layout.NameLeft, centerY, textScale, TextAlignment.LEFT, _host.ForegroundColor);
            DrawOptionalPrice(sprites, row.BestBuyQuote, layout.BuyPriceRect.Right - 8f * scale, centerY, textScale);
            DrawOptionalTrend(sprites, row.BestBuyQuote, NpcMarketMode.Buy, layout.BuyTrendRect.Right - 10f * scale, centerY, textScale, muted);
            DrawOptionalPrice(sprites, row.BestSellQuote, layout.SellPriceRect.Right - 8f * scale, centerY, textScale);
            DrawOptionalTrend(sprites, row.BestSellQuote, NpcMarketMode.Sell, layout.SellTrendRect.Right - 10f * scale, centerY, textScale, muted);
        }

        void DrawOptionalPrice(List<MySprite> sprites, NpcMarketStationQuote quote, float right, float centerY, float textScale)
        {
            if (quote == null)
            {
                DrawSteadyPlaceholder(sprites, right - 18f * _layoutScale, centerY, textScale);
                return;
            }

            DrawText(sprites, FormatingHelper.FormatSpaceCredits(quote.PersonalizedCurrentPricePerUnit) + " SC", right, centerY, textScale, TextAlignment.RIGHT, _host.ForegroundColor);
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

        void DrawRowBackground(List<MySprite> sprites, RectangleF rect, int columnIndex, int itemIndex)
        {

            
            var white = columnIndex % 2 == itemIndex % 2;
            
            if((_host.BackgroundColor == Color.White && white) || (_host.BackgroundColor == Color.Black && !white))
                return;
            
            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
            {
                Position = rect.Center,
                Size = rect.Size,
                Color = white ? CheckerWhite : CheckerBlack
            });
        }

        void DrawText(List<MySprite> sprites, string text, float x, float centerY, float scale, TextAlignment alignment, Color color)
        {
            var size = FormatingHelper.GetSizeInPixel(text ?? string.Empty, "White", scale, _host.Surface);
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = text ?? string.Empty, Position = new Vector2(x, centerY - size.Y * 0.5f), RotationOrScale = scale, Color = color, Alignment = alignment, FontId = "White" });
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
            var textColor = context.Style.GetTextColor(hovered || active);
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = text, Position = new Vector2(textX, textY), RotationOrScale = textScale, Color = textColor, Alignment = alignment, FontId = "White" });
            if (active)
            {
                sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = text, Position = new Vector2(textX + 0.7f * context.Scale, textY), RotationOrScale = textScale, Color = textColor, Alignment = alignment, FontId = "White" });
                var triangleX = alignment == TextAlignment.LEFT ? textX + textSize.X + 8f * context.Scale : textX - textSize.X - 8f * context.Scale;
                sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Triangle", Position = new Vector2(triangleX, rect.Center.Y), Size = new Vector2(8f * context.Scale, 6f * context.Scale), RotationOrScale = _sortDescending ? MathHelper.Pi : 0f, Color = textColor, Alignment = TextAlignment.CENTER });
            }
        }

        void RenderSearchButton(ControlTemplate control, ControlRenderContext context, List<MySprite> sprites)
        {
            var hovered = control.IsPointerOver;
            sprites.Add(new MySprite(SpriteType.TEXTURE, "Search")
            {
                Position = control.Bounds.Center,
                Size = new Vector2(18f * context.Scale),
                Color = context.Style.GetTextColor(hovered)
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

        void OnMarketRowClicked(object dataContext, object sender)
        {
            var target = dataContext as NpcMarketRowClickTarget;
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

        static Color GetDeltaColor(float delta, NpcMarketMode mode, Color muted)
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
