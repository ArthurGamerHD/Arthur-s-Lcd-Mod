using System;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using LcdMod.Client.Apps.Dialogs;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.CargoFilled
{
    internal enum ContainerTransfer
    {
        Send,
        Receive,
        Balance
    }

    internal sealed class ContainerChoice
    {
        public string Id;
        public string Label;
        public bool IsCategory;
    }

    internal sealed class ContainerActionRequest
    {
        public ContainerTransfer Transfer;
        public List<string> Targets;
        public List<string> Items;
        public List<string> Categories;
    }

    /// <summary>Three steps: choose the transfer, the target containers, then the items or categories to move.</summary>
    internal sealed class ContainerActionViewModel : DialogViewModel
    {
        readonly string _sourceName;
        readonly List<ContainerChoiceRow> _targets = new List<ContainerChoiceRow>();
        readonly List<ContainerChoiceRow> _items = new List<ContainerChoiceRow>();
        readonly Func<IList<ContainerChoice>> _loadItems;
        readonly Action<ContainerActionRequest> _apply;
        readonly Action<List<string>, List<string>> _saveFilter;
        readonly HashSet<string> _savedItems;
        readonly HashSet<string> _savedCategories;
        int _step;
        ContainerTransfer _transfer;
        bool _itemsLoaded;

        public ContainerActionViewModel(string sourceName, IEnumerable<ContainerChoice> targets,
            Func<IList<ContainerChoice>> loadItems, IEnumerable<string> savedItems, IEnumerable<string> savedCategories,
            Action<ContainerActionRequest> apply, Action<List<string>, List<string>> saveFilter)
        {
            _sourceName = sourceName ?? string.Empty;
            _loadItems = loadItems;
            _apply = apply;
            _saveFilter = saveFilter;
            _savedItems = new HashSet<string>(savedItems ?? new string[0]);
            _savedCategories = new HashSet<string>(savedCategories ?? new string[0]);
            foreach (var target in targets)
                _targets.Add(new ContainerChoiceRow(this, target, false));
            SendCommand = new DelegateCommand(_ => Choose(ContainerTransfer.Send));
            ReceiveCommand = new DelegateCommand(_ => Choose(ContainerTransfer.Receive));
            BalanceCommand = new DelegateCommand(_ => Choose(ContainerTransfer.Balance));
            BackCommand = new DelegateCommand(_ => GoTo(_step - 1));
            ToggleAllCommand = new DelegateCommand(_ => ToggleAll());
            NextCommand = new DelegateCommand(_ => GoToItems());
            ApplyCommand = new DelegateCommand(_ => Apply());
            Title = _sourceName;
        }

        public ICommand SendCommand { get; }
        public ICommand ReceiveCommand { get; }
        public ICommand BalanceCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand ToggleAllCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand ApplyCommand { get; }
        public string ApplyText => TransferText(_transfer);
        public int Step => _step;
        public bool IsChoosingTransfer => _step == 0;
        public bool IsChoosingRows => _step > 0;
        public bool IsChoosingTargets => _step == 1;
        public bool IsChoosingItems => _step == 2;
        public IReadOnlyList<ContainerChoiceRow> Rows => _step == 2 ? _items : _targets;
        public bool CanContinue => Count(_targets) > 0;
        public bool CanApply => Count(_items) > 0;

        void Choose(ContainerTransfer transfer)
        {
            _transfer = transfer;
            RaisePropertyChanged<string>(nameof(ApplyText));
            GoTo(1);
        }

        void GoToItems()
        {
            if (!CanContinue)
                return;
            if (!_itemsLoaded)
            {
                _itemsLoaded = true;
                foreach (var item in _loadItems())
                {
                    var row = new ContainerChoiceRow(this, item, true);
                    row.IsSelected = item.IsCategory ? _savedCategories.Contains(item.Id) : _savedItems.Contains(item.Id);
                    _items.Add(row);
                }
            }
            GoTo(2);
        }

        void GoTo(int step)
        {
            if (step < 0)
            {
                Close();
                return;
            }
            _step = step;
            Title = step == 1 ? _sourceName + " - " + Loc("LcdMod_Transfer_SelectTargets")
                : step == 2 ? _sourceName + " - " + Loc("LcdMod_Transfer_SelectItems")
                : _sourceName;
            RaisePropertyChanged<int>(nameof(Step));
            RaisePropertyChanged<bool>(nameof(IsChoosingTransfer));
            RaisePropertyChanged<bool>(nameof(IsChoosingRows));
            RaisePropertyChanged<bool>(nameof(IsChoosingTargets));
            RaisePropertyChanged<bool>(nameof(IsChoosingItems));
            RaisePropertyChanged<IReadOnlyList<ContainerChoiceRow>>(nameof(Rows));
            RefreshAvailability();
        }

        void ToggleAll()
        {
            var rows = _step == 2 ? _items : _targets;
            bool select = false;
            foreach (var row in rows)
                if (!row.IsCategory && !row.IsSelected)
                    select = true;
            foreach (var row in rows)
                if (!row.IsCategory)
                    row.IsSelected = select;
            RefreshAvailability();
        }

        internal void RefreshAvailability()
        {
            RaisePropertyChanged<bool>(nameof(CanContinue));
            RaisePropertyChanged<bool>(nameof(CanApply));
        }

        void Apply()
        {
            if (!CanApply)
                return;
            var request = new ContainerActionRequest
            {
                Transfer = _transfer,
                Targets = Selected(_targets, false),
                Items = Selected(_items, false),
                Categories = Selected(_items, true)
            };
            Close();
            if (_apply != null)
                _apply(request);
        }

        protected internal override void OnClosed()
        {
            if (_saveFilter == null)
                return;
            if (_itemsLoaded)
                _saveFilter(Selected(_items, false), Selected(_items, true));
            else
                _saveFilter(new List<string>(_savedItems), new List<string>(_savedCategories));
        }

        protected override void OnFontSizeChanged()
        {
            foreach (var row in _targets) row.RaisePropertyChanged<float>(nameof(ContainerChoiceRow.FontSize));
            foreach (var row in _items) row.RaisePropertyChanged<float>(nameof(ContainerChoiceRow.FontSize));
        }

        internal static string TransferText(ContainerTransfer transfer)
        {
            return Loc(transfer == ContainerTransfer.Send ? "LcdMod_Transfer_Send"
                : transfer == ContainerTransfer.Receive ? "LcdMod_Transfer_Receive"
                : "LcdMod_Transfer_Balance");
        }

        static List<string> Selected(List<ContainerChoiceRow> rows, bool categories)
        {
            var ids = new List<string>();
            foreach (var row in rows)
                if (row.IsSelected && row.IsCategory == categories)
                    ids.Add(row.Id);
            return ids;
        }

        static int Count(List<ContainerChoiceRow> rows)
        {
            int count = 0;
            foreach (var row in rows)
                if (row.IsSelected)
                    count++;
            return count;
        }
    }

    internal sealed class ContainerChoiceRow : ObservableObject
    {
        readonly ContainerActionViewModel _owner;
        bool _selected;

        public ContainerChoiceRow(ContainerActionViewModel owner, ContainerChoice choice, bool isItem)
        {
            _owner = owner;
            Id = choice.Id;
            Label = choice.Label;
            IsCategory = isItem && choice.IsCategory;
            Command = new DelegateCommand(_ =>
            {
                IsSelected = !IsSelected;
                _owner.RefreshAvailability();
            });
        }

        public string Id { get; }
        public string Label { get; }
        public bool IsCategory { get; }
        public ICommand Command { get; }
        public float FontSize => _owner.FontSize;
        public string Classes => (IsCategory ? "dialog-row category" : "dialog-row") + (_selected ? " selected" : string.Empty);

        public bool IsSelected
        {
            get { return _selected; }
            set
            {
                if (RaiseAndSetIfChanged(ref _selected, value, nameof(IsSelected)))
                    RaisePropertyChanged<string>(nameof(Classes));
            }
        }
    }
}
