using System;
using System.Collections.Generic;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Layout;
using Adk.Gui.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    /// <summary>
    /// Fills its bounds with equally sized tiles, choosing the column count whose tiles come closest to
    /// <see cref="PreferredTileAspectRatio"/> without leaving lopsided rows. An incomplete last row is centered.
    /// </summary>
    internal sealed class TilingPanel : Panel
    {
        public static readonly StyledProperty<float> SpacingProperty =
            StyledProperty.Register<TilingPanel, float>(nameof(Spacing), 6f, UiInvalidation.Measure);
        public float Spacing { get { return GetValue(SpacingProperty); } set { SetValue(SpacingProperty, value); } }

        public static readonly StyledProperty<float> PreferredTileAspectRatioProperty =
            StyledProperty.Register<TilingPanel, float>(nameof(PreferredTileAspectRatio), 1.6f, UiInvalidation.Measure);
        public float PreferredTileAspectRatio
        {
            get { return GetValue(PreferredTileAspectRatioProperty); }
            set { SetValue(PreferredTileAspectRatioProperty, value); }
        }

        /// <summary>Places the first complete row at the bottom and grows upward.</summary>
        public static readonly StyledProperty<bool> FillFromBottomProperty =
            StyledProperty.Register<TilingPanel, bool>(nameof(FillFromBottom), false, UiInvalidation.Measure);
        public bool FillFromBottom { get { return GetValue(FillFromBottomProperty); } set { SetValue(FillFromBottomProperty, value); } }

        protected override void DrawSelf(RenderContext context) { }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            int count = VisibleCount();
            if (count == 0)
                return Vector2.Zero;
            float width = float.IsPositiveInfinity(availableSize.X) ? 0f : availableSize.X;
            float height = float.IsPositiveInfinity(availableSize.Y) ? 0f : availableSize.Y;
            int columns, rows;
            SelectGrid(count, width, height, out columns, out rows);
            Vector2 cell = CellSize(width, height, columns, rows);
            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
                children[i].Measure(cell);
            return new Vector2(width, height);
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            int count = VisibleCount();
            if (count == 0)
                return;
            int columns, rows;
            SelectGrid(count, bounds.Width, bounds.Height, out columns, out rows);
            Vector2 cell = CellSize(bounds.Width, bounds.Height, columns, rows);
            float spacing = Spacing;
            int lastRowCount = count - (rows - 1) * columns;
            IReadOnlyList<VisualElement> children = VisualChildren;
            int index = 0;
            for (int i = 0; i < children.Count; i++)
            {
                VisualElement child = children[i];
                if (!child.Visible)
                    continue;
                int layoutRow = index / columns;
                int row = FillFromBottom ? rows - layoutRow - 1 : layoutRow;
                int column = index % columns;
                float rowOffset = layoutRow == rows - 1 && lastRowCount < columns
                    ? (columns - lastRowCount) * (cell.X + spacing) * .5f
                    : 0f;
                child.ArrangeInSlot(new RectangleF(
                    bounds.X + rowOffset + column * (cell.X + spacing),
                    bounds.Y + row * (cell.Y + spacing),
                    cell.X,
                    cell.Y));
                index++;
            }
        }

        Vector2 CellSize(float width, float height, int columns, int rows)
        {
            float spacing = Spacing;
            return new Vector2(
                Math.Max(0f, width - spacing * Math.Max(0, columns - 1)) / columns,
                Math.Max(0f, height - spacing * Math.Max(0, rows - 1)) / rows);
        }

        int VisibleCount()
        {
            int count = 0;
            IReadOnlyList<VisualElement> children = VisualChildren;
            for (int i = 0; i < children.Count; i++)
                if (children[i].Visible)
                    count++;
            return count;
        }

        void SelectGrid(int count, float width, float height, out int bestColumns, out int bestRows)
        {
            float spacing = Spacing;
            float preferred = Math.Max(.1f, PreferredTileAspectRatio);
            bestColumns = 1;
            bestRows = Math.Max(1, count);
            double bestScore = double.MaxValue;
            for (int columns = 1; columns <= count; columns++)
            {
                int rows = (int)Math.Ceiling(count / (float)columns);
                float cellWidth = Math.Max(1f, width - spacing * Math.Max(0, columns - 1)) / columns;
                float cellHeight = Math.Max(1f, height - spacing * Math.Max(0, rows - 1)) / rows;
                double shapeError = Math.Abs(Math.Log(Math.Max(.001f, cellWidth / cellHeight / preferred)));
                double emptyPenalty = (columns * rows - count) / (double)Math.Max(1, count);
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
