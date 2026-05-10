using System.Collections.Generic;
using Generated;
using LcdMod.Client.Gui;
using VRageMath;
using SliderCursorScale = LcdMod.Client.Terminal.Controls.Interactive.SliderCursorScale;
using SwitchToggleAlt = LcdMod.Client.Terminal.Controls.Interactive.SwitchToggleAlt;

namespace LcdMod.Client.Utility
{
    /// <summary>
    /// Receives gaze coordinates that should be consumed and mapped on the next render frame.
    /// </summary>
    public interface IEyeTracking : ISoundCapable,
        IUsesTerminalControl<SliderCursorScale>,
        IUsesTerminalControl<SwitchToggleAlt>
    {
        Sandbox.ModAPI.Ingame.IMyTextSurface Surface { get; }
        VRage.Game.ModAPI.Ingame.IMyCubeBlock Block { get; }
        
        int RotationOrSurfaceIndex { get; }

        Vector2 CursorPosition { get; }

        ICollection<InteractiveEntry> InteractiveEntries { get; }
        
        CursorType CursorType { get; }
        
        void LookAt(Vector2 onScreenCoordinates);

        void MouseScroll(int delta);
    }
}
