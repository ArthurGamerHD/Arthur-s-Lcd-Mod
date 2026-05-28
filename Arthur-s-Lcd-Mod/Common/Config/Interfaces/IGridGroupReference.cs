using Generated;
using VRage.Game.ModAPI;

namespace LcdMod.Common.Config.Interfaces
{
    /// <summary>
    /// Settings with this interface can clone projector config between themselves
    /// </summary>
    internal interface IGridGroupReference : ICloneSource
    {
        int GridLinkTypeInternal { get; set; }
    }
}
