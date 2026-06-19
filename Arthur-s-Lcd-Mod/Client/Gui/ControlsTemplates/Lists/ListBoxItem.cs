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
        { }

        public ListBoxItemModel<T> ItemModel => DataContext as ListBoxItemModel<T>;

        public override bool Click(object sender)
        {
            var handled = base.Click(sender);
            if (handled)
                MarkDirty();

            return handled;
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var model = ItemModel;
            if (model == null || !model.Selected)
            {
                base.RenderDefault(sprites);
                return;
            }

            var owner = model.Owner;
            var selectedPanel = owner != null && owner.SelectedPanelColor.HasValue
                ? owner.SelectedPanelColor.Value
                : ResolveColor(ThemeResources.AccentContainerColor);
            var selectedText = owner != null && owner.SelectedTextColor.HasValue
                ? owner.SelectedTextColor.Value
                : ResolveColor(ThemeResources.OnAccentContainerColor);

            var rect = GetViewBox();
            Border.CreateSpritesFromRect(rect, sprites, selectedPanel, GetRenderBorderRadiusPixels(), LayoutScale);
            RenderDefaultText(rect, sprites, selectedText);
        }
    }
}
