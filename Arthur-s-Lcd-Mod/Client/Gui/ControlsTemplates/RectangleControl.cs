using System;
using LcdMod.Client.Gui.Tooltip;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public class RectangleControl : ControlTemplate
    {
        public RectangleControl(RectangleF bounds, CursorType? cursor = null, object dataContext = null,
            Action<object, object> onClick = null, InteractiveTooltip tooltip = null)
            : base(cursor, dataContext, onClick, tooltip)
        {
            Rect = bounds;
        }

        public RectangleF Rect { get; private set; }

        public virtual void SetRect(RectangleF bounds)
        {
            Rect = bounds;
            ValidateLayout();
            MarkDirty();
        }

        public override RectangleF Bounds => Rect;

        public override void Arrange(RectangleF bounds)
        {
            SetRect(bounds);
        }

        public object RightClick { get; set; }

        protected override bool HitCore(Vector2 point)
        {
            return Rect.Contains(point);
        }
    }
}
