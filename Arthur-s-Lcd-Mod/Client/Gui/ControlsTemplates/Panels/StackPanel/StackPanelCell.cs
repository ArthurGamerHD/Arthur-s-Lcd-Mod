using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel
{
    public sealed class StackPanelCell
    {
        internal StackPanelCell(int visibleIndex, int itemIndex, RectangleF bounds)
        {
            VisibleIndex = visibleIndex;
            ItemIndex = itemIndex;
            Bounds = bounds;
        }

        public int VisibleIndex { get; private set; }
        public int ItemIndex { get; private set; }
        public RectangleF Bounds { get; private set; }
        public ControlBase Control { get; private set; }

        public StackPanelCell SetControl(ControlBase control)
        {
            Control = control;
            return this;
        }

        public void Render(ControlRenderContext context, List<MySprite> sprites)
        {
            if (Control != null)
                Control.Render(context, sprites);
        }
    }
}
