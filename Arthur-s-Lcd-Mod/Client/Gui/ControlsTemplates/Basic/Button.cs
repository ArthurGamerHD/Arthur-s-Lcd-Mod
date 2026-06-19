using System;
using LcdMod.Client.Gui.Tooltip;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Basic
{
    public partial class Button : RectangleControl
    {
        public Button(RectangleF bounds, ButtonModel model = null)
            : base(bounds, CursorType.Hand, model ?? new ButtonModel())
        {
        }

        public Button(RectangleF bounds, string text, Action<ButtonModel, object> clicked = null)
            : this(bounds, new ButtonModel { Text = text, Clicked = clicked })
        {
        }

        public Button(RectangleF bounds, CursorType? cursor, object dataContext = null,
            Action<object, object> onClick = null, InteractiveTooltip tooltip = null)
            : base(bounds, cursor, dataContext, onClick, tooltip)
        {
        }

        protected override Color GetRenderBackgroundColor()
        {
            return base.BackgroundColor;
        }

    }
}
