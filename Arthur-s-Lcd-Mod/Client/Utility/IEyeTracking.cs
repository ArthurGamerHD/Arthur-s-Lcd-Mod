using Generated;
using VRageMath;
using SwitchToggleAlt = LcdMod.Client.Terminal.Controls.Interactive.SwitchToggleAlt;

namespace LcdMod.Client.Utility
{
    /// <summary>
    /// Receives gaze coordinates that should be consumed and mapped on the next render frame.
    /// </summary>
    public interface IEyeTracking : ISoundCapable,
        IUsesTerminalControl<SwitchToggleAlt>
    {
        VRage.Game.ModAPI.Ingame.IMyCubeBlock Block { get; }

        Vector2 CursorPosition { get; }

        Vector2 HitTestOffset { get; }

        void LookAt(Vector2 onScreenCoordinates);

        void MouseScroll(int delta);
    }
}
