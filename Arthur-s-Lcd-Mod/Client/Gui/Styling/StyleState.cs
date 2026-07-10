using System;

namespace LcdMod.Client.Gui.Styling
{
    [Flags]
    public enum StyleState
    {
        None = 0,
        Hover = 1 << 0,
        Pressed = 1 << 1,
        Active = 1 << 2,
        Opened = 1 << 3,
        Selected = 1 << 4,
        Disabled = 1 << 5,
        Focused = 1 << 6,
        Dragged = 1 << 7
    }
}
