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
        public float ScrollerWidthPixels { get; private set; }
        public int ItemCount { get; private set; }
        public int Columns { get; private set; }
        public int MaxRows { get; private set; }
        public int RenderedRows { get; private set; }
        public int TotalRows { get; private set; }
        public int StartRow { get; private set; }
        public int StartIndex { get; private set; }
        public int VisibleCellCount { get; private set; }
        public bool IsScrollable { get; private set; }

        public static WrappedGrid Create(
            RectangleF viewBox,
            float contentTop,
            float footerHeight,
            float rowHeight,
            float minimumColumnWidth,
            int itemCount,
            float scale,
            int scrollStep,
            bool forceSingleColumn = false)
        {
            var grid = new WrappedGrid();
            grid.ViewBox = viewBox;
            grid.RowHeight = Math.Max(1f, rowHeight);
            grid.ItemCount = Math.Max(0, itemCount);

            float availableHeight = Math.Max(0f, viewBox.Bottom - contentTop - Math.Max(0f, footerHeight));
            grid.MaxRows = Math.Max(1, (int)Math.Floor(availableHeight / grid.RowHeight));

            bool scroll = false;
            int columns = 1;
            int totalRows = 0;

            for (int pass = 0; pass < 3; pass++)
            {
                float availableWidth = Math.Max(1f, viewBox.Width - (scroll ? grid.ScrollerWidthPixels : 0f));
                columns = CalculateColumns(availableWidth, minimumColumnWidth, forceSingleColumn, grid.ItemCount);
                totalRows = grid.ItemCount == 0 ? 0 : (int)Math.Ceiling(grid.ItemCount / (float)columns);

                bool nextScroll = totalRows > grid.MaxRows;
                if (nextScroll == scroll)
                    break;

                scroll = nextScroll;
            }

            grid.IsScrollable = scroll;
            grid.Columns = Math.Max(1, columns);
            grid.TotalRows = totalRows;
            grid.RenderedRows = totalRows == 0 ? 0 : Math.Min(grid.MaxRows, totalRows);

            float contentWidth = Math.Max(1f, viewBox.Width - (grid.IsScrollable ? grid.ScrollerWidthPixels : 0f));
            grid.ColumnWidth = contentWidth / grid.Columns;
            grid.ContentBounds = new RectangleF(
                viewBox.X,
                contentTop,
                contentWidth,
                grid.RenderedRows * grid.RowHeight);

            if (grid.IsScrollable)
            {
                int scrollableRows = Math.Max(1, grid.TotalRows - grid.MaxRows);
                grid.StartRow = scrollStep % (scrollableRows + 1);
            }

            grid.StartIndex = grid.StartRow * grid.Columns;
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
