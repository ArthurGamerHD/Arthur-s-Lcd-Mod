using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Graph.Helpers;
using Graph.System;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace Graph.Apps.Abstract
{
    public abstract class ItemsSurfaceScriptBase : SurfaceScriptBase
    {
        public static Dictionary<MyItemType, string> SpriteCache =
            new Dictionary<MyItemType, string>();

        static readonly Dictionary<MyDefinitionId, MyItemType> TypeCache = new Dictionary<MyDefinitionId, MyItemType>();

        readonly Dictionary<MyItemType, double> _itemsCache = new Dictionary<MyItemType, double>();

        public abstract Dictionary<MyItemType, double> ItemSource { get; }

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
                if (_selectedCategories != Config?.SelectedCategories)
                    LocalizedTitleCache = string.Empty;

                if (!string.IsNullOrEmpty(LocalizedTitleCache))
                    return LocalizedTitleCache;

                if (Config?.SelectedCategories != null)
                {
                    _selectedCategories = Config.SelectedCategories;
                    var sb = new StringBuilder();
                    foreach (var item in Config.SelectedCategories)
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
        long _clock;
        protected string PreviousType = "";

        protected ItemsSurfaceScriptBase(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface,
            block, size)
        {
        }


        protected virtual List<KeyValuePair<MyItemType, double>> ReadItems(IMyTerminalBlock lcd)
        {
            if (Config.HideEmpty || Config.SelectedItems.Any())
                _itemsCache.Clear();

            if (lcd == null || ItemSource == null)
                return new List<KeyValuePair<MyItemType, double>>();

            if (_itemsCache.Any())
            {
                var ar = _itemsCache.Keys.ToArray();
                foreach (var key in ar) // will be 0 unless Clear() was NOT called
                    _itemsCache[key] = 0;
            }


            if (!Config.HideEmpty)
            {
                foreach (var configSelectedItem in Config.SelectedItems)
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
                    ;
            }
        }

        public override void Run()
        {
            base.Run();

            _clock++;
            if (_clock % SCROLL_DELAY != 0 && !Dirty)
                return;

            if (Config == null)
                return;

            try
            {
                DrawItems();
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
        }

        public virtual void DrawItems()
        {
            var items = ReadItems(Block as IMyTerminalBlock);

            if (items.Count == 0)
            {
                if (Config.SelectedCategories.Any() || Config.SelectedBlocks.Any() || Config.SelectedItems.Any() ||
                    Config.SelectedGroups.Any() || Config.SelectedDefinition.Any() || Config.ReferenceBlock != 0)
                    EmptyWithFilters();
                else
                    Empty();

                return;
            }

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();

                AddBackground(sprites);
                DrawTitle(sprites);
                DrawFooter(sprites);

                switch (Config.DisplayMode)
                {
                    case DisplayMode.Legacy:
                        DrawList(sprites, items);
                        break;
                    case DisplayMode.Grid:
                        DrawGrid(sprites, items);
                        break;
                }

                frame.AddRange(sprites);
            }
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

                float viewportHeight = maxRows * (LINE_HEIGHT * Scale) - (SCROLLER_WIDTH * 2 * Scale);
                float scrollBarHeight = (float)maxRows / items.Count * viewportHeight;

                float totalScrollableRows = items.Count - maxRows;
                float scrollFraction = (totalScrollableRows > 0) ? start / totalScrollableRows : 0f;

                float scrollBarTrackHeight = viewportHeight;
                float scrollBarTravel = scrollBarTrackHeight - scrollBarHeight;

                float scrollBarY = scrollFraction * scrollBarTravel;
                float scrollBarCenter = scrollBarY + scrollBarHeight / 2f;

                var initialY = CaretY + SCROLLER_WIDTH * Scale;

                DrawScrollBar(sprites, Scale, initialY, viewportHeight, scrollBarCenter, scrollBarHeight);
            }

            int showCount = Math.Min(maxRows, items.Count);

            PreviousType = items[start].Key.TypeId;

            for (int visIdx = start; visIdx < start + showCount; visIdx++)
                DrawRow(sprites, items[visIdx], shouldScroll);
        }

        void DrawGrid(List<MySprite> sprites, List<KeyValuePair<MyItemType, double>> items)
        {
            var rowHeight = 3f * LINE_HEIGHT * Scale;
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

                float viewportHeight = maxRows * rowHeight - (SCROLLER_WIDTH * 2 * Scale);
                float scrollBarHeight = (float)maxRows / totalRows * viewportHeight;

                float totalScrollableRows = totalRows - maxRows;
                float scrollFraction = (totalScrollableRows > 0) ? startRow / totalScrollableRows : 0f;

                float scrollBarTrackHeight = viewportHeight;
                float scrollBarTravel = scrollBarTrackHeight - scrollBarHeight;

                float scrollBarY = scrollFraction * scrollBarTravel;
                float scrollBarCenter = scrollBarY + scrollBarHeight / 2f;

                var initialY = CaretY + SCROLLER_WIDTH * Scale;

                DrawScrollBar(sprites, Scale, initialY, viewportHeight, scrollBarCenter, scrollBarHeight);
            }

            int start = startRow * maxCols;
            int showCount = Math.Min(maxVisible, items.Count - start);
            var margin = ViewBox.Size.X * Margin;
            var contentStart = ViewBox.X + margin;
            var contentEnd = ViewBox.Width + ViewBox.X - margin;
            if (shouldScroll)
                contentEnd -= SCROLLER_WIDTH * Scale;
            var columnWidth = (contentEnd - contentStart) / maxCols;
            var gridHeight = maxRows * rowHeight;

            if (Config.DrawLines)
            {
                var lineColor = Config.HeaderColor;

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
            var perCol = MINIMUM_COL_WIDTH * Scale;
            return (int)(Math.Round(max / perCol - .5, MidpointRounding.AwayFromZero));
        }

        int GetMaxRowsFromSurface()
        {
            var max = ViewBox.Height - (CaretY - ViewBox.Y) - FooterHeight;
            var perLine = LINE_HEIGHT * Scale;
            return (int)(Math.Round(max / perLine - .5, MidpointRounding.AwayFromZero));
        }


        protected virtual void DrawRow(List<MySprite> frame, KeyValuePair<MyItemType, double> item, bool showScrollBar)
        {
            string sprite;
            string localizedName;

            var foreground = item.Value == 0 ? Config.ErrorColor : Surface.ScriptForegroundColor;

            if (!SpriteCache.TryGetValue(item.Key, out sprite))
            {
                var reference = new List<string>();
                var color = "ColorfulIcons_" + item.Key.ToString().Substring(16);
                const string notFound = "Textures\\FactionLogo\\Unknown.dds";

                Surface.GetSprites(reference);
                if (reference.Contains(color))
                    sprite = color;
                else if (reference.Contains(item.Key.ToString()))
                    sprite = item.Key.ToString();
                else sprite = notFound;

                AddToSpriteCache(item.Key, sprite);
            }

            var margin = ViewBox.Size.X * Margin;
            var xStart = ViewBox.X + margin;
            var xEnd = ViewBox.Width + ViewBox.X - margin;
            Vector2 position = ViewBox.Position;
            position.X = xStart;
            position.Y = CaretY;

            bool drawSeparatorLine = Config.SortMethod == SortMethod.Type && PreviousType != item.Key.TypeId;

            if (Config.DrawLines || drawSeparatorLine)
            {
                frame.Add(new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = new Vector2((xStart + xEnd) / 2f, position.Y),
                    Size = new Vector2(xEnd - xStart, 1),
                    Color = drawSeparatorLine ? Config.HeaderColor : Surface.ScriptForegroundColor,
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
            if (showScrollBar)
                position.X -= SCROLLER_WIDTH * Scale;
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

            CaretY += LINE_HEIGHT * Scale;
        }

        protected virtual void DrawGridCell(List<MySprite> frame,
            KeyValuePair<MyItemType, double> item, float xStart, float xEnd, bool moveToNextLine)
        {
            var gridCellHeight = 3 * LINE_HEIGHT * Scale;
            var cellPadding = (LINE_HEIGHT * Scale) / 2f;
            string sprite;
            var foreground = Surface.ScriptForegroundColor;

            if (!SpriteCache.TryGetValue(item.Key, out sprite))
            {
                var reference = new List<string>();
                var color = "ColorfulIcons_" + item.Key.ToString().Substring(16);
                const string notFound = "Textures\\FactionLogo\\Unknown.dds";

                Surface.GetSprites(reference);
                if (reference.Contains(color))
                    sprite = color;
                else if (reference.Contains(item.Key.ToString()))
                    sprite = item.Key.ToString();
                else sprite = notFound;

                AddToSpriteCache(item.Key, sprite);
            }

            Vector2 position = ViewBox.Position;
            position.X = xStart;
            position.Y = CaretY;
            var cellViewBox = GetCellViewBox(xStart, xEnd, position.Y, gridCellHeight, cellPadding);

            if (!Config.DrawLines)
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
                Color = item.Value == 0 ? Config.ErrorColor : Color.White
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

            Vector2 size = GetSizeInPixel(localizedName, "White", 1, Surface);
            float minProportion = Math.Min(nameRect.Width / size.X, nameRect.Height / size.Y);
            float fontSize = minProportion / Math.Max(0.0001f, FontScale);
            float renderedHeight = size.Y * fontSize * FontScale;
            Vector2 pos = nameRect.Center;
            pos.Y -= renderedHeight * 0.5f;
            pos.X = nameRect.Right;

            frame.Add(new MySprite(
                (SpriteType)2,
                localizedName,
                pos,
                null,
                foreground,
                "White",
                TextAlignment.RIGHT,
                fontSize * .95f * FontScale
            ));

            var qty = FormatingHelper.FormatItemQty(item.Value);
            size = GetSizeInPixel(qty, "White", 1, Surface);
            minProportion = Math.Min(numberRect.Width / size.X, numberRect.Height / size.Y);
            fontSize = minProportion / Math.Max(0.0001f, FontScale);
            renderedHeight = size.Y * fontSize * FontScale;
            pos = numberRect.Center;
            pos.Y -= renderedHeight * 0.5f;
            pos.X = numberRect.Right;

            frame.Add(new MySprite(
                (SpriteType)2,
                qty,
                pos,
                null,
                foreground,
                "White",
                TextAlignment.RIGHT,
                fontSize * .95f * FontScale
            ));
        }

        protected void DrawScrollBar(List<MySprite> frame, float scale, float initialY, float viewportHeight,
            float scrollBarCenter, float scrollBarHeight)
        {
            float barXCenter = ViewBox.X + ViewBox.Width - (SCROLLER_WIDTH / 2f) * scale;
            int barWidth = (int)(SCROLLER_WIDTH * scale);

            var trackCenter = new Vector2(barXCenter,
                (float)Math.Round(initialY + viewportHeight / 2f, MidpointRounding.ToEven));
            DrawCapsule(frame, trackCenter, barWidth, viewportHeight,
                new Color(Surface.ScriptForegroundColor.R, Surface.ScriptForegroundColor.G,
                    Surface.ScriptForegroundColor.B, 127));

            var thumbCenter = new Vector2(barXCenter,
                (float)Math.Round(initialY + scrollBarCenter, MidpointRounding.ToEven));
            DrawCapsule(frame, thumbCenter, barWidth, scrollBarHeight,
                new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B, 250));
        }


        /// <summary>
        /// Draws a "capsule": a rectangle plus two half-circles.
        /// </summary>
        void DrawCapsule(List<MySprite> frame, Vector2 center, int width, float height, Color color)
        {
            // Base rectangle
            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = center,
                Size = new Vector2(width, height + .5f),
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            var capsSize = new Vector2(width);

            // Top cap (semicircle pointing down, rotation = 0)
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = new Vector2(center.X, center.Y - height / 2f),
                Size = capsSize, // circle diameter
                RotationOrScale = 0f, // 0 rad → flat side up, round side down
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            // Bottom cap (semicircle pointing up, rotation = π)
            frame.Add(new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = new Vector2(center.X, center.Y + height / 2f),
                Size = capsSize,
                RotationOrScale = (float)Math.PI, // flipped
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }


        protected override void DrawTitle(List<MySprite> frame)
        {
            var margin = ViewBox.Size.X * Margin;
            float headerScale = LayoutScale;
            float titleBarHeight = TITLE_BAR_HEIGHT_BASE * headerScale;

            Vector2 position = ViewBox.Position;
            position.X += margin;
            position.Y += (ViewBox.Size.Y * Margin) / 2;

            CaretY = position.Y;

            if (!TitleVisible)
                return;

            AddHeaderSprite(frame, new MySprite()
            {
                Type = SpriteType.TEXTURE,
                Data = Icon,
                Position = position + new Vector2(20f) * headerScale,
                Size = new Vector2(40f * headerScale),
                Color = Config.HeaderColor,
                Alignment = TextAlignment.CENTER
            });
            position.X += ViewBox.Width / 8f;

            var stockText = MyTexts.Get(MyStringId.GetOrCompute("BlockPropertyTitle_Stockpile"));
            var endSize = Surface.MeasureStringInPixels(stockText, "White", Scale * 1.3f * FontScale);

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
                RotationOrScale = Scale * 1.3f * FontScale,
                Color = Config.HeaderColor,
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
                RotationOrScale = Scale * 1.3f * FontScale,
                Color = Config.HeaderColor,
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