using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Lists
{
    public sealed class ListBoxModel<T> : ControlModelBase
    {
        public ListBoxModel()
        {
            Items = new List<T>();
            SelectedEntries = new List<T>();
            RowHeight = 32f;
            ScrollerWidthPixels = 6f;
            MultiSelect = true;
        }

        public IList<T> Items { get; set; }
        public IList<T> SelectedEntries { get; set; }
        public bool MultiSelect { get; set; }
        public float RowHeight { get; set; }
        public float ScrollerWidthPixels { get; set; }
        public Func<T, string> TextSelector { get; set; }
        public Action<T> EntryClicked { get; set; }
        public Color? SelectedPanelColor { get; set; }
        public Color? SelectedTextColor { get; set; }

        public int Count => Items == null ? 0 : Items.Count;

        public T GetItem(int index)
        {
            if (Items == null || index < 0 || index >= Items.Count)
                return default(T);

            return Items[index];
        }

        public string GetText(T item)
        {
            if (TextSelector != null)
                return TextSelector(item) ?? string.Empty;

            return item == null ? string.Empty : item.ToString();
        }

        public bool IsSelected(T item)
        {
            return SelectedEntries != null && SelectedEntries.Contains(item);
        }

        public void SelectClicked(T item, int index)
        {
            EnsureSelectedEntries();

            var input = MyAPIGateway.Input;
            bool ctrl = input != null && input.IsAnyCtrlKeyPressed();
            bool shift = input != null && input.IsAnyShiftKeyPressed();

            if (!MultiSelect)
            {
                ReplaceSelection(item);
            }
            else if (ctrl)
            {
                ToggleSelection(item);
            }
            else if (shift)
            {
                AddRangeFromFirstSelected(index);
            }
            else
            {
                ReplaceSelection(item);
            }

            if (EntryClicked != null)
                EntryClicked(item);
        }

        void EnsureSelectedEntries()
        {
            if (SelectedEntries == null)
                SelectedEntries = new List<T>();
        }

        void ReplaceSelection(T item)
        {
            SelectedEntries.Clear();
            AddSelected(item);
        }

        void ToggleSelection(T item)
        {
            if (SelectedEntries.Contains(item))
                SelectedEntries.Remove(item);
            else
                SelectedEntries.Add(item);
        }

        void AddRangeFromFirstSelected(int clickedIndex)
        {
            if (Items == null || clickedIndex < 0 || clickedIndex >= Items.Count)
                return;

            int firstSelectedIndex = GetFirstSelectedIndex();
            if (firstSelectedIndex < 0)
            {
                AddSelected(Items[clickedIndex]);
                return;
            }

            int start = Math.Min(firstSelectedIndex, clickedIndex);
            int end = Math.Max(firstSelectedIndex, clickedIndex);

            for (int i = start; i <= end; i++)
                AddSelected(Items[i]);
        }

        int GetFirstSelectedIndex()
        {
            if (SelectedEntries == null || Items == null)
                return -1;

            for (int i = 0; i < SelectedEntries.Count; i++)
            {
                int index = Items.IndexOf(SelectedEntries[i]);
                if (index >= 0)
                    return index;
            }

            return -1;
        }

        void AddSelected(T item)
        {
            if (!SelectedEntries.Contains(item))
                SelectedEntries.Add(item);
        }
    }
}
