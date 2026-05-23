using System;
using LcdMod.Client.Helpers;
using Sandbox.Game.Entities;

namespace LcdMod.Client.Gui.Tooltip
{
    public sealed class StaticTooltipLine : ITooltipLine
    {
        readonly string _text;

        public StaticTooltipLine(string text)
        {
            _text = text ?? string.Empty;
        }

        public string GetText()
        {
            return _text;
        }

        public object GetDataContext()
        {
            return null;
        }

        public Action<object, object> GetOnClick()
        {
            return null;
        }

        public MySoundPair GetClickSound()
        {
            return AudioHelper.HudClick;
        }

        public CursorType? GetCursor()
        {
            return null;
        }

        public bool IsClickable => false;

        public override string ToString()
        {
            return GetText();
        }
    }
}