using System;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using LcdMod.Client.Market;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
{
    internal sealed class NpcMarketItemDialog : Dialog
    {
        static readonly NpcMarketMode[] Modes = { NpcMarketMode.Buy, NpcMarketMode.Sell };
        static readonly NpcMarketStationSortColumn[] SortColumns =
        {
            NpcMarketStationSortColumn.Distance,
            NpcMarketStationSortColumn.Station,
            NpcMarketStationSortColumn.Price,
            NpcMarketStationSortColumn.Trend
        };

        readonly NpcMarketApp _parent;
        readonly string _itemKey;
        readonly string _fallbackDisplayName;
        readonly string _fallbackSpriteName;
        readonly ScrollPanel _scrollPanel = new ScrollPanel();
        readonly ComboBox<NpcMarketMode> _modeCombo;
        readonly List<Button> _sortButtons = new List<Button>();
        readonly Dictionary<string, Button> _rowButtonsByQuoteKey =
            new Dictionary<string, Button>(StringComparer.Ordinal);
        readonly List<NpcMarketStationQuote> _quotes = new List<NpcMarketStationQuote>();
        NpcMarketStationSortColumn _sortColumn = NpcMarketStationSortColumn.Price;
        bool _sortDescending;
        ControlStyle _headerStyle;
        ControlStyle _comboStyle;

        public NpcMarketItemDialog(NpcMarketApp parent, string itemKey)
            : base(parent)
        {
            _parent = parent;
            _itemKey = itemKey;
            var group = parent.GetItemGroup(itemKey);
            _fallbackDisplayName = group != null ? group.DisplayName : "Market item";
            _fallbackSpriteName = group != null ? group.SpriteName : "MissingIcon";
            _sortDescending = parent.Mode == NpcMarketMode.Sell;
            _scrollPanel.ManualScrollInertiaEnabled = false;
            _scrollPanel.ScrollChanged = delegate { _parent.AppHost.RenderSprites(); };
            _modeCombo = new ComboBox<NpcMarketMode>(Modes, GetModeLabel, OnModeChanged, _parent.AppHost.RenderSprites);
            EnsureSortButtons();
        }

        protected override void RenderCore(InteractiveSurfaceScript owner, RectangleF viewBox, float scale,
            float fontScale, IMyTextSurface surface, Color textColor, Color backgroundColor, Color panelColor,
            Vector2 cursorPosition)
        {
            EnsureContainer(viewBox);
            ContainerControl.ClearChildren();
            RefreshQuotes();

            var outer = 18f * scale;
            var cardWidth = Math.Min(620f * scale, Math.Max(1f, viewBox.Width - outer * 2f));
            var cardHeight = Math.Min(430f * scale, Math.Max(1f, viewBox.Height - outer * 2f));
            var card = new RectangleF(viewBox.Center.X - cardWidth * 0.5f, viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth, cardHeight);
            RegisterDialogCard(card);

            Sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", surface.TextureSize / 2f, surface.TextureSize,
                new Color(0, 0, 0, 128)));
            Border.CreateSpritesFromRect(new RectangleF(card.Position + 3f * scale, card.Size), Sprites,
                GetThemeColor(Constants.SHADOW), radiusScale: scale);
            Border.CreateSpritesFromRect(card, Sprites, GetThemeColor(Constants.SURFACE_CONTAINER_HIGH),
                radiusScale: scale);

            var context = CreateRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);
            var padding = 18f * scale;
            var headerTop = card.Y + 14f * scale;
            var headerHeight = 66f * scale;
            var group = _parent.GetItemGroup(_itemKey);
            DrawItemHeader(group, card, headerTop, headerHeight, scale, fontScale, surface);

            var comboWidth = 104f * scale;
            var comboHeight = 30f * scale;
            var comboRect = new RectangleF(card.Right - padding - comboWidth, headerTop + 24f * scale, comboWidth,
                comboHeight);
            _modeCombo.Configure(comboRect, scale, GetComboStyle());
            _modeCombo.SetSelectedValue(_parent.Mode);
            ContainerControl.AddChild(_modeCombo);
            _modeCombo.Render(context, Sprites);

            var tableHeaderTop = headerTop + headerHeight;
            var tableHeaderHeight = 28f * scale;
            var footerHeight = 8f * scale;
            var rowHeight = 34f * scale;
            var listTop = tableHeaderTop + tableHeaderHeight;
            var listBottom = card.Bottom - padding - footerHeight;
            var listRect = new RectangleF(card.X + padding, listTop, card.Width - padding * 2f,
                Math.Max(0f, listBottom - listTop));
            var tableRect = new RectangleF(card.X + padding, tableHeaderTop, card.Width - padding * 2f,
                Math.Max(0f, listBottom - tableHeaderTop));
            var tableContentRect = InsetListContent(tableRect, scale);
            var listContentRect = new RectangleF(tableContentRect.X, listTop,
                tableContentRect.Width, Math.Max(0f, tableContentRect.Bottom - listTop));

            DrawListPanel(tableRect, scale);
            ConfigureScrollPanel(listContentRect, rowHeight, scale);
            DrawHeaders(tableHeaderTop, tableHeaderHeight, tableContentRect, context, scale);
            DrawRows(listContentRect, rowHeight, context, scale, fontScale, surface, cursorPosition);

        }

        static RectangleF InsetListContent(RectangleF rect, float scale)
        {
            var inset = 4f * scale;
            return new RectangleF(rect.X + inset, rect.Y, Math.Max(0f, rect.Width - inset * 2f), rect.Height);
        }

        void DrawListPanel(RectangleF rect, float scale)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            Border.CreateSpritesFromRect(rect, Sprites, GetThemeColor(Constants.SECONDARY_CONTAINER),
                radiusScale: scale);
        }

        void DrawItemHeader(NpcMarketItemGroup group, RectangleF card, float top, float height, float scale,
            float fontScale, IMyTextSurface surface)
        {
            var sprite = group != null ? group.SpriteName : _fallbackSpriteName;
            var name = group != null ? group.DisplayName : _fallbackDisplayName;
            var iconSize = 42f * scale;
            var iconCenter = new Vector2(card.X + 64f * scale, top + height * 0.5f);
            Sprites.Add(new MySprite(SpriteType.TEXTURE, string.IsNullOrEmpty(sprite) ? "MissingIcon" : sprite)
            {
                Position = iconCenter,
                Size = new Vector2(iconSize),
                Color = Color.White
            });

            var nameScale = 0.78f * scale * fontScale;
            var size = FormatingHelper.GetSizeInPixel(name, "White", nameScale, surface);
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = name,
                Position = new Vector2(iconCenter.X + iconSize * 0.5f + 12f * scale,
                    top + height * 0.5f - size.Y * 0.5f),
                RotationOrScale = nameScale,
                Color = GetThemeColor(Constants.ON_SURFACE),
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });
        }

        void RefreshQuotes()
        {
            _quotes.Clear();
            var group = _parent.GetItemGroup(_itemKey);
            if (group != null)
                _quotes.AddRange(group.Quotes);
            _quotes.Sort(new NpcMarketStationDialogComparer(_parent.Mode, _sortColumn, _sortDescending));
        }

        void ConfigureScrollPanel(RectangleF rect, float rowHeight, float scale)
        {
            _scrollPanel.Configure(rect, rect.Y, 0f, rowHeight, _quotes.Count,
                _scrollPanel.AutomaticScrollerWidthPixels * scale, 0f);
            _scrollPanel.SetScrollBarColors(GetThemeColor(Constants.SURFACE_CONTAINER_HIGHEST),
                GetThemeColor(Constants.ON_SURFACE));
            _scrollPanel.SetVisible(true);
            ContainerControl.AddChild(_scrollPanel);
        }

        void DrawHeaders(float top, float height, RectangleF listRect, ControlRenderContext context, float scale)
        {
            var right = _scrollPanel.ContentViewportBounds.Right;
            var distanceRight = listRect.X + 66f * scale;
            var priceLeft = right - 164f * scale;
            var trendLeft = right - 76f * scale;
            var rects = new[]
            {
                new RectangleF(listRect.X, top, distanceRight - listRect.X, height),
                new RectangleF(distanceRight, top, Math.Max(0f, priceLeft - distanceRight), height),
                new RectangleF(priceLeft, top, Math.Max(0f, trendLeft - priceLeft), height),
                new RectangleF(trendLeft, top, Math.Max(0f, right - trendLeft), height)
            };

            for (var i = 0; i < _sortButtons.Count; i++)
            {
                var button = _sortButtons[i];
                button.SetRect(rects[i]);
                button.SetStyle(GetHeaderStyle());
                button.SetVisible(rects[i].Width > 0f);
                ContainerControl.AddChild(button);
                button.Render(context, Sprites);
            }

            Sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
            {
                Position = new Vector2((listRect.X + right) * 0.5f, top + height - scale),
                Size = new Vector2(Math.Max(0f, right - listRect.X), scale),
                Color = new Color(GetThemeColor(Constants.ON_SURFACE), 0.62f)
            });
        }

        void DrawRows(RectangleF listRect, float rowHeight, ControlRenderContext context, float scale, float fontScale,
            IMyTextSurface surface, Vector2 cursorPosition)
        {
            var start = _scrollPanel.StartRow;
            var end = Math.Min(_quotes.Count, start + _scrollPanel.RenderRows);
            ConfigureRowButtons(start, end, rowHeight);
            BeginClip(Sprites, _scrollPanel.ContentViewportBounds);
            if (_quotes.Count == 0)
                DrawEmpty(listRect, scale, fontScale);

            for (var i = start; i < end; i++)
            {
                var top = _scrollPanel.ContentBounds.Y + (i - start) * rowHeight;
                DrawQuote(_quotes[i], top, rowHeight, _scrollPanel.ContentViewportBounds.Right, scale, fontScale,
                    surface, cursorPosition, GetRowButton(_quotes[i]));
            }

            Sprites.Add(MySprite.CreateClearClipRect());
            _scrollPanel.Render(context, Sprites);
        }

        void ConfigureRowButtons(int start, int end, float rowHeight)
        {
            foreach (var button in _rowButtonsByQuoteKey.Values)
                button.SetVisible(false);

            for (var index = start; index < end; index++)
            {
                var quoteKey = GetQuoteKey(_quotes[index]);
                var rect = new RectangleF(_scrollPanel.ContentViewportBounds.X,
                    _scrollPanel.ContentBounds.Y + (index - start) * rowHeight,
                    _scrollPanel.ContentViewportBounds.Width, rowHeight);
                Button button;
                if (!_rowButtonsByQuoteKey.TryGetValue(quoteKey, out button))
                {
                    button = new Button(rect, CursorType.Hand, quoteKey, OnStationClicked);
                    button.ClickSound = AudioHelper.HudGps3;
                    _rowButtonsByQuoteKey[quoteKey] = button;
                    _scrollPanel.AddChild(button);
                }
                else
                {
                    button.SetRect(rect);
                }
                button.SetVisible(true);
            }
        }

        Button GetRowButton(NpcMarketStationQuote quote)
        {
            Button button;
            return quote != null && _rowButtonsByQuoteKey.TryGetValue(GetQuoteKey(quote), out button) ? button : null;
        }

        void DrawQuote(NpcMarketStationQuote quote, float top, float height, float right, float scale, float fontScale,
            IMyTextSurface surface, Vector2 cursor, Button button)
        {
            if (button != null && button.IsPointerOver)
            {
                Sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple")
                {
                    Position = button.Bounds.Center,
                    Size = button.Bounds.Size,
                    Color = new Color(GetThemeColor(Constants.ON_SURFACE), 0.10f)
                });
            }

            var textScale = 0.56f * scale * fontScale;
            var centerY = top + height * 0.5f;
            var distanceRight = _scrollPanel.ContentViewportBounds.X + 60f * scale;
            var priceLeft = right - 164f * scale;
            var priceRight = right - 86f * scale;
            var trendRight = right - 10f * scale;
            var stationLeft = distanceRight + 8f * scale;
            var stationWidth = Math.Max(0f, priceLeft - stationLeft - 8f * scale);
            DrawText(FormatingHelper.DistanceToString((float)quote.DistanceMeters), distanceRight, centerY, textScale,
                TextAlignment.RIGHT, GetThemeColor(Constants.ON_SURFACE), surface);
            DrawText(TrimText(FormatStation(quote), stationWidth, textScale, surface), stationLeft, centerY, textScale,
                TextAlignment.LEFT, GetThemeColor(Constants.ON_SURFACE), surface);
            DrawText(FormatingHelper.FormatSpaceCredits(quote.PersonalizedCurrentPricePerUnit) + " SC", priceRight,
                centerY, textScale, TextAlignment.RIGHT, GetThemeColor(Constants.ON_SURFACE), surface);
            DrawText(FormatTrend(quote.EffectiveViewerChangePercent), trendRight, centerY, textScale,
                TextAlignment.RIGHT, GetThemeColor(Constants.ON_SURFACE), surface);
        }

        void DrawText(string text, float x, float centerY, float scale, TextAlignment alignment, Color color,
            IMyTextSurface surface)
        {
            var size = FormatingHelper.GetSizeInPixel(text, "White", scale, surface);
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(x, centerY - size.Y * 0.5f),
                RotationOrScale = scale,
                Color = color,
                Alignment = alignment,
                FontId = "White"
            });
        }

        void DrawEmpty(RectangleF rect, float scale, float fontScale)
        {
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = MyTexts.GetString("MarketWatchTab_SearchMessage_NoResults"),
                Position = new Vector2(rect.Center.X, rect.Center.Y - 10f * scale),
                RotationOrScale = 0.5f * scale * fontScale,
                Color = GetThemeColor(Constants.ON_SURFACE),
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });
        }

        void EnsureSortButtons()
        {
            for (var i = 0; i < SortColumns.Length; i++)
            {
                var button = new Button(default(RectangleF), new StationHeaderModel
                {
                    Column = SortColumns[i],
                    Clicked = OnSortClicked
                });
                button.CustomRender = RenderHeader;
                _sortButtons.Add(button);
            }
        }

        void RenderHeader(ControlBase control, ControlRenderContext context, List<MySprite> sprites)
        {
            var model = control.DataContext as StationHeaderModel;
            if (model == null)
                return;
            var active = model.Column == _sortColumn;
            var text = GetHeaderLabel(model.Column);
            var textScale = 0.54f * context.Scale * context.FontScale;
            var size = FormatingHelper.GetSizeInPixel(text, "White", textScale, context.Surface);
            var alignment = model.Column == NpcMarketStationSortColumn.Station ? TextAlignment.LEFT : TextAlignment.RIGHT;
            var x = alignment == TextAlignment.LEFT ? control.Bounds.X + 10f * context.Scale : control.Bounds.Right - 10f * context.Scale;
            var y = control.Bounds.Center.Y - size.Y * 0.5f;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(x, y),
                RotationOrScale = textScale,
                Color = context.Style.GetTextColor(active || control.IsPointerOver),
                Alignment = alignment,
                FontId = "White"
            });
            if (active)
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = text,
                    Position = new Vector2(x + 0.7f * context.Scale, y),
                    RotationOrScale = textScale,
                    Color = context.Style.GetTextColor(true),
                    Alignment = alignment,
                    FontId = "White"
                });
            }
            if (!active)
                return;
            var triangleX = model.Column == NpcMarketStationSortColumn.Station
                ? x + size.X + 8f * context.Scale
                : control.Bounds.Right - 4f * context.Scale;
            sprites.Add(new MySprite(SpriteType.TEXTURE, "Triangle")
            {
                Position = new Vector2(triangleX, control.Bounds.Center.Y),
                Size = new Vector2(8f * context.Scale, 6f * context.Scale),
                RotationOrScale = _sortDescending ? MathHelper.Pi : 0f,
                Color = context.Style.GetTextColor(true)
            });
        }

        void OnSortClicked(ButtonModel model, object sender)
        {
            var header = model as StationHeaderModel;
            if (header == null)
                return;
            if (_sortColumn == header.Column)
                _sortDescending = !_sortDescending;
            else
            {
                _sortColumn = header.Column;
                _sortDescending = header.Column == NpcMarketStationSortColumn.Price &&
                                  _parent.Mode == NpcMarketMode.Sell;
            }
            _scrollPanel.ResetScroll(false);
            _parent.AppHost.RenderSprites();
        }

        void OnModeChanged(NpcMarketMode mode)
        {
            _parent.SetMode(mode);
            if (_sortColumn == NpcMarketStationSortColumn.Price)
                _sortDescending = mode == NpcMarketMode.Sell;
            _scrollPanel.ResetScroll(false);
            _parent.AppHost.RenderSprites();
        }

        void OnStationClicked(object dataContext, object sender)
        {
            var quoteKey = dataContext as string;
            NpcMarketStationQuote quote = null;
            for (var i = 0; i < _quotes.Count; i++)
            {
                if (string.Equals(GetQuoteKey(_quotes[i]), quoteKey, StringComparison.Ordinal))
                {
                    quote = _quotes[i];
                    break;
                }
            }
            if (quote != null)
                _parent.CreateTemporaryGps(quote, _fallbackDisplayName);
        }

        static string GetQuoteKey(NpcMarketStationQuote quote)
        {
            return quote == null
                ? string.Empty
                : quote.ItemKey + "|" + quote.SellerFactionId + "|" + quote.StationId;
        }

        ControlStyle GetHeaderStyle()
        {
            if (_headerStyle == null)
                _headerStyle = new ControlStyle(GetThemeColor(Constants.ON_SURFACE), Color.Transparent);
            else
                _headerStyle.SetColors(GetThemeColor(Constants.ON_SURFACE), Color.Transparent);
            return _headerStyle;
        }

        ControlStyle GetComboStyle()
        {
            if (_comboStyle == null)
                _comboStyle = Button.CreatePrimaryButtonStyle(ParentTheme);
            else
                _comboStyle.ThemeColors = ParentTheme;
            return _comboStyle;
        }

        static string GetModeLabel(NpcMarketMode mode)
        {
            return MyTexts.GetString(mode == NpcMarketMode.Buy ? "StoreScreenBuyHeader" : "StoreScreenSellHeader");
        }

        static string GetHeaderLabel(NpcMarketStationSortColumn column)
        {
            switch (column)
            {
                case NpcMarketStationSortColumn.Distance:
                    return MyTexts.GetString("MarketWatchTab_Column_Distance");
                case NpcMarketStationSortColumn.Station:
                    return MyTexts.GetString("StoreBlock_Column_Name");
                case NpcMarketStationSortColumn.Trend:
                    return MyTexts.GetString("StoreBlock_Column_Trend");
                default:
                    return MyTexts.GetString("StoreBlock_Column_PricePerUnit");
            }
        }

        static string FormatStation(NpcMarketStationQuote quote)
        {
            return quote.StationName;
        }

        static string FormatTrend(float trend)
        {
            if (Math.Abs(trend) < 0.05f)
                return "-";
            return (trend > 0f ? "+" : string.Empty) + trend.ToString("0.#") + "%";
        }

        static string TrimText(string text, float width, float scale, IMyTextSurface surface)
        {
            var value = text ?? string.Empty;
            if (width <= 0f)
                return string.Empty;
            while (value.Length > 0 && FormatingHelper.GetSizeInPixel(value, "White", scale, surface).X > width)
                value = value.Substring(0, value.Length - 1);
            return value == text ? value : value + FormatingHelper.ELLIPSIS;
        }

        static void BeginClip(List<MySprite> sprites, RectangleF bounds)
        {
            sprites.Add(MySprite.CreateClipRect(new Rectangle((int)Math.Floor(bounds.X), (int)Math.Floor(bounds.Y),
                Math.Max(0, (int)Math.Ceiling(bounds.Right) - (int)Math.Floor(bounds.X)),
                Math.Max(0, (int)Math.Ceiling(bounds.Bottom) - (int)Math.Floor(bounds.Y)))));
        }

        sealed class StationHeaderModel : ButtonModel
        {
            public NpcMarketStationSortColumn Column { get; set; }
        }
    }
}
