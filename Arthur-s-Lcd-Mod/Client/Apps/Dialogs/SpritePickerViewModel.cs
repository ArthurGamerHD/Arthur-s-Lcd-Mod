using System;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.Dialogs
{
    /// <summary>Searchable sprite list. Single mode picks on click; multi mode toggles rows and applies the set.</summary>
    internal sealed class SpritePickerViewModel : DialogViewModel
    {
        readonly List<SpriteRow> _all = new List<SpriteRow>();
        readonly List<SpriteRow> _filtered = new List<SpriteRow>();
        readonly List<string> _selected = new List<string>();
        readonly Action<string> _picked;
        readonly Action<string[]> _applied;
        string _search = string.Empty;
        string _anchor;

        SpritePickerViewModel(IEnumerable<string> sprites, Action<string> picked, Action<string[]> applied,
            IEnumerable<string> selected)
        {
            _picked = picked;
            _applied = applied;
            ApplyCommand = new DelegateCommand(_ => Apply());
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string sprite in sprites)
                if (!string.IsNullOrEmpty(sprite) && seen.Add(sprite))
                    _all.Add(new SpriteRow(this, sprite));
            if (selected != null)
                foreach (string sprite in selected)
                    if (!string.IsNullOrWhiteSpace(sprite) && !Contains(_selected, sprite.Trim()))
                        _selected.Add(sprite.Trim());
            Filter();
            RefreshSelection();
        }

        public static SpritePickerViewModel Single(IEnumerable<string> sprites, Action<string> picked)
        {
            return new SpritePickerViewModel(sprites, picked, null, null);
        }

        public static SpritePickerViewModel Multiple(IEnumerable<string> sprites, IEnumerable<string> selected,
            Action<string[]> applied)
        {
            return new SpritePickerViewModel(sprites, null, applied, selected);
        }

        public Func<bool> IsToggleModifierDown { get; set; } = () => false;
        public Func<bool> IsRangeModifierDown { get; set; } = () => false;

        public bool AllowMultiple => _applied != null;
        public IReadOnlyList<SpriteRow> Sprites => _filtered;
        public ICommand ApplyCommand { get; }
        public bool IsEmpty => _filtered.Count == 0;
        public string SelectionText => Loc("LcdMod_SpritePicker_SelectedFormat", _selected.Count);

        public string EmptyText => _search.Trim().Length == 0
            ? Loc("LcdMod_SpritePicker_Empty")
            : Loc("LcdMod_SpritePicker_NoMatchesFormat", _search.Trim());

        public string SearchText
        {
            get { return _search; }
            set
            {
                if (!RaiseAndSetIfChanged(ref _search, value ?? string.Empty, nameof(SearchText)))
                    return;
                Filter();
                RaisePropertyChanged<string>(nameof(EmptyText));
            }
        }

        internal bool IsSelected(string sprite)
        {
            return AllowMultiple && Contains(_selected, sprite);
        }

        internal void Click(SpriteRow row)
        {
            if (!AllowMultiple)
            {
                Close();
                if (_picked != null)
                    _picked(row.Name);
                return;
            }

            if (IsRangeModifierDown() && _anchor != null)
                SelectRange(_anchor, row.Name);
            else if (IsToggleModifierDown())
            {
                if (!Remove(_selected, row.Name))
                    _selected.Add(row.Name);
                _anchor = row.Name;
            }
            else
            {
                _selected.Clear();
                _selected.Add(row.Name);
                _anchor = row.Name;
            }
            RefreshSelection();
        }

        void SelectRange(string from, string to)
        {
            int start = IndexOf(from);
            int end = IndexOf(to);
            if (start < 0 || end < 0)
            {
                _selected.Clear();
                _selected.Add(to);
                _anchor = to;
                return;
            }
            _selected.Clear();
            for (int i = Math.Min(start, end); i <= Math.Max(start, end); i++)
                _selected.Add(_filtered[i].Name);
        }

        void Apply()
        {
            var result = _selected.ToArray();
            Close();
            if (_applied != null)
                _applied(result);
        }

        protected override void OnFontSizeChanged()
        {
            foreach (var row in _all)
                row.RaisePropertyChanged<float>(nameof(SpriteRow.FontSize));
        }

        void Filter()
        {
            _filtered.Clear();
            string query = _search.Trim();
            foreach (var row in _all)
                if (query.Length == 0 || row.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    _filtered.Add(row);
            RaisePropertyChanged<IReadOnlyList<SpriteRow>>(nameof(Sprites));
            RaisePropertyChanged<bool>(nameof(IsEmpty));
        }

        void RefreshSelection()
        {
            foreach (var row in _all)
                row.Refresh();
            RaisePropertyChanged<string>(nameof(SelectionText));
        }

        int IndexOf(string sprite)
        {
            for (int i = 0; i < _filtered.Count; i++)
                if (string.Equals(_filtered[i].Name, sprite, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        static bool Contains(List<string> list, string value)
        {
            foreach (string item in list)
                if (string.Equals(item, value, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static bool Remove(List<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                    return true;
                }
            return false;
        }
    }

    internal sealed class SpriteRow : ObservableObject
    {
        readonly SpritePickerViewModel _owner;
        string _classes;

        public SpriteRow(SpritePickerViewModel owner, string name)
        {
            _owner = owner;
            Name = name;
            Command = new DelegateCommand(_ => _owner.Click(this));
            _classes = ClassesFor(false);
        }

        public string Name { get; }
        public ICommand Command { get; }
        public string Classes => _classes;
        public float FontSize => _owner.FontSize;

        internal void Refresh()
        {
            RaiseAndSetIfChanged(ref _classes, ClassesFor(_owner.IsSelected(Name)), nameof(Classes));
        }

        static string ClassesFor(bool selected)
        {
            return selected ? "dialog-row selected" : "dialog-row";
        }
    }
}
