using System;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using LcdMod.Client.Apps.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.ButtonPad
{
    /// <summary>A searchable list where clicking an entry picks it and closes; the current choice is highlighted.</summary>
    internal abstract class ListPickerViewModel<T> : DialogViewModel where T : class
    {
        readonly List<T> _items = new List<T>();
        readonly List<PickerRow> _rows = new List<PickerRow>();
        readonly Action<T> _picked;
        string _search = string.Empty;

        protected ListPickerViewModel(Action<T> picked)
        {
            _picked = picked;
        }

        public IReadOnlyList<PickerRow> Rows => _rows;
        public bool IsEmpty => _rows.Count == 0;

        protected override void OnCompactChanged()
        {
            foreach (var row in _rows)
                row.RaiseLayout();
        }
        protected abstract string NothingText { get; }

        public string EmptyText => _search.Trim().Length == 0 ? NothingText : Loc("LcdMod_ButtonPad_NoMatches");

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

        protected abstract string NameOf(T item);
        protected abstract string IdOf(T item);
        protected abstract string IconOf(T item);
        protected abstract bool IsCurrent(T item);

        protected void SetItems(IEnumerable<T> items)
        {
            _items.Clear();
            if (items != null)
                foreach (var item in items)
                    if (item != null)
                        _items.Add(item);
            Filter();
            RaisePropertyChanged<string>(nameof(EmptyText));
        }

        protected void ClearSearch()
        {
            _search = string.Empty;
            RaisePropertyChanged<string>(nameof(SearchText));
        }

        void Filter()
        {
            _rows.Clear();
            string query = _search.Trim();
            foreach (var item in _items)
            {
                string name = NameOf(item) ?? string.Empty;
                string id = IdOf(item) ?? string.Empty;
                if (query.Length > 0 && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0 &&
                    id.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var captured = item;
                _rows.Add(new PickerRow(this, name, string.IsNullOrEmpty(IconOf(item)) ? "MissingIcon" : IconOf(item),
                    IsCurrent(item), () => Pick(captured)));
            }
            RaisePropertyChanged<IReadOnlyList<PickerRow>>(nameof(Rows));
            RaisePropertyChanged<bool>(nameof(IsEmpty));
        }

        void Pick(T item)
        {
            Close();
            if (_picked != null)
                _picked(item);
        }

        protected override void OnFontSizeChanged()
        {
            foreach (var row in _rows)
                row.RaisePropertyChanged<float>(nameof(PickerRow.FontSize));
        }
    }

    internal sealed class PickerRow : ObservableObject
    {
        readonly DialogViewModel _owner;

        public PickerRow(DialogViewModel owner, string name, string icon, bool current, Action pick)
        {
            _owner = owner;
            Name = name;
            Icon = icon;
            Classes = current ? "dialog-row selected" : "dialog-row";
            Command = new DelegateCommand(_ => pick());
        }

        public string Name { get; }
        public string Icon { get; }
        public string Classes { get; }
        public ICommand Command { get; }
        public float FontSize => _owner.FontSize;
        // Compact dialogs fill a very short screen; smaller rows keep three of them visible.
        public float RowHeight => _owner.IsCompact ? 26f : 44f;
        public float IconSize => _owner.IsCompact ? 20f : 38f;
        public VRageMath.Vector4 RowMargin => new VRageMath.Vector4(0f, 0f, 0f, _owner.IsCompact ? 2f : 3f);
        public VRageMath.Vector4 IconMargin => new VRageMath.Vector4(0f, 0f, _owner.IsCompact ? 6f : 10f, 0f);

        internal void RaiseLayout()
        {
            RaisePropertyChanged<float>(nameof(RowHeight));
            RaisePropertyChanged<float>(nameof(IconSize));
            RaisePropertyChanged<VRageMath.Vector4>(nameof(RowMargin));
            RaisePropertyChanged<VRageMath.Vector4>(nameof(IconMargin));
        }
    }

    internal sealed class TargetPickerViewModel : ListPickerViewModel<PickActionTargetResult>
    {
        public static readonly PickActionTargetKind[] Kinds =
        {
            PickActionTargetKind.Block, PickActionTargetKind.Group, PickActionTargetKind.BlockType, PickActionTargetKind.BlockSubtype
        };

        readonly Func<PickActionTargetKind, IList<PickActionTargetResult>> _load;
        readonly PickActionTargetResult _current;
        PickActionTargetKind _kind;

        public TargetPickerViewModel(Func<PickActionTargetKind, IList<PickActionTargetResult>> load,
            PickActionTargetResult current, Action<PickActionTargetResult> picked) : base(picked)
        {
            _load = load;
            _current = current;
            _kind = current == null ? PickActionTargetKind.Block : current.Kind;
            SetItems(_load(_kind));
        }

        public PickActionTargetKind Kind => _kind;
        protected override string NothingText => Loc("LcdMod_ButtonPad_TargetDialog_NoTargets");

        public void SelectKind(PickActionTargetKind kind)
        {
            if (!RaiseAndSetIfChanged(ref _kind, kind, nameof(Kind)))
                return;
            ClearSearch();
            SetItems(_load(kind));
        }

        public static string KindLabel(PickActionTargetKind kind)
        {
            switch (kind)
            {
                case PickActionTargetKind.Group: return Loc("LcdMod_ButtonPad_TargetKind_Group");
                case PickActionTargetKind.BlockType: return Loc("LcdMod_ButtonPad_TargetKind_BlockType");
                case PickActionTargetKind.BlockSubtype: return Loc("LcdMod_ButtonPad_TargetKind_BlockSubtype");
                default: return Loc("LcdMod_ButtonPad_TargetKind_Block");
            }
        }

        protected override string NameOf(PickActionTargetResult item) => item.DisplayName;
        protected override string IdOf(PickActionTargetResult item) => item.Id;
        protected override string IconOf(PickActionTargetResult item) => item.SpriteName;

        protected override bool IsCurrent(PickActionTargetResult item)
        {
            return _current != null && _current.Kind == item.Kind &&
                   string.Equals(_current.Id, item.Id, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class ActionPickerViewModel : ListPickerViewModel<ButtonPanelActionSettings>
    {
        readonly ButtonPanelActionSettings _current;

        public ActionPickerViewModel(IEnumerable<ButtonPanelActionSettings> actions, ButtonPanelActionSettings current,
            Action<ButtonPanelActionSettings> picked) : base(picked)
        {
            _current = current;
            SetItems(actions);
        }

        protected override string NothingText => Loc("LcdMod_ButtonPad_ActionDialog_NoCompatibleActions");
        protected override string NameOf(ButtonPanelActionSettings item) => item.DisplayName ?? item.BaseId;
        protected override string IdOf(ButtonPanelActionSettings item) => item.BaseId;
        protected override string IconOf(ButtonPanelActionSettings item) => item.SpriteName;

        protected override bool IsCurrent(ButtonPanelActionSettings item)
        {
            return _current != null && string.Equals(_current.BaseId, item.BaseId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
