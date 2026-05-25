using System;
using System.Collections.Generic;
using System.Text;
using Generated;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using ColorExtensions = LcdMod.Client.Extensions.ColorExtensions;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;

namespace LcdMod.Client.SurfaceScripts.Abstract
{
    public abstract partial class PowerSurfaceScriptBase : SurfaceScriptBase, IMultiDisplayMode
    {
        protected override ConfigKind ConfigKind => ConfigKind.Power;
        protected const float LINE = 22f;
        protected const float MINIMUM_COL_WIDTH = 400f;
        protected const float SCROLLER_WIDTH = 8f;
        protected const int SCROLL_DELAY = 12;
        protected const float GRID_CELL_LINES = 6f;

        protected struct PowerEntryDefinition
        {
            public readonly string Key;
            public readonly string DisplayNameToken;
            public readonly string FallbackName;

            public PowerEntryDefinition(string key, string displayNameToken, string fallbackName)
            {
                Key = key;
                DisplayNameToken = displayNameToken;
                FallbackName = fallbackName;
            }
        }

        struct PowerEntry
        {
            public string Name;
            public float Usage;
            public double Current;
            public double Max;
            public string UsageLine;
            public int DetectedBlocks;

            public PowerEntry(string name)
            {
                Name = name;
                Usage = 0f;
                Current = 0;
                Max = 0;
                UsageLine = string.Empty;
                DetectedBlocks = 0;
            }
        }

        struct PowerTotals
        {
            public double Current;
            public double Max;
            public int Count;
        }

        readonly Dictionary<string, PowerEntry> _entriesByKey = new Dictionary<string, PowerEntry>();
        readonly Dictionary<string, PowerTotals> _totalsByKey = new Dictionary<string, PowerTotals>();
        string[] _entryOrder;
        PowerEntry[] _entriesOrdered;
        readonly List<PowerEntry> _visibleEntries = new List<PowerEntry>();
        readonly List<IMyPowerProducer> _producers = new List<IMyPowerProducer>();

        Color _ascentColor = Color.White;
        string _usagePrefix = string.Empty;
        string _maxLabelCache = string.Empty;
        string _currentLabelCache = string.Empty;

        protected abstract PowerEntryDefinition[] EntryDefinitions { get; }

        protected PowerSurfaceScriptBase(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface,
            block, size)
        {
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.GridAndLegacy;
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _ascentColor = ColorExtensions.DeriveAccentColor(AppConfig.HeaderColor, .4f, 0.5);

            RefreshEntryLabels();
            _maxLabelCache = string.Empty;
            _currentLabelCache = string.Empty;
        }

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            DrawTitle(sprites);
            DrawFooter(sprites);

            SumPowerSources(Block?.CubeGrid, _totalsByKey);
            UpdateEntryValues();
            BuildVisibleEntries();

            if (string.IsNullOrEmpty(_maxLabelCache))
                _maxLabelCache = MyTexts.Get(MyStringId.GetOrCompute("BlockPropertiesText_MaxOutput")).ToString();
            if (string.IsNullOrEmpty(_currentLabelCache))
                _currentLabelCache = MyTexts.Get(MyStringId.GetOrCompute("BlockPropertyProperties_CurrentOutput"))
                    .ToString();

            switch (AppConfig.DisplayMode)
            {
                case (int)DisplayMode.Grid:
                    DrawGridLike(
                        sprites,
                        _visibleEntries,
                        _maxLabelCache,
                        _currentLabelCache,
                        false,
                        AppConfig.DrawLines,
                        AppConfig.DrawLines,
                        AppConfig.DrawLines);
                    break;
                default:
                    DrawDefaultView(
                        sprites,
                        _visibleEntries,
                        _maxLabelCache,
                        _currentLabelCache);
                    break;
            }

            return sprites;
        }

        protected abstract bool TryMapProducerType(string typeId, IMyPowerProducer producer, out string entryKey);

        protected void InitializeEntries()
        {
            var definitions = EntryDefinitions;

            _entryOrder = new string[definitions.Length];
            _entriesOrdered = new PowerEntry[definitions.Length];

            _entriesByKey.Clear();
            _totalsByKey.Clear();

            for (int i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                _entryOrder[i] = definition.Key;
                _entriesByKey[definition.Key] = new PowerEntry(ResolveDisplayName(definition));
                _totalsByKey[definition.Key] = new PowerTotals();
            }

            _usagePrefix = MyTexts.Get(MyStringId.GetOrCompute("HudInfoNamePowerUsage")) + " ";
            SyncOrderedEntries();
        }

