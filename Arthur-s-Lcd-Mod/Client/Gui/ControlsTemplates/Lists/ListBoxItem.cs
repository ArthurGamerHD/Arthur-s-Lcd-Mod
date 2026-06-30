using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Lists
{
    public sealed class ListBoxItem<T> : RectangleControl
    {
        public ListBoxItem(RectangleF bounds, ListBoxItemModel<T> model)
            : base(bounds, CursorType.Hand, model)
        {
        }

        public ListBoxItemModel<T> ItemModel => DataContext as ListBoxItemModel<T>;

        public override bool Click(object sender)
        {
            var handled = base.Click(sender);
            if (handled)
                MarkDirty();

            return handled;
        }

        protected override StyleState GetStyleState()
        {
            StyleState state = base.GetStyleState();
            var model = ItemModel;
            if (model != null && model.Selected)
                state |= StyleState.Selected;

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
            if (model != null && model.Selected && !IsMouseOver && !IsPressed)
            {
                if (owner != null && owner.SelectedPanelColor.HasValue)
                    panelColor = owner.SelectedPanelColor.Value;
                if (owner != null && owner.SelectedTextColor.HasValue)
                    textColor = owner.SelectedTextColor.Value;
            }

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
    }
}
