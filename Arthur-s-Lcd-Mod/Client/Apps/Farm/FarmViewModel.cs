using System;
using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Markdown;
using LcdMod.Common.Config.Components;
using Adk.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Farm
{
    /// <summary>What the farm view model reads from its app (<see cref="FarmApp"/>; a design-time host in the preview).</summary>
    internal interface IFarmHost
    {
        bool IsGridDisplay { get; }
        Color NativeBackground { get; }
        ColorConfigComponent FarmColors { get; }
        IReadOnlyList<FarmApp.FarmEntry> NativeEntries { get; }
    }

    [PreviewWith("Columns", 3)]
    [PreviewWith("CardHeight", 100f)]
    internal sealed class FarmViewModel : Views.CardGridViewModel
    {
        static readonly Color GrowthBarColor = new Color(68, 210, 92);
        static readonly Color WaterBarColor = new Color(64, 156, 255);

        readonly IFarmHost _app;
        readonly List<FarmDisplayEntry> _compactEntries = new List<FarmDisplayEntry>();
        readonly List<FarmDisplayEntry> _cardEntries = new List<FarmDisplayEntry>();
        readonly Dictionary<long, FarmDisplayEntry> _byId = new Dictionary<long, FarmDisplayEntry>();
        bool _isCompact = true;
        int _entryCount;

        public event System.Action DetailsOpened;

        public FarmViewModel(IFarmHost app)
        {
            _app = app;
        }

        [PreviewWith(3)]
        public IReadOnlyList<FarmDisplayEntry> CompactEntries => _compactEntries;
        public IReadOnlyList<FarmDisplayEntry> CardEntries => _cardEntries;
        [PreviewWith(true)]
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
        [PreviewWith(1L, 2L, 3L)]
        public long Key { get; private set; }
        [PreviewWith("MyObjectBuilder_ConsumableItem/Vegetables", "MyObjectBuilder_ConsumableItem/Mushrooms", "MyObjectBuilder_PhysicalObject/Grain")]
        public string Icon => _icon;
        [PreviewWith("MyObjectBuilder_ConsumableItem/Vegetables", "MyObjectBuilder_ConsumableItem/Mushrooms", "MyObjectBuilder_PhysicalObject/Grain")]
        public string OutputSprite => _outputSprite;
        [PreviewWith("Vegetables", "Mushrooms", "Grain")]
        public string Label => _label;
        [PreviewWith("82%", "35%", "10%")]
        public string PercentText => _percentText;
        [PreviewWith("4 min", "18 min", "32 min")]
        public string RemainingText => _remainingText;
        [PreviewWith(0.82f, 0.35f, 0.1f)]
        public float Growth => _growth;
        [PreviewWith(0.9f, 0.45f, 0.12f)]
        public float Water => _water;
        [PreviewWith("#44D25C")]
        public Color GrowthColor => _growthColor;
        [PreviewWith("#FFFFFF", "#E0A010", "#FFFFFF")]
        public Color WaterColor => _waterColor;
        [PreviewWith("#81C784", "#E0A010", "#602020")]
        public Color StatusColor => _statusColor;
        [PreviewWith("Farm Plot 1", "Farm Plot 2", "Farm Plot 3")]
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
            Set(ref _title, farm.Title, nameof(Title));
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
