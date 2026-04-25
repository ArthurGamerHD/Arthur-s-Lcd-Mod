using System.Collections.Generic;
using Generated;
using Graph.System.TerminalControls.Generic;
using VRage.ModAPI;

namespace Graph.System
{
    public interface IMultiDisplayMode : IUsesTerminalControl<ComboboxDisplayMode>
    {
        List<MyTerminalControlComboBoxItem> GetDisplayModes();
    }
}
