using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Client.Utility;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class ComboboxItemDisplayMode : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxItemDisplayMode()
        {
            var comboBox = CreateControl<IMyTerminalControlCombobox>("ComboboxItemDisplayMode");
            comboBox.Getter = Getter;
            comboBox.ComboBoxContent = Content;
            comboBox.Setter = Setter;
            comboBox.Visible = Visible;
            comboBox.Title = MyStringId.GetOrCompute("DisplayName_Item_Display");
            TerminalControl = comboBox;
        }

        static void Content(List<MyTerminalControlComboBoxItem> items)
        {
            items.AddRange(DisplayModes.Items);
        }

        static void Setter(IMyTerminalBlock block, long value)
        {
            ConfigManager.ModifyComponentForCurrentSurface<ItemDisplayConfigComponent>(
                block,
                Constants.ITEM_DISPLAY,
                config => config.DisplayMode = (int)value);
        }

        static long Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetComponentForCurrentSurface<ItemDisplayConfigComponent>(
                block,
                Constants.ITEM_DISPLAY);
            var legacy = ConfigManager.GetComponentForCurrentSurface<GeneralConfigComponent>(
                block,
                Constants.GENERAL);
            return (long)config.ResolveDisplayMode(legacy);
        }
    }
}
