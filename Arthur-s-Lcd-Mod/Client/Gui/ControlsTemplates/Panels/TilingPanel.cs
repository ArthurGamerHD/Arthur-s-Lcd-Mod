using System;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    /// <summary>
    /// Fills its bounds with a responsive row/column tile arrangement. Children keep
    /// their identity; only their arranged rectangles change when the bounds or child
    /// collection changes.
    /// </summary>
    public sealed class TilingPanel : Panel
    {
        float _gapPixels = 6f;
        float _paddingPixels = 3f;
        float _preferredTileAspectRatio = 1.6f;
        bool _fillFromBottom;

        public float GapPixels
        {
            get { return _gapPixels; }
            set
            {
                float next = Math.Max(0f, value);
                if (Math.Abs(_gapPixels - next) < 0.01f)
                    return;
                _gapPixels = next;
                InvalidateLayout();
            }
        }

        public float PaddingPixels
        {
            get { return _paddingPixels; }
            set
            {
                float next = Math.Max(0f, value);
                if (Math.Abs(_paddingPixels - next) < 0.01f)
                    return;
                _paddingPixels = next;
                InvalidateLayout();
            }
        }

        public float PreferredTileAspectRatio
        {
            get { return _preferredTileAspectRatio; }
            set
            {
                float next = Math.Max(0.1f, value);
                if (Math.Abs(_preferredTileAspectRatio - next) < 0.01f)
                    return;
                _preferredTileAspectRatio = next;
                InvalidateLayout();
            }
        }

        /// <summary>
        /// Places the first complete row at the bottom and grows upward. When the final
        /// row is incomplete, it remains centered at the top of the panel.
        /// </summary>
        public bool FillFromBottom
        {
            get { return _fillFromBottom; }
            set
            {
                if (_fillFromBottom == value)
                    return;
                _fillFromBottom = value;
                InvalidateLayout();
            }
        }

        protected override void ArrangeChildren()
        {
            var children = VisualChildren;
            int count = children != null ? children.Count : 0;
            if (count == 0)
                return;

            float scale = Math.Max(0.01f, LayoutScale);
            float padding = Math.Min(Math.Min(Rect.Width, Rect.Height) * 0.5f, PaddingPixels * scale);
            float gap = GapPixels * scale;
            float width = Math.Max(0f, Rect.Width - padding * 2f);
            float height = Math.Max(0f, Rect.Height - padding * 2f);

            int columns;
            int rows;
            SelectGrid(count, width, height, gap, PreferredTileAspectRatio, out columns, out rows);

            float cellWidth = Math.Max(0f, width - gap * Math.Max(0, columns - 1)) / columns;
            float cellHeight = Math.Max(0f, height - gap * Math.Max(0, rows - 1)) / rows;
            int lastRowCount = count - (rows - 1) * columns;

            for (int i = 0; i < count; i++)
            {
                var child = children[i] as ControlTemplate;
                if (child == null)
                    continue;

                int layoutRow = i / columns;
                int row = FillFromBottom ? rows - layoutRow - 1 : layoutRow;
                int column = i % columns;
                float rowOffset = layoutRow == rows - 1 && lastRowCount < columns
                    ? (columns - lastRowCount) * (cellWidth + gap) * 0.5f
                    : 0f;
                child.Arrange(new RectangleF(
                    Rect.X + padding + rowOffset + column * (cellWidth + gap),
                    Rect.Y + padding + row * (cellHeight + gap),
                    cellWidth,
                    cellHeight));
            }
        }

        static void SelectGrid(
            int count,
            float width,
            float height,
            float gap,
            float preferredAspectRatio,
            out int bestColumns,
            out int bestRows)
        {
            bestColumns = 1;
            bestRows = Math.Max(1, count);
            double bestScore = double.MaxValue;

            for (int columns = 1; columns <= count; columns++)
            {
                int rows = (int)Math.Ceiling(count / (float)columns);
                float cellWidth = Math.Max(1f, width - gap * Math.Max(0, columns - 1)) / columns;
                float cellHeight = Math.Max(1f, height - gap * Math.Max(0, rows - 1)) / rows;
                float aspect = cellWidth / cellHeight;
                int emptyCells = columns * rows - count;
                double shapeError = Math.Abs(Math.Log(Math.Max(0.001f, aspect / preferredAspectRatio)));
                // Empty cells create visibly lopsided rows (for example 3 + 1 for four
                // children), so they must outweigh modest improvements in tile aspect.
                double emptyPenalty = emptyCells / (double)Math.Max(1, count);
                double score = shapeError + emptyPenalty;

                if (score >= bestScore)
                    continue;

                bestScore = score;
                bestColumns = columns;
                bestRows = rows;
            }
        }
    }
}
