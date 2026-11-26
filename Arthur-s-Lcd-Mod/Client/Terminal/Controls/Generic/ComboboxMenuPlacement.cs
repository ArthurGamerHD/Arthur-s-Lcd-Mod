using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    /// <summary>The side of a multi-function display that holds its bar (page bar or taskbar).</summary>
    public sealed partial class ComboboxMenuPlacement : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxMenuPlacement()
        {
            var combo = CreateControl<IMyTerminalControlCombobox>("MultiFunctionMenuPlacementCombobox");
            combo.Getter = Getter;
            combo.Setter = Setter;
            combo.ComboBoxContent = Content;
            combo.Visible = Visible;
            combo.Title = MyStringId.GetOrCompute(MOD_PREFIX + "MultiFunction_MenuPlacement");
            TerminalControl = combo;
        }

        static void Content(List<MyTerminalControlComboBoxItem> list)
        {
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)MenuPlacement.Bottom, Value = MyStringId.GetOrCompute(MOD_PREFIX + "MultiFunction_Placement_Bottom")
            });
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)MenuPlacement.Top, Value = MyStringId.GetOrCompute(MOD_PREFIX + "MultiFunction_Placement_Top")
            });
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)MenuPlacement.Left, Value = MyStringId.GetOrCompute(MOD_PREFIX + "MultiFunction_Placement_Left")
            });
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)MenuPlacement.Right, Value = MyStringId.GetOrCompute(MOD_PREFIX + "MultiFunction_Placement_Right")
            });
        }

        long Getter(IMyTerminalBlock block)
        {
            var cfg = ConfigManager.GetComponentForTerminalApp<MultiFunctionConfigComponent>(block);
            return cfg == null ? 0 : (long)cfg.MenuPlacement;
        }

        void Setter(IMyTerminalBlock block, long value)
        {
            ConfigManager.ModifyComponentForTerminalApp<MultiFunctionConfigComponent>(
                block,
                cfg => cfg.MenuPlacement = (MenuPlacement)value);
        }
    }
}
