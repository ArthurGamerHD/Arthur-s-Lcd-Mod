using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LcdMod.Client.Extensions;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel;
using LcdMod.Client.Gui.ControlsTemplates.Panels.WrappedGrid;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.Apps.Abstract
{
    public abstract class ItemsAppBase : AppBase
    {
        protected virtual SortMethod SortMethod => (SortMethod)AppConfig.SortMethod;

        public static Dictionary<MyItemType, string> SpriteCache =
            new Dictionary<MyItemType, string>();

        static readonly Dictionary<MyDefinitionId, MyItemType> TypeCache = new Dictionary<MyDefinitionId, MyItemType>();

        readonly Dictionary<MyItemType, double> _itemsCache = new Dictionary<MyItemType, double>();
        readonly List<KeyValuePair<MyItemType, double>> _items = new List<KeyValuePair<MyItemType, double>>();
        float _caretY;
        float _footerHeight;
        protected string LocalizedTitleCache = string.Empty;

        public abstract Dictionary<MyItemType, double> ItemSource { get; }
        protected virtual string DefaultTitle => "<Title not Set>";

        protected new ScreenConfigWithItems AppConfig => (ScreenConfigWithItems)base.AppConfig;
        protected IMyCubeBlock Block => Host.Block;
        protected Sandbox.ModAPI.Ingame.IMyTextSurface Surface => Host.Surface;
        protected RectangleF ViewBox => Host.ViewBox;
        protected float Scale => Host.Scale;
        protected float FontScale => Host.Surface.FontSize;
        protected float LayoutScale => Scale * FontScale;
        protected Color ForegroundColor => Host.ForegroundColor;
        protected Color BackgroundColor => Host.BackgroundColor;
        protected GridLogic GridLogic => Host.GridLogic;
        protected bool TitleVisible => Host.TitleVisible;
        protected float CaretY
        {
            get { return _caretY; }
            set { _caretY = value; }
        }
        protected float FooterHeight
        {
            get { return _footerHeight; }
            set { _footerHeight = value; }
        }
        public bool HasItems => _items.Count > 0;
        public bool HasFilters { get; private set; }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.GridAndLegacy;
        }

        const int SPRITE_CACHE_MAX_SIZE = 256;

        protected static void AddToSpriteCache(MyItemType key, string sprite)
        {
            SpriteCache[key] = sprite;
            if (SpriteCache.Count > SPRITE_CACHE_MAX_SIZE)
            {
                var oldest = SpriteCache.Keys.First();
                SpriteCache.Remove(oldest);
            }
        }

        protected readonly Dictionary<MyItemType, string> LocKeysCache = new Dictionary<MyItemType, string>();

        string[] _selectedCategories;

        public string Title
        {
            get
            {
                if (_selectedCategories != AppConfig?.SelectedCategories)
                    LocalizedTitleCache = string.Empty;

                if (!string.IsNullOrEmpty(LocalizedTitleCache))
                    return LocalizedTitleCache;

                if (AppConfig?.SelectedCategories != null)
                {
                    _selectedCategories = AppConfig.SelectedCategories;
                    var sb = new StringBuilder();
                    foreach (var item in AppConfig.SelectedCategories)
                        sb.Append(ItemCategoryHelper.GetGroupDisplayName(item) + ", ");

                    if (sb.Length != 0)
                    {
                        sb.Length -= 2;
                        LocalizedTitleCache = sb.ToString();
                    }
                }

                if (string.IsNullOrEmpty(LocalizedTitleCache))
                    LocalizedTitleCache = MyTexts.GetString(DefaultTitle);

                return LocalizedTitleCache;
            }
        }

        protected const int TITLE_HEIGHT = 35;
        protected const int LINE_HEIGHT = 30;
        protected const int MINIMUM_COL_WIDTH = 220;
        protected const int SCROLLER_WIDTH = 8;
        protected const int SCROLL_DELAY = 12;
        protected string PreviousType = "";

        protected ItemsAppBase(ScreenConfigWithItems config, IAppHost host) : base(config, host)
        {
        }


        protected virtual List<KeyValuePair<MyItemType, double>> ReadItems(IMyTerminalBlock lcd)
        {
            if (AppConfig.HideEmpty || AppConfig.SelectedItems.Any())
                _itemsCache.Clear();

            if (lcd == null || ItemSource == null)
                return new List<KeyValuePair<MyItemType, double>>();

            if (_itemsCache.Any())
            {
                var ar = _itemsCache.Keys.ToArray();
                foreach (var key in ar) // will be 0 unless Clear() was NOT called
                    _itemsCache[key] = 0;
            }


            if (!AppConfig.HideEmpty)
            {
                foreach (var configSelectedItem in AppConfig.SelectedItems)
                {
                    MyItemType type;
                    if (!TypeCache.TryGetValue(configSelectedItem, out type))
                    {
                        type = MyItemType.Parse(configSelectedItem.ToString());
                        TypeCache[configSelectedItem] = type;
                    }

                    _itemsCache[type] = 0;
                }
            }

            foreach (var keyValuePair in ItemSource)
                _itemsCache[keyValuePair.Key] = (keyValuePair.Value);


            switch (SortMethod)
            {
                case SortMethod.Type:
                    var sortedByType = new SortedDictionary<MyItemType, double>(ItemTypeComparer.Instance);
                    foreach (var entry in _itemsCache)
                    {
                        sortedByType[entry.Key] = entry.Value;
                    }

                    return sortedByType.ToList();
                default:
                    var sortedByValue = new SortedDictionary<double, List<KeyValuePair<MyItemType, double>>>(
                        DescendingDoubleComparer.Instance);
                    foreach (var entry in _itemsCache)
                    {
                        List<KeyValuePair<MyItemType, double>> bucket;
                        if (!sortedByValue.TryGetValue(entry.Value, out bucket))
                        {
                            bucket = new List<KeyValuePair<MyItemType, double>>();
                            sortedByValue[entry.Value] = bucket;
                        }

                        bucket.Add(entry);
                    }

                    return sortedByValue.SelectMany(b => b.Value).ToList();
            }
        }

        public override void Update()
        {
            _items.Clear();
            
            if (AppConfig == null)
                return;

            try
            {
                var items = ReadItems(Block as IMyTerminalBlock);
                _items.AddRange(items);
                HasFilters = AppConfig.SelectedCategories.Any() || AppConfig.SelectedBlocks.Any() ||
                             AppConfig.SelectedItems.Any() || AppConfig.SelectedGroups.Any() ||
                             AppConfig.SelectedDefinition.Any();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            LocKeysCache.Clear();
            LocalizedTitleCache = string.Empty;
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            _caretY = ContentTop();
            _footerHeight = 0f;
            DrawFooter(sprites);

            switch (AppConfig.DisplayMode)
            {
                case (int)DisplayMode.Legacy:
                    DrawList(sprites, _items);
                    break;
                case (int)DisplayMode.Grid:
                    DrawGrid(sprites, _items);
                    break;
            }

            return sprites;
        }

        void DrawList(List<MySprite> sprites, List<KeyValuePair<MyItemType, double>> items)
        {
            var rowHeight = LINE_HEIGHT * Scale;
            var panel = ScrollPanel.Create(
                ViewBox,
                CaretY,
                FooterHeight,
                rowHeight,
                items.Count,
                SCROLLER_WIDTH * Scale,
                GetScrollStep(SCROLL_DELAY / 6));

            RenderScrollPanelBar(sprites, panel);

            var stack = StackPanel.Create(panel.ContentBounds, rowHeight, items.Count, panel.GetStartIndex(1));
            if (stack.VisibleCellCount <= 0)
                return;

            PreviousType = items[stack.StartIndex].Key.TypeId;
            var renderContext = CreateItemRenderContext();

            for (int i = 0; i < stack.VisibleCellCount; i++)
            {
                var cell = stack.GetCell(i);
                cell.SetControl(CreateListItemControl(items[cell.ItemIndex], cell.Bounds));
                cell.Render(renderContext, sprites);
            }

            CaretY = panel.ContentBounds.Y + panel.MaxVisibleRows * rowHeight;
        }

        void DrawGrid(List<MySprite> sprites, List<KeyValuePair<MyItemType, double>> items)
        {
            var rowHeight = 3f * LINE_HEIGHT * Scale;
            int step = GetScrollStep(SCROLL_DELAY / 6);
            var panel = CreateGridScrollPanel(rowHeight, items.Count, step);
            var grid = WrappedGrid.Create(panel.ContentBounds, rowHeight, MINIMUM_COL_WIDTH * Scale, items.Count);
            grid = WrappedGrid.Create(
                panel.ContentBounds,
                rowHeight,
                MINIMUM_COL_WIDTH * Scale,
                items.Count,
                panel.GetStartIndex(grid.Columns));

            RenderScrollPanelBar(sprites, panel);

            if (AppConfig.DrawLines)
                DrawWrappedGridLines(sprites, panel, grid);

            if (grid.VisibleCellCount <= 0)
                return;

            PreviousType = items[grid.StartIndex].Key.TypeId;
            var renderContext = CreateItemRenderContext();

            for (int gridIdx = 0; gridIdx < grid.VisibleCellCount; gridIdx++)
            {
                var cell = grid.GetCell(gridIdx);
                cell.SetControl(CreateGridItemControl(items[cell.ItemIndex], cell.Bounds));
                cell.Render(renderContext, sprites);
            }

            CaretY = panel.ContentBounds.Y + panel.MaxVisibleRows * rowHeight;
        }

        ControlRenderContext CreateItemRenderContext()
        {
            return new ControlRenderContext(
                Surface,
                Scale,
                FontScale,
                Surface.ScriptForegroundColor,
                AppConfig.HeaderColor,
                new Vector2(float.NaN, float.NaN));
        }

        void RenderScrollPanelBar(List<MySprite> sprites, ScrollPanel panel)
        {
            if (panel == null || !panel.IsScrollable)
                return;

            var trackColor = new Color(Surface.ScriptForegroundColor.R, Surface.ScriptForegroundColor.G,
                Surface.ScriptForegroundColor.B, 127);
            var thumbColor = new Color(AppConfig.HeaderColor.R, AppConfig.HeaderColor.G,
                AppConfig.HeaderColor.B, 250);
            panel.RenderScrollBar(sprites, trackColor, thumbColor);
        }

        ScrollPanel CreateGridScrollPanel(float rowHeight, int itemCount, int scrollStep)
        {
            var panel = ScrollPanel.Create(
                ViewBox,
                CaretY,
                FooterHeight,
                rowHeight,
                0,
                SCROLLER_WIDTH * Scale,
                scrollStep);

            for (int pass = 0; pass < 3; pass++)
            {
                var grid = WrappedGrid.Create(panel.ContentBounds, rowHeight, MINIMUM_COL_WIDTH * Scale, itemCount);
                panel = ScrollPanel.Create(
                    ViewBox,
                    CaretY,
                    FooterHeight,
                    rowHeight,
                    grid.TotalRows,
                    SCROLLER_WIDTH * Scale,
                    scrollStep);
            }

            return panel;
        }

        void DrawWrappedGridLines(List<MySprite> sprites, ScrollPanel panel, WrappedGrid grid)
        {
            var lineColor = AppConfig.HeaderColor;
            var contentStart = panel.ContentBounds.X;
            var contentEnd = panel.ContentBounds.Right;
            var gridHeight = panel.MaxVisibleRows * grid.RowHeight;

            for (int row = 0; row <= panel.MaxVisibleRows; row++)
            {
                var y = panel.ContentBounds.Y + row * grid.RowHeight;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2((contentStart + contentEnd) / 2f, y),
                    Size = new Vector2(contentEnd - contentStart, 2f),
                    Color = lineColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            for (int col = 0; col <= grid.Columns; col++)
            {
                var x = col == grid.Columns ? contentEnd : contentStart + col * grid.ColumnWidth;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(x, panel.ContentBounds.Y + gridHeight / 2f),
                    Size = new Vector2(2f, gridHeight),
                    Color = lineColor,
                    Alignment = TextAlignment.CENTER
                });
            }
        }

        ControlBase CreateGridItemControl(KeyValuePair<MyItemType, double> item, RectangleF bounds)
        {
            var model = new GridItemControlModel(item, ResolveSprite(item.Key), GetGridCellForeground(item))
            {
                Style = new ControlStyle(Surface.ScriptForegroundColor, GetGridCellPanelColor(item)),
                CustomRender = RenderGridItemControl
            };

            return new RectangleControl(bounds, CursorType.Default, model);
        }

        void RenderGridItemControl(ControlBase control, ControlRenderContext context, List<MySprite> frame)
        {
            var model = control.Model as GridItemControlModel;
            if (model == null)
                return;

            var rect = control.Bounds;
            var cellPadding = (LINE_HEIGHT * Scale) / 2f;
            var cellViewBox = GetCellViewBox(rect.X, rect.Right, rect.Y, rect.Height, cellPadding);

            if (!AppConfig.DrawLines)
                DrawCellBackground(frame, model.Item, rect.X, rect.Right, rect.Y, rect.Height, cellPadding);

            PreviousType = model.Item.Key.TypeId;
            var slots = GetCellSlots(cellViewBox.X, cellViewBox.Right, cellViewBox.Y, cellViewBox.Bottom, LINE_HEIGHT);
            DrawCellContent(frame, model.Item, model.Sprite, model.Foreground, slots);
        }

        string ResolveSprite(MyItemType itemType)
        {
            string sprite;
            if (SpriteCache.TryGetValue(itemType, out sprite))
                return sprite;

            var reference = new List<string>();
            var color = "ColorfulIcons_" + itemType.ToString().Substring(16);
            const string notFound = "Textures\\FactionLogo\\Unknown.dds";

            Surface.GetSprites(reference);
            if (reference.Contains(color))
                sprite = color;
            else if (reference.Contains(itemType.ToString()))
                sprite = itemType.ToString();
            else
                sprite = notFound;

            AddToSpriteCache(itemType, sprite);
            return sprite;
        }

        Color GetGridCellForeground(KeyValuePair<MyItemType, double> item)
        {
            if (AppConfig.DrawLines && item.Value == 0)
                return new Color(96, 32, 32);

            return Surface.ScriptForegroundColor;
        }

        protected virtual Color GetGridCellPanelColor(KeyValuePair<MyItemType, double> item)
        {
            return item.Value == 0 ? AppConfig.ErrorColor : AppConfig.HeaderColor;
        }

        ControlBase CreateListItemControl(KeyValuePair<MyItemType, double> item, RectangleF bounds)
        {
            var model = new ListItemControlModel(item, ResolveSprite(item.Key), GetListItemForeground(item))
            {
                Style = new ControlStyle(Surface.ScriptForegroundColor, BackgroundColor),
                CustomRender = RenderListItemControl
            };

            return new RectangleControl(bounds, CursorType.Default, model);
        }

        void RenderListItemControl(ControlBase control, ControlRenderContext context, List<MySprite> frame)
        {
            var model = control.Model as ListItemControlModel;
            if (model == null)
                return;

            DrawListItemContent(frame, model.Item, model.Sprite, model.Foreground, control.Bounds);
        }

        protected virtual void DrawListItemContent(List<MySprite> frame, KeyValuePair<MyItemType, double> item,
            string sprite, Color foreground, RectangleF bounds)
        {
            string localizedName;

            var margin = 0f;
            var xStart = bounds.X + margin;
            var xEnd = bounds.Right - margin;
            Vector2 position = bounds.Position;
            position.X = xStart;

            bool drawSeparatorLine = AppConfig.SortMethod == (int)SortMethod.Type && PreviousType != item.Key.TypeId;

            if (AppConfig.DrawLines || drawSeparatorLine)
            {
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = new Vector2((xStart + xEnd) / 2f, position.Y),
                    Size = new Vector2(xEnd - xStart, 1),
                    Color = drawSeparatorLine ? AppConfig.HeaderColor : Surface.ScriptForegroundColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            PreviousType = item.Key.TypeId;

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = sprite,
                Position = position + new Vector2(20f, 15) * Scale,
                Size = new Vector2(LINE_HEIGHT * Scale),
                Alignment = TextAlignment.CENTER,
                Color = item.Value == 0 ? new Color(96, 32, 32) : Color.White
            });
            position.X += (xEnd - xStart) / 8f;

            var clip = new Rectangle((int)position.X, (int)position.Y,
                (int)Math.Max(0, xEnd - position.X - 105 * Scale),
                (int)(position.Y + (LINE_HEIGHT + 5) * Scale));

            frame.Add(MySprite.CreateClipRect(clip));

            if (!LocKeysCache.TryGetValue(item.Key, out localizedName))
            {
                var key =
                    MyDefinitionManager.Static.TryGetPhysicalItemDefinition(item.Key).DisplayNameEnum?.ToString() ??
                    item.Key.SubtypeId;
                var sb = new StringBuilder(MyTexts.GetString(key));
                TrimText(ref sb, clip.Width);
                localizedName = sb.ToString();
                LocKeysCache[item.Key] = sb.ToString();
            }

            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = localizedName,
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = foreground,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });
            frame.Add(MySprite.CreateClearClipRect());
            position.X = xEnd;
            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = FormatingHelper.FormatItemQty(item.Value),
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = foreground,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });

        }

        Color GetListItemForeground(KeyValuePair<MyItemType, double> item)
        {
            return item.Value == 0 ? AppConfig.ErrorColor : Surface.ScriptForegroundColor;
        }

        protected virtual void DrawCellContent(List<MySprite> frame, KeyValuePair<MyItemType, double> item,
            string sprite, Color foreground, MyTuple<RectangleF, RectangleF, RectangleF> slots)
        {
            string localizedName;
            var iconRect = slots.Item1;
            var numberRect = slots.Item2;
            var nameRect = slots.Item3;

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = sprite,
                Position = new Vector2(iconRect.X, iconRect.Y + iconRect.Height / 2f),
                Size = new Vector2(iconRect.Width),
                Alignment = TextAlignment.LEFT,
                Color = item.Value == 0 ? AppConfig.ErrorColor : Color.White
            });

            if (!LocKeysCache.TryGetValue(item.Key, out localizedName))
            {
                var key =
                    MyDefinitionManager.Static.TryGetPhysicalItemDefinition(item.Key).DisplayNameEnum?.ToString() ??
                    item.Key.SubtypeId;
                var sb = new StringBuilder(MyTexts.GetString(key));
                TrimText(ref sb, nameRect.Width);
                localizedName = sb.ToString();
                LocKeysCache[item.Key] = sb.ToString();
            }

            Vector2 size = FormatingHelper.GetSizeInPixel(localizedName, "White", 1, Surface);
            float minProportion = Math.Min(nameRect.Width / size.X, nameRect.Height / size.Y);
            float fontSize = minProportion;
            float renderedHeight = size.Y * fontSize * FontScale;
            Vector2 pos = nameRect.Center;
            pos.Y -= renderedHeight * 0.5f;
            pos.X = nameRect.Right;

            frame.Add(new MySprite(
                SpriteType.TEXT,
                localizedName,
                pos,
                null,
                foreground,
                "White",
                TextAlignment.RIGHT,
                fontSize * .95f * FontScale
            ));

            var qty = FormatingHelper.FormatItemQty(item.Value);
            size = FormatingHelper.GetSizeInPixel(qty, "White", 1, Surface);
            minProportion = Math.Min(numberRect.Width / size.X, numberRect.Height / size.Y);
            fontSize = minProportion;
            renderedHeight = size.Y * fontSize * FontScale;
            pos = numberRect.Center;
            pos.Y -= renderedHeight * 0.5f;
            pos.X = numberRect.Right;

            frame.Add(new MySprite(
                SpriteType.TEXT,
                qty,
                pos,
                null,
                foreground,
                "White",
                TextAlignment.RIGHT,
                fontSize * .95f * FontScale
            ));
        }

        protected virtual void DrawFooter(List<MySprite> frame)
        {
        }

        float ContentTop()
        {
            return TitleVisible ? ViewBox.Y + 40f * Scale * FontScale : ViewBox.Y;
        }

        static int GetScrollStep(float secondsPerStep)
        {
            try
            {
                var session = MyAPIGateway.Session;
                if (session == null)
                    return 0;

                if (secondsPerStep <= 0f)
                    secondsPerStep = 1f / 60f;

                var ticksPerStep = Math.Max(1, (int)Math.Round(secondsPerStep * 60f));
                return (int)(session.GameplayFrameCounter / ticksPerStep);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"[LcdMod] ItemsApp GetScrollStep error: {ex.Message}");
                return 0;
            }
        }

        protected virtual RectangleF GetCellViewBox(float xStart, float xEnd, float yStart, float cellHeight,
            float cellPadding)
        {
            var innerLeft = xStart + cellPadding;
            var innerRight = xEnd - cellPadding;
            var innerTop = yStart + cellPadding;
            var innerBottom = yStart + cellHeight - cellPadding;
            return new RectangleF(innerLeft, innerTop, innerRight - innerLeft, innerBottom - innerTop);
        }

        protected virtual MyTuple<RectangleF, RectangleF, RectangleF> GetCellSlots(float innerLeft, float innerRight,
            float innerTop, float innerBottom, float spacing)
        {
            var topRowHeight = spacing * Scale;
            var bottomRowTop = innerTop + topRowHeight;
            var bottomRowHeight = Math.Max(0f, innerBottom - bottomRowTop);
            var iconSize = innerBottom - innerTop;
            var contentLeft = innerLeft + iconSize;
            var contentWidth = Math.Max(0f, innerRight - contentLeft);

            var iconRect = new RectangleF(innerLeft, innerTop, iconSize, iconSize);
            var numberRect = new RectangleF(contentLeft, innerTop, contentWidth, topRowHeight);
            var nameRect = new RectangleF(contentLeft, bottomRowTop, contentWidth, bottomRowHeight);
            return new MyTuple<RectangleF, RectangleF, RectangleF>(iconRect, numberRect, nameRect);
        }

        protected void TrimText(ref StringBuilder sb, float availableWidth, float fontSize = 1)
        {
            Vector2 textSize = Surface.MeasureStringInPixels(sb, "White", fontSize * Scale * FontScale);

            if (textSize.X <= availableWidth)
                return;

            var source = sb.ToString();
            for (int i = source.Length - 1; i > 0; i--)
            {
                sb.Clear();
                sb.Append(FormatingHelper.TrimName(source, i));
                textSize = Surface.MeasureStringInPixels(sb, "White", fontSize * Scale * FontScale);

                if (textSize.X <= availableWidth)
                    break;
            }
        }

        protected virtual void DrawCellBackground(List<MySprite> frame, KeyValuePair<MyItemType, double> item,
            float xStart, float xEnd, float yStart, float cellHeight, float cellPadding)
        {
            var rl = xStart + cellPadding / 2;
            var rr = xEnd - cellPadding / 2;
            var rt = yStart + cellPadding / 2;
            var rb = yStart + cellHeight - cellPadding / 2;

            var backgroundColor = item.Value == 0 ? AppConfig.ErrorColor : AppConfig.HeaderColor;
            var accent = backgroundColor.MulValue(0.2f);
            var cellRect = new RectangleF(rl, rt, rr - rl, rb - rt);
            var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
            Border.CreateSpritesFromRect(dropShadow, frame, accent, .2f);
            Border.CreateSpritesFromRect(cellRect, frame, backgroundColor, .2f);
        }

        protected Vector2 ToScreenMargin(Vector2 absoluteCenterInViewBox)
        {
            return new Vector2(absoluteCenterInViewBox.X, 512f - absoluteCenterInViewBox.Y);
        }

        sealed class GridItemControlModel : ControlModelBase
        {
            public readonly KeyValuePair<MyItemType, double> Item;
            public readonly string Sprite;
            public readonly Color Foreground;

            public GridItemControlModel(KeyValuePair<MyItemType, double> item, string sprite, Color foreground)
            {
                Item = item;
                Sprite = sprite;
                Foreground = foreground;
            }
        }

        sealed class ListItemControlModel : ControlModelBase
        {
            public readonly KeyValuePair<MyItemType, double> Item;
            public readonly string Sprite;
            public readonly Color Foreground;

            public ListItemControlModel(KeyValuePair<MyItemType, double> item, string sprite, Color foreground)
            {
                Item = item;
                Sprite = sprite;
                Foreground = foreground;
            }
        }
    }


    sealed class ItemTypeComparer : IComparer<MyItemType>
    {
        public static readonly ItemTypeComparer Instance = new ItemTypeComparer();

        public int Compare(MyItemType a, MyItemType b)
        {
            int typeCmp = string.Compare(a.TypeId, b.TypeId, StringComparison.CurrentCulture);
            if (typeCmp != 0)
                return typeCmp;
            return string.Compare(a.SubtypeId, b.SubtypeId, StringComparison.CurrentCulture);
        }
    }

    sealed class DescendingDoubleComparer : IComparer<double>
    {
        public static readonly DescendingDoubleComparer Instance = new DescendingDoubleComparer();

        public int Compare(double a, double b) => b.CompareTo(a);
    }
}
