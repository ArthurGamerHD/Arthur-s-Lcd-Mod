using System;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels.WrappedGrid
{
    public sealed class WrappedGrid
    {
        WrappedGrid()
        {
        }

        public RectangleF ViewBox { get; private set; }
        public RectangleF ContentBounds { get; private set; }
        public float RowHeight { get; private set; }
        public float ColumnWidth { get; private set; }
        public int ItemCount { get; private set; }
        public int Columns { get; private set; }
        public int MaxRows { get; private set; }
        public int RenderedRows { get; private set; }
        public int TotalRows { get; private set; }
        public int StartIndex { get; private set; }
        public int VisibleCellCount { get; private set; }

        public static WrappedGrid Create(
            RectangleF contentBounds,
            float rowHeight,
            float minimumColumnWidth,
            int itemCount,
            int startIndex = 0,
            bool forceSingleColumn = false)
        {
            var grid = new WrappedGrid();
            grid.ViewBox = contentBounds;
            grid.ContentBounds = contentBounds;
            grid.RowHeight = Math.Max(1f, rowHeight);
            grid.ItemCount = Math.Max(0, itemCount);
            grid.StartIndex = Math.Max(0, Math.Min(startIndex, grid.ItemCount));

            grid.MaxRows = Math.Max(1, (int)Math.Floor(Math.Max(0f, contentBounds.Height) / grid.RowHeight));
            grid.Columns = CalculateColumns(contentBounds.Width, minimumColumnWidth, forceSingleColumn, grid.ItemCount);
            grid.TotalRows = grid.ItemCount == 0 ? 0 : (int)Math.Ceiling(grid.ItemCount / (float)grid.Columns);
            grid.RenderedRows = grid.TotalRows == 0 ? 0 : Math.Min(grid.MaxRows, Math.Max(0, grid.TotalRows - grid.StartIndex / grid.Columns));

            float contentWidth = Math.Max(1f, contentBounds.Width);
            grid.ColumnWidth = contentWidth / grid.Columns;
            grid.VisibleCellCount = Math.Min(grid.MaxRows * grid.Columns, Math.Max(0, grid.ItemCount - grid.StartIndex));
            return grid;
        }

        public WrappedGridCell GetCell(int visibleIndex)
        {
            if (VisibleCellCount <= 0)
                return new WrappedGridCell(0, StartIndex, 0, 0, new RectangleF(ContentBounds.X, ContentBounds.Y, 0f, 0f));

            if (visibleIndex < 0)
                visibleIndex = 0;
            else if (visibleIndex >= VisibleCellCount)
                visibleIndex = VisibleCellCount - 1;

            int row = visibleIndex / Columns;
            int column = visibleIndex % Columns;
            int itemIndex = StartIndex + visibleIndex;
            float x = ContentBounds.X + column * ColumnWidth;
            float right = column == Columns - 1 ? ContentBounds.Right : x + ColumnWidth;
            float y = ContentBounds.Y + row * RowHeight;

            return new WrappedGridCell(
                visibleIndex,
                itemIndex,
                row,
                column,
                new RectangleF(x, y, right - x, RowHeight));
        }

        static int CalculateColumns(float availableWidth, float minimumColumnWidth, bool forceSingleColumn, int itemCount)
        {
            if (forceSingleColumn)
                return 1;

            float columnWidth = Math.Max(1f, minimumColumnWidth);
            int columns = Math.Max(1, (int)Math.Floor(availableWidth / columnWidth));
            if (itemCount > 0)
                columns = Math.Min(columns, itemCount);

            return Math.Max(1, columns);
        }
    }
}
