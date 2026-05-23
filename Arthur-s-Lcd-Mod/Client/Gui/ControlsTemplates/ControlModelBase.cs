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

        public virtual bool CanClick
        {
            get { return OnClick != null; }
        }

        public virtual bool CanSecondaryClick
        {
            get { return OnSecondaryClick != null; }
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
    }
}
