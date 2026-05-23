using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels.WrappedGrid
{
    public sealed class WrappedGridCell
    {
        internal WrappedGridCell(int visibleIndex, int itemIndex, int row, int column, RectangleF bounds)
        {
            VisibleIndex = visibleIndex;
            ItemIndex = itemIndex;
            Row = row;
            Column = column;
            Bounds = bounds;
        }

        public int VisibleIndex { get; private set; }
        public int ItemIndex { get; private set; }
        public int Row { get; private set; }
        public int Column { get; private set; }
        public RectangleF Bounds { get; private set; }
    }
}