        void RefreshEntryLabels()
        {
            if (_entriesByKey.Count == 0)
                InitializeEntries();

            var definitions = EntryDefinitions;
            for (int i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                var entry = _entriesByKey[definition.Key];
                entry.Name = ResolveDisplayName(definition);
                _entriesByKey[definition.Key] = entry;
            }

            _usagePrefix = MyTexts.Get(MyStringId.GetOrCompute("HudInfoNamePowerUsage")) + " ";
            SyncOrderedEntries();
        }

        string ResolveDisplayName(PowerEntryDefinition definition)
        {
            var localized = MyTexts.GetString(definition.DisplayNameToken);
            if (string.IsNullOrEmpty(localized) || localized == definition.DisplayNameToken)
                return definition.FallbackName;
            return localized;
        }

        void UpdateEntryValues()
        {
            if (_entriesByKey.Count == 0)
                InitializeEntries();

            for (int i = 0; i < _entryOrder.Length; i++)
            {
                var key = _entryOrder[i];
                var totals = _totalsByKey[key];
                var usage = totals.Max > 0 ? (float)Math.Min(Math.Max(totals.Current / totals.Max, 0), 1) : 0f;

                var entry = _entriesByKey[key];
                entry.Usage = usage;
                entry.Current = totals.Current;
                entry.Max = totals.Max;
                entry.UsageLine = _usagePrefix + FormatingHelper.PercentageToString(usage);
                entry.DetectedBlocks = totals.Count;
                _entriesByKey[key] = entry;
            }

            SyncOrderedEntries();
        }

        void BuildVisibleEntries()
        {
            _visibleEntries.Clear();
            var hideEmpty = AppConfig == null || AppConfig.HideEmpty;
            for (int i = 0; i < _entriesOrdered.Length; i++)
            {
                if (!hideEmpty || _entriesOrdered[i].DetectedBlocks > 0)
                    _visibleEntries.Add(_entriesOrdered[i]);
            }
        }

        void SyncOrderedEntries()
        {
            for (int i = 0; i < _entryOrder.Length; i++)
                _entriesOrdered[i] = _entriesByKey[_entryOrder[i]];
        }

