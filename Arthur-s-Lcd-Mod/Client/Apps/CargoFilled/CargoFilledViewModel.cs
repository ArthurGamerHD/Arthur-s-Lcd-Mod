using System.Collections.Generic;

namespace LcdMod.Client.Apps.CargoFilled
{
    internal sealed class CargoFilledViewModel : Views.CardGridViewModel
    {
        readonly CargoFilledApp _app;
        readonly List<CargoPercentageEntry> _entries = new List<CargoPercentageEntry>();
        readonly Dictionary<long, CargoPercentageEntry> _byId = new Dictionary<long, CargoPercentageEntry>();

        public Views.StatusMessage Message { get; } = new Views.StatusMessage();

        public CargoFilledViewModel(CargoFilledApp app)
        {
            _app = app;
        }

        public IReadOnlyList<CargoPercentageEntry> Entries => _entries;
        protected override float MinimumColumnWidth => _app.NativeGridMode ? 220f : 0f;
        protected override float RowHeight => _app.NativeGridMode ? 72f : 36f;
        protected override int CardCount => _entries.Count;

        public void Refresh()
        {
            _entries.Clear();
            foreach (CargoEntry entry in _app.NativeEntries)
            {
                CargoPercentageEntry display;
                if (!_byId.TryGetValue(entry.EntryId, out display))
                    _byId[entry.EntryId] = display = new CargoPercentageEntry(entry.EntryId);
                display.Update(entry.Icon, entry.Name, entry.Percentage, entry.PercentageText, entry.Clicked);
                display.UpdateAppearance(_app.NativeEntryClasses, _app.NativeFill(entry.Percentage));
                _entries.Add(display);
            }
            RaisePropertyChanged<IReadOnlyList<CargoPercentageEntry>>(nameof(Entries));
            UpdateCardGrid();
        }
    }
}
