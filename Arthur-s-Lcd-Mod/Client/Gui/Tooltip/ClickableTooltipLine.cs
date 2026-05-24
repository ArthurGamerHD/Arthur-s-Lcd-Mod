using System;
using LcdMod.Client.Helpers;
using Sandbox.Game.Entities;

namespace LcdMod.Client.Gui.Tooltip
{
    public sealed class ClickableTooltipLine : ITooltipLine
    {
        readonly string _text;
        readonly object _dataContext;
        readonly Action<object, object> _onClick;

        public ClickableTooltipLine(string text, object dataContext, Action<object, object> onClick)
        {
            _text = text ?? string.Empty;
            _dataContext = dataContext;
            _onClick = onClick;
        }

        public MySoundPair ClickSound { get; set; } = AudioHelper.HudClick;

        public string GetText()
        {
            return _text;
        }

        public object GetDataContext()
        {
            return _dataContext ?? this;
        }

        public Action<object, object> GetOnClick()
        {
            return _onClick;
        }

        public MySoundPair GetClickSound()
        {
            return ClickSound;
        }

        public CursorType? GetCursor()
        {
            return IsClickable ? (CursorType?)CursorType.Hand : null;
        }

        public bool IsClickable => _onClick != null;

        public override string ToString()
        {
            return GetText();
        }
    }
}