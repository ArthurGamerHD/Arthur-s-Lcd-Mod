using System;
using System.Collections.Generic;
using System.Text;
using Generated;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.Controls;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using ColorExtensions = VRageMath.ColorExtensions;

namespace LcdMod.Client.Apps.Abstract
{
    public abstract partial class PercentageSurfaceScript<TEntry> : SurfaceScriptBase, IMultiDisplayMode
    {
        protected override ConfigKind ConfigKind => ConfigKind.Colorable;
        protected const int SCROLLER_WIDTH = 8;
        protected const int LINE_HEIGHT = 40;
        protected const int MINIMUM_COL_WIDTH = 220;
        protected const int SCROLL_DELAY = 12;

        Color _scriptForegroundColor;

        protected PercentageSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface,
            block, size)
        {
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes() => DisplayModes.GridAndLegacy;

        public override void SafeRun()
        {
            if (AppConfig == null) return;

            if (_scriptForegroundColor != Surface.ScriptForegroundColor)
                LayoutChanged();

            Scale = GetAutoScaleUniform();
            UpdateViewBox();

            var entries = BuildEntries();
            if (entries.Count == 0)
            {
                Empty();
                return;
            }

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();

                AddBackground(sprites);
                DrawTitle(sprites);

                switch (AppConfig.DisplayMode)
                {
                    case (int)DisplayMode.Grid:
                        DrawGrid(sprites, entries);
                        break;
                    default:
                        DrawList(sprites, entries);
                        break;
                }

                frame.AddRange(sprites);
            }
        }

        protected override void LayoutChanged()
        {
            _scriptForegroundColor = Surface.ScriptForegroundColor;
            base.LayoutChanged();
        }

        List<TEntry> BuildEntries()
        {
            var entries = new List<TEntry>();
            ReadEntries(entries);
            SortEntries(entries);
            return entries;
        }

        protected abstract void ReadEntries(List<TEntry> entries);

        protected virtual void SortEntries(List<TEntry> entries)
        {
        }

        protected abstract string GetEntryName(TEntry entry);

        protected abstract float GetEntryPercentage(TEntry entry);

        protected virtual Color? GetEntryUsageColor(float pct)
        {
            return null;
        }

        protected virtual string GetNumber(float pct)
        {
            return FormatingHelper.PercentageToString(pct);
        }

        protected virtual Color GetEntryBarFillColor()
        {
            return AppConfig.HeaderColor;
        }

        protected virtual Color GetEntryBarBackgroundColor()
        {
            return BackgroundColor.DeriveAccentColor();
        }

