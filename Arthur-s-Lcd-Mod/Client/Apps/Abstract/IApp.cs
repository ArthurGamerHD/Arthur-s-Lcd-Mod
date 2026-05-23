using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;

namespace LcdMod.Client.Apps.Abstract
{
    public interface IApp
    {
        void Update();
        void LayoutChanged();
        List<MySprite> GetSprites();
    }
}