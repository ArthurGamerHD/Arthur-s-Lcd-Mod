using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Generic
{
    public sealed partial class ListboxReferenceBlockSelection : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        readonly List<IMyTerminalBlock> _scratch = new List<IMyTerminalBlock>();
        readonly List<IMySlimBlock> _blocks = new List<IMySlimBlock>();
        readonly List<IMyCubeGrid> _grids = new List<IMyCubeGrid>();

        public ListboxReferenceBlockSelection()
        {
            var listbox = CreateControl<IMyTerminalControlListbox>("ReferenceBlockSelection");
            listbox.ListContent = Getter;
            listbox.ItemSelected = Setter;
            listbox.Visible = Visible;
            listbox.VisibleRowsCount = 8;
            listbox.Multiselect = false;
            listbox.Title = MyStringId.GetOrCompute("Reference block");
            TerminalControl = listbox;
        }

        void Setter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selection)
        {
            var provider = GetReferenceBlockProvider(block);
            var slot = GetReferenceSlot(provider);
            if (slot == null)
                return;

            var selectedBlockId = ListBoxItemHelper.GetLongUserData(selection.FirstOrDefault());
            if (!ConfigManager.ModifyComponentForCurrentSurface<BlockReferenceConfigComponent>(
                    block,
                    slot,
                    config => config.EntityId = selectedBlockId))
                return;

            RemapHelper.PinBlock(selectedBlockId);
        }

        void Getter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> blockList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var provider = GetReferenceBlockProvider(block);
            if (provider == null)
                return;

            var slot = GetReferenceSlot(provider);
            var reference = slot == null
                ? null
                : ConfigManager.GetComponentForCurrentSurface<BlockReferenceConfigComponent>(block, slot);
            if (reference == null)
                return;

            blockList.Add(new MyTerminalControlListBoxItem(
                MyStringId.GetOrCompute("(none)"),
                MyStringId.GetOrCompute(string.Empty),
                0L));

            GetReferenceBlocks(block.CubeGrid, provider);

            for (int i = 0; i < _scratch.Count; i++)
            {
                var referenceBlock = _scratch[i];
                blockList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                    referenceBlock.CustomName,
                    referenceBlock.CubeGrid.DisplayName,
                    referenceBlock.EntityId));
            }

            AddConfiguredReferenceIfMissing(blockList, provider, reference.EntityId);

            var selection = blockList.FirstOrDefault(a => ListBoxItemHelper.GetLongUserData(a) == reference.EntityId);
            if (selection != null)
                selected.Add(selection);
        }

        void GetReferenceBlocks(IMyCubeGrid rootGrid, IReferenceBlockSelection provider)
        {
            _scratch.Clear();
            _grids.Clear();

            if (provider.TryGetReferenceBlockCandidates(_scratch))
                return;

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
                    var referenceBlock = _blocks[j].FatBlock as IMyTerminalBlock;
                    if (referenceBlock != null && provider.IsReferenceBlockCandidate(referenceBlock))
                        _scratch.Add(referenceBlock);
                }
            }
        }

        static void AddConfiguredReferenceIfMissing(
            List<MyTerminalControlListBoxItem> blockList,
            IReferenceBlockSelection provider,
            long referenceBlockId)
        {
            if (referenceBlockId == 0L || blockList.Any(a => ListBoxItemHelper.GetLongUserData(a) == referenceBlockId))
                return;

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(referenceBlockId, out entity))
                return;

            var referenceBlock = entity as IMyTerminalBlock;
            if (referenceBlock == null || referenceBlock.MarkedForClose || !provider.IsReferenceBlockCandidate(referenceBlock))
                return;

            blockList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                referenceBlock.CustomName,
                referenceBlock.CubeGrid.DisplayName,
                referenceBlock.EntityId));
        }

        IReferenceBlockSelection GetReferenceBlockProvider(IMyTerminalBlock block)
        {
            if (block == null)
                return null;

            var surfaceIndex = GetThisSurfaceIndex(block);
            return ConfigManager.GetAppsForBlock(block)
                .FirstOrDefault(app => app.RotationOrSurfaceIndex == surfaceIndex) as IReferenceBlockSelection;
        }

        static string GetReferenceSlot(IReferenceBlockSelection provider)
        {
            if (provider is DockingAlignment)
                return Constants.DOCKABLE_REFERENCE;

            if (provider is RenderProxySurfaceScript)
                return Constants.RENDER_PROXY_REFERENCE;

            return null;
        }
    }
}
