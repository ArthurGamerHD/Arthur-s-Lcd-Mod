using System.Collections.Generic;
using Generated;
using LcdMod.Client.TerminalControls.Generic;
using VRage.ModAPI;

namespace LcdMod.Client.TerminalControls
{
    public interface IMultiDisplayMode : IUsesTerminalControl<ComboboxDisplayMode>
    {
        List<MyTerminalControlComboBoxItem> GetDisplayModes();
    }
}
