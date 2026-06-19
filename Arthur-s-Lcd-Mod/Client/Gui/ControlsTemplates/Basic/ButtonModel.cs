using System;

namespace LcdMod.Client.Gui.ControlsTemplates.Basic
{
    public class ButtonModel : ControlModelBase
    {
        public ButtonModel()
        {
            Cursor = CursorType.Hand;
        }

        public string Text { get; set; }
        public bool Enabled { get; set; } = true;
        public Action<ButtonModel, object> Clicked { get; set; }

        public override bool CanClick => Enabled && (Clicked != null || base.CanClick);

        public override bool Click(object sender)
        {
            if (!Enabled)
                return false;

            if (Clicked != null)
            {
                Clicked(this, sender);
                return true;
            }

            return base.Click(sender);
        }

        public override string ToString()
        {
            return Text ?? string.Empty;
        }
    }
}
