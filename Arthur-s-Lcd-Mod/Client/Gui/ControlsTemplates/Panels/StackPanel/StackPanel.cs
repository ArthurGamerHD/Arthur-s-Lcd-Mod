using System;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel
{
    public class StackPanel : LcdMod.Client.Gui.ControlsTemplates.Panels.Panel
    {
        float _rowHeight = 30f;
        float _gap;

        public StackPanel()
        {
        }

        public StackPanel(ControlTemplate parent)
        {
            AttachTo(parent);
        }

        public StackPanel(ControlTemplate parent, RectangleF bounds)
            : base(bounds)
        {
            AttachTo(parent);
        }

        public float RowHeight
        {
            get { return _rowHeight; }
            set
            {
                float next = Math.Max(1f, value);
                if (_rowHeight == next)
                    return;

                _rowHeight = next;
                InvalidateLayout();
            }
        }

        public float Gap
        {
            get { return _gap; }
            set
            {
                float next = Math.Max(0f, value);
                if (_gap == next)
                    return;

                _gap = next;
                InvalidateLayout();
            }
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            return new Vector2(
                Math.Max(0f, availableSize.X),
                StackPanelLayout.CalculateTotalHeight(RowHeight, Gap, VisualChildren.Count));
        }

        protected override void ArrangeChildren()
        {
            var children = VisualChildren;
            if (children == null || children.Count == 0)
                return;

            float strideHeight = RowHeight + Gap;
            float totalHeight = StackPanelLayout.CalculateTotalHeight(RowHeight, Gap, children.Count);
            var layoutBounds = new RectangleF(Rect.X, Rect.Y, Rect.Width, totalHeight);

            var layout = StackPanelLayout.Create(layoutBounds, strideHeight, children.Count, 0);

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i] as ControlTemplate;
                if (child == null)
                    continue;

                var cell = layout.GetCell(i);
                child.Arrange(RemoveTrailingGap(cell.Bounds));
            }
        }

        RectangleF RemoveTrailingGap(RectangleF bounds)
        {
            return new RectangleF(
                bounds.X,
                bounds.Y,
                bounds.Width,
                Math.Max(0f, bounds.Height - Gap));
        }
    }
}
