using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Utility;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Lists
{
    public sealed class ListBoxItem<T> : RectangleControl
    {
        readonly RectangleControl _dragHandle;

        public ListBoxItem(RectangleF bounds, ListBoxItemModel<T> model)
            : base(bounds, CursorType.Hand, model)
        {
            _dragHandle = new RectangleControl(default(RectangleF), CursorType.Hand);
            _dragHandle.SetDraggable(true);
            _dragHandle.SetOnBeginDrag(OnDragHandleBeginDrag);
            _dragHandle.SetOnDrag(OnDragHandleDragged);
            _dragHandle.SetOnEndDrag(OnDragHandleEndDrag);
            AddChild(_dragHandle);
        }

        public ListBoxItemModel<T> ItemModel => DataContext as ListBoxItemModel<T>;

        public bool IsDragGhost { get; set; }
        public bool IsDraggedVisual { get; set; }

        public override bool Click(object sender)
        {
            var handled = base.Click(sender);
            if (handled)
                MarkDirty();

            return handled;
        }

        protected override bool CanResolveChildren(Vector2 point, bool selfHit)
        {
            return !IsDragGhost && selfHit;
        }

        protected override bool HitCore(Vector2 point)
        {
            return !IsDragGhost && base.HitCore(point);
        }

        protected override StyleState GetStyleState()
        {
            StyleState state = base.GetStyleState();
            var model = ItemModel;
            var owner = model != null ? model.Owner : null;
            if (model != null && model.Selected)
                state |= StyleState.Selected;
            if (IsDraggedVisual || (owner != null && model != null && owner.IsDraggingItem(model.Item)))
                state |= StyleState.Dragged;

            return state;
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var model = ItemModel;
            var owner = model != null ? model.Owner : null;
            var rect = GetViewBox();

            Color panelColor = GetRenderBackgroundColor();
            Color textColor = GetRenderTextColor();

            // Optional model colors remain a fallback for the plain selected
            // state. Hover/pressed and combined styles are still allowed to win.
            if (!IsDragGhost && model != null && model.Selected && !IsMouseOver && !IsPressed)
            {
                if (owner != null && owner.SelectedPanelColor.HasValue)
                    panelColor = owner.SelectedPanelColor.Value;
                if (owner != null && owner.SelectedTextColor.HasValue)
                    textColor = owner.SelectedTextColor.Value;
            }

            ConfigureDragHandle(rect, owner);

            if (!IsDragGhost && owner != null && model != null && owner.IsDraggingItem(model.Item))
                return;

            BorderRenderer.CreateSpritesFromRect(
                rect,
                sprites,
                panelColor,
                GetRenderBorderRadiusPixels(),
                LayoutScale);

            if (model != null && owner != null && owner.ItemRenderer != null)
            {
                owner.ItemRenderer(this, model.Item, sprites);
                return;
            }

            RenderDefaultText(rect, sprites, textColor);
        }

        void ConfigureDragHandle(RectangleF rect, ListBoxModel<T> owner)
        {
            if (_dragHandle == null)
                return;

            if (owner == null || owner.EntryMoved == null || owner.DragHandleWidthPixels <= 0f || rect.Width <= 0f || rect.Height <= 0f)
            {
                _dragHandle.SetVisible(false);
                _dragHandle.SetEnabled(false);
                return;
            }

            var width = Math.Min(rect.Width, Math.Max(1f, owner.DragHandleWidthPixels));
            _dragHandle.SetRect(new RectangleF(rect.Right - width, rect.Y, width, rect.Height));
            _dragHandle.SetDataContext(ItemModel);
            _dragHandle.SetVisible(true);
            _dragHandle.SetEnabled(true);
            _dragHandle.SetCursor(CursorType.Hand);
        }

        void OnDragHandleBeginDrag(object dataContext, object sender)
        {
            var model = ItemModel;
            var owner = model == null ? null : model.Owner;
            if (owner == null || owner.EntryMoved == null)
                return;

            Vector2 pointer;
            if (!TryGetPointerPosition(sender, out pointer))
                pointer = Bounds.Center;

            owner.BeginDragItem(model.Item, model.Index, Bounds, pointer);
            MarkDirty();
        }

        bool OnDragHandleDragged(object dataContext, object sender, Vector2 delta)
        {
            var model = ItemModel;
            var owner = model == null ? null : model.Owner;
            if (owner == null || owner.EntryMoved == null)
                return false;

            Vector2 pointer;
            if (TryGetPointerPosition(sender, out pointer))
                owner.UpdateDraggedPointer(pointer);
            else if (owner.DraggingItem)
                owner.UpdateDraggedPointer(owner.DraggedPointerPosition + delta);

            // The row/control under the captured drag handle can be rebound after
            // the queue is reordered. Keep moving the item that began the drag,
            // otherwise the cursor can alternately move the hidden row and the
            // row now occupying that index, causing visible flicker.
            var draggedItem = owner.DraggingItem ? owner.DraggedItem : model.Item;
            if (owner.MoveDraggedItemToPointer(draggedItem, owner.DraggedPointerPosition))
                MarkDirty();
            return true;
        }

        void OnDragHandleEndDrag(object dataContext, object sender)
        {
            var model = ItemModel;
            var owner = model == null ? null : model.Owner;
            if (owner != null)
                owner.EndDragItem();

            MarkDirty();
        }

        static bool TryGetPointerPosition(object sender, out Vector2 position)
        {
            position = default(Vector2);
            var screen = sender as IEyeTracking;
            if (screen == null)
                return false;

            position = screen.CursorPosition + screen.HitTestOffset;
            return !float.IsNaN(position.X) && !float.IsNaN(position.Y);
        }
    }
}
