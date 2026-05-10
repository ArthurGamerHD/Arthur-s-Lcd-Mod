using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Interfaces;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public class ListboxSingleBlockSelection<T> : TerminalControlsWrapper where T : class, IMyTerminalBlock
    {
        public override IMyTerminalControl TerminalControl => _terminalControl;
        IMyTerminalControl _terminalControl;
        
        readonly List<T> _reference = new List<T>();
        readonly List<IMySlimBlock> _blocks = new List<IMySlimBlock>();
        readonly List<IMyCubeGrid> _grids = new List<IMyCubeGrid>();
        
        protected void CreateListbox(string id, string title)
        {
            var listbox = CreateControl<IMyTerminalControlListbox>(id);
            listbox.ListContent = Getter;
            listbox.ItemSelected = Setter;
            listbox.Visible = Visible;
            listbox.VisibleRowsCount = 8;
            listbox.Multiselect = false;
            listbox.Title = MyStringId.GetOrCompute(title);
            _terminalControl = listbox;
        }

        void Setter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selection)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as IConfigWithReferenceBlock;
            if(config == null)
                return;

            config.ReferenceBlock = ListBoxItemHelper.GetLongUserData(selection.FirstOrDefault());
            RemapHelper.PinBlock(config.ReferenceBlock);
            ConfigManager.Sync(block);
        }

        void Getter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> blockList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as IConfigWithReferenceBlock;
            if(config == null)
                return;
            
            GetReferenceBlocks(block.CubeGrid);

            blockList.Add(new MyTerminalControlListBoxItem(
                MyStringId.GetOrCompute("(none)"),
                MyStringId.GetOrCompute(string.Empty),
                0L));

            blockList.AddRange(_reference.Select(a => ListBoxItemHelper.GetOrComputeListBoxItem(
                a.CustomName,
                a.CubeGrid.DisplayName,
                a.EntityId)));

            AddConfiguredReferenceIfMissing(blockList, config.ReferenceBlock);
            
            var selection = blockList.FirstOrDefault(a => ListBoxItemHelper.GetLongUserData(a) == config.ReferenceBlock);
            
            if(selection != null)
                selected.Add(selection);
        }

        void GetReferenceBlocks(IMyCubeGrid rootGrid)
        {
            _reference.Clear();
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
                    var referenceBlock = _blocks[j].FatBlock as T;
                    if (referenceBlock != null)
                        _reference.Add(referenceBlock);
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

            var referenceBlock = entity as T;
            if (referenceBlock == null || referenceBlock.MarkedForClose)
                return;

            blockList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                referenceBlock.CustomName,
                referenceBlock.CubeGrid.DisplayName,
                referenceBlock.EntityId));
        }
    }
}
