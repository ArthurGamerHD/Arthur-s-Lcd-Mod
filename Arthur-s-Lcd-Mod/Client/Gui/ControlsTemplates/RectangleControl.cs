using System;
using LcdMod.Client.Gui.Tooltip;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public class RectangleControl : ControlBase
    {
        public RectangleControl(RectangleF bounds, CursorType? cursor = null, object dataContext = null,
            Action<object, object> onClick = null, InteractiveTooltip tooltip = null)
            : base(cursor, dataContext, onClick, tooltip)
        {
            Rect = bounds;
        }

        public RectangleF Rect { get; private set; }

        public void SetRect(RectangleF bounds)
        {
            Rect = bounds;
        }

        public override RectangleF Bounds => Rect;
        public object RightClick { get; set; }

        protected override bool HitCore(Vector2 point)
        {
            return Rect.Contains(point);
        }
    }
}