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
        string _selectionScript;
        public List<MyTerminalControlListBoxItem> Selection { get; private set; }

        protected void CreateListbox(string id, string title)
        {
            CreateListbox(id, title, true);
        }

        protected void CreateListbox(string id, string title, bool multiselect)
        {
            var listbox = CreateControl<IMyTerminalControlListbox>(id);
            listbox.ListContent = Getter;
            listbox.ItemSelected = Setter;
            listbox.Visible = Visible;
            listbox.VisibleRowsCount = 8;
            listbox.Multiselect = multiselect;
            listbox.Title = MyStringId.GetOrCompute(title);
            _terminalControl = listbox;
        }

        protected virtual void Getter(IMyTerminalBlock b, List<MyTerminalControlListBoxItem> itemList,
            List<MyTerminalControlListBoxItem> selected)
        {
            UpdateSelectionContext(b);

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

        protected virtual void Setter(IMyTerminalBlock b, List<MyTerminalControlListBoxItem> selection)
        {
            UpdateSelectionContext(b);
            Selection = selection == null || selection.Count == 0
                ? null
                : new List<MyTerminalControlListBoxItem>(selection);
        }

        void UpdateSelectionContext(IMyTerminalBlock block)
        {
            string script = GetSelectedSurfaceScript(block);
            if (_selectionScript == script)
                return;

            _selectionScript = script;
            Selection = null;
        }

        string GetSelectedSurfaceScript(IMyTerminalBlock block)
        {
            var provider = block as IMyTextSurfaceProvider;
            if (provider == null || provider.SurfaceCount <= 0)
                return null;

            int index = GetThisSurfaceIndex(block);
            if (index < 0 || index >= provider.SurfaceCount)
                return null;

            return provider.GetSurface(index)?.Script;
        }
    }
}
