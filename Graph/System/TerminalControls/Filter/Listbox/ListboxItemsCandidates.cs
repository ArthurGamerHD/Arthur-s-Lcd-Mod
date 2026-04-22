using System;
using System.Collections.Generic;
using System.Linq;
using Graph.Helpers;
using Graph.System.Config;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Graph.System.TerminalControls.Filter.Listbox
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
            var screenSettings = ConfigManager.GetConfigForCurrentScreen(b);

            if (screenSettings == null)
                return;
            
            itemList.AddRange(ItemCategoryHelper.Groups.Where(g => !screenSettings.SelectedCategories.Contains(g))
                .Select(g => ListBoxItemHelper.GetOrComputeListBoxItem(ItemCategoryHelper.GetGroupName(g), string.Empty, g)));
            
            var allItems = MyDefinitionManager.Static.GetAllDefinitions().Where(WhiteList).Where(a => !screenSettings.SelectedItems.Contains(a.Id)).ToList();

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
