using System.Collections.Generic;
using System.Linq;
using Graph.Helpers;
using Graph.System.Config;
using Graph.System.Config.Models;
using Graph.System.Config.Models.Apps;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace Graph.System.TerminalControls.Generic
{
    /// <summary>
    /// Single-select listbox of cockpits / control seats / RCs / fighter cockpits
    /// on the LCD's grid. Used by the ore-scanner script as its "what is up/down"
    /// orientation reference. Stores the chosen block's EntityId in
    /// <see cref="ScreenConfigGeneral.OreScannerReferenceId"/>; 0 = "no reference".
    /// </summary>
    public sealed partial class ListboxOreScannerReference : TerminalControlsWrapper
    {
        public override IMyTerminalControl TerminalControl { get; }

        readonly List<IMyShipController> _scratch = new List<IMyShipController>();

        public ListboxOreScannerReference()
        {
            var listbox = CreateControl<IMyTerminalControlListbox>("OreScannerReferenceListbox");
            listbox.ListContent = Getter;
            listbox.ItemSelected = Setter;
            listbox.Visible = Visible;
            listbox.VisibleRowsCount = 6;
            listbox.Multiselect = false;
            listbox.Title = MyStringId.GetOrCompute("LCDMod_OreScanner_Reference");
            TerminalControl = listbox;
        }

        void Setter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selection)
        {
            var cfg = ConfigManager.GetConfigForCurrentScreen(block);
            if (cfg == null) return;
            cfg.OreScannerReferenceId = selection.FirstOrDefault()?.UserData as long? ?? 0;
            ConfigManager.Sync(block);
        }

        void Getter(IMyTerminalBlock block,
            List<MyTerminalControlListBoxItem> blockList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var cfg = ConfigManager.GetConfigForCurrentScreen(block);
            if (cfg == null) return;

            _scratch.Clear();
            MyAPIGateway.TerminalActionsHelper
                .GetTerminalSystemForGrid(block.CubeGrid)
                ?.GetBlocksOfType(_scratch);

            // Always offer an explicit "(none)" entry so the user can clear it.
            blockList.Add(new MyTerminalControlListBoxItem(
                MyStringId.GetOrCompute("LCDMod_OreScanner_Reference_None"),
                MyStringId.GetOrCompute(string.Empty),
                (long)0L));

            for (int i = 0; i < _scratch.Count; i++)
            {
                var sc = _scratch[i];
                blockList.Add(ListBoxItemHelper.GetOrComputeListBoxItem(
                    sc.CustomName,
                    sc.CubeGrid.DisplayName,
                    sc.EntityId));
            }

            var match = blockList.FirstOrDefault(a =>
                (a.UserData as long? ?? 0) == cfg.OreScannerReferenceId);
            if (match != null)
                selected.Add(match);
        }
    }
}
