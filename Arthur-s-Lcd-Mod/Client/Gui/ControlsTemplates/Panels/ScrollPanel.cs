using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    public sealed class ScrollPanel
    {
        ScrollPanel()
        {
        }

        public RectangleF ViewBox { get; private set; }
        public RectangleF ContentBounds { get; private set; }
        public float RowHeight { get; private set; }
        public float ScrollerWidthPixels { get; private set; }
        public int TotalRows { get; private set; }
        public int MaxVisibleRows { get; private set; }
        public int VisibleRows { get; private set; }
        public int StartRow { get; private set; }
        public bool IsScrollable { get; private set; }

        public static ScrollPanel Create(
            RectangleF viewBox,
            float contentTop,
            float footerHeight,
            float rowHeight,
            int totalRows,
            float scrollerWidthPixels,
            int scrollStep)
        {
            var panel = new ScrollPanel();
            panel.ViewBox = viewBox;
            panel.RowHeight = Math.Max(1f, rowHeight);
            panel.ScrollerWidthPixels = Math.Max(0f, scrollerWidthPixels);
            panel.TotalRows = Math.Max(0, totalRows);

            float availableHeight = Math.Max(0f, viewBox.Bottom - contentTop - Math.Max(0f, footerHeight));
            panel.MaxVisibleRows = Math.Max(1, (int)Math.Floor(availableHeight / panel.RowHeight));
            panel.IsScrollable = panel.TotalRows > panel.MaxVisibleRows;
            panel.VisibleRows = panel.TotalRows == 0 ? 0 : Math.Min(panel.TotalRows, panel.MaxVisibleRows);

            if (panel.IsScrollable)
            {
                int scrollableRows = Math.Max(1, panel.TotalRows - panel.MaxVisibleRows);
                panel.StartRow = scrollStep % (scrollableRows + 1);
            }

            float contentWidth = Math.Max(1f, viewBox.Width - (panel.IsScrollable ? panel.ScrollerWidthPixels : 0f));
            panel.ContentBounds = new RectangleF(
                viewBox.X,
                contentTop,
                contentWidth,
                panel.MaxVisibleRows * panel.RowHeight);

            return panel;
        }

        public int GetStartIndex(int columns)
        {
            return StartRow * Math.Max(1, columns);
        }

        public void RenderScrollBar(List<MySprite> sprites, Color trackColor, Color thumbColor)
        {
            if (!IsScrollable || sprites == null || TotalRows <= 0)
                return;

            float viewportHeight = Math.Max(1f, MaxVisibleRows * RowHeight - ScrollerWidthPixels * 2f);
            float scrollBarHeight = Math.Max(1f, (float)MaxVisibleRows / TotalRows * viewportHeight);
            float totalScrollableRows = TotalRows - MaxVisibleRows;
            float scrollFraction = totalScrollableRows > 0f ? StartRow / totalScrollableRows : 0f;
            float scrollBarTravel = Math.Max(0f, viewportHeight - scrollBarHeight);
            float scrollBarCenter = scrollFraction * scrollBarTravel + scrollBarHeight / 2f;
            float initialY = ContentBounds.Y + ScrollerWidthPixels;
            float barXCenter = ViewBox.X + ViewBox.Width - ScrollerWidthPixels / 2f;
            int barWidth = Math.Max(1, (int)ScrollerWidthPixels);

            var trackCenter = new Vector2(
                barXCenter,
                (float)Math.Round(initialY + viewportHeight / 2f, MidpointRounding.ToEven));
            DrawCapsule(sprites, trackCenter, barWidth, viewportHeight, trackColor);

            var thumbCenter = new Vector2(
                barXCenter,
                (float)Math.Round(initialY + scrollBarCenter, MidpointRounding.ToEven));
            DrawCapsule(sprites, thumbCenter, barWidth, scrollBarHeight, thumbColor);
        }

        static void DrawCapsule(List<MySprite> sprites, Vector2 center, int width, float height, Color color)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = center,
                Size = new Vector2(width, height + .5f),
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            var capsSize = new Vector2(width);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = new Vector2(center.X, center.Y - height / 2f),
                Size = capsSize,
                RotationOrScale = 0f,
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            sprites.Add(new MySprite
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
