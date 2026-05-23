using System;
using Sandbox.Game.Entities;

namespace LcdMod.Client.Gui.Tooltip
{
    public interface ITooltipLine
    {
        string GetText();
        object GetDataContext();
        Action<object, object> GetOnClick();
        MySoundPair GetClickSound();
        CursorType? GetCursor();
        bool IsClickable { get; }
    }
}