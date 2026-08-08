using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Layout;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class ComboboxButtonStyle : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxButtonStyle()
        {
            var combo = CreateControl<IMyTerminalControlCombobox>("ButtonStyle");
            combo.Getter = Getter;
            combo.Setter = Setter;
            combo.ComboBoxContent = Content;
            combo.Visible = Visible;
            combo.Title = MyStringId.GetOrCompute("Button Style");
            combo.Tooltip = MyStringId.GetOrCompute("Visual style used by Button Panel buttons.");
            TerminalControl = combo;
        }

        static void Content(List<MyTerminalControlComboBoxItem> items)
        {
            items.Add(CreateItem(ButtonPanelStyle.Default, "Default"));
            items.Add(CreateItem(ButtonPanelStyle.Classic, "Classic"));
            items.Add(CreateItem(ButtonPanelStyle.Transparent, "Transparent"));
            items.Add(CreateItem(ButtonPanelStyle.Border, "Border"));
        }

        static MyTerminalControlComboBoxItem CreateItem(ButtonPanelStyle style, string name)
        {
            return new MyTerminalControlComboBoxItem
            {
                Key = (long)style,
                Value = MyStringId.GetOrCompute(name)
            };
        }

        static void Setter(IMyTerminalBlock block, long value)
        {
            var style = Normalize(value);
            ConfigManager.ModifyComponentForTerminalApp<ButtonPanelConfigComponent>(
                block,
                config => config.ButtonStyle = (int)style);
        }

        static long Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetComponentForTerminalApp<ButtonPanelConfigComponent>(block);
            return (long)Normalize(config?.ButtonStyle ?? (int)ButtonPanelStyle.Default);
        }

        static ButtonPanelStyle Normalize(long value)
        {
            switch ((ButtonPanelStyle)value)
            {
                case ButtonPanelStyle.Classic:
                    return ButtonPanelStyle.Classic;
                case ButtonPanelStyle.Transparent:
                    return ButtonPanelStyle.Transparent;
                case ButtonPanelStyle.Border:
                    return ButtonPanelStyle.Border;
                default:
                    return ButtonPanelStyle.Default;
            }
        }
    }
}
