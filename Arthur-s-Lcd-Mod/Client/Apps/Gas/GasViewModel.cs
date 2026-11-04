using System;
using System.Collections.Generic;
using LcdMod.Client.Helpers;
using Adk.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Gas
{
    /// <summary>What the gas view model reads from its app (<see cref="GasApp"/>; a design-time host in the preview).</summary>
    internal interface IGasHost
    {
        IReadOnlyList<GasEntry> Entries { get; }
        bool NativeGridMode { get; }
        string NativeEntryClasses { get; }
        Color NativeFill(float percentage);
    }

    [PreviewWith("Columns", 2)]
    [PreviewWith("CardHeight", 72f)]
    internal sealed class GasViewModel : Views.CardGridViewModel
    {
        readonly IGasHost _app;
        readonly List<GasDisplayEntry> _entries = new List<GasDisplayEntry>();
        readonly Dictionary<long, GasDisplayEntry> _byId = new Dictionary<long, GasDisplayEntry>();

        public GasViewModel(IGasHost app)
        {
            _app = app;
        }

        [PreviewWith(4)]
        public IReadOnlyList<GasDisplayEntry> Entries => _entries;
        protected override float MinimumColumnWidth => _app.NativeGridMode ? 220f : 0f;
        protected override float RowHeight => _app.NativeGridMode ? 72f : 36f;
        protected override int CardCount => _entries.Count;

        public void Refresh()
        {
            _entries.Clear();
            IReadOnlyList<GasEntry> source = _app.Entries;
            for (int i = 0; i < source.Count; i++)
            {
                GasEntry gas = source[i];
                GasDisplayEntry entry;
                if (!_byId.TryGetValue(gas.EntryId, out entry))
                    _byId[gas.EntryId] = entry = new GasDisplayEntry(gas.EntryId);
                entry.Update(gas.Icon, gas.Name, gas.Percentage);
                entry.UpdateAppearance(_app.NativeEntryClasses, _app.NativeFill(gas.Percentage));
                _entries.Add(entry);
            }
            RaisePropertyChanged<IReadOnlyList<GasDisplayEntry>>(nameof(Entries));
            UpdateCardGrid();
        }
    }

    internal sealed class GasDisplayEntry : ObservableObject
    {
        string _icon;
        string _name;
        float _percentage;
        string _classes = "card";
        Color _fill;

        public GasDisplayEntry(long key)
        {
            Key = key;
        }

        [PreviewWith(1L, 2L, 3L, 4L)]
        public long Key { get; private set; }
        [PreviewWith("MyObjectBuilder_OxygenTank/LargeHydrogenTank", "MyObjectBuilder_OxygenTank/LargeHydrogenTank", "MyObjectBuilder_OxygenTank/Oxygen", "MyObjectBuilder_OxygenTank/Oxygen")]
        public string Icon => _icon;
        [PreviewWith("Hydrogen Tank 1", "Hydrogen Tank 2", "Oxygen Tank", "Oxygen Tank (Spare)")]
        public string Name => _name;
        [PreviewWith(0.82f, 0.41f, 0.97f, 0.05f)]
        public float Percentage => _percentage;
        [PreviewWith("82%", "41%", "97%", "5%")]
        public string PercentageText => FormatingHelper.PercentageToString(_percentage);
        [PreviewWith("card")]
        public string Classes => _classes;
        [PreviewWith("#D1B1D7", "#D1B1D7", "#D1B1D7", "#602020")]
        public Color Fill => _fill;

        public void UpdateAppearance(string classes, Color fill)
        {
            RaiseAndSetIfChanged(ref _classes, classes, nameof(Classes));
            RaiseAndSetIfChanged(ref _fill, fill, nameof(Fill));
        }

        public void Update(string icon, string name, float percentage)
        {
            if (!string.Equals(_icon, icon, StringComparison.Ordinal))
            {
                _icon = string.IsNullOrWhiteSpace(icon) ? "MissingIcon" : icon;
                RaisePropertyChanged<string>(nameof(Icon));
            }

            if (!string.Equals(_name, name, StringComparison.Ordinal))
            {
                _name = name ?? string.Empty;
                RaisePropertyChanged<string>(nameof(Name));
            }

            if (_percentage.Equals(percentage))
                return;

            _percentage = percentage;
            RaisePropertyChanged<float>(nameof(Percentage));
            RaisePropertyChanged<string>(nameof(PercentageText));
        }
    }
}
