using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using ArgumentOutOfRangeException = LcdMod.Common.ArgumentOutOfRangeException;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    /// <summary>
    /// Layout panel that places children into explicit percentage rows/columns.
    /// It draws a SquareSimple background using a transparent color by default,
    /// so callers can opt into a visible background by overriding BackgroundColor.
    /// </summary>
    public sealed partial class Grid : Panel
    {
        public new static readonly StyleProperty<Color> BackgroundColorProperty =
            StyleProperty.Register<Grid, Color>("BackgroundColor", (Color?)Color.Transparent);

        sealed class GridPlacement
        {
            public int Column;
            public int Row;
            public int ColumnSpan = 1;
            public int RowSpan = 1;
        }

        readonly Dictionary<ControlTemplate, GridPlacement> _placements =
            new Dictionary<ControlTemplate, GridPlacement>();

        public Grid() : this(default(RectangleF))
        {
        }

        public Grid(RectangleF bounds, float[] columns = null, float[] rows = null)
            : base(bounds)
        {
            Columns = columns;
            Rows = rows;
            BackgroundTexture = "SquareSimple";
        }

        public Grid(ControlTemplate parent, int cols, int rows)
            : this(default(RectangleF), CreateEqualSegments(cols), CreateEqualSegments(rows))
        {
            AttachTo(parent);
        }

        public Grid(ControlTemplate parent, RectangleF bounds, int cols, int rows)
            : this(bounds, CreateEqualSegments(cols), CreateEqualSegments(rows))
        {
            AttachTo(parent);
        }

        public float[] Columns { get; set; }
        public float[] Rows { get; set; }
        public string BackgroundTexture { get; set; }

        protected override Color GetRenderBackgroundColor()
        {
            return base.BackgroundColor;
        }

        public void SetColumns(params float[] columns)
        {
            Columns = columns;
            InvalidateLayout();
        }

        public void SetRows(params float[] rows)
        {
            Rows = rows;
            InvalidateLayout();
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

        public RectangleF GetCellBounds(int column, int row, int columnSpan = 1, int rowSpan = 1)
        {
            int columnCount = GetSegmentCount(Columns);
            int rowCount = GetSegmentCount(Rows);
            int safeColumn = Clamp(column, 0, columnCount - 1);
            int safeRow = Clamp(row, 0, rowCount - 1);
            int endColumn = Clamp(safeColumn + Math.Max(1, columnSpan) - 1, safeColumn, columnCount - 1);
            int endRow = Clamp(safeRow + Math.Max(1, rowSpan) - 1, safeRow, rowCount - 1);

            float x = GetSegmentStart(Rect.X, Rect.Width, Columns, safeColumn, columnCount);
            float y = GetSegmentStart(Rect.Y, Rect.Height, Rows, safeRow, rowCount);
            float right = GetSegmentEnd(Rect.X, Rect.Width, Columns, endColumn, columnCount);
            float bottom = GetSegmentEnd(Rect.Y, Rect.Height, Rows, endRow, rowCount);

            return new RectangleF(x, y, Math.Max(0f, right - x), Math.Max(0f, bottom - y));
        }

        public T Set<T>(T child, int col, int row, int colSpan = 1, int rowSpan = 1)
            where T : ControlTemplate
        {
            if (child == null)
                return null;

            ValidatePlacement(col, row, colSpan, rowSpan);
            AddChild(child);

            _placements[child] = new GridPlacement
            {
                Column = col,
                Row = row,
                ColumnSpan = colSpan,
                RowSpan = rowSpan
            };

            InvalidateLayout();
            return child;
        }

        public bool RemoveChild(ControlTemplate child)
        {
            _placements.Remove(child);
            return base.RemoveChild(child);
        }

        public override void ClearChildren()
        {
            _placements.Clear();
            base.ClearChildren();
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            EnsureLayout();
            DrawBackground(sprites);
            RenderChildren(sprites);
        }

        protected override void ArrangeChildren()
        {
            var children = VisualChildren;
            if (children == null)
                return;

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                ArrangeChild(child as ControlTemplate, GetChildBounds(child as ControlTemplate, i));
            }
        }

        RectangleF GetChildBounds(ControlTemplate child, int childIndex)
        {
            GridPlacement placement;
            if (child != null && _placements.TryGetValue(child, out placement))
                return GetCellBounds(placement.Column, placement.Row, placement.ColumnSpan, placement.RowSpan);

            return GetCellBounds(childIndex);
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

        static void ArrangeChild(ControlTemplate child, RectangleF bounds)
        {
            if (child == null)
                return;

            child.Arrange(bounds);
        }

        void ValidatePlacement(int column, int row, int columnSpan, int rowSpan)
        {
            if (column < 0)
                throw new ArgumentOutOfRangeException("column");

            if (row < 0)
                throw new ArgumentOutOfRangeException("row");

            if (columnSpan < 1)
                throw new ArgumentOutOfRangeException("columnSpan");

            if (rowSpan < 1)
                throw new ArgumentOutOfRangeException("rowSpan");

            int columnCount = GetSegmentCount(Columns);
            int rowCount = GetSegmentCount(Rows);

            if (column >= columnCount)
                throw new ArgumentOutOfRangeException("column");

            if (row >= rowCount)
                throw new ArgumentOutOfRangeException("row");

            if (column + columnSpan > columnCount)
                throw new ArgumentOutOfRangeException("columnSpan");

            if (row + rowSpan > rowCount)
                throw new ArgumentOutOfRangeException("rowSpan");
        }

        static float[] CreateEqualSegments(int count)
        {
            int safeCount = Math.Max(1, count);
            var values = new float[safeCount];

            for (int i = 0; i < values.Length; i++)
                values[i] = 1f;

            return values;
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
