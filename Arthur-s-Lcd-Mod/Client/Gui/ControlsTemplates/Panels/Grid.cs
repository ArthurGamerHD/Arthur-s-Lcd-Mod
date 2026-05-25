using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    /// <summary>
    /// Layout panel that places children into explicit percentage rows/columns.
    /// It draws a SquareSimple background using a transparent color by default,
    /// so callers can opt into a visible background by overriding BackgroundColor.
    /// </summary>
    public sealed class Grid : Panel
    {
        public Grid() : this(default(RectangleF))
        {
        }

        public Grid(RectangleF bounds, float[] columns = null, float[] rows = null)
            : base(bounds)
        {
            Columns = columns;
            Rows = rows;
            BackgroundTexture = "SquareSimple";
            BackgroundColor = Color.Transparent;
        }

        public float[] Columns { get; set; }
        public float[] Rows { get; set; }
        public string BackgroundTexture { get; set; }
        public Color BackgroundColor { get; set; }

        public void SetColumns(params float[] columns)
        {
            Columns = columns;
            ArrangeChildren();
        }

        public void SetRows(params float[] rows)
        {
            Rows = rows;
            ArrangeChildren();
        }

        public RectangleF GetCellBounds(int childIndex)
        {
            int columnCount = GetSegmentCount(Columns);
            int rowCount = GetSegmentCount(Rows);
            int totalCells = Math.Max(1, columnCount * rowCount);
            int index = Clamp(childIndex, 0, totalCells - 1);
            int row = index / columnCount;
            int column = index % columnCount;

            float x = GetSegmentStart(Rect.X, Rect.Width, Columns, column, columnCount);
            float y = GetSegmentStart(Rect.Y, Rect.Height, Rows, row, rowCount);
            float right = GetSegmentEnd(Rect.X, Rect.Width, Columns, column, columnCount);
            float bottom = GetSegmentEnd(Rect.Y, Rect.Height, Rows, row, rowCount);

            return new RectangleF(x, y, Math.Max(0f, right - x), Math.Max(0f, bottom - y));
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            ArrangeChildren();
            DrawBackground(sprites);
            RenderChildren(context, sprites);
        }

        protected override void ArrangeChildren()
        {
            var children = Children;
            if (children == null)
                return;

            for (int i = 0; i < children.Count; i++)
                SetChildBounds(children[i], GetCellBounds(i));
        }

        void DrawBackground(List<MySprite> sprites)
        {
            if (sprites == null || string.IsNullOrEmpty(BackgroundTexture) || Rect.Width <= 0f || Rect.Height <= 0f)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = BackgroundTexture,
                Position = Rect.Center,
                Size = Rect.Size,
                Color = BackgroundColor,
                Alignment = TextAlignment.CENTER
            });
        }

        static void SetChildBounds(ControlBase child, RectangleF bounds)
        {
            if (child == null)
                return;

            var rectangle = child as RectangleControl;
            if (rectangle != null)
            {
                rectangle.SetRect(bounds);
                return;
            }

            var panel = child as Panel;
            if (panel != null)
            {
                panel.SetRect(bounds);
            }
        }

        static int GetSegmentCount(float[] segments)
        {
            return segments == null || segments.Length == 0 ? 1 : segments.Length;
        }

        static float GetSegmentStart(float origin, float size, float[] segments, int index, int count)
        {
            if (segments == null || segments.Length == 0)
                return origin;

            float total = GetPositiveTotal(segments);
            if (total <= 0f)
                return origin + size * index / count;

            float offset = 0f;
            for (int i = 0; i < index && i < segments.Length; i++)
                offset += Math.Max(0f, segments[i]) / total * size;

            return origin + offset;
        }

        static float GetSegmentEnd(float origin, float size, float[] segments, int index, int count)
        {
            if (segments == null || segments.Length == 0)
                return origin + size;

            if (index >= count - 1)
                return origin + size;

            return GetSegmentStart(origin, size, segments, index + 1, count);
        }

        static float GetPositiveTotal(float[] segments)
        {
            if (segments == null)
                return 0f;

            float total = 0f;
            for (int i = 0; i < segments.Length; i++)
                total += Math.Max(0f, segments[i]);

            return total;
        }

        static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
