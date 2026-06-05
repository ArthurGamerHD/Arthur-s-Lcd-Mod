using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    /// <summary>
    /// Non-visual rectangular container for child controls.
    /// </summary>
    public class Panel : ControlBase
    {
        public Panel()
            : this(default(RectangleF))
        {
        }

        public Panel(RectangleF bounds, CursorType? cursor = null, object dataContext = null)
            : base(cursor, dataContext)
        {
            Rect = bounds;
        }

        public RectangleF Rect { get; private set; }

        public virtual void SetRect(RectangleF bounds)
        {
            Rect = bounds;
            ArrangeChildren();
            ValidateLayout();
            MarkDirty();
        }

        public override RectangleF Bounds
        {
            get { return Rect; }
        }

        public override void Arrange(RectangleF bounds)
        {
            SetRect(bounds);
        }

        protected virtual void ArrangeChildren()
        {
        }

        protected void EnsureLayout()
        {
            if (!IsLayoutDirty)
                return;

            ArrangeChildren();
            ValidateLayout();
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            EnsureLayout();
            RenderChildren(context, sprites);
        }

        protected void RenderChildren(ControlRenderContext context, List<MySprite> sprites)
        {
            var children = Children;
            if (children == null)
                return;

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null)
                    child.Render(context, sprites);
            }
        }

        protected override bool HitCore(Vector2 point)
        {
            return Rect.Contains(point);
        }
    }
}
