using Generated;
using Graph.System.TerminalControls.Scale;
using System;
using System.Collections.Generic;
using Graph.Helpers;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace Graph.Apps.Utility
{
    /// <summary>
    /// Receives gaze coordinates that should be consumed and mapped on the next render frame.
    /// </summary>
    public interface IEyeTracking : ISoundCapable, IUsesTerminalControl<SliderCursorScale>
    {
        Sandbox.ModAPI.Ingame.IMyTextSurface Surface { get; }
        VRage.Game.ModAPI.Ingame.IMyCubeBlock Block { get; }
        
        int RotationOrSurfaceIndex { get; }

        Vector2 CursorPosition { get; }

        ICollection<InteractiveEntry> InteractiveEntries { get; }
        
        CursorType CursorType { get; }
        
        void LookAt(Vector2 onScreenCoordinates);
    }
}
