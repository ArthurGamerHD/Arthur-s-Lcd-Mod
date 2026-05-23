using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates;

namespace LcdMod.Client.Apps.Abstract
{
    public interface IAppInteractive : IApp
    {
        List<ControlBase> InteractiveList { get; }
        bool HasVisibleItems();
        void OnMouseScroll(int delta, ref bool handled);
    }
}