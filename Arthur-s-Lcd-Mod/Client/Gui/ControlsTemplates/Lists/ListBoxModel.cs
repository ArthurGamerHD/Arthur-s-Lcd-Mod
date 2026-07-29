using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Lists
{
    public delegate void ListBoxItemRenderHandler<T>(
        ListBoxItem<T> control,
        T item,
        List<MySprite> sprites);

    public sealed class ListBoxModel<T> : ControlModelBase
    {
        public ListBoxModel()
        {
            Items = new List<T>();
            SelectedEntries = new List<T>();
            RowHeight = 32f;
            ScrollerWidthPixels = 6f;
            MultiSelect = true;
            SelectionEnabled = true;
        }

        public IList<T> Items { get; set; }
        public IList<T> SelectedEntries { get; set; }
        public bool MultiSelect { get; set; }
        public bool SelectionEnabled { get; set; }
        public float RowHeight { get; set; }
        public float ScrollerWidthPixels { get; set; }
        public Func<T, string> TextSelector { get; set; }
        public Action<T> EntryClicked { get; set; }
        public Action<T, int, int> EntryMoved { get; set; }
        public Func<Vector2, int> DragTargetIndexResolver { get; set; }
        public Func<T, int, int, int> DragTargetIndexFilter { get; set; }
        public Action<Vector2> DragPointerChanged { get; set; }
        public ListBoxItemRenderHandler<T> ItemRenderer { get; set; }
        public float DragHandleWidthPixels { get; set; }
        public bool DraggingItem { get; private set; }
        public T DraggedItem { get; private set; }
        public int DraggedItemIndex { get; private set; }
        public Vector2 DraggedPointerPosition { get; private set; }
        public Vector2 DraggedPointerOffset { get; private set; }
        public RectangleF DraggedItemBounds { get; private set; }
        public Color? SelectedPanelColor { get; set; }
        public Color? SelectedTextColor { get; set; }

        public int Count => Items?.Count ?? 0;

        public T GetItem(int index)
        {
            if (Items == null || index < 0 || index >= Items.Count)
                return default(T);

            return Items[index];
        }

        public string GetText(T item)
        {
            if (TextSelector != null)
                return TextSelector(item) ?? string.Empty;

            return item == null ? string.Empty : item.ToString();
        }

        public bool IsSelected(T item)
        {
            return SelectedEntries != null && SelectedEntries.Contains(item);
        }

        public void SelectClicked(T item, int index)
        {
            if (SelectionEnabled)
            {
                EnsureSelectedEntries();

                var input = MyAPIGateway.Input;
                bool ctrl = input != null && input.IsAnyCtrlKeyPressed();
                bool shift = input != null && input.IsAnyShiftKeyPressed();

                if (!MultiSelect)
                {
                    ReplaceSelection(item);
                }
                else if (ctrl)
                {
                    ToggleSelection(item);
                }
                else if (shift)
                {
                    AddRangeFromFirstSelected(index);
                }
                else
                {
                    ReplaceSelection(item);
                }
            }

            if (EntryClicked != null)
                EntryClicked(item);
        }

        void EnsureSelectedEntries()
        {
            if (SelectedEntries == null)
                SelectedEntries = new List<T>();
        }

        void ReplaceSelection(T item)
        {
            SelectedEntries.Clear();
            AddSelected(item);
        }

        void ToggleSelection(T item)
        {
            if (SelectedEntries.Contains(item))
                SelectedEntries.Remove(item);
            else
                SelectedEntries.Add(item);
        }

        void AddRangeFromFirstSelected(int clickedIndex)
        {
            if (Items == null || clickedIndex < 0 || clickedIndex >= Items.Count)
                return;

            int firstSelectedIndex = GetFirstSelectedIndex();
            if (firstSelectedIndex < 0)
            {
                AddSelected(Items[clickedIndex]);
                return;
            }

            int start = Math.Min(firstSelectedIndex, clickedIndex);
            int end = Math.Max(firstSelectedIndex, clickedIndex);

            for (int i = start; i <= end; i++)
                AddSelected(Items[i]);
        }

        int GetFirstSelectedIndex()
        {
            if (SelectedEntries == null || Items == null)
                return -1;

            for (int i = 0; i < SelectedEntries.Count; i++)
            {
                int index = Items.IndexOf(SelectedEntries[i]);
                if (index >= 0)
                    return index;
            }

            return -1;
        }


        public int IndexOf(T item)
        {
            return Items == null ? -1 : Items.IndexOf(item);
        }

        public void BeginDragItem(T item, int index, RectangleF bounds, Vector2 pointerPosition)
        {
            DraggingItem = true;
            DraggedItem = item;
            DraggedItemIndex = index;
            DraggedItemBounds = bounds;
            DraggedPointerPosition = pointerPosition;
            DraggedPointerOffset = pointerPosition - bounds.Position;
        }

        public void UpdateDraggedPointer(Vector2 pointerPosition)
        {
            if (!DraggingItem)
                return;

            DraggedPointerPosition = pointerPosition;
            if (DragPointerChanged != null)
                DragPointerChanged(pointerPosition);
        }

        public void EndDragItem()
        {
            DraggingItem = false;
            DraggedItem = default(T);
            DraggedItemIndex = -1;
            DraggedPointerPosition = default(Vector2);
            DraggedPointerOffset = default(Vector2);
            DraggedItemBounds = default(RectangleF);
        }

        public bool IsDraggingItem(T item)
        {
            return DraggingItem && Equals(DraggedItem, item);
        }

        public bool TryGetDragGhost(out T item, out int index, out RectangleF bounds)
        {
            item = default(T);
            index = -1;
            bounds = default(RectangleF);

            if (!DraggingItem || DraggedItemBounds.Width <= 0f || DraggedItemBounds.Height <= 0f)
                return false;

            item = DraggedItem;
            index = DraggedItemIndex;
            bounds = new RectangleF(
                DraggedPointerPosition.X - DraggedPointerOffset.X,
                DraggedPointerPosition.Y - DraggedPointerOffset.Y,
                DraggedItemBounds.Width,
                DraggedItemBounds.Height);
            return true;
        }

        public bool MoveDraggedItemToPointer(T item, Vector2 pointerPosition)
        {
            var resolver = DragTargetIndexResolver;
            if (EntryMoved == null || resolver == null || Items == null || Items.Count <= 1)
                return false;

            // Drag callbacks are delivered to the handle control that started
            // the gesture, but list rows are virtualized/rebound by index while
            // the drag is in progress. Always resolve movement against the
            // original dragged item so the invisible source row cannot cause
            // adjacent entries to be moved back and forth.
            if (DraggingItem)
                item = DraggedItem;

            int currentIndex = Items.IndexOf(item);
            if (currentIndex < 0)
                return false;

            int targetIndex = resolver(pointerPosition);
            if (targetIndex < 0)
                targetIndex = 0;
            if (targetIndex >= Items.Count)
                targetIndex = Items.Count - 1;

            var targetFilter = DragTargetIndexFilter;
            if (targetFilter != null)
            {
                targetIndex = targetFilter(item, currentIndex, targetIndex);
                if (targetIndex < 0)
                    targetIndex = 0;
                if (targetIndex >= Items.Count)
                    targetIndex = Items.Count - 1;
            }

            if (targetIndex == currentIndex)
            {
                if (DraggingItem && Equals(DraggedItem, item))
                    DraggedItemIndex = currentIndex;
                return false;
            }

            EntryMoved(item, currentIndex, targetIndex);
            if (DraggingItem && Equals(DraggedItem, item))
                DraggedItemIndex = targetIndex;
            return true;
        }

        void AddSelected(T item)
        {
            if (!SelectedEntries.Contains(item))
                SelectedEntries.Add(item);
        }
    }
}
