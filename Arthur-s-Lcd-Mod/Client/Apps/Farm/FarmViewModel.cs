using System;
using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Markdown;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Farm
{
    internal sealed class FarmViewModel : Views.CardGridViewModel
    {
        static readonly Color GrowthBarColor = new Color(68, 210, 92);
        static readonly Color WaterBarColor = new Color(64, 156, 255);

        readonly FarmApp _app;
        readonly List<FarmDisplayEntry> _compactEntries = new List<FarmDisplayEntry>();
        readonly List<FarmDisplayEntry> _cardEntries = new List<FarmDisplayEntry>();
        readonly Dictionary<long, FarmDisplayEntry> _byId = new Dictionary<long, FarmDisplayEntry>();
        bool _isCompact = true;
        int _entryCount;

        public event System.Action DetailsOpened;

        public FarmViewModel(FarmApp app)
        {
            _app = app;
        }

        public IReadOnlyList<FarmDisplayEntry> CompactEntries => _compactEntries;
        public IReadOnlyList<FarmDisplayEntry> CardEntries => _cardEntries;
        public bool IsCompact => _isCompact;
        public bool IsCard => !_isCompact;

        protected override float MinimumColumnWidth => _isCompact ? 100f : 220f;
        protected override float RowHeight => _isCompact ? 100f : 72f;
        protected override int CardCount => _entryCount;

        public void Refresh()
        {
            bool compact = _app.IsGridDisplay;
            if (RaiseAndSetIfChanged(ref _isCompact, compact, nameof(IsCompact)))
            {
                RaisePropertyChanged<bool>(nameof(IsCard));
                _compactEntries.Clear();
                _cardEntries.Clear();
            }

            List<FarmDisplayEntry> target = compact ? _compactEntries : _cardEntries;
            target.Clear();
            Color background = _app.NativeBackground;
            ColorConfigComponent colors = _app.FarmColors;
            Color growthColor = GrowthBarColor.EnsureMinimalContrast(background);
            IReadOnlyList<FarmApp.FarmEntry> source = _app.NativeEntries;
            for (int i = 0; i < source.Count; i++)
            {
                FarmApp.FarmEntry farm = source[i];
                FarmDisplayEntry entry;
                if (!_byId.TryGetValue(farm.EntryId, out entry))
                    _byId[farm.EntryId] = entry = new FarmDisplayEntry(farm.EntryId, () => DetailsOpened?.Invoke());
                entry.Update(farm, growthColor, GetWaterBarColor(colors, farm.WaterRatio).EnsureMinimalContrast(background));
                target.Add(entry);
            }
            _entryCount = target.Count;
            RaisePropertyChanged<IReadOnlyList<FarmDisplayEntry>>(compact ? nameof(CompactEntries) : nameof(CardEntries));
            UpdateCardGrid();
        }

        static Color GetWaterBarColor(ColorConfigComponent colors, float waterRatio)
        {
            if (waterRatio < .3f)
                return colors.ResolveErrorColor();
            if (waterRatio < .6f)
                return colors.ResolveWarningColor();
            return WaterBarColor;
        }
    }

    internal sealed class FarmDisplayEntry : ObservableObject, IFarmPlotDetailsSource
    {
        string _icon;
        string _outputSprite;
        string _label;
        string _percentText;
        string _remainingText;
        float _growth;
        float _water;
        Color _growthColor;
        Color _waterColor;
        Color _statusColor;
        string _title;
        string _detailsSource;
        MarkdownDocument _details;

        static readonly MarkdownParser Parser = new MarkdownParser();

        readonly Action _detailsOpened;

        public FarmDisplayEntry(long key, Action detailsOpened)
        {
            Key = key;
            _detailsOpened = detailsOpened;
        }

        public void NotifyDetailsOpened()
        {
            _detailsOpened?.Invoke();
        }
        public long Key { get; private set; }
        public string Icon => _icon;
        public string OutputSprite => _outputSprite;
        public string Label => _label;
        public string PercentText => _percentText;
        public string RemainingText => _remainingText;
        public float Growth => _growth;
        public float Water => _water;
        public Color GrowthColor => _growthColor;
        public Color WaterColor => _waterColor;
        public Color StatusColor => _statusColor;
        public string Title => _title;
        public MarkdownDocument Details => _details;

        public void Update(FarmApp.FarmEntry farm, Color growthColor, Color waterColor)
        {
            Set(ref _icon, string.IsNullOrEmpty(farm.OutputSprite) ? "MissingIcon" : farm.OutputSprite, nameof(Icon));
            Set(ref _outputSprite, farm.OutputSprite ?? string.Empty, nameof(OutputSprite));
            Set(ref _label, farm.OutputName, nameof(Label));
            Set(ref _percentText, farm.PercentText, nameof(PercentText));
            Set(ref _remainingText, farm.RemainingText, nameof(RemainingText));
            RaiseAndSetIfChanged(ref _growth, farm.Ratio, nameof(Growth));
            RaiseAndSetIfChanged(ref _water, farm.WaterRatio, nameof(Water));
            RaiseAndSetIfChanged(ref _growthColor, growthColor, nameof(GrowthColor));
            RaiseAndSetIfChanged(ref _waterColor, waterColor, nameof(WaterColor));
            RaiseAndSetIfChanged(ref _statusColor, farm.StatusColor, nameof(StatusColor));
            var block = farm.Plot != null ? farm.Plot.Block : null;
            Set(ref _title, block == null ? string.Empty
                : !string.IsNullOrEmpty(block.CustomName) ? block.CustomName : block.DisplayNameText, nameof(Title));
            string details = farm.DetailsMarkdown ?? string.Empty;
            if (!string.Equals(details, _detailsSource, StringComparison.Ordinal))
            {
                _detailsSource = details;
                _details = Parser.Parse(details);
                RaisePropertyChanged<MarkdownDocument>(nameof(Details));
            }
        }

        void Set(ref string field, string value, string name)
        {
            value = value ?? string.Empty;
            if (string.Equals(field, value, StringComparison.Ordinal)) return;
            field = value;
            RaisePropertyChanged<string>(name);
        }
    }
}
