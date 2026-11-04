using System;
using System.Collections.Generic;
using Acrux.Core.Input;
using Adk.Mvvm;
using LcdMod.Common.Config.Components;

namespace LcdMod.Client.Apps.Inventory
{
    /// <summary>
    /// How the inventory shows its items (the block's display mode): the view's style class (card, grid, list or
    /// table) selects the item template and the cell layout. Byte-sized, so the view's styled property crosses the bridge.
    /// </summary>
    public enum InventoryLayout : byte
    {
        List = 0,
        Card = 1,
        Grid = 2,
        Table = 3,
    }

    internal static class InventoryLayouts
    {
        public static InventoryLayout From(ItemDisplayMode mode)
        {
            switch (mode)
            {
                case ItemDisplayMode.Card: return InventoryLayout.Card;
                case ItemDisplayMode.Grid: return InventoryLayout.Grid;
                case ItemDisplayMode.Table: return InventoryLayout.Table;
                default: return InventoryLayout.List;
            }
        }

        // Card and grid show items as tiles, without column headers.
        public static bool IsTiles(InventoryLayout layout) => layout == InventoryLayout.Card || layout == InventoryLayout.Grid;
    }

    internal sealed class InventoryViewModel : ObservableObject
    {
        IReadOnlyList<InventoryItemModel> _items = new InventoryItemModel[0];
        InventoryLayout _layout;
        [PreviewWith(8)]
        public IReadOnlyList<InventoryItemModel> Items => _items;
        [PreviewWith("Table")]
        public InventoryLayout Layout => _layout;
        [PreviewWith("Name")]
        public string NameHeader { get; private set; }
        [PreviewWith("Amount")]
        public string AmountHeader { get; private set; }
        public bool SortByAmount { get; private set; }
        [PreviewWith(true)]
        public bool SortByName => !SortByAmount;
        public bool SortDescending { get; private set; }
        public ICommand NameSortCommand { get; set; }
        public ICommand AmountSortCommand { get; set; }

        public void Configure(InventoryLayout layout, string nameHeader, string amountHeader, bool sortByAmount, bool descending)
        {
            if (_layout == layout && NameHeader == nameHeader && AmountHeader == amountHeader &&
                SortByAmount == sortByAmount && SortDescending == descending) return;
            _layout = layout;
            NameHeader = nameHeader; AmountHeader = amountHeader;
            SortByAmount = sortByAmount; SortDescending = descending;
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
            for (int i = 0; i < items.Count; i++)
            {
                snapshot[i] = items[i];
                items[i].SetOdd((i & 1) != 0);
            }
            _items = snapshot;
            RaisePropertyChanged<IReadOnlyList<InventoryItemModel>>(nameof(Items));
        }
    }

    internal sealed class InventoryItemModel : ObservableObject
    {
        string _icon, _name, _amount;
        int _status;
        bool _odd;
        [PreviewWith(false, true, false, true, false, true, false, true)]
        public bool Odd => _odd;
        [PreviewWith("MyObjectBuilder_Ingot/Iron", "MyObjectBuilder_Ingot/Nickel", "MyObjectBuilder_Ingot/Silicon", "MyObjectBuilder_Ore/Ice", "MyObjectBuilder_Component/SteelPlate", "MyObjectBuilder_Component/Construction", "MyObjectBuilder_Component/Motor", "MyObjectBuilder_AmmoMagazine/NATO_25x184mm")]
        public string Icon => _icon;
        [PreviewWith("Iron Ingot", "Nickel Ingot", "Silicon Wafer", "Ice", "Steel Plate", "Construction Comp.", "Motor", "Gatling Ammo Box")]
        public string Name => _name;
        [PreviewWith("5.4M", "12.9k", "8,240", "2.10M", "1,560", "742", "36", "0")]
        public string Amount => _amount;
        [PreviewWith(0, 0, 0, 0, 0, 1, 2, 2)]
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

        public void SetOdd(bool odd)
        {
            RaiseAndSetIfChanged(ref _odd, odd, nameof(Odd));
        }
    }
}
