using System.Collections.Generic;
using LcdMod.Client.Gui;
using VRage.Game.GUI.TextPanel;

namespace LcdMod.Client.Apps.Abstract
{
    public interface IApp
    {
        void Update();
        void LayoutChanged();
        List<MySprite> GetSprites();
        IReadOnlyList<Control> Children { get; }
        bool HasVisibleItems();
        void OnMouseScroll(int delta, ref bool handled);
    }

}
