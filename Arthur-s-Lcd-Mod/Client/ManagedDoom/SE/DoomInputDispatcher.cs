using System.Collections.Generic;
using Sandbox.ModAPI;

namespace ManagedDoom.SE
{
    /// <summary>
    /// Samples raw keyboard and optional mouse input once per Space Engineers
    /// frame for the locally controlled cockpit referenced by an active Doom
    /// surface. No camera transform or control blacklist is applied.
    /// </summary>
    public static class DoomInputDispatcher
    {
        static readonly List<SECockpitUserInput> Inputs =
            new List<SECockpitUserInput>();

        public static void Register(SECockpitUserInput input)
        {
            if (input != null && !Inputs.Contains(input))
                Inputs.Add(input);
        }

        public static void Unregister(SECockpitUserInput input)
        {
            if (input != null)
                Inputs.Remove(input);
        }

        public static void CaptureInput()
        {
            var gui = MyAPIGateway.Gui;
            bool blocked = gui != null &&
                (gui.IsCursorVisible ||
                 gui.ChatEntryVisible ||
                 gui.ActiveGamePlayScreen != null);

            SECockpitUserInput owner = null;
            if (!blocked)
            {
                for (int i = Inputs.Count - 1; i >= 0; i--)
                {
                    var candidate = Inputs[i];
                    if (candidate != null && candidate.TryCaptureFrameInput())
                    {
                        owner = candidate;
                        break;
                    }
                }
            }

            // Ensure stale held keys cannot continue driving Doom after the
            // terminal opens, the player leaves the seat, or another app owns
            // the selected cockpit.
            for (int i = Inputs.Count - 1; i >= 0; i--)
            {
                var input = Inputs[i];
                if (input != null && !object.ReferenceEquals(input, owner))
                    input.ClearCapturedInput();
            }
        }
    }
}

namespace LcdMod
{
    public partial class LcdModSessionComponent
    {
        public override void HandleInput()
        {
            base.HandleInput();
            ManagedDoom.SE.DoomInputDispatcher.CaptureInput();
        }
    }
}
