using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Generated;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Components;
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

namespace LcdMod.Client.SurfaceScripts.Abstract
{
    public abstract partial class ItemsSurfaceScriptBase : SurfaceScriptBase, IMultiDisplayMode
    {
        protected FilterConfigComponent FilterComponent => Config.GetComponent<FilterConfigComponent>();
        protected BlockSelectionConfigComponent BlockSelectionComponent => Config.GetComponent<BlockSelectionConfigComponent>();
        protected ItemSelectionConfigComponent ItemSelectionComponent => Config.GetComponent<ItemSelectionConfigComponent>();
        protected override SortMethod SortMethod => (SortMethod)FilterComponent.SortMethod;

        public static Dictionary<MyItemType, string> SpriteCache =
            new Dictionary<MyItemType, string>();

        static readonly Dictionary<MyDefinitionId, MyItemType> TypeCache = new Dictionary<MyDefinitionId, MyItemType>();

        readonly Dictionary<MyItemType, double> _itemsCache = new Dictionary<MyItemType, double>();

        public abstract Dictionary<MyItemType, double> ItemSource { get; }

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

        public override string Title
        {
            get
            {
                if (_selectedCategories != ItemSelectionComponent.SelectedCategories)
                    LocalizedTitleCache = string.Empty;

                if (!string.IsNullOrEmpty(LocalizedTitleCache))
                    return LocalizedTitleCache;

                if (ItemSelectionComponent.SelectedCategories != null)
                {
                    _selectedCategories = ItemSelectionComponent.SelectedCategories;
                    var sb = new StringBuilder();
                    foreach (var item in ItemSelectionComponent.SelectedCategories)
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
        protected const int SCROLL_DELAY = 12;
        long _clock;
        bool _hasDrawnAtLeastOnce;
        bool _needsImmediateDraw;
        protected string PreviousType = "";

        protected ItemsSurfaceScriptBase(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface,
            block, size)
        {
        }


        protected virtual List<KeyValuePair<MyItemType, double>> ReadItems(IMyTerminalBlock lcd)
        {
            if (FilterComponent.HideEmpty || ItemSelectionComponent.GetSelectedItems().Any())
                _itemsCache.Clear();

            if (lcd == null || ItemSource == null)
                return new List<KeyValuePair<MyItemType, double>>();

            if (_itemsCache.Any())
            {
                var ar = _itemsCache.Keys.ToArray();
                foreach (var key in ar) // will be 0 unless Clear() was NOT called
                    _itemsCache[key] = 0;
            }


            if (!FilterComponent.HideEmpty)
            {
                foreach (var configSelectedItem in ItemSelectionComponent.GetSelectedItems())
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

        public override void SafeRun()
        {
            if (!IsScreenReadyToRender)
                return;

            _clock++;
            if (_hasDrawnAtLeastOnce && _clock % SCROLL_DELAY != 0 && !Dirty && !_needsImmediateDraw)
                return;

            try
            {
                RenderSprites();
                _hasDrawnAtLeastOnce = true;
                _needsImmediateDraw = false;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
            }
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            LocKeysCache.Clear();
            LocalizedTitleCache = string.Empty;
            _needsImmediateDraw = true;
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            var items = ReadItems(Block as IMyTerminalBlock);

            if (items.Count == 0)
            {
                if (ItemSelectionComponent.SelectedCategories.Any() || BlockSelectionComponent.SelectedBlocks.Any() ||
                    BlockSelectionComponent.SelectedGroups.Any() || ItemSelectionComponent.SelectedDefinition.Any() )
                    AddEmptyWithFiltersSprites(sprites);
                else
                    AddEmptySprites(sprites);

                return sprites;
            }

            AddBackground(sprites);
            DrawTitle(sprites);
            DrawFooter(sprites);

            switch (GeneralComponent.DisplayMode)
            {
                case (int)DisplayMode.Legacy:
                    DrawList(sprites, items);
                    break;
                case (int)DisplayMode.Grid:
                    DrawGrid(sprites, items);
                    break;
            }

            return sprites;
        }

        void DrawList(List<MySprite> sprites, List<KeyValuePair<MyItemType, double>> items)
        {
            int maxRows = GetMaxRowsFromSurface();
            if (maxRows < 1)
                maxRows = 1;

            bool shouldScroll = items.Count > maxRows;

            int start = 0;

            if (shouldScroll)
            {
                int totalSteps = items.Count - maxRows;
                if (totalSteps < 1) totalSteps = 1;

                int step = GetScrollStep(SCROLL_DELAY / 6);

                start = step % (totalSteps + 1);

            }

            int showCount = Math.Min(maxRows, items.Count);

            PreviousType = items[start].Key.TypeId;

            for (int visIdx = start; visIdx < start + showCount; visIdx++)
                DrawRow(sprites, items[visIdx], shouldScroll);
        }

        void DrawGrid(List<MySprite> sprites, List<KeyValuePair<MyItemType, double>> items)
        {
            var rowHeight = 3f * LINE_HEIGHT * ConfiguredScale;
            var viewportAvailableHeight = ViewBox.Height - (CaretY - ViewBox.Y) - FooterHeight;
            int maxRows = Math.Max(1, (int)Math.Floor(viewportAvailableHeight / rowHeight));
            int maxCols = Math.Max(1, GetMaxColsFromSurface());

            int maxVisible = maxRows * maxCols;
            bool shouldScroll = items.Count > maxVisible;

            int startRow = 0;

            if (shouldScroll)
            {
                int totalRows = (int)Math.Ceiling(items.Count / (float)maxCols);
                int totalSteps = totalRows - maxRows;
                if (totalSteps < 1) totalSteps = 1;

                int step = GetScrollStep(SCROLL_DELAY / 6);

                startRow = step % (totalSteps + 1);

            }

            int start = startRow * maxCols;
            int showCount = Math.Min(maxVisible, items.Count - start);
            var margin = 0f;
            var contentStart = ViewBox.X + margin;
            var contentEnd = ViewBox.Width + ViewBox.X - margin;
            var columnWidth = (contentEnd - contentStart) / maxCols;
            var gridHeight = maxRows * rowHeight;

            if (GeneralComponent.DrawLines)
            {
                var lineColor = ColorComponent.ResolveHeaderColor(Block as IMyTerminalBlock);

                for (int row = 0; row <= maxRows; row++)
                {
                    var y = CaretY + row * rowHeight;
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

                for (int col = 0; col <= maxCols; col++)
                {
                    var x = contentStart + col * columnWidth;
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "SquareSimple",
                        Position = new Vector2(x, CaretY + gridHeight / 2f),
                        Size = new Vector2(2f, gridHeight),
                        Color = lineColor,
                        Alignment = TextAlignment.CENTER
                    });
                }
            }

            PreviousType = items[start].Key.TypeId;

            for (int gridIdx = 0; gridIdx < showCount; gridIdx++)
            {
                int visIdx = start + gridIdx;
                int col = gridIdx % maxCols;
                float xStart = contentStart + col * columnWidth;
                float xEnd = (col == maxCols - 1) ? contentEnd : xStart + columnWidth;
                bool moveToNextLine = (col == maxCols - 1) || (gridIdx == showCount - 1);
                DrawGridCell(sprites, items[visIdx], xStart, xEnd, moveToNextLine);
            }
        }

        int GetMaxColsFromSurface()
        {
            var max = ViewBox.Width - (ViewBox.X);
            var perCol = MINIMUM_COL_WIDTH * ConfiguredScale;
            return (int)(Math.Round(max / perCol - .5, MidpointRounding.AwayFromZero));
        }

        int GetMaxRowsFromSurface()
        {
            var max = ViewBox.Height - (CaretY - ViewBox.Y) - FooterHeight;
            var perLine = LINE_HEIGHT * ConfiguredScale;
            return (int)(Math.Round(max / perLine - .5, MidpointRounding.AwayFromZero));
        }


        protected virtual void DrawRow(List<MySprite> frame, KeyValuePair<MyItemType, double> item, bool showScrollBar)
        {
            string sprite;
            string localizedName;

            var foreground = item.Value == 0 ? ColorComponent.ResolveErrorColor() : Surface.ScriptForegroundColor;

            if (!SpriteCache.TryGetValue(item.Key, out sprite))
            {
                sprite = TextureHelper.ResolveItemSprite(item.Key, Surface);
                AddToSpriteCache(item.Key, sprite);
            }

            var margin = 0f;
            var xStart = ViewBox.X + margin;
            var xEnd = ViewBox.Width + ViewBox.X - margin;
            Vector2 position = ViewBox.Position;
            position.X = xStart;
            position.Y = CaretY;

            bool drawSeparatorLine = FilterComponent.SortMethod == (int)SortMethod.Type && PreviousType != item.Key.TypeId;

            if (GeneralComponent.DrawLines || drawSeparatorLine)
            {
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = new Vector2((xStart + xEnd) / 2f, position.Y),
                    Size = new Vector2(xEnd - xStart, 1),
                    Color = drawSeparatorLine ? ColorComponent.ResolveHeaderColor(Block as IMyTerminalBlock) : Surface.ScriptForegroundColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            PreviousType = item.Key.TypeId;

            DrawItemIcon(frame,
                sprite,
                position + new Vector2(20f, 15) * ConfiguredScale,
                new Vector2(LINE_HEIGHT * ConfiguredScale),
                TextAlignment.CENTER,
                item.Value == 0 ? ColorComponent.ResolveErrorColor() : Color.White);
            position.X += (xEnd - xStart) / 8f;

            var clip = new Rectangle((int)position.X, (int)position.Y,
                (int)Math.Max(0, xEnd - position.X - 105 * ConfiguredScale),
                (int)(position.Y + (LINE_HEIGHT + 5) * ConfiguredScale));

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
                RotationOrScale = ConfiguredScale * FontScale,
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
                RotationOrScale = ConfiguredScale * FontScale,
                Color = foreground,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });

            CaretY += LINE_HEIGHT * ConfiguredScale;
        }

        protected virtual void DrawGridCell(List<MySprite> frame,
            KeyValuePair<MyItemType, double> item, float xStart, float xEnd, bool moveToNextLine)
        {
            var gridCellHeight = 3 * LINE_HEIGHT * ConfiguredScale;
            var cellPadding = (LINE_HEIGHT * ConfiguredScale) / 2f;
            string sprite;
            var foreground = Surface.ScriptForegroundColor;

            if (!SpriteCache.TryGetValue(item.Key, out sprite))
            {
                sprite = TextureHelper.ResolveItemSprite(item.Key, Surface);
                AddToSpriteCache(item.Key, sprite);
            }

            Vector2 position = ViewBox.Position;
            position.X = xStart;
            position.Y = CaretY;
            var cellViewBox = GetCellViewBox(xStart, xEnd, position.Y, gridCellHeight, cellPadding);

            if (!GeneralComponent.DrawLines)
            {
                DrawCellBackground(frame, item, xStart, xEnd, position.Y, gridCellHeight, cellPadding);
            }
            else if (item.Value == 0)
            {
                foreground = new Color(96, 32, 32);
            }

            PreviousType = item.Key.TypeId;
            var slots = GetCellSlots(cellViewBox.X, cellViewBox.Right, cellViewBox.Y, cellViewBox.Bottom, LINE_HEIGHT);
            DrawCellContent(frame, item, sprite, foreground, slots);

            if (moveToNextLine)
                CaretY += gridCellHeight;
        }


        protected virtual void DrawItemIcon(List<MySprite> frame, string icon, Vector2 position, Vector2 size,
            TextAlignment alignment, Color backgroundColor)
        {
            if (frame == null || size.X <= 0f || size.Y <= 0f)
                return;

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = position,
                Size = size,
                Alignment = alignment,
                Color = backgroundColor
            });

            if (string.IsNullOrEmpty(icon))
                return;

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = icon,
                Position = position,
                Size = size,
                Alignment = alignment,
                Color = Color.White
            });
        }

