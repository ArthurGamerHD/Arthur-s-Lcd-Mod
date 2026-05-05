using Generated;
using LcdMod.Client.TerminalControls.Generic;
using Sandbox.ModAPI;

namespace LcdMod.Client.TerminalControls
{
    public interface IReferenceBlockSelection : IUsesTerminalControl<ListboxReferenceBlockSelection>
    {
        bool IsReferenceBlockCandidate(IMyTerminalBlock block);
    }
}
