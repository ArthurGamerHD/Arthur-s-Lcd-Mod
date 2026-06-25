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
    public sealed partial class ComboboxSorting : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        public ComboboxSorting()
        {
            var slider = CreateControl<IMyTerminalControlCombobox>("ComboboxSorting");
            slider.Getter = Getter;
            slider.ComboBoxContent = Content;
            slider.Setter = Setter;
            slider.Visible = Visible;
            slider.Title = MyStringId.GetOrCompute("ScreenDebugAdminMenu_SortBy");

            TerminalControl = slider;
        }

        void Content(List<MyTerminalControlComboBoxItem> obj)
        {
            obj.Add(new MyTerminalControlComboBoxItem
            {
                Key = 0,
                Value = MyStringId.GetOrCompute("StoreBlock_Column_Amount")
            });
            
            obj.Add(new MyTerminalControlComboBoxItem
            {
                Key = 1,
                Value = MyStringId.GetOrCompute("ScreenDebugSpawnMenu_ItemType")
            });
        }

        void Setter(IMyTerminalBlock block, long l)
        {
            ConfigManager.ModifyComponentForCurrentSurface<FilterConfigComponent>(
                block,
                Constants.FILTERS,
                config => config.SortMethod = (int)l);
        }

        long Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetComponentForCurrentSurface<FilterConfigComponent>(
                block,
                Constants.FILTERS);
            if (config == null)
                return 1;

            return config.SortMethod;
        }
    }
}
