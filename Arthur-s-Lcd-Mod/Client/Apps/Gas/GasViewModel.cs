using System;
using System.Collections.Generic;
using LcdMod.Client.Helpers;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Gas
{
    internal sealed class GasViewModel : Views.CardGridViewModel
    {
        readonly GasApp _app;
        readonly List<GasDisplayEntry> _entries = new List<GasDisplayEntry>();
        readonly Dictionary<long, GasDisplayEntry> _byId = new Dictionary<long, GasDisplayEntry>();

        public GasViewModel(GasApp app)
        {
            _app = app;
        }

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

        public long Key { get; private set; }
        public string Icon => _icon;
        public string Name => _name;
        public float Percentage => _percentage;
        public string PercentageText => FormatingHelper.PercentageToString(_percentage);
        public string Classes => _classes;
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
