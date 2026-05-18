using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;

using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using AntennaSurfaceScript = LcdMod.Client.SurfaceScripts.AntennaSurfaceScript;
using CargoFilledSurfaceScript = LcdMod.Client.SurfaceScripts.CargoFilledSurfaceScript;
using IMyBlockGroup = Sandbox.ModAPI.Ingame.IMyBlockGroup;
using InventoryLcdSurfaceScript = LcdMod.Client.Apps.InventoryLcdSurfaceScript;
using ProjectorLcdSurfaceScript = LcdMod.Client.Apps.ProjectorLcdSurfaceScript;
using ScreenConfigWithBlocks = LcdMod.Common.Config.Models.Apps.ScreenConfigWithBlocks;

namespace LcdMod.Client.Terminal.Controls.Filter.Listbox
{
    public sealed partial class ListboxBlockCandidates : TerminalControlsListbox
    {
        readonly List<IMyCubeGrid> _grids = new List<IMyCubeGrid>();
        readonly List<IMySlimBlock> _blocks = new List<IMySlimBlock>();
        readonly List<IMyBlockGroup> _groups = new List<IMyBlockGroup>();

        public ListboxBlockCandidates()
        {
            CreateListbox("CandidatesBlocks", "EventControllerBlock_AvailableBlocks_Title");
        }

        protected override void Getter(IMyTerminalBlock b, List<MyTerminalControlListBoxItem> blockList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var screenSettings = ConfigManager.GetConfigForCurrentScreen(b) as ScreenConfigWithBlocks;

            if (screenSettings == null)
                return;

            _grids.Clear();
            _groups.Clear();

            var script = ((IMyTextSurfaceProvider)b).GetSurface(GetThisSurfaceIndex(b)).Script;

            var referenceGrid = b.CubeGrid;
            
            if (script != AntennaSurfaceScript.ID) // antenna does not support groups
            {
                MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(b.CubeGrid).GetBlockGroups(_groups,
                    g => !screenSettings.SelectedGroups.Contains(g.Name));
                blockList.AddRange(_groups.Select(a => ListBoxItemHelper.GetOrComputeListBoxItem(
                    $"*{a.Name}*",
                    $"{MyStringId.GetOrCompute("Terminal_GroupTitle")} {a.Name}",
                    a.Name)));
            }

            _blocks.Clear();

            referenceGrid.GetBlocks(_blocks, c => IsValidBlock(c, b, screenSettings, script));
            blockList.AddRange(_blocks.Select(a => ListBoxItemHelper.GetOrComputeListBoxItem(
                a.FatBlock.DisplayNameText,
                a.FatBlock.DisplayNameText,
                a.FatBlock.EntityId)));

            MyAPIGateway.GridGroups.GetGroup(referenceGrid, GridLinkTypeEnum.Logical, _grids);
            
            foreach (var grid in _grids)
            {
                if (grid == b.CubeGrid)
                    continue;

                _blocks.Clear();

                grid.GetBlocks(_blocks, c => IsValidBlock(c, b, screenSettings, script));

                blockList.AddRange(_blocks.Select(a => ListBoxItemHelper.GetOrComputeListBoxItem(
                    $"@{a.FatBlock.DisplayNameText}@",
                    a.FatBlock.CubeGrid.DisplayName + " => " + a.FatBlock.DisplayNameText,
                    a.FatBlock.EntityId)));

                _blocks.Clear();
            }

            base.Getter(b, blockList, selected);
        }

        bool IsValidBlock(IMySlimBlock block, IMyTerminalBlock referenceBlock, ScreenConfigWithBlocks config, string script)
        {
            var fat = block?.FatBlock;

            if (fat == null ||  
                config.SelectedBlocks.Contains(fat.EntityId) || 
                fat.GetUserRelationToOwner(referenceBlock.OwnerId) > MyRelationsBetweenPlayerAndBlock.FactionShare)  
                return false;

            switch (script)
            {
                case InventoryLcdSurfaceScript.ID:
                case ProjectorLcdSurfaceScript.ID:
                case CargoFilledSurfaceScript.ID:
                    return fat.HasInventory; 

                case AntennaSurfaceScript.ID:
                    return fat is IMyLaserAntenna || fat is IMyRadioAntenna || fat is IMyBeacon; 

                default:
                    throw new Exception("Unhandled filter for script type: " + script);
            }
        }
    }
}
