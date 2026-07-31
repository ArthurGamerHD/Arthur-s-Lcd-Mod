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
        public InteractiveRenderHandler CustomRender { get; set; }
        public Action<object, object> OnClick { get; set; }
        public Action<object, object> OnSecondaryClick { get; set; }
        public Action<object, object> OnMiddleClick { get; set; }
        public Action<object, object> OnBackClick { get; set; }
        public Action<object, object> OnForwardClick { get; set; }
        public Func<object, int, bool> OnScroll { get; set; }
        public Func<object, bool> OnHover { get; set; }

        public virtual bool CanClick => OnClick != null;

        public virtual bool CanSecondaryClick => OnSecondaryClick != null;

        public virtual bool CanMiddleClick => OnMiddleClick != null;

        public virtual bool CanBackClick => OnBackClick != null;

        public virtual bool CanForwardClick => OnForwardClick != null;

        public virtual bool CanScroll => OnScroll != null;

        public virtual bool CanHover => OnHover != null;

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

        public virtual bool MiddleClick(object sender)
        {
            if (OnMiddleClick == null)
                return false;

            OnMiddleClick(this, sender);
            return true;
        }

        public virtual bool BackClick(object sender)
        {
            if (OnBackClick == null)
                return false;

            OnBackClick(this, sender);
            return true;
        }

        public virtual bool ForwardClick(object sender)
        {
            if (OnForwardClick == null)
                return false;

            OnForwardClick(this, sender);
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
