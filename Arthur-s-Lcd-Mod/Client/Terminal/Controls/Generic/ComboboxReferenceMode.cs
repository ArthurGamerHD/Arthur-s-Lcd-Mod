using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;


namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class ComboboxReferenceMode : TerminalControlsWrapper
    {
        static readonly List<MyTerminalControlComboBoxItem> Modes = new List<MyTerminalControlComboBoxItem>
        {
            new MyTerminalControlComboBoxItem { Key = (long)ReferenceMode.Auto, Value = MyStringId.GetOrCompute("Auto") },
            new MyTerminalControlComboBoxItem { Key = (long)ReferenceMode.Screen, Value = MyStringId.GetOrCompute("Screen") },
            new MyTerminalControlComboBoxItem { Key = (long)ReferenceMode.Controller, Value = MyStringId.GetOrCompute("Controller") }
        };

        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxReferenceMode()
        {
            var combo = CreateControl<IMyTerminalControlCombobox>("ReferenceMode");
            combo.Getter = Getter;
            combo.Setter = Setter;
            combo.ComboBoxContent = Content;
            combo.Visible = Visible;
            combo.Title = MyStringId.GetOrCompute("Reference");
            TerminalControl = combo;
        }

        static void Content(List<MyTerminalControlComboBoxItem> items)
        {
            items.AddRange(Modes);
        }

        static void Setter(IMyTerminalBlock block, long value)
        {
            ConfigManager.ModifyComponentForCurrentSurface<InteractiveConfigComponent>(
                block,
                Constants.INTERACTION,
                config => config.ReferenceMode = (int)value);
        }

        static long Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetComponentForCurrentSurface<InteractiveConfigComponent>(
                block,
                Constants.INTERACTION);
            return config?.ReferenceMode ?? (long)ReferenceMode.Auto;
        }
    }
}
