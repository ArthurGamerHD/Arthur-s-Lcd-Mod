using Generated;
using Graph.System.TerminalControls.Scale;
using VRageMath;

namespace Graph.Apps.Utility
{
    /// <summary>
    /// Receives gaze coordinates that should be consumed and mapped on the next render frame.
    /// </summary>
    public interface IEyeTracking : IUsesTerminalControl<SliderCursorScale>
    {
        Sandbox.ModAPI.Ingame.IMyTextSurface Surface { get; }
        VRage.Game.ModAPI.Ingame.IMyCubeBlock Block { get; }
        
        int RotationOrSurfaceIndex { get; }
        
        void LookAt(Vector2 onScreenCoordinates);
    }
}
