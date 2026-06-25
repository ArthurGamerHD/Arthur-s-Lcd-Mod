using System;
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
    public sealed partial class ComboboxGraphWindow : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxGraphWindow()
        {
            var combo = CreateControl<IMyTerminalControlCombobox>("GraphWindowCombo");
            combo.Getter = Getter;
            combo.Setter = Setter;
            combo.ComboBoxContent = Content;
            combo.Visible = Visible;
            combo.Title = MyStringId.GetOrCompute(MOD_PREFIX + "GraphWindow");
            TerminalControl = combo;
        }

        static void Content(List<MyTerminalControlComboBoxItem> list)
        {
            list.Add(new MyTerminalControlComboBoxItem { Key = 0, Value = MyStringId.GetOrCompute(MOD_PREFIX + "GW_1s") });
            list.Add(new MyTerminalControlComboBoxItem { Key = 1, Value = MyStringId.GetOrCompute(MOD_PREFIX + "GW_5s") });
            list.Add(new MyTerminalControlComboBoxItem { Key = 2, Value = MyStringId.GetOrCompute(MOD_PREFIX + "GW_30s") });
            list.Add(new MyTerminalControlComboBoxItem { Key = 3, Value = MyStringId.GetOrCompute(MOD_PREFIX + "GW_1m") });
            list.Add(new MyTerminalControlComboBoxItem { Key = 4, Value = MyStringId.GetOrCompute(MOD_PREFIX + "GW_5m") });
            list.Add(new MyTerminalControlComboBoxItem { Key = 5, Value = MyStringId.GetOrCompute(MOD_PREFIX + "GW_30m") });
        }

        long Getter(IMyTerminalBlock block)
        {
            var cfg = ConfigManager.GetComponentForTerminalApp<PowerConfigComponent>(block);
            return GetConfiguredTier(cfg);
        }

        void Setter(IMyTerminalBlock block, long value)
        {
            ConfigManager.ModifyComponentForTerminalApp<PowerConfigComponent>(
                block,
                cfg =>
                {
                    cfg.PowerHistoryTier = (int)value;
                    cfg.GraphWindowIndex = (int)value;
                });
        }

        static int GetConfiguredTier(PowerConfigComponent cfg)
        {
            if (cfg == null)
                return 2;

            int value = cfg.PowerHistoryTier >= 0 ? cfg.PowerHistoryTier : cfg.GraphWindowIndex;
            return Math.Max(0, Math.Min(value, 5));
        }
    }
}