        void SumPowerSources(IMyCubeGrid grid, Dictionary<string, PowerTotals> totals)
        {
            for (int i = 0; i < _entryOrder.Length; i++)
            {
                var key = _entryOrder[i];
                totals[key] = new PowerTotals();
            }

            if (grid == null)
                return;

            _producers.Clear();
            GridHelper.GetAllLogicBlocksOfType(grid, _producers, GridLinkTypeEnum.Logical);

            for (int i = 0; i < _producers.Count; i++)
            {
                var prod = _producers[i];
                var typeId = string.Empty;

                try
                {
                    typeId = prod.BlockDefinition.TypeIdString ?? string.Empty;
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, GetType());
                }

                string key;
                if (!TryMapProducerType(typeId, prod, out key))
                    continue;
                if (!totals.ContainsKey(key))
                    continue;

                var values = totals[key];
                values.Current += ToWatts(prod?.CurrentOutput ?? 0);
                values.Max += ToWatts(prod?.MaxOutput ?? 0);
                values.Count++;
                totals[key] = values;
            }
        }

        void DrawDefaultView(List<MySprite> sprites, List<PowerEntry> entries, string maxLabel, string currentLabel)
        {
            var rowHeight = GRID_CELL_LINES * LINE * Scale;
            var viewportAvailableHeight = ViewBox.Height - (CaretY - ViewBox.Y) - FooterHeight;
            int maxRows = Math.Max(1, (int)Math.Floor(viewportAvailableHeight / rowHeight));

            int maxVisible = maxRows;
            bool shouldScroll = entries.Count > maxVisible;
            int startRow = 0;

            if (shouldScroll)
            {
                int totalRows = entries.Count;
                int totalSteps = Math.Max(1, totalRows - maxRows);
                int step = GetScrollStep(SCROLL_DELAY / 6);
                startRow = step % (totalSteps + 1);

                float viewportHeight = maxRows * rowHeight - (SCROLLER_WIDTH * 2 * Scale);
                float scrollBarHeight = (float)maxRows / totalRows * viewportHeight;
                float totalScrollableRows = totalRows - maxRows;
                float scrollFraction = totalScrollableRows > 0 ? startRow / totalScrollableRows : 0f;
                float scrollBarTravel = viewportHeight - scrollBarHeight;
                float scrollBarY = scrollFraction * scrollBarTravel;
                float scrollBarCenter = scrollBarY + scrollBarHeight / 2f;
                float initialY = CaretY + SCROLLER_WIDTH * Scale;

                DrawScrollBar(sprites, Scale, initialY, viewportHeight, scrollBarCenter, scrollBarHeight);
            }

            int start = startRow;
            int showCount = Math.Min(maxVisible, entries.Count - start);

            float margin = 0f;
            float contentStart = ViewBox.X + margin;
            float contentEnd = ViewBox.Width + ViewBox.X - margin;
            if (shouldScroll)
                contentEnd -= SCROLLER_WIDTH * Scale;

            if (AppConfig.DrawLines)
            {
                for (int row = 0; row <= maxRows; row++)
                {
                    var y = CaretY + row * rowHeight;
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Circle",
                        Position = new Vector2((contentStart + contentEnd) / 2f, y),
                        Size = new Vector2(contentEnd - contentStart, 2f),
                        Color = ForegroundColor,
                        Alignment = TextAlignment.CENTER
                    });
                }
            }

            for (int gridIdx = 0; gridIdx < showCount; gridIdx++)
            {
                int idx = start + gridIdx;
                int row = gridIdx;
                float yStart = CaretY + row * rowHeight;
                DrawGridPowerCell(sprites, entries[idx], contentStart, contentEnd, yStart, rowHeight, maxLabel,
                    currentLabel,
                    true);
            }
        }

        void DrawGridLike(List<MySprite> sprites, List<PowerEntry> entries, string maxLabel, string currentLabel,
            bool forceSingleColumn, bool drawLineSprites, bool drawVerticalLines, bool drawCellsAsLines)
        {
            var rowHeight = GRID_CELL_LINES * LINE * Scale;
            var viewportAvailableHeight = ViewBox.Height - (CaretY - ViewBox.Y) - FooterHeight;
            int maxRows = Math.Max(1, (int)Math.Floor(viewportAvailableHeight / rowHeight));
            int maxCols = forceSingleColumn ? 1 : Math.Max(1, GetMaxColsFromSurface());

            int maxVisible = maxRows * maxCols;
            bool shouldScroll = entries.Count > maxVisible;
            int startRow = 0;

            if (shouldScroll)
            {
                int totalRows = (int)Math.Ceiling(entries.Count / (float)maxCols);
                int totalSteps = Math.Max(1, totalRows - maxRows);
                int step = GetScrollStep(SCROLL_DELAY / 6);
                startRow = step % (totalSteps + 1);

                float viewportHeight = maxRows * rowHeight - (SCROLLER_WIDTH * 2 * Scale);
                float scrollBarHeight = (float)maxRows / totalRows * viewportHeight;
                float totalScrollableRows = totalRows - maxRows;
                float scrollFraction = totalScrollableRows > 0 ? startRow / totalScrollableRows : 0f;
                float scrollBarTravel = viewportHeight - scrollBarHeight;
                float scrollBarY = scrollFraction * scrollBarTravel;
                float scrollBarCenter = scrollBarY + scrollBarHeight / 2f;
                float initialY = CaretY + SCROLLER_WIDTH * Scale;

                DrawScrollBar(sprites, Scale, initialY, viewportHeight, scrollBarCenter, scrollBarHeight);
            }

            int start = startRow * maxCols;
            int showCount = Math.Min(maxVisible, entries.Count - start);

            float margin = 0f;
            float contentStart = ViewBox.X + margin;
            float contentEnd = ViewBox.Width + ViewBox.X - margin;
            if (shouldScroll)
                contentEnd -= SCROLLER_WIDTH * Scale;
            float columnWidth = (contentEnd - contentStart) / maxCols;
            float gridHeight = maxRows * rowHeight;

            if (drawLineSprites)
            {
                var lineColor = new Color(AppConfig.HeaderColor.R, AppConfig.HeaderColor.G, AppConfig.HeaderColor.B);
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

                if (drawVerticalLines)
                {
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
            }

            for (int gridIdx = 0; gridIdx < showCount; gridIdx++)
            {
                int idx = start + gridIdx;
                int col = gridIdx % maxCols;
                int row = gridIdx / maxCols;
                float xStart = contentStart + col * columnWidth;
                float xEnd = (col == maxCols - 1) ? contentEnd : xStart + columnWidth;
                float yStart = CaretY + row * rowHeight;
                DrawGridPowerCell(sprites, entries[idx], xStart, xEnd, yStart, rowHeight, maxLabel, currentLabel,
                    drawCellsAsLines);
            }
        }

        int GetMaxColsFromSurface()
        {
            var max = ViewBox.Width - ViewBox.X;
            var perCol = MINIMUM_COL_WIDTH * Scale;
            return (int)Math.Max(1, Math.Round(max / perCol - .5, MidpointRounding.AwayFromZero));
        }

        void DrawGridPowerCell(List<MySprite> sprites, PowerEntry entry, float xStart, float xEnd,
            float yStart, float rowHeight, string maxLabel, string currentLabel, bool drawAsLines)
        {
            var cellPadding = (LINE * Scale) / 2f;
            var cellView = GetCellViewBox(xStart, xEnd, yStart, rowHeight, cellPadding);
            var slots = GetCellSlots(cellView.X, cellView.Right, cellView.Y, cellView.Bottom, LINE);

            if (!drawAsLines)
            {
                var backgroundColor = entry.Current <= 0 ? AppConfig.ErrorColor : AppConfig.HeaderColor;
                var hsv = VRageMath.ColorExtensions.ColorToHSV(backgroundColor);
                hsv.Z *= 0.2f;

                var cellRect = new RectangleF(
                    xStart + cellPadding / 2f,
                    yStart + cellPadding / 2f,
                    (xEnd - xStart) - cellPadding,
                    rowHeight - cellPadding);
                var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
                Border.CreateSpritesFromRect(dropShadow, sprites, hsv.HSVtoColor(), .2f);
                Border.CreateSpritesFromRect(cellRect, sprites, backgroundColor, .2f);
            }

            var iconRect = slots.Item1;
            var numberRect = slots.Item2;
            var nameRect = slots.Item3;
            var foreground = entry.Current <= 0 && drawAsLines ? AppConfig.ErrorColor : Surface.ScriptForegroundColor;

            DrawCellPie(sprites, iconRect, entry.Usage);

            var titleSb = new StringBuilder(entry.Name);
            TrimText(ref titleSb, numberRect.Width);
            var titlePos = numberRect.Center;
            titlePos.X = numberRect.Right;
            titlePos.Y -= numberRect.Height * 0.5f;

            sprites.Add(new MySprite(
                SpriteType.TEXT,
                titleSb.ToString(),
                titlePos,
                null,
                foreground,
                "White",
                TextAlignment.RIGHT,
                1.1f * Scale * FontScale
            ));

            var info = new StringBuilder();
            info.AppendLine(maxLabel + FormatingHelper.WattsToString(entry.Max));
            info.AppendLine(currentLabel + FormatingHelper.WattsToString(entry.Current));
            info.AppendLine(entry.UsageLine);
            TrimText(ref info, nameRect.Width, 0.7f);

            var infoPos = nameRect.Center;
            infoPos.X = nameRect.Right;
            infoPos.Y -= nameRect.Height * 0.4f;

            sprites.Add(new MySprite(
                SpriteType.TEXT,
                info.ToString(),
                infoPos,
                null,
                foreground,
                "White",
                TextAlignment.RIGHT,
                .9f * Scale * FontScale
            ));
        }

        void DrawCellPie(List<MySprite> sprites, RectangleF iconRect, float usage)
        {
            var pieSize = new Vector2(iconRect.Width, iconRect.Height);
            var pieOrigo = new Vector2(iconRect.X + iconRect.Width / 2f, iconRect.Y + iconRect.Height / 2f);
            var margin = ToScreenMargin(pieOrigo);

            PieChartPanel.CreateSprites(
                sprites,
                string.Empty,
                (IMyTextSurface)Surface,
                margin,
                pieSize,
                usage,
                _ascentColor,
                true,
                false);
        }

        void DrawScrollBar(List<MySprite> frame, float scale, float initialY, float viewportHeight,
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
                new Color(AppConfig.HeaderColor.R, AppConfig.HeaderColor.G, AppConfig.HeaderColor.B, 250));
        }

        void DrawCapsule(List<MySprite> frame, Vector2 center, int width, float height, Color color)
        {
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = center,
                Size = new Vector2(width, height + .5f),
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            var capsSize = new Vector2(width);

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = new Vector2(center.X, center.Y - height / 2f),
                Size = capsSize,
                RotationOrScale = 0f,
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            frame.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = new Vector2(center.X, center.Y + height / 2f),
                Size = capsSize,
                RotationOrScale = (float)Math.PI,
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        protected static double ToWatts(float powerUnit)
        {
            return powerUnit * 1000000;
        }
    }
}
