using System.Collections.Generic;
using System.Linq;
using Graph.Helpers;
using Graph.System.Config;
using Graph.System.Config.Interfaces;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace Graph.System.TerminalControls.Generic
{
    public sealed partial class ListboxReferenceBlockSelection : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        readonly List<IMyTerminalBlock> _scratch = new List<IMyTerminalBlock>();

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
            var config = ConfigManager.GetConfigForCurrentScreen(block) as IConfigWithReferenceBlock;
            if (config == null)
                return;

            config.ReferenceBlock = selection.FirstOrDefault()?.UserData as long? ?? 0L;
            ConfigManager.Sync(block);
        }

        void Getter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> blockList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block) as IConfigWithReferenceBlock;
            if (config == null)
                return;

            var provider = GetReferenceBlockProvider(block);
            if (provider == null)
                return;

            blockList.Add(new MyTerminalControlListBoxItem(
                MyStringId.GetOrCompute("(none)"),
                MyStringId.GetOrCompute(string.Empty),
                0L));

            _scratch.Clear();
            MyAPIGateway.TerminalActionsHelper
                .GetTerminalSystemForGrid(block.CubeGrid)
                ?.GetBlocksOfType(_scratch, provider.IsReferenceBlockCandidate);

            for (int i = 0; i < _scratch.Count; i++)
            {
                var referenceBlock = _scratch[i];
                blockList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                    referenceBlock.CustomName,
                    referenceBlock.CubeGrid.DisplayName,
                    referenceBlock.EntityId));
            }

            var selection = blockList.FirstOrDefault(a => (a.UserData as long? ?? 0L) == config.ReferenceBlock);
            if (selection != null)
                selected.Add(selection);
        }

        IReferenceBlockSelection GetReferenceBlockProvider(IMyTerminalBlock block)
        {
            if (block == null)
                return null;

            var surface = GetThisSurface(block);
            return ConfigManager.GetAppsForBlock(block)
                .FirstOrDefault(app => app.Surface.Equals(surface)) as IReferenceBlockSelection;
        }
    }
}
