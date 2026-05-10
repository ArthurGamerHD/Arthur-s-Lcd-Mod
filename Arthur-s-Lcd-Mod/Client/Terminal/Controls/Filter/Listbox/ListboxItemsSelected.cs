using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.ModAPI;
using ScreenConfigWithItems = LcdMod.Common.Config.Models.Apps.ScreenConfigWithItems;

namespace LcdMod.Client.Terminal.Controls.Filter.Listbox
{
    public sealed partial class ListboxItemsSelected : TerminalControlsListbox
    {
        public ListboxItemsSelected()
        {
            CreateListbox("SelectedItems", "BlockPropertyTitle_ConveyorSorterFilterItemsList");
        }

        protected override void Getter(IMyTerminalBlock b, List<MyTerminalControlListBoxItem> itemList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var screenSettings = ConfigManager.GetConfigForCurrentScreen(b) as ScreenConfigWithItems;

            if (screenSettings == null)
                return;

            itemList.AddRange(screenSettings.SelectedCategories
                .Select(g => ListBoxItemHelper.GetOrComputeListBoxItem(ItemCategoryHelper.GetGroupName(g), string.Empty, g)));

            foreach (var item in screenSettings.SelectedItems)
            {
                MyTerminalControlListBoxItem listBoxItem;
                if (!ListBoxItemHelper.TryGetListBoxItem(item, out listBoxItem))
                {
                    string name = null;
                    string desc = null;

                    var itemDef = MyDefinitionManager.Static.TryGetPhysicalItemDefinition(item);

                    if (itemDef.DisplayNameEnum != null)
                        name = MyTexts.GetString(itemDef.DisplayNameEnum.Value);

                    if (itemDef.DescriptionEnum != null)
                        desc = MyTexts.GetString(itemDef.DescriptionEnum.Value);

                    if (string.IsNullOrEmpty(name))
                        name = $"@{item}@";

                    if (string.IsNullOrEmpty(desc))
                        desc = $"@{item}@";

                    listBoxItem = ListBoxItemHelper.GetOrComputeListBoxItem(name, desc, item);
                }

                itemList.Add(listBoxItem);
            }

            base.Getter(b, itemList, selected);
        }
    }
}
