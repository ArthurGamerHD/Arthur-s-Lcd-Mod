using Generated;

namespace LcdMod.Common.Config.Interfaces
{
    /// <summary>
    /// Settings with this interface can clone projector config between themselves
    /// </summary>
    internal interface IProjectorReference : IConfigWithReferenceBlock, ICloneSource
    { }
}
