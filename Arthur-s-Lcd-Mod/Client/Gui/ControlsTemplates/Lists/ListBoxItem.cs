using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Lists
{
    public sealed class ListBoxItem<T> : RectangleControl
    {
        public ListBoxItem(RectangleF bounds, ListBoxItemModel<T> model)
            : base(bounds, CursorType.Hand, model)
        { }

        public ListBoxItemModel<T> ItemModel
        {
            get { return DataContext as ListBoxItemModel<T>; }
        }

        public override bool Click(object sender)
        {
            var handled = base.Click(sender);
            if (handled)
                MarkDirty();

            return handled;
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            var model = ItemModel;
            if (model == null || !model.Selected)
            {
                base.RenderDefault(context, sprites);
                return;
            }

            var style = context.Style;
            var owner = model.Owner;
            var selectedPanel = owner?.SelectedPanelColor ?? style.GetPanelColor(true);
            var selectedText = owner?.SelectedTextColor ?? style.GetTextColor(true);

            var selectedStyle = new ControlStyle(selectedText, selectedPanel)
            {
                BorderPercentage = style.BorderPercentage,
                HoverPanelColor = selectedPanel,
                HoverTextColor = selectedText,
                Padding = style.Padding
            };

            var selectedContext = new ControlRenderContext(
                context.Surface,
                context.Scale,
                context.FontScale,
                selectedStyle,
                context.Theme,
                context.CursorPosition);

            var rect = GetViewBox();
            Border.CreateSpritesFromRect(rect, sprites, selectedPanel, selectedStyle.BorderPercentage);
            RenderDefaultText(rect, selectedContext, sprites);
        }
    }
}
