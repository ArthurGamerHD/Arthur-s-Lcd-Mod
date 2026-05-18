using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Grid;
using LcdMod.Client.Helpers;
using LcdMod.Client.Gui.Controls;
using LcdMod.Client.Gui.Models.Antenna;
using LcdMod.Client.SurfaceScripts;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;
using VRageMath;
using ScreenConfigWithBlocks = LcdMod.Common.Config.Models.Apps.ScreenConfigWithBlocks;

namespace LcdMod.Client.Apps
{
    public sealed class AntennaApp : AppBase
    {
        const float LINE = 22f;
        const float MINIMUM_COL_WIDTH = 400f;
        const float SCROLLER_WIDTH = 8f;
        const int SCROLL_DELAY = 12;
        const float GRID_CELL_LINES = 6f;
        const float LASER_ICON_SOURCE_SIZE = 190f;
        const float LASER_ICON_TOP_PADDING = 64f;
        const float LASER_ICON_BOTTOM_PADDING = 22f;

        public ScreenConfigWithBlocks Config => (ScreenConfigWithBlocks)AppConfig;
        readonly List<AntennaEntry> _entries = new List<AntennaEntry>();
        readonly List<AntennaCollector> _collectors = new List<AntennaCollector>();
        public bool HasEntries => _entries.Count > 0;

        public AntennaApp(ScreenConfigWithBlocks config, SurfaceScriptBase script) : base(config, script)
        {
        }

        public override void Update()
        {
            if (_collectors.Count == 0)
                BuildCollectors();

            _entries.Clear();
            var gridLogic = Host.GridLogic;
            if (gridLogic == null)
                return;

            for (int i = 0; i < _collectors.Count; i++)
                _collectors[i].Collect(gridLogic, _entries);

            _entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();

            switch (Config.DisplayMode)
            {
                case (int)DisplayMode.Grid:
                    DrawGridLike(sprites, false, Config.DrawLines, Config.DrawLines, Config.DrawLines);
                    break;
                default:
                    DrawDefaultView(sprites);
                    break;
            }

            return sprites;
        }

        void BuildCollectors()
        {
            _collectors.Add(new LaserAntennaCollector(Host));
            _collectors.Add(new RadioAntennaCollector(Host));
            _collectors.Add(new BeaconCollector(Host));
        }

        void DrawDefaultView(List<MySprite> sprites)
        {
            var rowHeight = GRID_CELL_LINES * LINE * Host.Scale;
            float caretY = GetContentTop();
            float footerHeight = GetFooterHeight();
            var viewportAvailableHeight = Host.ViewBox.Height - (caretY - Host.ViewBox.Y) - footerHeight;
            int maxRows = Math.Max(1, (int)Math.Floor(viewportAvailableHeight / rowHeight));

            int maxVisible = maxRows;
            bool shouldScroll = _entries.Count > maxVisible;
            int startRow = 0;

            if (shouldScroll)
            {
                int totalRows = _entries.Count;
                int totalSteps = Math.Max(1, totalRows - maxRows);
                int step = GetScrollStep(SCROLL_DELAY / 6);
                startRow = step % (totalSteps + 1);

                float viewportHeight = maxRows * rowHeight - (SCROLLER_WIDTH * 2 * Host.Scale);
                float scrollBarHeight = (float)maxRows / totalRows * viewportHeight;
                float totalScrollableRows = totalRows - maxRows;
                float scrollFraction = totalScrollableRows > 0 ? startRow / totalScrollableRows : 0f;
                float scrollBarTravel = viewportHeight - scrollBarHeight;
                float scrollBarY = scrollFraction * scrollBarTravel;
                float scrollBarCenter = scrollBarY + scrollBarHeight / 2f;
                float initialY = caretY + SCROLLER_WIDTH * Host.Scale;

                DrawScrollBar(sprites, Host.Scale, initialY, viewportHeight, scrollBarCenter, scrollBarHeight);
            }

            int start = startRow;
            int showCount = Math.Min(maxVisible, _entries.Count - start);

            float margin = 0f;
            float contentStart = Host.ViewBox.X + margin;
            float contentEnd = Host.ViewBox.Width + Host.ViewBox.X - margin;
            if (shouldScroll)
                contentEnd -= SCROLLER_WIDTH * Host.Scale;

            if (Config.DrawLines)
            {
                for (int row = 0; row <= maxRows; row++)
                {
                    var y = caretY + row * rowHeight;
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Circle",
                        Position = new Vector2((contentStart + contentEnd) / 2f, y),
                        Size = new Vector2(contentEnd - contentStart, 2f),
                        Color = Host.ForegroundColor,
                        Alignment = TextAlignment.CENTER
                    });
                }
            }

