using System.Collections.Generic;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;

namespace LcdMod.Client.Apps.Abstract
{
    public interface IApp : IVisualStyleScope
    {
        void Update();
        void LayoutChanged();
        List<MySprite> GetSprites();
        IReadOnlyList<Control> LogicalChildren { get; }
        IReadOnlyList<Control> VisualChildren { get; }
        bool HasVisibleItems();
        void OnMouseScroll(int delta, ref bool handled);
    }

}
