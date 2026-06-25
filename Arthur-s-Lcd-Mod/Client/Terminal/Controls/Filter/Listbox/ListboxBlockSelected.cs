using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using AntennaSurfaceScript = LcdMod.Client.SurfaceScripts.AntennaSurfaceScript;

namespace LcdMod.Client.Terminal.Controls.Filter.Listbox
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
            var settings = ConfigManager.GetConfigForBlock(b);
            var surface = settings == null ? null : settings.GetSurfaceConfig(GetThisSurfaceIndex(b));
            var screenSettings = surface == null ? null : surface.TryGet<BlockSelectionConfigComponent>(Constants.BLOCKS);

            if (screenSettings == null)
                return;
            
            var script = ((IMyTextSurfaceProvider)b).GetSurface(GetThisSurfaceIndex(b)).Script;
            var selectedGroups = screenSettings.SelectedGroups ?? new string[0];
            var selectedBlocks = screenSettings.SelectedBlocks ?? new long[0];

            if (script != AntennaSurfaceScript.ID) // antenna does not support groups
                blockList.AddRange(selectedGroups.Select(a => ListBoxItemHelper.GetOrComputeListBoxItem(
                    $"*{a}*",
                    $"{MyTexts.GetString("Terminal_GroupTitle")} {a}",
                    a)));

            if (!selectedBlocks.Any())
                return;

            foreach (var id in selectedBlocks)
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
