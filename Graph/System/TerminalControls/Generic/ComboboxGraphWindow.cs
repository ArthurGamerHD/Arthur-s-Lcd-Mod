using System.Collections.Generic;
using Graph.Apps.Power;
using Graph.System.Config;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace Graph.System.TerminalControls.Generic
{
    public sealed class ComboboxGraphWindow : TerminalControlsWrapper
    {
        protected override string[] VisibleForScripts { get; } =
        {
            EnergyDashboardSurfaceScript.ID
        };

        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxGraphWindow()
        {
            var combo = CreateControl<IMyTerminalControlCombobox>("GraphWindowCombo");
            combo.Getter = Getter;
            combo.Setter = Setter;
            combo.ComboBoxContent = Content;
            combo.Visible = Visible;
            combo.Title = MyStringId.GetOrCompute("LCDMod_GraphWindow");
            TerminalControl = combo;
        }

        static void Content(List<MyTerminalControlComboBoxItem> list)
        {
            list.Add(new MyTerminalControlComboBoxItem { Key = 0, Value = MyStringId.GetOrCompute("LCDMod_GW_1s") });
            list.Add(new MyTerminalControlComboBoxItem { Key = 1, Value = MyStringId.GetOrCompute("LCDMod_GW_5s") });
            list.Add(new MyTerminalControlComboBoxItem { Key = 2, Value = MyStringId.GetOrCompute("LCDMod_GW_30s") });
            list.Add(new MyTerminalControlComboBoxItem { Key = 3, Value = MyStringId.GetOrCompute("LCDMod_GW_1m") });
            list.Add(new MyTerminalControlComboBoxItem { Key = 4, Value = MyStringId.GetOrCompute("LCDMod_GW_5m") });
        }

        long Getter(IMyTerminalBlock block)
        {
            var cfg = ConfigManager.GetConfigForCurrentScreen(block);
            return cfg?.GraphWindowIndex ?? 2;
        }

        void Setter(IMyTerminalBlock block, long value)
        {
            var cfg = ConfigManager.GetConfigForCurrentScreen(block);
            if (cfg == null) return;
            cfg.GraphWindowIndex = (int)value;
            ConfigManager.Sync(block);
        }
    }
}
