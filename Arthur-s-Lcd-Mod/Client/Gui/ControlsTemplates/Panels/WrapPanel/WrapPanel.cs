using System;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel
{
    public class WrapPanel : LcdMod.Client.Gui.ControlsTemplates.Panels.Panel
    {
        float _rowHeight = 32f;
        float _minimumColumnWidth = 96f;
        bool _forceSingleColumn;
        float _horizontalGap;
        float _verticalGap;

        public WrapPanel()
        {
        }

        public WrapPanel(ControlTemplate parent)
        {
            AttachTo(parent);
        }

        public WrapPanel(ControlTemplate parent, RectangleF bounds)
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

        public float MinimumColumnWidth
        {
            get { return _minimumColumnWidth; }
            set
            {
                float next = Math.Max(1f, value);
                if (_minimumColumnWidth == next)
                    return;

                _minimumColumnWidth = next;
                InvalidateLayout();
            }
        }

        public bool ForceSingleColumn
        {
            get { return _forceSingleColumn; }
            set
            {
                if (_forceSingleColumn == value)
                    return;

                _forceSingleColumn = value;
                InvalidateLayout();
            }
        }

        public float HorizontalGap
        {
            get { return _horizontalGap; }
            set
            {
                float next = Math.Max(0f, value);
                if (_horizontalGap == next)
                    return;

                _horizontalGap = next;
                InvalidateLayout();
            }
        }

        public float VerticalGap
        {
            get { return _verticalGap; }
            set
            {
                float next = Math.Max(0f, value);
                if (_verticalGap == next)
                    return;

                _verticalGap = next;
                InvalidateLayout();
            }
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            int rows = WrapPanelLayout.CalculateTotalRows(
                availableSize.X,
                MinimumColumnWidth,
                VisualChildren.Count,
                ForceSingleColumn);

            float height = rows * RowHeight + Math.Max(0, rows - 1) * VerticalGap;
            return new Vector2(Math.Max(0f, availableSize.X), height);
        }

        protected override void ArrangeChildren()
        {
            var children = VisualChildren;
            if (children == null || children.Count == 0)
                return;

            float strideHeight = RowHeight + VerticalGap;
            int rows = WrapPanelLayout.CalculateTotalRows(
                Rect.Width,
                MinimumColumnWidth,
                children.Count,
                ForceSingleColumn);

            var layoutBounds = new RectangleF(
                Rect.X,
                Rect.Y,
                Rect.Width,
                rows * strideHeight);

            var layout = WrapPanelLayout.Create(
                layoutBounds,
                strideHeight,
                MinimumColumnWidth,
                children.Count,
                0,
                ForceSingleColumn);

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i] as ControlTemplate;
                if (child == null)
                    continue;

                var cell = layout.GetCell(i);
                child.Arrange(ApplyGap(cell.Bounds));
            }
        }

        RectangleF ApplyGap(RectangleF bounds)
        {
            float width = Math.Max(0f, bounds.Width - HorizontalGap);
            float height = Math.Max(0f, bounds.Height - VerticalGap);
            return new RectangleF(bounds.X, bounds.Y, width, height);
        }
    }
}
