using Generated;
using Graph.System.TerminalControls.Generic;
using Sandbox.ModAPI;

namespace Graph.System
{
    public interface IReferenceBlockSelection : IUsesTerminalControl<ListboxReferenceBlockSelection>
    {
        bool IsReferenceBlockCandidate(IMyTerminalBlock block);
    }
}
