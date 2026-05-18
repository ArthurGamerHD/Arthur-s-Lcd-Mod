using System.Collections.Generic;
using LcdMod.Client.Utility;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps
{
    public interface IAppInteractive : IApp
    {
        List<InteractiveEntry> InteractiveList { get; }
        bool HasVisibleItems();
    }
}