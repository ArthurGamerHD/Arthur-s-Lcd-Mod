using System.Collections.Generic;
using LcdMod.Client.Config;

using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;
using ScreenConfigWithFilters = LcdMod.Common.Config.Models.Apps.ScreenConfigWithFilters;

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
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigWithFilters;
            if (config == null)
                return;

            config.SortMethod = (int)l;
            ConfigManager.Sync(block);
        }

        long Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigWithFilters;
            if (config == null)
                return 1;

            return config.SortMethod;
        }
    }
}
