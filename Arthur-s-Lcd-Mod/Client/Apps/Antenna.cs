using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Generated;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.Controls;
using LcdMod.Client.Gui.Models.Antenna;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Terminal.Controls.Groups;
using LcdMod.Client.Utility;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.ModAPI;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using LabelSeparator = LcdMod.Client.Terminal.Controls.Filter.LabelSeparator;
using SeparatorFilter = LcdMod.Client.Terminal.Controls.Filter.SeparatorFilter;
using SwitchToggleLines = LcdMod.Client.Terminal.Controls.Generic.SwitchToggleLines;

namespace LcdMod.Client.Apps
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class AntennaSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControl<SwitchToggleLines>,
        IUsesTerminalControl<SeparatorFilter>,
        IUsesTerminalControl<LabelSeparator>,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>,
        IMultiDisplayMode
    {
        protected override ConfigKind ConfigKind => ConfigKind.WithBlocks;
        const float LINE = 22f;
        const float MINIMUM_COL_WIDTH = 400f;
        const float SCROLLER_WIDTH = 8f;
        const int SCROLL_DELAY = 12;
        const float GRID_CELL_LINES = 6f;
        const float LASER_ICON_SOURCE_SIZE = 190f;
        const float LASER_ICON_TOP_PADDING = 64f;
        const float LASER_ICON_BOTTOM_PADDING = 22f;
        readonly List<AntennaEntry> _entries = new List<AntennaEntry>();
        readonly List<AntennaCollector> _collectors = new List<AntennaCollector>();

        public const string ID = "AntennaGraph";
        public const string TITLE = "Antenna";

        protected override string DefaultTitle => TITLE;

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.GridAndLegacy;
        }

        public AntennaSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        void BuildCollectors()
        {
            _collectors.Add(new LaserAntennaCollector(this));
            _collectors.Add(new RadioAntennaCollector(this));
            _collectors.Add(new BeaconCollector(this));
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _collectors.Clear();
        }

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            if (!_collectors.Any())
                BuildCollectors();

            BuildEntries(_entries);

            if (_entries.Count == 0)
            {
                Empty();
                return;
            }


            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                DrawFooter(sprites);

                switch (AppConfig.DisplayMode)
                {
                    case (int)DisplayMode.Grid:
                        DrawGridLike(sprites, _entries, false, AppConfig.DrawLines, AppConfig.DrawLines, AppConfig.DrawLines);
                        break;
                    default:
                        DrawDefaultView(sprites, _entries);
                        break;
                }


                frame.AddRange(sprites);
            }
        }

        void BuildEntries(List<AntennaEntry> entries)
        {
            entries.Clear();

            if (GridLogic == null)
                return;

            for (int i = 0; i < _collectors.Count; i++)
                _collectors[i].Collect(GridLogic, entries);

            entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        void DrawDefaultView(List<MySprite> sprites, List<AntennaEntry> entries)
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

            float margin = ViewBox.Width;
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
                DrawAntennaCell(sprites, entries[idx], contentStart, contentEnd, yStart, rowHeight, true);
            }
        }

        void DrawGridLike(List<MySprite> sprites, List<AntennaEntry> entries,
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
                DrawAntennaCell(sprites, entries[idx], xStart, xEnd, yStart, rowHeight, drawCellsAsLines);
            }
        }

        void DrawAntennaCell(List<MySprite> sprites, AntennaEntry entry, float xStart, float xEnd,
            float yStart, float rowHeight, bool drawAsLines)
        {
            var cellPadding = LINE * Scale / 2f;
            var cellView = GetCellViewBox(xStart, xEnd, yStart, rowHeight, cellPadding);
            var slots = GetCellSlots(cellView.X, cellView.Right, cellView.Y, cellView.Bottom, LINE);

            if (!drawAsLines)
            {
                var backgroundColor = !entry.IsFunctional ? AppConfig.ErrorColor : AppConfig.HeaderColor;
                var hsv = ColorExtensions.ColorToHSV(backgroundColor);
                hsv.Z *= 0.2f;

                var cellRect = new RectangleF(
                    xStart + cellPadding / 2f,
                    yStart + cellPadding / 2f,
                    (xEnd - xStart) - cellPadding,
                    rowHeight - cellPadding);
                var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
                RectanglePanel.CreateSpritesFromRect(dropShadow, sprites, hsv.HSVtoColor(), .2f);
                RectanglePanel.CreateSpritesFromRect(cellRect, sprites, backgroundColor, .2f);
            }

            var iconRect = slots.Item1;
            var numberRect = slots.Item2;
            var nameRect = slots.Item3;
            var foreground = drawAsLines ? entry.StatusColor : Surface.ScriptForegroundColor;

            var iconSize = new Vector2(iconRect.Width, iconRect.Height);
            var centeringOffsetY = 0f;

            var skipLaserOffset = string.Equals(entry.StatusIcon, "RotationPlane", StringComparison.Ordinal) ||
                                  string.Equals(entry.StatusIcon, "GridPower", StringComparison.Ordinal) ||
                                  string.Equals(entry.StatusIcon, "Search", StringComparison.Ordinal);

            if (entry.UseLaserIconCompensation && !skipLaserOffset)
            {
                // Laser antenna icons are vertically off-centered in their source texture.
                var sourceCenterOffset = (LASER_ICON_TOP_PADDING - LASER_ICON_BOTTOM_PADDING) * 0.5f;
                var normalizedCenterOffset = sourceCenterOffset / LASER_ICON_SOURCE_SIZE;
                centeringOffsetY = -(iconSize.Y * normalizedCenterOffset);
            }

            var scaledIconRightOverhang = Math.Max(0f, (iconSize.X - iconSize.X) * 0.5f);

            if (scaledIconRightOverhang > 0f)
            {
                numberRect = new RectangleF(
                    numberRect.X + scaledIconRightOverhang,
                    numberRect.Y,
                    Math.Max(0f, numberRect.Width - scaledIconRightOverhang),
                    numberRect.Height);
                nameRect = new RectangleF(
                    nameRect.X + scaledIconRightOverhang,
                    nameRect.Y,
                    Math.Max(0f, nameRect.Width - scaledIconRightOverhang),
                    nameRect.Height);
            }

            var iconPos = new Vector2(
                iconRect.X,
                iconRect.Y + iconRect.Height / 2f + centeringOffsetY);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = entry.StatusIcon,
                Position = iconPos,
                Size = iconSize,
                Alignment = TextAlignment.LEFT,
                Color = entry.StatusColor
            });

            var titleSb = new StringBuilder(entry.Name ?? string.Empty);
            var titleTrimWidth = Math.Max(0f, numberRect.Width - (4f * Scale));
            TrimText(ref titleSb, titleTrimWidth, 1.1f);
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
            var statusText = entry.StatusText ?? string.Empty;
            var statusLines = statusText.Split('\n');
            var infoTrimWidth = Math.Max(0f, nameRect.Width - (6f * Scale));
            for (int i = 0; i < statusLines.Length; i++)
            {
                var line = statusLines[i].TrimEnd('\r');
                var lineSb = new StringBuilder(line);
                TrimText(ref lineSb, infoTrimWidth, 0.9f);
                info.AppendLine(lineSb.ToString());
            }

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

        int GetMaxColsFromSurface()
        {
            var max = ViewBox.Width - ViewBox.X;
            var perCol = MINIMUM_COL_WIDTH * Scale;
            return (int)Math.Max(1, Math.Round(max / perCol - .5, MidpointRounding.AwayFromZero));
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
    }
}
