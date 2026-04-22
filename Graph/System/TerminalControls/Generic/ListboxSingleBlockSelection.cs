using System.Collections.Generic;
using System.Linq;
using Graph.Helpers;
using Graph.System.Config;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace Graph.System.TerminalControls.Generic
{
    public partial class ListboxSingleBlockSelection<T> : TerminalControlsWrapper where T : class, IMyTerminalBlock
    {
        public override IMyTerminalControl TerminalControl => _terminalControl;
        IMyTerminalControl _terminalControl;
        
        readonly List<T> _reference = new List<T>();
        
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
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if(config == null)
                return;

            config.ReferenceBlock = selection.First().UserData as long? ?? 0;
            ConfigManager.Sync(block);
        }

        void Getter(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> blockList,
            List<MyTerminalControlListBoxItem> selected)
        {
            var config = ConfigManager.GetConfigForCurrentScreen(block);
            if(config == null)
                return;
            
            _reference.Clear();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(block.CubeGrid)?.GetBlocksOfType(_reference);
            if (!_reference.Any())
                return;

            blockList.AddRange(_reference.Select(a => ListBoxItemHelper.GetOrComputeListBoxItem(
                a.CustomName,
                a.CubeGrid.DisplayName,
                a.EntityId)));
            
            var selection = blockList.FirstOrDefault(a => (a.UserData as long? ?? 0) == config.ReferenceBlock);
            
            if(selection != null)
                selected.Add(selection);
        }
    }
}
