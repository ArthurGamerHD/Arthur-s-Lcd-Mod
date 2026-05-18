using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps
{
    public interface IApp
    {
        void Update();
        void LayoutChanged();
        List<MySprite> GetSprites();
    }
}