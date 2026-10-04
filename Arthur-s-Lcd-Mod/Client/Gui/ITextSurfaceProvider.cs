using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Gui
{
    public interface ITextSurfaceProvider
    {
        IMyTextSurface TextSurface { get; }
    }
}
