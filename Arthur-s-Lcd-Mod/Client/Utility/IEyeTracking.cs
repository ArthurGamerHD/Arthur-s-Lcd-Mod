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
    public interface IEyeTracking : IInputBlock,
        ISoundCapable,
        IUsesTerminalControl<SliderCursorScale>,
        IUsesTerminalControl<SwitchToggleAlt>
    {
        Vector2 CursorPosition { get; }

        Vector2 HitTestOffset { get; }

        ICollection<InteractiveEntry> InteractiveEntries { get; }
        
        CursorType CursorType { get; }
        
        void LookAt(Vector2 onScreenCoordinates);

        void MouseScroll(int delta);
    }
}
