using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Terminal.Controls;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using ColorExtensions = VRageMath.ColorExtensions;

namespace LcdMod.Client.SurfaceScripts.Abstract
{
    internal sealed class PercentageApp<TEntry>
    {
        const int LINE_HEIGHT = 40;
        const int SCROLL_DELAY = 12;
        readonly PercentageSurfaceScript<TEntry> _owner;

        public PercentageApp(PercentageSurfaceScript<TEntry> owner)
        {
            _owner = owner;
        }

        public List<MySprite> GetSprites()
        {
            var entries = new List<TEntry>();
            _owner.ReadEntriesInternal(entries);
            _owner.SortEntriesInternal(entries);
            if (entries.Count == 0)
                return null;

            var sprites = new List<MySprite>();
            _owner.AddBackgroundInternal(sprites);
            _owner.DrawTitleInternal(sprites);

            switch (_owner.DisplayModeInternal)
            {
                case (int)DisplayMode.Grid:
                    DrawGrid(sprites, entries);
                    break;
                default:
                    DrawList(sprites, entries);
                    break;
            }

            return sprites;
        }

        void DrawList(List<MySprite> sprites, List<TEntry> entries)
        {
            var rowHeight = LINE_HEIGHT * _owner.Proportion;
            var viewportAvailableHeight = _owner.ViewBox.Height - (_owner.CaretYInternal - _owner.ViewBox.Y) - _owner.FooterHeightInternal;
            int maxRows = Math.Max(1, (int)Math.Floor(viewportAvailableHeight / rowHeight));
            bool shouldScroll = entries.Count > maxRows;

            int start = 0;
            if (shouldScroll)
            {
                int totalSteps = Math.Max(1, entries.Count - maxRows);
                int step = GetScrollStep(SCROLL_DELAY / 6f);
                start = step % (totalSteps + 1);
            }

            int showCount = Math.Min(maxRows, entries.Count - start);
            for (int i = 0; i < showCount; i++)
                DrawRow(sprites, entries[start + i]);
        }

