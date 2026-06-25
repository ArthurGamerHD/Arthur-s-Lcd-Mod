using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.ModAPI;

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
            var settings = ConfigManager.GetConfigForBlock(b);
            var surface = settings == null ? null : settings.GetSurfaceConfig(GetThisSurfaceIndex(b));
            var screenSettings = surface == null ? null : surface.TryGet<ItemSelectionConfigComponent>(Constants.ITEMS);

            if (screenSettings == null)
                return;

            var selectedCategories = screenSettings.SelectedCategories ?? new string[0];
            var selectedDefinitions = screenSettings.SelectedDefinition ?? new string[0];

            itemList.AddRange(selectedCategories
                .Select(g => ListBoxItemHelper.GetOrComputeListBoxItem(ItemCategoryHelper.GetGroupName(g), string.Empty, g)));

            foreach (var selectedDefinition in selectedDefinitions)
            {
                MyDefinitionId item;
                if (!MyDefinitionId.TryParse(selectedDefinition, out item))
                    continue;

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
