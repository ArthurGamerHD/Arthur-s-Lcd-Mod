using System;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Inventory
{
    internal sealed class InventoryViewModel : ObservableObject
    {
        IReadOnlyList<InventoryItemModel> _items = new InventoryItemModel[0];
        float _fontSize = 1f;
        Vector2 _viewport;
        bool _tiles, _cards, _table;
        public IReadOnlyList<InventoryItemModel> Items => _items;
        public bool Tiles => _tiles;
        public bool Cards => _cards;
        public bool Table => _table;
        public bool ShowHeaders => !_tiles;
        public float FontSize => _fontSize;
        public string FontFamily { get; private set; } = "White";
        public string NameHeader { get; private set; }
        public string AmountHeader { get; private set; }
        public bool SortByAmount { get; private set; }
        public bool SortByName => !SortByAmount;
        public bool SortDescending { get; private set; }
        public ICommand NameSortCommand { get; set; }
        public ICommand AmountSortCommand { get; set; }
        public float HeaderHeight => 30f * _fontSize;
        public float CellHeight => (_tiles ? 90f : 30f) * _fontSize;
        public float CellWidth { get; private set; }
        public float AmountHeaderWidth => Math.Min(_viewport.X, Math.Max(105f * _fontSize, _viewport.X * .22f));
        public Vector4 NameHeaderMargin => new Vector4(38f * _fontSize, 0f, AmountHeaderWidth, 0f);

        public void Configure(bool tiles, bool cards, bool table, float fontSize, string font,
            string nameHeader, string amountHeader, bool sortByAmount, bool descending)
        {
            fontSize = Math.Max(.1f, fontSize);
            if (_tiles == tiles && _cards == cards && _table == table && _fontSize == fontSize &&
                FontFamily == font && NameHeader == nameHeader && AmountHeader == amountHeader &&
                SortByAmount == sortByAmount && SortDescending == descending) return;
            _tiles = tiles; _cards = cards; _table = table; _fontSize = fontSize;
            FontFamily = font; NameHeader = nameHeader; AmountHeader = amountHeader;
            SortByAmount = sortByAmount; SortDescending = descending;
            UpdateItemPresentation();
            UpdateCellWidth();
            RaisePropertyChanged<object>(string.Empty);
        }

        public void SetViewport(Vector2 size)
        {
            if (_viewport == size) return;
            _viewport = size;
            UpdateCellWidth();
            RaisePropertyChanged<object>(string.Empty);
        }

        public void SetItems(IReadOnlyList<InventoryItemModel> items)
        {
            bool changed = items.Count != _items.Count;
            if (!changed)
                for (int i = 0; i < items.Count; i++)
                    if (!ReferenceEquals(items[i], _items[i])) { changed = true; break; }
            if (!changed) return;
            // Space Engineers disallows INotifyCollectionChanged. Publish a new
            // source only for membership/order changes; item instances remain stable.
            var snapshot = new InventoryItemModel[items.Count];
            for (int i = 0; i < items.Count; i++) snapshot[i] = items[i];
            _items = snapshot;
            UpdateItemPresentation();
            UpdateCellWidth();
            RaisePropertyChanged<object>(string.Empty);
        }

        void UpdateItemPresentation()
        {
            for (int i = 0; i < _items.Count; i++)
                _items[i].SetPresentation(_tiles, _cards, _table, (i & 1) != 0);
        }

        void UpdateCellWidth()
        {
            float width = Math.Max(0f, _viewport.X);
            int columns = _tiles ? Math.Max(1, (int)(width / (220f * _fontSize))) : 1;
            columns = Math.Min(columns, Math.Max(1, _items.Count));
            float height = Math.Max(0f, _viewport.Y - (ShowHeaders ? HeaderHeight : 0f));
            if (((_items.Count + columns - 1) / columns) * CellHeight > height)
            {
                width = Math.Max(0f, width - 5f);
                columns = _tiles ? Math.Max(1, (int)(width / (220f * _fontSize))) : 1;
                columns = Math.Min(columns, Math.Max(1, _items.Count));
            }
            CellWidth = Math.Max(0f, width / columns - .001f);
        }
    }

    internal sealed class InventoryItemModel : ObservableObject
    {
        string _icon, _name, _amount;
        int _status;
        bool _tiles, _cards, _table, _odd;
        public bool Tiles => _tiles;
        public bool Cards => _cards;
        public bool Table => _table;
        public bool Odd => _odd;
        public string Icon => _icon;
        public string Name => _name;
        public string Amount => _amount;
        public int Status => _status;
        public ICommand Command { get; private set; }

        public InventoryItemModel(Action clicked)
        {
            Command = new DelegateCommand(_ => { if (clicked != null) clicked(); });
        }

        public void Update(string icon, string name, string amount, int status)
        {
            RaiseAndSetIfChanged(ref _icon, string.IsNullOrWhiteSpace(icon) ? "MissingIcon" : icon, nameof(Icon));
            RaiseAndSetIfChanged(ref _name, name ?? string.Empty, nameof(Name));
            RaiseAndSetIfChanged(ref _amount, amount ?? string.Empty, nameof(Amount));
            RaiseAndSetIfChanged(ref _status, status, nameof(Status));
        }

        public void SetPresentation(bool tiles, bool cards, bool table, bool odd)
        {
            RaiseAndSetIfChanged(ref _tiles, tiles, nameof(Tiles));
            RaiseAndSetIfChanged(ref _cards, cards, nameof(Cards));
            RaiseAndSetIfChanged(ref _table, table, nameof(Table));
            RaiseAndSetIfChanged(ref _odd, odd, nameof(Odd));
        }
    }
}