        void DrawRow(List<MySprite> frame, TEntry entry, bool showScrollBar)
        {
            var margin = 0f;
            Vector2 position = ViewBox.Position;
            position.X += margin;
            position.Y = CaretY;

            var pct = MathHelper.Clamp(GetEntryPercentage(entry), 0f, 1f);

            if (AppConfig.DrawLines)
            {
                frame.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(ViewBox.Center.X, position.Y),
                    Size = new Vector2(ViewBox.Width - 2f * margin, 2f),
                    Color = ForegroundColor,
                    Alignment = TextAlignment.CENTER
                });
            }

            var clip = new Rectangle((int)position.X, (int)position.Y,
                (int)(ViewBox.Width - position.X + (ViewBox.X) - 145 * Scale),
                (int)(LINE_HEIGHT * Scale));

            var barMargin = 8 * Scale;
            Vector2 size;
            if (showScrollBar)
                size = new Vector2(ViewBox.Width - position.X + (ViewBox.X) - SCROLLER_WIDTH * Scale, clip.Height) -
                       barMargin;
            else
                size = new Vector2(ViewBox.Width - position.X + (ViewBox.X), clip.Height) - barMargin;

            BarPanel.CreateSprites(
                frame,
                new Vector2(clip.Location.X, clip.Location.Y + 1 * Scale) + barMargin / 2f,
                size,
                GetEntryBarFillColor(),
                GetEntryBarBackgroundColor(),
                pct,
                GetEntryUsageColor(pct));

            frame.Add(MySprite.CreateClipRect(clip));

            position.X += 16 * Scale;
            position.Y += 4 * Scale;

            var text = new MySprite
            {
                Type = SpriteType.TEXT,
                Data = GetEntryName(entry),
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            };

            frame.Add(text);

            frame.Add(MySprite.CreateClearClipRect());

            position.X = ViewBox.Width + ViewBox.X - margin;
            if (showScrollBar)
                position.X -= SCROLLER_WIDTH * Scale;

            var percentage = new MySprite
            {
                Type = SpriteType.TEXT,
                Data = GetNumber(pct),
                Position = position,
                RotationOrScale = Scale * FontScale,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            };

            frame.Add(percentage);
            CaretY += LINE_HEIGHT * Scale;
        }

        void DrawList(List<MySprite> sprites, List<TEntry> entries)
        {
            var rowHeight = LINE_HEIGHT * Scale;
            var viewportAvailableHeight = ViewBox.Height - (CaretY - ViewBox.Y) - FooterHeight;
            int maxRows = Math.Max(1, (int)Math.Floor(viewportAvailableHeight / rowHeight));
            bool shouldScroll = entries.Count > maxRows;

            int start = 0;
            if (shouldScroll)
            {
                int totalSteps = Math.Max(1, entries.Count - maxRows);
                int step = GetScrollStep(SCROLL_DELAY / 6);
                start = step % (totalSteps + 1);

                float viewportHeight = maxRows * rowHeight - (SCROLLER_WIDTH * 2 * Scale);
                float scrollBarHeight = (float)maxRows / entries.Count * viewportHeight;
                float totalScrollableRows = entries.Count - maxRows;
                float scrollFraction = totalScrollableRows > 0 ? start / totalScrollableRows : 0f;
                float scrollBarTravel = viewportHeight - scrollBarHeight;
                float scrollBarY = scrollFraction * scrollBarTravel;
                float scrollBarCenter = scrollBarY + scrollBarHeight / 2f;
                float initialY = CaretY + SCROLLER_WIDTH * Scale;

                DrawScrollBar(sprites, Scale, initialY, viewportHeight, scrollBarCenter, scrollBarHeight);
            }

            int showCount = Math.Min(maxRows, entries.Count - start);
            for (int i = 0; i < showCount; i++)
            {
                int idx = start + i;
                DrawRow(sprites, entries[idx], shouldScroll);
            }
        }

        void DrawGrid(List<MySprite> sprites, List<TEntry> entries)
        {
            var rowHeight = 2f * LINE_HEIGHT * Scale;
            var viewportAvailableHeight = ViewBox.Height - (CaretY - ViewBox.Y) - FooterHeight;
            int maxRows = Math.Max(1, (int)Math.Floor(viewportAvailableHeight / rowHeight));
            int maxCols = Math.Max(1, GetMaxColsFromSurface());

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

            if (AppConfig.DrawLines)
            {
                var lineColor =  AppConfig.HeaderColor;
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

            for (int gridIdx = 0; gridIdx < showCount; gridIdx++)
            {
                int idx = start + gridIdx;
                int col = gridIdx % maxCols;
                int row = gridIdx / maxCols;
                float xStart = contentStart + col * columnWidth;
                float xEnd = (col == maxCols - 1) ? contentEnd : xStart + columnWidth;
                float yStart = CaretY + row * rowHeight;
                DrawGridCell(sprites, entries[idx], xStart, xEnd, yStart, rowHeight);
            }
        }

        void DrawGridCell(List<MySprite> frame, TEntry entry, float xStart, float xEnd, float yStart, float rowHeight)
        {
            var cellPadding = (LINE_HEIGHT * Scale) / 3f;
            var pct = MathHelper.Clamp(GetEntryPercentage(entry), 0f, 1f);
            var cellView = GetCellViewBox(xStart, xEnd, yStart, rowHeight, cellPadding);

            if (!AppConfig.DrawLines)
            {
                var backgroundColor =  AppConfig.HeaderColor;
                var hsv = ColorExtensions.ColorToHSV(backgroundColor);
                hsv.Z *= 0.2f;
                var cellRect = new RectangleF(
                    xStart + cellPadding / 2f,
                    yStart + cellPadding / 2f,
                    (xEnd - xStart) - cellPadding,
                    rowHeight - cellPadding);
                var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
                RectanglePanel.CreateSpritesFromRect(dropShadow, frame, hsv.HSVtoColor(), .2f);
                RectanglePanel.CreateSpritesFromRect(cellRect, frame, backgroundColor, .2f);
            }

            var nameHeight = Math.Max(0f, cellView.Height * .45f);
            var nameRect = new RectangleF(cellView.X, cellView.Y, cellView.Width, nameHeight);
            var bottomRect = new RectangleF(cellView.X, nameRect.Bottom, cellView.Width,
                Math.Max(0f, cellView.Bottom - nameRect.Bottom));

            var name = new StringBuilder(GetEntryName(entry) ?? string.Empty);
            TrimText(ref name, nameRect.Width);

            var namePos = new Vector2(nameRect.X + 2f * Scale, nameRect.Y + 2f * Scale);
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = name.ToString(),
                Position = namePos,
                RotationOrScale = .9f * Scale * FontScale,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
            });

            var barWidth = bottomRect.Width * (2f / 3f);
            var textRect = new RectangleF(bottomRect.X + barWidth, bottomRect.Y, bottomRect.Width - barWidth,
                bottomRect.Height);
            var barRect = new RectangleF(bottomRect.X, bottomRect.Y, barWidth, bottomRect.Height);

            var barInnerPaddingX = 2f * Scale;
            var barInnerPaddingY = bottomRect.Height * 0.2f;
            var fillColor = Extensions.ColorExtensions.DeriveAccentColor(AppConfig.HeaderColor, .4f, 0.5);
            BarPanel.CreateSprites(
                frame,
                new Vector2(barRect.X + barInnerPaddingX, barRect.Y + barInnerPaddingY + (2f * Scale)),
                new Vector2(
                    Math.Max(1f, barRect.Width - 2f * barInnerPaddingX),
                    Math.Max(1f, barRect.Height - 2f * barInnerPaddingY)),
                 fillColor,
                 fillColor.DeriveAccentColor(.6f, 0.7),
                pct,
                GetEntryUsageColor(pct));

            var pctText = GetNumber(pct);
            var pctPos = new Vector2(textRect.Right - (2f * Scale), textRect.Y + 2f * Scale);
            frame.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = pctText,
                Position = pctPos,
                RotationOrScale = .95f * Scale * FontScale,
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });
        }

        protected virtual int GetMaxColsFromSurface()
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
                new Color(AppConfig.HeaderColor.R,  AppConfig.HeaderColor.G,  AppConfig.HeaderColor.B, 250));
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
