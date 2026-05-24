using System;
using LcdMod.Client.Gui.Tooltip;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public abstract class ControlModelBase
    {
        protected ControlModelBase()
        {
            Cursor = CursorType.Default;
        }

        public CursorType Cursor { get; set; }
        public InteractiveTooltip Tooltip { get; set; }
        public ControlStyle Style { get; set; }
        public InteractiveRenderHandler CustomRender { get; set; }
        public Action<object, object> OnClick { get; set; }
        public Action<object, object> OnSecondaryClick { get; set; }
        public Func<object, int, bool> OnScroll { get; set; }
        public Func<object, bool> OnHover { get; set; }

        public virtual bool CanClick
        {
            get { return OnClick != null; }
        }

        public virtual bool CanSecondaryClick
        {
            get { return OnSecondaryClick != null; }
        }

        public virtual bool CanScroll
        {
            get { return OnScroll != null; }
        }

        public virtual bool CanHover
        {
            get { return OnHover != null; }
        }

        public virtual bool Click(object sender)
        {
            if (OnClick == null)
                return false;

            OnClick(this, sender);
            return true;
        }

        public virtual bool SecondaryClick(object sender)
        {
            if (OnSecondaryClick == null)
                return false;

            OnSecondaryClick(this, sender);
            return true;
        }

        public virtual bool Scroll(object sender, int delta)
        {
            return OnScroll != null && OnScroll(sender, delta);
        }

        public virtual bool Hover(object sender)
        {
            return OnHover != null && OnHover(sender);
        }
    }
}
