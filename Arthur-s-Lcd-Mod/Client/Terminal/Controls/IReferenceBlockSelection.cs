using System.Collections.Generic;
using Generated;
using LcdMod.Client.Terminal.Controls.Generic;
using Sandbox.ModAPI;

namespace LcdMod.Client.Terminal.Controls
{
    public interface IReferenceBlockSelection : IUsesTerminalControl<ListboxReferenceBlockSelection>
    {
        bool IsReferenceBlockCandidate(IMyTerminalBlock block);
        bool TryGetReferenceBlockCandidates(List<IMyTerminalBlock> blocks);
    }
}
