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
    /// <summary>How a multi-function display shows its apps: full-screen pages or windows.</summary>
    public sealed partial class ComboboxMultiFunctionMode : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxMultiFunctionMode()
        {
            var combo = CreateControl<IMyTerminalControlCombobox>("MultiFunctionModeCombobox");
            combo.Getter = Getter;
            combo.Setter = Setter;
            combo.ComboBoxContent = Content;
            combo.Visible = Visible;
            combo.Title = MyStringId.GetOrCompute(MOD_PREFIX + "MultiFunction_Mode");
            TerminalControl = combo;
        }

        static void Content(List<MyTerminalControlComboBoxItem> list)
        {
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)MultiFunctionMode.Pages, Value = MyStringId.GetOrCompute(MOD_PREFIX + "MultiFunction_Mode_Pages")
            });
            list.Add(new MyTerminalControlComboBoxItem
            {
                Key = (long)MultiFunctionMode.Windows, Value = MyStringId.GetOrCompute(MOD_PREFIX + "MultiFunction_Mode_Windows")
            });
        }

        long Getter(IMyTerminalBlock block)
        {
            var cfg = ConfigManager.GetComponentForTerminalApp<MultiFunctionConfigComponent>(block);
            return cfg == null ? 0 : (long)cfg.Mode;
        }

        void Setter(IMyTerminalBlock block, long value)
        {
            ConfigManager.ModifyComponentForTerminalApp<MultiFunctionConfigComponent>(
                block,
                cfg => cfg.Mode = (MultiFunctionMode)value);
        }
    }
}
