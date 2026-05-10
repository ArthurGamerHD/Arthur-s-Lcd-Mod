using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Terminal.Controls.Filter.Listbox
{
    public abstract class TerminalControlsListbox : TerminalControlFilter
    {
        public override IMyTerminalControl TerminalControl => _terminalControl;
        IMyTerminalControl _terminalControl;
        public List<MyTerminalControlListBoxItem> Selection { get; private set; }

        protected void CreateListbox(string id, string title)
        {
            var listbox = CreateControl<IMyTerminalControlListbox>(id);
            listbox.ListContent = Getter;
            listbox.ItemSelected = Setter;
            listbox.Visible = Visible;
            listbox.VisibleRowsCount = 8;
            listbox.Multiselect = true;
            listbox.Title = MyStringId.GetOrCompute(title);
            _terminalControl = listbox;
        }

        protected virtual void Getter(IMyTerminalBlock b, List<MyTerminalControlListBoxItem> itemList,
            List<MyTerminalControlListBoxItem> selected)
        {
            if (Selection == null || !Selection.Any())
                return;

            for (var index = 0; index < Selection.Count;)
            {
                if (itemList.Contains(Selection[index]))
                {
                    selected.Add(Selection[index]);
                    index++;
                }
                else
                {
                    Selection.RemoveAtFast(index);
                }
            }

        }

        void Setter(IMyTerminalBlock b, List<MyTerminalControlListBoxItem> selection) => Selection =  selection;
    }
}
