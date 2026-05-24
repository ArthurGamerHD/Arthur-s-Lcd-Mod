using System;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel
{
    public sealed class StackPanel
    {
        StackPanel()
        {
        }

        public RectangleF ContentBounds { get; private set; }
        public float RowHeight { get; private set; }
        public int ItemCount { get; private set; }
        public int MaxRows { get; private set; }
        public int RenderedRows { get; private set; }
        public int StartIndex { get; private set; }
        public int VisibleCellCount { get; private set; }

        public static StackPanel Create(
            RectangleF contentBounds,
            float rowHeight,
            int itemCount,
            int startIndex = 0)
        {
            var panel = new StackPanel();
            panel.ContentBounds = contentBounds;
            panel.RowHeight = Math.Max(1f, rowHeight);
            panel.ItemCount = Math.Max(0, itemCount);
            panel.StartIndex = Math.Max(0, Math.Min(startIndex, panel.ItemCount));
            panel.MaxRows = Math.Max(1, (int)Math.Floor(Math.Max(0f, contentBounds.Height) / panel.RowHeight));
            panel.VisibleCellCount = Math.Min(panel.MaxRows, Math.Max(0, panel.ItemCount - panel.StartIndex));
            panel.RenderedRows = panel.VisibleCellCount;
            return panel;
        }

        public StackPanelCell GetCell(int visibleIndex)
        {
            if (VisibleCellCount <= 0)
                return new StackPanelCell(0, StartIndex, new RectangleF(ContentBounds.X, ContentBounds.Y, 0f, 0f));

            if (visibleIndex < 0)
                visibleIndex = 0;
            else if (visibleIndex >= VisibleCellCount)
                visibleIndex = VisibleCellCount - 1;

            int itemIndex = StartIndex + visibleIndex;
            float y = ContentBounds.Y + visibleIndex * RowHeight;

            return new StackPanelCell(
                visibleIndex,
                itemIndex,
                new RectangleF(ContentBounds.X, y, ContentBounds.Width, RowHeight));
        }
    }
}
