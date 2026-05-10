using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    /// <summary>
    /// Single-select listbox of cockpits / control seats / RCs / fighter cockpits
    /// on the Lcd's grid. Used by the ore-scanner script as its "what is up/down"
    /// orientation reference. Stores the chosen block's EntityId in
    /// <see cref="ScreenConfigGeneral.OreScannerReferenceId"/>; 0 = "no reference".
    /// </summary>
    public sealed class ListboxOreScannerReference : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        readonly List<IMyShipController> _scratch = new List<IMyShipController>();
        readonly List<IMySlimBlock> _blocks = new List<IMySlimBlock>();
        readonly List<IMyCubeGrid> _grids = new List<IMyCubeGrid>();

        public ListboxOreScannerReference()
        {
            var listbox = CreateControl<IMyTerminalControlListbox>("OreScannerReferenceListbox");
            listbox.ListContent = Getter;
            listbox.ItemSelected = Setter;
            listbox.Visible = Visible;
            listbox.VisibleRowsCount = 6;
            listbox.Multiselect = false;
            listbox.Title = MyStringId.GetOrCompute("LcdMod_OreScanner_Reference");
            TerminalControl = listbox;
        }

        void Setter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selection)
        {
            var cfg = ConfigManager.GetConfigForCurrentScreen(block);
            if (cfg == null) return;
            cfg.OreScannerReferenceId = ListBoxItemHelper.GetLongUserData(selection.FirstOrDefault());
            RemapHelper.PinBlock(cfg.OreScannerReferenceId);
            ConfigManager.Sync(block);
        }

        void Getter(IMyTerminalBlock block,
            List<MyTerminalControlListBoxItem> blockList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var cfg = ConfigManager.GetConfigForCurrentScreen(block);
            if (cfg == null) return;

            GetReferenceBlocks(block.CubeGrid);

            // Always offer an explicit "(none)" entry so the user can clear it.
            blockList.Add(new MyTerminalControlListBoxItem(
                MyStringId.GetOrCompute("LcdMod_OreScanner_Reference_None"),
                MyStringId.GetOrCompute(string.Empty),
                0L));

            for (int i = 0; i < _scratch.Count; i++)
            {
                var sc = _scratch[i];
                blockList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                    sc.CustomName,
                    sc.CubeGrid.DisplayName,
                    sc.EntityId));
            }

            AddConfiguredReferenceIfMissing(blockList, cfg.OreScannerReferenceId);

            var match = blockList.FirstOrDefault(a =>
                ListBoxItemHelper.GetLongUserData(a) == cfg.OreScannerReferenceId);
            if (match != null)
                selected.Add(match);
        }

        void GetReferenceBlocks(IMyCubeGrid rootGrid)
        {
            _scratch.Clear();
            _grids.Clear();

            if (rootGrid == null)
                return;

            MyAPIGateway.GridGroups.GetGroup(rootGrid, GridLinkTypeEnum.Logical, _grids);
            if (_grids.Count == 0 || !_grids.Contains(rootGrid))
                _grids.Add(rootGrid);

            for (int i = 0; i < _grids.Count; i++)
            {
                var grid = _grids[i];
                if (grid == null)
                    continue;

                _blocks.Clear();
                grid.GetBlocks(_blocks);

                for (int j = 0; j < _blocks.Count; j++)
                {
                    var referenceBlock = _blocks[j].FatBlock as IMyShipController;
                    if (referenceBlock != null)
                        _scratch.Add(referenceBlock);
                }
            }
        }

        static void AddConfiguredReferenceIfMissing(List<MyTerminalControlListBoxItem> blockList, long referenceBlockId)
        {
            if (referenceBlockId == 0L || blockList.Any(a => ListBoxItemHelper.GetLongUserData(a) == referenceBlockId))
                return;

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(referenceBlockId, out entity))
                return;

            var referenceBlock = entity as IMyShipController;
            if (referenceBlock == null || referenceBlock.MarkedForClose)
                return;

            blockList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                referenceBlock.CustomName,
                referenceBlock.CubeGrid.DisplayName,
                referenceBlock.EntityId));
        }
    }
}
