using System;

namespace LcdMod.Client.Gui.UserControls.Power
{
    /// <summary>One line of a power entry's details: live text, optionally an action such as cycling a battery's mode.</summary>
    internal sealed class PowerDetailLine
    {
        readonly Func<string> _text;
        readonly Func<bool> _canClick;

        public PowerDetailLine(string text)
            : this(() => text)
        {
        }

        public PowerDetailLine(Func<string> text, Action clicked = null, Func<bool> canClick = null)
        {
            _text = text ?? (() => string.Empty);
            Clicked = clicked;
            _canClick = canClick;
        }

        public string Text => _text() ?? string.Empty;
        public Action Clicked { get; }
        public bool IsAction => Clicked != null;
        public bool CanClick => Clicked != null && (_canClick == null || _canClick());

        public void Click()
        {
            if (CanClick)
                Clicked();
        }
    }
}
