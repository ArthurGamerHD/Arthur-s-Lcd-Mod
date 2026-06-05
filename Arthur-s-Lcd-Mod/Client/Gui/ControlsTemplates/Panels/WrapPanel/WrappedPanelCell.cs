using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel
{
    public sealed class WrapPanelCell
    {
        internal WrapPanelCell(int visibleIndex, int itemIndex, int row, int column, RectangleF bounds)
        {
            VisibleIndex = visibleIndex;
            ItemIndex = itemIndex;
            Row = row;
            Column = column;
            Bounds = bounds;
        }

        public int VisibleIndex { get; private set; }
        public int ItemIndex { get; private set; }
        public int Row { get; private set; }
        public int Column { get; private set; }
        public RectangleF Bounds { get; private set; }
        public ControlBase Control { get; private set; }

        public WrapPanelCell SetControl(ControlBase control)
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
