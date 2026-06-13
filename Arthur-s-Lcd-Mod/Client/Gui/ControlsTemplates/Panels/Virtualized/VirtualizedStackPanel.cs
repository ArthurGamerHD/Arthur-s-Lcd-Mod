using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized
{
    public class VirtualizedStackPanel<T> : Panel, IScrollContent
    {
        readonly List<Control> _pool = new List<Control>();
        float _rowHeight = 30f;
        float _gap;

        public IList<T> ItemsSource { get; set; }
        public CreateVirtualizedControlHandler<T> CreateControl { get; set; }
        public BindVirtualizedControlHandler<T> BindControl { get; set; }

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
            return MeasureContent(availableSize);
        }

        public Vector2 MeasureContent(Vector2 availableSize)
        {
            return new Vector2(
                Math.Max(0f, availableSize.X),
                StackPanelLayout.CalculateTotalHeight(RowHeight, Gap, GetItemCount()));
        }

        public void ArrangeViewport(RectangleF viewport, float scrollOffset)
        {
            SetRect(new RectangleF(viewport.X, viewport.Y - scrollOffset, viewport.Width, MeasureContent(viewport.Size).Y));
            HidePool();

            int itemCount = GetItemCount();
            if (itemCount <= 0)
                return;

            float stride = RowHeight + Gap;
            int startIndex = Math.Max(0, (int)Math.Floor(Math.Max(0f, scrollOffset) / stride));
            float rowOffset = Math.Max(0f, scrollOffset - startIndex * stride);
            int visibleCount = Math.Min(
                itemCount - startIndex,
                Math.Max(1, (int)Math.Ceiling((viewport.Height + rowOffset) / stride) + 1));

            for (int i = 0; i < visibleCount; i++)
            {
                int itemIndex = startIndex + i;
                var control = GetPooledControl(i, ItemsSource[itemIndex]);
                BindControlIfNeeded(control, ItemsSource[itemIndex], itemIndex);
                control.SetVisible(true);

                float y = viewport.Y - rowOffset + i * stride;
                control.Arrange(new RectangleF(viewport.X, y, viewport.Width, RowHeight));
            }

            ValidateLayout();
        }

        int GetItemCount()
        {
            return ItemsSource?.Count ?? 0;
        }

        ControlTemplate GetPooledControl(int poolIndex, T item)
        {
            while (_pool.Count <= poolIndex)
            {
                var control = CreateControl?.Invoke(item);
                if (control == null)
                    control = new RectangleControl(default(RectangleF));

                _pool.Add(control);
                AddChild(control);
            }

            return _pool[poolIndex] as ControlTemplate;
        }

        void BindControlIfNeeded(ControlTemplate control, T item, int index)
        {
            if (BindControl != null)
                BindControl(control, item, index);
        }

        void HidePool()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null)
                    _pool[i].SetVisible(false);
            }
        }
    }
}
