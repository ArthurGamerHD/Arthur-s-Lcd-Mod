using System.Collections.Generic;
using Graph.Apps.Inventory;
using Graph.Apps.Refinery;
using Graph.System.Config;
using Graph.System.Config.Models.Apps;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace Graph.System.TerminalControls.Generic
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

            config.SortInternal = (int)l;
            ConfigManager.Sync(block);
        }

        long Getter(IMyTerminalBlock block)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as ScreenConfigWithFilters;
            if (config == null)
                return 1;

            return config.SortInternal;
        }
    }
}