        void DrawGrid(List<MySprite> sprites, List<TEntry> entries)
        {
            var rowHeight = 2f * LINE_HEIGHT * _owner.Proportion;
            var viewportAvailableHeight = _owner.ViewBox.Height - (_owner.CaretYInternal - _owner.ViewBox.Y) - _owner.FooterHeightInternal;
            int maxRows = Math.Max(1, (int)Math.Floor(viewportAvailableHeight / rowHeight));
            int maxCols = Math.Max(1, _owner.GetMaxColsFromSurfaceInternal());
            int maxVisible = maxRows * maxCols;
            bool shouldScroll = entries.Count > maxVisible;
            int startRow = 0;

            if (shouldScroll)
            {
                int totalRows = (int)Math.Ceiling(entries.Count / (float)maxCols);
                int totalSteps = Math.Max(1, totalRows - maxRows);
                int step = GetScrollStep(SCROLL_DELAY / 6f);
                startRow = step % (totalSteps + 1);
            }

            int start = startRow * maxCols;
            int showCount = Math.Min(maxVisible, entries.Count - start);
            float contentStart = _owner.ViewBox.X;
            float contentEnd = _owner.ViewBox.Width + _owner.ViewBox.X;
            float columnWidth = (contentEnd - contentStart) / maxCols;
            float gridHeight = maxRows * rowHeight;

            if (_owner.DrawLinesInternal)
            {
                var lineColor = _owner.HeaderColorInternal;
                for (int row = 0; row <= maxRows; row++)
                {
                    var y = _owner.CaretYInternal + row * rowHeight;
                    sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2((contentStart + contentEnd) / 2f, y), Size = new Vector2(contentEnd - contentStart, 2f), Color = lineColor, Alignment = TextAlignment.CENTER });
                }
                for (int col = 0; col <= maxCols; col++)
                {
                    var x = contentStart + col * columnWidth;
                    sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(x, _owner.CaretYInternal + gridHeight / 2f), Size = new Vector2(2f, gridHeight), Color = lineColor, Alignment = TextAlignment.CENTER });
                }
            }

            for (int gridIdx = 0; gridIdx < showCount; gridIdx++)
            {
                int idx = start + gridIdx;
                int col = gridIdx % maxCols;
                int row = gridIdx / maxCols;
                float xStart = contentStart + col * columnWidth;
                float xEnd = (col == maxCols - 1) ? contentEnd : xStart + columnWidth;
                float yStart = _owner.CaretYInternal + row * rowHeight;
                DrawGridCell(sprites, entries[idx], xStart, xEnd, yStart, rowHeight);
            }
        }

        void DrawRow(List<MySprite> frame, TEntry entry)
        {
            Vector2 position = _owner.ViewBox.Position;
            position.Y = _owner.CaretYInternal;
            var pct = MathHelper.Clamp(_owner.GetEntryPercentageInternal(entry), 0f, 1f);

            if (_owner.DrawLinesInternal)
                frame.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(_owner.ViewBox.Center.X, position.Y), Size = new Vector2(_owner.ViewBox.Width, 2f), Color = _owner.ForegroundColorInternal, Alignment = TextAlignment.CENTER });

            var clip = new Rectangle((int)position.X, (int)position.Y, (int)(_owner.ViewBox.Width - position.X + _owner.ViewBox.X - 145 * _owner.Proportion), (int)(LINE_HEIGHT * _owner.Proportion));
            var barMargin = 8 * _owner.Proportion;
            Vector2 size = new Vector2(_owner.ViewBox.Width - position.X + _owner.ViewBox.X, clip.Height) - barMargin;

            BarPanel.CreateSprites(frame, new Vector2(clip.Location.X, clip.Location.Y + _owner.Proportion) + barMargin / 2f, size, _owner.GetEntryBarFillColorInternal(), _owner.GetEntryBarBackgroundColorInternal(), pct, _owner.GetEntryUsageColorInternal(pct));
            frame.Add(MySprite.CreateClipRect(clip));
            position.X += 16 * _owner.Proportion;
            position.Y += 4 * _owner.Proportion;
            frame.Add(new MySprite { Type = SpriteType.TEXT, Data = _owner.GetEntryNameInternal(entry), Position = position, RotationOrScale = _owner.Proportion * _owner.FontScaleInternal, Color = _owner.SurfaceInternal.ScriptForegroundColor, Alignment = TextAlignment.LEFT, FontId = "White" });
            frame.Add(MySprite.CreateClearClipRect());
            position.X = _owner.ViewBox.Width + _owner.ViewBox.X;
            frame.Add(new MySprite { Type = SpriteType.TEXT, Data = _owner.GetNumberInternal(pct), Position = position, RotationOrScale = _owner.Proportion * _owner.FontScaleInternal, Color = _owner.SurfaceInternal.ScriptForegroundColor, Alignment = TextAlignment.RIGHT, FontId = "White" });
            _owner.CaretYInternal += LINE_HEIGHT * _owner.Proportion;
        }

        void DrawGridCell(List<MySprite> frame, TEntry entry, float xStart, float xEnd, float yStart, float rowHeight)
        {
            var cellPadding = (LINE_HEIGHT * _owner.Proportion) / 3f;
            var pct = MathHelper.Clamp(_owner.GetEntryPercentageInternal(entry), 0f, 1f);
            var cellView = _owner.GetCellViewBoxInternal(xStart, xEnd, yStart, rowHeight, cellPadding);

            if (!_owner.DrawLinesInternal)
            {
                var backgroundColor = _owner.HeaderColorInternal;
                var hsv = ColorExtensions.ColorToHSV(backgroundColor);
                hsv.Z *= 0.2f;
                var cellRect = new RectangleF(xStart + cellPadding / 2f, yStart + cellPadding / 2f, (xEnd - xStart) - cellPadding, rowHeight - cellPadding);
                var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
                Border.CreateSpritesFromRect(dropShadow, frame, hsv.HSVtoColor(),
                    radiusScale: _owner.Proportion);
                Border.CreateSpritesFromRect(cellRect, frame, backgroundColor,
                    radiusScale: _owner.Proportion);
            }

            var nameHeight = Math.Max(0f, cellView.Height * .45f);
            var nameRect = new RectangleF(cellView.X, cellView.Y, cellView.Width, nameHeight);
            var bottomRect = new RectangleF(cellView.X, nameRect.Bottom, cellView.Width, Math.Max(0f, cellView.Bottom - nameRect.Bottom));
            var name = new StringBuilder(_owner.GetEntryNameInternal(entry) ?? string.Empty);
            _owner.TrimTextInternal(ref name, nameRect.Width);
            frame.Add(new MySprite { Type = SpriteType.TEXT, Data = name.ToString(), Position = new Vector2(nameRect.X + 2f * _owner.Proportion, nameRect.Y + 2f * _owner.Proportion), RotationOrScale = .9f * _owner.Proportion * _owner.FontScaleInternal, Color = _owner.SurfaceInternal.ScriptForegroundColor, Alignment = TextAlignment.LEFT, FontId = "White" });

            var barWidth = bottomRect.Width * (2f / 3f);
            var textRect = new RectangleF(bottomRect.X + barWidth, bottomRect.Y, bottomRect.Width - barWidth, bottomRect.Height);
            var barRect = new RectangleF(bottomRect.X, bottomRect.Y, barWidth, bottomRect.Height);
            var barInnerPaddingX = 2f * _owner.Proportion;
            var barInnerPaddingY = bottomRect.Height * 0.2f;
            var fillColor = Extensions.ColorExtensions.DeriveAccentColor(_owner.HeaderColorInternal, .4f, 0.5);
            BarPanel.CreateSprites(frame, new Vector2(barRect.X + barInnerPaddingX, barRect.Y + barInnerPaddingY + (2f * _owner.Proportion)), new Vector2(Math.Max(1f, barRect.Width - 2f * barInnerPaddingX), Math.Max(1f, barRect.Height - 2f * barInnerPaddingY)), fillColor, fillColor.DeriveAccentColor(.6f, 0.7), pct, _owner.GetEntryUsageColorInternal(pct));
            frame.Add(new MySprite { Type = SpriteType.TEXT, Data = _owner.GetNumberInternal(pct), Position = new Vector2(textRect.Right - (2f * _owner.Proportion), textRect.Y + 2f * _owner.Proportion), RotationOrScale = .95f * _owner.Proportion * _owner.FontScaleInternal, Color = _owner.SurfaceInternal.ScriptForegroundColor, Alignment = TextAlignment.RIGHT, FontId = "White" });
        }

        int GetScrollStep(float secondsPerStep)
        {
            try
            {
                var sess = MyAPIGateway.Session;
                if (sess == null)
                    return 0;
                if (secondsPerStep <= 0f)
                    secondsPerStep = 1f / 60f;
                int ticksPerStep = Math.Max(1, (int)Math.Round(secondsPerStep * 60f));
                return sess.GameplayFrameCounter / ticksPerStep;
            }
            catch
            {
                return 0;
            }
        }

    }
}
