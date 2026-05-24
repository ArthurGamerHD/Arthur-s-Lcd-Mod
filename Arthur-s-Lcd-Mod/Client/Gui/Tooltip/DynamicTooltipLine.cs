using System;
using Sandbox.Game.Entities;

namespace LcdMod.Client.Gui.Tooltip
{
    public sealed class DynamicTooltipLine : ITooltipLine
    {
        readonly Func<string> _getText;
        readonly Func<bool> _isClickable;
        readonly Func<object> _getDataContext;
        readonly Func<Action<object, object>> _getOnClick;
        readonly Func<CursorType?> _getCursor;
        readonly Func<MySoundPair> _getClickSound;

        public DynamicTooltipLine(
            Func<string> getText,
            Func<bool> isClickable = null,
            Func<object> getDataContext = null,
            Func<Action<object, object>> getOnClick = null,
            Func<CursorType?> getCursor = null,
            Func<MySoundPair> getClickSound = null)
        {
            _getText = getText;
            _isClickable = isClickable;
            _getDataContext = getDataContext;
            _getOnClick = getOnClick;
            _getCursor = getCursor;
            _getClickSound = getClickSound;
        }

        public string GetText()
        {
            return _getText != null ? _getText() : string.Empty;
        }

        public bool IsClickable => _isClickable != null && _isClickable();

        public object GetDataContext() => _getDataContext != null ? _getDataContext() : this;

        public Action<object, object> GetOnClick() => _getOnClick?.Invoke();

        public CursorType? GetCursor() => _getCursor?.Invoke();

        public MySoundPair GetClickSound() => _getClickSound?.Invoke();

        public override string ToString() => GetText();
    }
}