        protected virtual void DrawCellContent(List<MySprite> frame, KeyValuePair<MyItemType, double> item,
            string sprite, Color foreground, MyTuple<RectangleF, RectangleF, RectangleF> slots)
        {
            string localizedName;
            var iconRect = slots.Item1;
            var numberRect = slots.Item2;
            var nameRect = slots.Item3;

            DrawItemIcon(frame,
                sprite,
                new Vector2(iconRect.X, iconRect.Y + iconRect.Height / 2f),
                new Vector2(iconRect.Width),
                TextAlignment.LEFT,
                item.Value == 0 ? ColorComponent.ResolveErrorColor() : Color.White);

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

        public override void DrawTitle(List<MySprite> frame)
        {
            var margin = 0f;
            float headerScale = LayoutScale;
            float titleBarHeight = TITLE_BAR_HEIGHT_BASE * headerScale;

            Vector2 position = ViewBox.Position;
            position.X += margin;
            position.Y += 0f;

            CaretY = position.Y;

            if (!TitleVisible)
                return;

            AddHeaderSprite(frame, new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = Icon,
                Position = position + new Vector2(20f) * headerScale,
                Size = new Vector2(40f * headerScale),
                Color = ColorComponent.ResolveHeaderColor(Block as IMyTerminalBlock),
                Alignment = TextAlignment.CENTER
            });
            position.X += ViewBox.Width / 8f;

            var stockText = MyTexts.Get(MyStringId.GetOrCompute("BlockPropertyTitle_Stockpile"));
            var endSize = Surface.MeasureStringInPixels(stockText, "White", ConfiguredScale * 1.3f * FontScale);

            var availableSize = new Rectangle((int)position.X, (int)position.Y,
                (int)(ViewBox.Width - position.X + (ViewBox.X) - endSize.X - (2 * margin)),
                (int)(position.Y + TITLE_HEIGHT * headerScale));
            frame.Add(MySprite.CreateClipRect(availableSize));


            var displayName = GetCachedTitleText(availableSize.Width);

            AddHeaderSprite(frame, new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = displayName,
                Position = position,
                RotationOrScale = ConfiguredScale * 1.3f * FontScale,
                Color = ColorComponent.ResolveHeaderColor(Block as IMyTerminalBlock),
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            frame.Add(MySprite.CreateClearClipRect());
            position.X = ViewBox.Width + ViewBox.X - margin;

            AddHeaderSprite(frame, new MySprite()
            {
                Type = SpriteType.TEXT,
                Data = stockText.ToString(),
                Position = position,
                RotationOrScale = ConfiguredScale * 1.3f * FontScale,
                Color = ColorComponent.ResolveHeaderColor(Block as IMyTerminalBlock),
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });

            CaretY += titleBarHeight;
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
