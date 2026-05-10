using System.Collections.Generic;
using Generated;
using LcdMod.Client.Terminal.Controls.Generic;
using VRage.ModAPI;

namespace LcdMod.Client.Terminal.Controls
{
    public interface IMultiDisplayMode : IUsesTerminalControl<ComboboxDisplayMode>
    {
        List<MyTerminalControlComboBoxItem> GetDisplayModes();
    }
}
