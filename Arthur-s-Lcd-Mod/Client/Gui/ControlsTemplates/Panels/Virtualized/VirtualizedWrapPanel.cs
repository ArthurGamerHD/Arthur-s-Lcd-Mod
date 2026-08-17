using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized
{
    public class VirtualizedWrapPanel<T> : Panel, IScrollContent
    {
        readonly List<Control> _pool = new List<Control>();
        float _rowHeight = 32f;
        float _minimumColumnWidth = 96f;
        bool _forceSingleColumn;
        float _horizontalGap;
        float _verticalGap;
        IList<T> _itemsSource;
        IObservableList<T> _observableItemsSource;

        public IList<T> ItemsSource
        {
            get { return _itemsSource; }
            set
            {
                if (ReferenceEquals(_itemsSource, value))
                    return;

                UnbindObservableItemsSource();
                _itemsSource = value;
                _observableItemsSource = value as IObservableList<T>;
                BindObservableItemsSource();
                if (_itemsSource == null)
                    UnbindPool();
                InvalidateLayout();
            }
        }
        public CreateVirtualizedControlHandler<T> CreateControl { get; set; }
        public BindVirtualizedControlHandler<T> BindControl { get; set; }

        public float RowHeight
        {
            get { return _rowHeight; }
            set
            {
                float next = Math.Max(1f, value);
                if (Math.Abs(_rowHeight - next) < 0.1)
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
                if (Math.Abs(_minimumColumnWidth - next) < 0.1)
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
                if (Math.Abs(_horizontalGap - next) < 0.1)
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
                if (Math.Abs(_verticalGap - next) < 0.1)
                    return;

                _verticalGap = next;
                InvalidateLayout();
            }
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            return MeasureContent(availableSize);
        }

        public Vector2 MeasureContent(Vector2 availableSize)
        {
            int rows = WrapPanelLayout.CalculateTotalRows(
                availableSize.X,
                MinimumColumnWidth,
                GetItemCount(),
                ForceSingleColumn);

            float height = rows * RowHeight + Math.Max(0, rows - 1) * VerticalGap;
            return new Vector2(Math.Max(0f, availableSize.X), height);
        }

        public void ArrangeViewport(RectangleF viewport, float scrollOffset)
        {
            var desired = MeasureContent(viewport.Size);
            SetRect(new RectangleF(viewport.X, viewport.Y - scrollOffset, viewport.Width, desired.Y));
            HidePool();

            int itemCount = GetItemCount();
            if (itemCount <= 0)
                return;

            float stride = RowHeight + VerticalGap;
            int columns = Math.Max(1, WrapPanelLayout.Create(
                new RectangleF(viewport.X, viewport.Y, viewport.Width, Math.Max(RowHeight, viewport.Height)),
                stride,
                MinimumColumnWidth,
                itemCount,
                0,
                ForceSingleColumn).Columns);

            int startRow = Math.Max(0, (int)Math.Floor(Math.Max(0f, scrollOffset) / stride));
            float rowOffset = Math.Max(0f, scrollOffset - startRow * stride);
            int startIndex = Math.Min(itemCount, startRow * columns);
            int visibleRows = Math.Max(1, (int)Math.Ceiling((viewport.Height + rowOffset) / stride) + 1);
            int visibleCount = Math.Min(itemCount - startIndex, visibleRows * columns);
            float columnWidth = Math.Max(1f, viewport.Width) / columns;

            for (int i = 0; i < visibleCount; i++)
            {
                int itemIndex = startIndex + i;
                int row = i / columns;
                int column = i % columns;
                var control = GetPooledControl(i, ItemsSource[itemIndex]);
                BindControlIfNeeded(control, ItemsSource[itemIndex], itemIndex);
                control.SetVisible(true);

                float x = viewport.X + column * columnWidth;
                float right = column == columns - 1 ? viewport.Right : x + columnWidth;
                float y = viewport.Y - rowOffset + row * stride;
                control.Arrange(ApplyGap(new RectangleF(x, y, right - x, stride)));
            }

            ValidateLayout();
        }

        RectangleF ApplyGap(RectangleF bounds)
        {
            float width = Math.Max(0f, bounds.Width - HorizontalGap);
            float height = Math.Max(0f, bounds.Height - VerticalGap);
            return new RectangleF(bounds.X, bounds.Y, width, height);
        }

        int GetItemCount()
        {
            return ItemsSource?.Count ?? 0;
        }

        void BindObservableItemsSource()
        {
            if (_observableItemsSource == null)
                return;

            _observableItemsSource.ItemAdded += OnItemAdded;
            _observableItemsSource.ItemRemoved += OnItemRemoved;
            _observableItemsSource.ItemChanged += OnItemChanged;
        }

        void UnbindObservableItemsSource()
        {
            if (_observableItemsSource == null)
                return;

            _observableItemsSource.ItemAdded -= OnItemAdded;
            _observableItemsSource.ItemRemoved -= OnItemRemoved;
            _observableItemsSource.ItemChanged -= OnItemChanged;
            _observableItemsSource = null;
        }

        void OnItemAdded(IObservableCollection<T> sender, T item)
        {
            InvalidateLayout();
        }

        void OnItemRemoved(IObservableCollection<T> sender, T item)
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                var control = _pool[i] as ControlTemplate;
                if (control != null && Equals(control.DataContext, item))
                    control.SetDataContext(null);
            }

            InvalidateLayout();
        }

        void OnItemChanged(IObservableCollection<T> sender, T item)
        {
            MarkDirty();
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

        void UnbindPool()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                var control = _pool[i] as ControlTemplate;
                if (control != null)
                    control.SetDataContext(null);
            }
        }
    }
}
