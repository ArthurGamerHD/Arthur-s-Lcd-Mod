#if DEBUG
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.VisibleTreeDebug
{
    public sealed partial class ListboxVisibleTreeDebugBlockSelection : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        readonly List<IMyTerminalBlock> _blocks = new List<IMyTerminalBlock>();

        public ListboxVisibleTreeDebugBlockSelection()
        {
            var listbox = CreateControl<IMyTerminalControlListbox>("VisibleTreeDebugBlockSelection");
            listbox.ListContent = Getter;
            listbox.ItemSelected = Setter;
            listbox.Visible = Visible;
            listbox.VisibleRowsCount = 8;
            listbox.Multiselect = false;
            listbox.Title = MyStringId.GetOrCompute("Debug block");
            TerminalControl = listbox;
        }

        void Setter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selection)
        {
            var settings = ConfigManager.GetConfigForBlock(block);
            var surface = settings == null ? null : settings.GetSurfaceConfig(GetThisSurfaceIndex(block));
            if (settings == null || !settings.CanWriteConfig(surface))
                return;
            var reference = surface?.TryGet<BlockReferenceConfigComponent>(Constants.VisibleTreeReference);
            var app = surface?.TryGet<VisibleTreeDebugConfigComponent>(Constants.APP);
            if (settings == null || reference == null || app == null)
                return;

            long selectedBlockId = ListBoxItemHelper.GetLongUserData(selection.FirstOrDefault());
            if (reference.EntityId != selectedBlockId)
            {
                reference.EntityId = selectedBlockId;
                app.ReferenceScreenIndex = GetFirstScreenIndex(selectedBlockId);
            }

            RemapHelper.PinBlock(reference.EntityId);
            ConfigManager.Sync(block, settings);
        }

        void Getter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> blockList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var reference = ConfigManager.GetComponentForCurrentSurface<BlockReferenceConfigComponent>(
                block,
                Constants.VisibleTreeReference);
            if (reference == null)
                return;

            blockList.Add(new MyTerminalControlListBoxItem(
                MyStringId.GetOrCompute("(none)"),
                MyStringId.GetOrCompute(string.Empty),
                0L));

            GetBlocks();
            for (int i = 0; i < _blocks.Count; i++)
            {
                var candidate = _blocks[i];
                blockList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                    candidate.CustomName,
                    candidate.CubeGrid.DisplayName,
                    candidate.EntityId));
            }

            AddConfiguredReferenceIfMissing(blockList, reference.EntityId);

            var selection = blockList.FirstOrDefault(a => ListBoxItemHelper.GetLongUserData(a) == reference.EntityId);
            if (selection != null)
                selected.Add(selection);
        }

        void GetBlocks()
        {
            _blocks.Clear();
            var seen = new HashSet<long>();
            foreach (var instance in SurfaceScriptBase.Instances)
            {
                var block = instance?.Block as IMyTerminalBlock;
                if (block == null || block.MarkedForClose || !seen.Add(block.EntityId))
                    continue;

                _blocks.Add(block);
            }
        }

        static int GetFirstScreenIndex(long blockId)
        {
            foreach (var instance in SurfaceScriptBase.Instances)
            {
                var block = instance?.Block as IMyTerminalBlock;
                if (block != null && block.EntityId == blockId)
                    return instance.RotationOrSurfaceIndex;
            }

            return 0;
        }

        static void AddConfiguredReferenceIfMissing(List<MyTerminalControlListBoxItem> blockList, long referenceBlockId)
        {
            if (referenceBlockId == 0L || blockList.Any(a => ListBoxItemHelper.GetLongUserData(a) == referenceBlockId))
                return;

            VRage.ModAPI.IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(referenceBlockId, out entity))
                return;

            var referenceBlock = entity as IMyTerminalBlock;
            if (referenceBlock == null || referenceBlock.MarkedForClose)
                return;

            blockList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                referenceBlock.CustomName,
                referenceBlock.CubeGrid.DisplayName,
                referenceBlock.EntityId));
        }
    }
}
#endif