            for (int gridIdx = 0; gridIdx < showCount; gridIdx++)
            {
                int idx = start + gridIdx;
                float yStart = caretY + gridIdx * rowHeight;
                DrawAntennaCell(sprites, _entries[idx], contentStart, contentEnd, yStart, rowHeight, true);
            }
        }

        void DrawGridLike(List<MySprite> sprites, bool forceSingleColumn, bool drawLineSprites, bool drawVerticalLines, bool drawCellsAsLines)
        {
            var rowHeight = GRID_CELL_LINES * LINE * Host.Scale;
            float caretY = GetContentTop();
            float footerHeight = GetFooterHeight();
            var viewportAvailableHeight = Host.ViewBox.Height - (caretY - Host.ViewBox.Y) - footerHeight;
            int maxRows = Math.Max(1, (int)Math.Floor(viewportAvailableHeight / rowHeight));
            int maxCols = forceSingleColumn ? 1 : Math.Max(1, GetMaxColsFromSurface());

            int maxVisible = maxRows * maxCols;
            bool shouldScroll = _entries.Count > maxVisible;
            int startRow = 0;

            if (shouldScroll)
            {
                int totalRows = (int)Math.Ceiling(_entries.Count / (float)maxCols);
                int totalSteps = Math.Max(1, totalRows - maxRows);
                int step = GetScrollStep(SCROLL_DELAY / 6);
                startRow = step % (totalSteps + 1);

                float viewportHeight = maxRows * rowHeight - (SCROLLER_WIDTH * 2 * Host.Scale);
                float scrollBarHeight = (float)maxRows / totalRows * viewportHeight;
                float totalScrollableRows = totalRows - maxRows;
                float scrollFraction = totalScrollableRows > 0 ? startRow / totalScrollableRows : 0f;
                float scrollBarTravel = viewportHeight - scrollBarHeight;
                float scrollBarY = scrollFraction * scrollBarTravel;
                float scrollBarCenter = scrollBarY + scrollBarHeight / 2f;
                float initialY = caretY + SCROLLER_WIDTH * Host.Scale;

                DrawScrollBar(sprites, Host.Scale, initialY, viewportHeight, scrollBarCenter, scrollBarHeight);
            }

            int start = startRow * maxCols;
            int showCount = Math.Min(maxVisible, _entries.Count - start);

            float contentStart = Host.ViewBox.X;
            float contentEnd = Host.ViewBox.Width + Host.ViewBox.X;
            if (shouldScroll)
                contentEnd -= SCROLLER_WIDTH * Host.Scale;
            float columnWidth = (contentEnd - contentStart) / maxCols;
            float gridHeight = maxRows * rowHeight;

            if (drawLineSprites)
            {
                var lineColor = new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B);
                for (int row = 0; row <= maxRows; row++)
                {
                    var y = caretY + row * rowHeight;
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
                            Position = new Vector2(x, caretY + gridHeight / 2f),
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
                float yStart = caretY + row * rowHeight;
                DrawAntennaCell(sprites, _entries[idx], xStart, xEnd, yStart, rowHeight, drawCellsAsLines);
            }
        }

        int GetMaxColsFromSurface()
        {
            var max = Host.ViewBox.Width - Host.ViewBox.X;
            var perCol = MINIMUM_COL_WIDTH * Host.Scale;
            return (int)Math.Max(1, Math.Round(max / perCol - .5, MidpointRounding.AwayFromZero));
        }

        float GetContentTop()
        {
            if (!Host.TitleVisible)
                return Host.ViewBox.Y;

            float layoutScale = Host.Scale * Host.Surface.FontSize;
            return Host.ViewBox.Y + (40f * layoutScale);
        }

        static float GetFooterHeight() => 0f;

        static int GetScrollStep(float secondsPerStep)
        {
            try
            {
                var sess = MyAPIGateway.Session;
                if (sess == null)
                    return 0;

                if (secondsPerStep <= 0f)
                    secondsPerStep = 1f / 60f;

                int ticksPerStep = Math.Max(1, (int)Math.Round(secondsPerStep * 60f));
                return (int)(sess.GameplayFrameCounter / ticksPerStep);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"[LcdMod] AntennaApp.GetScrollStep error: {ex.Message}");
                return 0;
            }
        }

        void DrawAntennaCell(List<MySprite> sprites, AntennaEntry entry, float xStart, float xEnd, float yStart, float rowHeight, bool drawAsLines)
        {
            var cellPadding = LINE * Host.Scale / 2f;
            var innerLeft = xStart + cellPadding;
            var innerRight = xEnd - cellPadding;
            var innerTop = yStart + cellPadding;
            var innerBottom = yStart + rowHeight - cellPadding;
            var topRowHeight = LINE * Host.Scale;
            var bottomRowTop = innerTop + topRowHeight;
            var bottomRowHeight = Math.Max(0f, innerBottom - bottomRowTop);
            var iconSize = innerBottom - innerTop;
            var contentLeft = innerLeft + iconSize;
            var contentWidth = Math.Max(0f, innerRight - contentLeft);

            var iconRect = new RectangleF(innerLeft, innerTop, iconSize, iconSize);
            var numberRect = new RectangleF(contentLeft, innerTop, contentWidth, topRowHeight);
            var nameRect = new RectangleF(contentLeft, bottomRowTop, contentWidth, bottomRowHeight);

            if (!drawAsLines)
            {
                var backgroundColor = !entry.IsFunctional ? Config.ErrorColor : Config.HeaderColor;
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

            var foreground = drawAsLines ? entry.StatusColor : Host.Surface.ScriptForegroundColor;
            var iconSizeVector = new Vector2(iconRect.Width, iconRect.Height);
            var centeringOffsetY = 0f;
            var skipLaserOffset = string.Equals(entry.StatusIcon, "RotationPlane", StringComparison.Ordinal)
                                  || string.Equals(entry.StatusIcon, "GridPower", StringComparison.Ordinal)
                                  || string.Equals(entry.StatusIcon, "Search", StringComparison.Ordinal);

            if (entry.UseLaserIconCompensation && !skipLaserOffset)
            {
                var sourceCenterOffset = (LASER_ICON_TOP_PADDING - LASER_ICON_BOTTOM_PADDING) * 0.5f;
                var normalizedCenterOffset = sourceCenterOffset / LASER_ICON_SOURCE_SIZE;
                centeringOffsetY = -(iconSizeVector.Y * normalizedCenterOffset);
            }

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = entry.StatusIcon,
                Position = new Vector2(iconRect.X, iconRect.Y + iconRect.Height / 2f + centeringOffsetY),
                Size = iconSizeVector,
                Alignment = TextAlignment.LEFT,
                Color = entry.StatusColor
            });

            var titleSb = new StringBuilder(entry.Name ?? string.Empty);
            TrimText(titleSb, Math.Max(0f, numberRect.Width - (4f * Host.Scale)), 1.1f);
            var titlePos = numberRect.Center;
            titlePos.X = numberRect.Right;
            titlePos.Y -= numberRect.Height * 0.5f;
            sprites.Add(new MySprite(SpriteType.TEXT, titleSb.ToString(), titlePos, null, foreground, "White",
                TextAlignment.RIGHT, 1.1f * Host.Scale * Host.Surface.FontSize));

            var info = new StringBuilder();
            var lines = (entry.StatusText ?? string.Empty).Split('\n');
            var infoTrimWidth = Math.Max(0f, nameRect.Width - (6f * Host.Scale));
            for (int i = 0; i < lines.Length; i++)
            {
                var lineSb = new StringBuilder(lines[i].TrimEnd('\r'));
                TrimText(lineSb, infoTrimWidth, 0.9f);
                info.AppendLine(lineSb.ToString());
            }

            var infoPos = nameRect.Center;
            infoPos.X = nameRect.Right;
            infoPos.Y -= nameRect.Height * 0.4f;
            sprites.Add(new MySprite(SpriteType.TEXT, info.ToString(), infoPos, null, foreground, "White",
                TextAlignment.RIGHT, .9f * Host.Scale * Host.Surface.FontSize));
        }

        void TrimText(StringBuilder sb, float availableWidth, float fontSize = 1f)
        {
            var textSize = Host.Surface.MeasureStringInPixels(sb, "White", fontSize * Host.Scale * Host.Surface.FontSize);
            if (textSize.X <= availableWidth)
                return;

            var source = sb.ToString();
            for (int i = source.Length - 1; i > 0; i--)
            {
                sb.Clear();
                sb.Append(FormatingHelper.TrimName(source, i));
                textSize = Host.Surface.MeasureStringInPixels(sb, "White", fontSize * Host.Scale * Host.Surface.FontSize);
                if (textSize.X <= availableWidth)
                    break;
            }
        }

        void DrawScrollBar(List<MySprite> frame, float scale, float initialY, float viewportHeight, float scrollBarCenter, float scrollBarHeight)
        {
            float barXCenter = Host.ViewBox.X + Host.ViewBox.Width - (SCROLLER_WIDTH / 2f) * scale;
            int barWidth = (int)(SCROLLER_WIDTH * scale);

            var trackCenter = new Vector2(barXCenter, (float)Math.Round(initialY + viewportHeight / 2f, MidpointRounding.ToEven));
            DrawCapsule(frame, trackCenter, barWidth, viewportHeight,
                new Color(Host.Surface.ScriptForegroundColor.R, Host.Surface.ScriptForegroundColor.G,
                    Host.Surface.ScriptForegroundColor.B, 127));

            var thumbCenter = new Vector2(barXCenter, (float)Math.Round(initialY + scrollBarCenter, MidpointRounding.ToEven));
            DrawCapsule(frame, thumbCenter, barWidth, scrollBarHeight,
                new Color(Config.HeaderColor.R, Config.HeaderColor.G, Config.HeaderColor.B, 250));
        }

        static void DrawCapsule(List<MySprite> frame, Vector2 center, int width, float height, Color color)
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
