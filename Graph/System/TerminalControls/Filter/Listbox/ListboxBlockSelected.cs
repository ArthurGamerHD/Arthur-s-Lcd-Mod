using System.Collections.Generic;
using System.Linq;
using Graph.Apps.Antenna;
using Graph.Helpers;
using Graph.System.Config;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace Graph.System.TerminalControls.Filter.Listbox
{
    public sealed partial class ListboxBlockSelected : TerminalControlsListbox
    {
        public ListboxBlockSelected()
        {
            CreateListbox("SelectedBlocks", "EventControllerBlock_SelectedBlocks_Title");
        }

        protected override void Getter(IMyTerminalBlock b, List<MyTerminalControlListBoxItem> blockList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var screenSettings = ConfigManager.GetConfigForCurrentScreen(b);

            if (screenSettings == null)
                return;
            
            var script = ((IMyTextSurfaceProvider)b).GetSurface(GetThisSurfaceIndex(b)).Script;
            if (script != AntennaSurfaceScript.ID) // antenna does not support groups
                blockList.AddRange(screenSettings.SelectedGroups.Select(a => ListBoxItemHelper.GetOrComputeListBoxItem(
                    $"*{a}*",
                    $"{MyTexts.GetString("Terminal_GroupTitle")} {a}",
                    a)));

            if (!screenSettings.SelectedBlocks.Any())
                return;

            foreach (var id in screenSettings.SelectedBlocks)
            {
                var block = MyAPIGateway.Entities.GetEntityById(id) as IMyCubeBlock;

                if (block == null) 
                    continue;
                
                MyTerminalControlListBoxItem listBoxItem;
                if (!ListBoxItemHelper.TryGetListBoxItem(block.EntityId, out listBoxItem))
                {
                    if (block.CubeGrid.Equals(b.CubeGrid))
                    {
                        listBoxItem = ListBoxItemHelper.GetOrComputeListBoxItem(
                            block.DisplayNameText,
                            block.DisplayNameText,
                            block.EntityId);
                    }
                    else if (block.CubeGrid.IsInSameLogicalGroupAs(b.CubeGrid))
                    {
                        listBoxItem = ListBoxItemHelper.GetOrComputeListBoxItem(
                            $"@{block.DisplayNameText}@",
                            block.CubeGrid.DisplayName + " => " + block.DisplayNameText,
                            block.EntityId);
                    }
                    else
                    {
                        listBoxItem = ListBoxItemHelper.GetOrComputeListBoxItem(
                            MyTexts.GetString(MyStringId.Get("EventControllerBlock_UnknownBlock")),
                            string.Format(
                                MyTexts.GetString(MyStringId.Get("EventControllerBlock_UnknownBlockTooltip")),
                                block.EntityId),
                            block.EntityId);
                    }
                }

                blockList.Add(listBoxItem);
            }

            base.Getter(b, blockList, selected);
        }
    }
}
