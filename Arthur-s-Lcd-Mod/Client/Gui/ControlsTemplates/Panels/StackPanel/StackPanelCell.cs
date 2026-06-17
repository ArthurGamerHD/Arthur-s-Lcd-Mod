using System.Collections.Generic;
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
        public ControlTemplate Control { get; private set; }

        public StackPanelCell SetControl(ControlTemplate control)
        {
            Control = control;
            return this;
        }

        public void Render(List<MySprite> sprites)
        {
            if (Control != null)
                Control.Render(sprites);
        }
    }
}
