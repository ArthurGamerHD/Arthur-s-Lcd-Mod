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
        long _selectionBlockId;
        int _selectionSurfaceIndex = int.MinValue;
        string _selectionScript;
        public List<MyTerminalControlListBoxItem> Selection { get; private set; }

        /// <summary>Whether several items can be selected; the ModAPI control does not expose it.</summary>
        public bool Multiselect { get; private set; }

        /// <summary>The list's items and selection, as the terminal lists them.</summary>
        public void ReadItems(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> items,
            List<MyTerminalControlListBoxItem> selected)
        {
            Getter(block, items, selected);
        }

        /// <summary>Selects items as a click in the terminal list would.</summary>
        public void Select(IMyTerminalBlock block, List<MyTerminalControlListBoxItem> selection)
        {
            Setter(block, selection);
        }

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
            Multiselect = multiselect;
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
                MyTerminalControlListBoxItem item;
                if (TryFindSelectionItem(itemList, Selection[index], out item))
                {
                    selected.Add(item);
                    Selection[index] = item;
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
            var blockId = block?.EntityId ?? 0L;
            var surfaceIndex = block != null ? GetThisSurfaceIndex(block) : -1;
            string script = GetSelectedSurfaceScript(block);

            if (_selectionBlockId == blockId &&
                _selectionSurfaceIndex == surfaceIndex &&
                (string.IsNullOrEmpty(script) ||
                 string.IsNullOrEmpty(_selectionScript) ||
                 _selectionScript == script))
            {
                if (!string.IsNullOrEmpty(script))
                    _selectionScript = script;

                return;
            }

            _selectionBlockId = blockId;
            _selectionSurfaceIndex = surfaceIndex;
            _selectionScript = script;
            Selection = null;
        }

        string GetSelectedSurfaceScript(IMyTerminalBlock block)
        {
            return Config.ConfigManager.GetTerminalScript(block, GetThisSurfaceIndex(block));
        }

        static bool TryFindSelectionItem(
            List<MyTerminalControlListBoxItem> itemList,
            MyTerminalControlListBoxItem selection,
            out MyTerminalControlListBoxItem item)
        {
            item = null;
            if (itemList == null || selection == null)
                return false;

            for (var i = 0; i < itemList.Count; i++)
            {
                var candidate = itemList[i];
                if (candidate == null)
                    continue;

                if (ReferenceEquals(candidate, selection) ||
                    Equals(candidate.UserData, selection.UserData))
                {
                    item = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
