using Adk.Mvvm;
using System.Collections.Generic;

using VRageMath;

namespace LcdMod.Client.Apps.CargoFilled
{
    /// <summary>What the cargo view model reads from its app (<see cref="CargoFilledApp"/>; a design-time host in the preview).</summary>
    internal interface ICargoFilledHost
    {
        IReadOnlyList<CargoEntry> NativeEntries { get; }
        bool NativeGridMode { get; }
        string NativeEntryClasses { get; }
        Color NativeFill(float percentage);
    }

    [PreviewWith("Message.Text", "")]
    [PreviewWith("Message.IsVisible", false)]
    [PreviewWith("Message.HasIcon", false)]
    [PreviewWith("Message.Color", "#FFFFFF")]
    [PreviewWith("Columns", 2)]
    [PreviewWith("CardHeight", 72f)]
    internal sealed class CargoFilledViewModel : Views.CardGridViewModel
    {
        readonly ICargoFilledHost _app;
        readonly List<CargoPercentageEntry> _entries = new List<CargoPercentageEntry>();
        readonly Dictionary<long, CargoPercentageEntry> _byId = new Dictionary<long, CargoPercentageEntry>();

        public Views.StatusMessage Message { get; } = new Views.StatusMessage();

        public CargoFilledViewModel(ICargoFilledHost app)
        {
            _app = app;
        }

        [PreviewWith(4)]
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
