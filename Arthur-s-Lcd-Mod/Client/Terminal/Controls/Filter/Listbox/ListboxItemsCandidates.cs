using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace LcdMod.Client.Terminal.Controls.Filter.Listbox
{
    public sealed partial class ListboxItemsCandidates : TerminalControlsListbox
    {
        public ListboxItemsCandidates()
        {
            CreateListbox("CandidatesItems", "BlockPropertyTitle_ConveyorSorterCandidatesList");
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

            itemList.AddRange(ItemCategoryHelper.Groups.Where(g => !selectedCategories.Contains(g))
                .Select(g => ListBoxItemHelper.GetOrComputeListBoxItem(ItemCategoryHelper.GetGroupName(g), string.Empty, g)));
            
            var allItems = MyDefinitionManager.Static.GetAllDefinitions()
                .Where(WhiteList)
                .Where(a => !selectedDefinitions.Contains(a.Id.ToString()))
                .ToList();

            itemList.AddRange(allItems.Select(a => ListBoxItemHelper.GetOrComputeListBoxItem(a.DisplayNameText,a.DescriptionText, a.Id)));

            base.Getter(b, itemList, selected);
        }

        public bool WhiteList(object a)
        {
            var item = a as MyPhysicalItemDefinition;
            
            if(item == null)
                return false;

            var id = item.Id.ToString();
            if(id.Contains("_TreeObject/") || id.Contains("GunObject/GoodAIReward") || id.Contains("GunObject/CubePlacerItem") )
                return false;
            
            return true;

        }
    }
}
