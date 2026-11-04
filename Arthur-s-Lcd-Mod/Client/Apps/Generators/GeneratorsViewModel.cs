using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Helpers;
using Adk.Mvvm;
using Acrux.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.Generators
{
    /// <summary>What the generators view model reads from and asks of its app (<see cref="GeneratorsApp"/>; a design-time host in the preview).</summary>
    internal interface IGeneratorsHost
    {
        bool IsGridDisplay { get; }
        float SurfaceFontSize { get; }
        IReadOnlyList<PowerApp.PowerEntry> NativeEntries { get; }
        string NativeMaxLabel { get; }
        string NativeCurrentLabel { get; }
        Color NativeAccentFill { get; }
        Color NativeHeaderColor { get; }
        Color NativeErrorColor { get; }
        Color NativeForeground { get; }
        bool NativeDrawLines { get; }
        int RangeTier { get; }
        void SetRangeTier(int tier);
    }

    [PreviewWith("Columns", 1)]
    [PreviewWith("CardHeight", 132f)]
    internal sealed class GeneratorsViewModel : Views.CardGridViewModel
    {
        readonly IGeneratorsHost _app;
        readonly List<GeneratorDisplayEntry> _entries = new List<GeneratorDisplayEntry>();
        readonly Dictionary<string, GeneratorDisplayEntry> _byKey = new Dictionary<string, GeneratorDisplayEntry>();
        int _rangeTier = -1;
        float _fontScale = 1f;

        public GeneratorsViewModel(IGeneratorsHost app)
        {
            _app = app;
        }

        [PreviewWith(4)]
        public IReadOnlyList<GeneratorDisplayEntry> Entries => _entries;
        [PreviewWith("Graph Time Window:")]
        public string GraphWindowLabel => LocHelper.GetLoc("LcdMod_GraphWindow") + ":";
        [PreviewWith(2)]
        public int RangeTier => _rangeTier;
        [PreviewWith(1f)]
        public float FontScale => _fontScale;

        public void SetRangeTier(int tier)
        {
            _app.SetRangeTier(tier);
            RaiseAndSetIfChanged(ref _rangeTier, _app.RangeTier, nameof(RangeTier));
        }

        protected override float MinimumColumnWidth => _app.IsGridDisplay ? 400f : 0f;
        protected override float RowHeight => 132f;
        protected override int CardCount => _entries.Count;

        public void Refresh()
        {
            RaiseAndSetIfChanged(ref _rangeTier, _app.RangeTier, nameof(RangeTier));
            RaiseAndSetIfChanged(ref _fontScale, _app.SurfaceFontSize, nameof(FontScale));
            _entries.Clear();
            IReadOnlyList<PowerApp.PowerEntry> source = _app.NativeEntries;
            for (int i = 0; i < source.Count; i++)
            {
                PowerApp.PowerEntry power = source[i];
                GeneratorDisplayEntry entry;
                if (!_byKey.TryGetValue(power.Key, out entry))
                    _byKey[power.Key] = entry = new GeneratorDisplayEntry(power.Key);
                bool grid = _app.IsGridDisplay;
                bool lines = _app.NativeDrawLines;
                entry.Update(power, _app.NativeMaxLabel, _app.NativeCurrentLabel, grid && !lines, !grid && lines,
                    grid && lines, _app.NativeHeaderColor, _app.NativeErrorColor, _app.NativeForeground, _app.NativeAccentFill);
                _entries.Add(entry);
            }
            RaisePropertyChanged<IReadOnlyList<GeneratorDisplayEntry>>(nameof(Entries));
            UpdateCardGrid();
        }
    }

    internal sealed class GeneratorDisplayEntry : ObservableObject
    {
        static readonly Color Transparent = new Color(0, 0, 0, 0);
        string _label = string.Empty;
        string _info = string.Empty;
        float _usage;
        BackgroundBrush _usageBrush;
        Color _textColor;
        Color _cardBackground;
        Color _cardShadow;
        Color _lineColor;
        Vector4 _lineThickness;

        public GeneratorDisplayEntry(string key) { Key = key; }
        [PreviewWith("solar", "wind", "reactor", "engine")]
        public string Key { get; private set; }
        [PreviewWith("Solar Panels", "Wind Turbines", "Reactors", "Hydrogen Engines")]
        public string Label => _label;
        [PreviewWith("Max: 3 MW\nCurrent: 1.86 MW\n24 blocks", "Max: 4 MW\nCurrent: 1.4 MW\n10 blocks", "Max: 30 MW\nCurrent: 14.4 MW\n2 blocks", "Max: 5 MW\nCurrent: 0 W\nIdle")]
        public string InfoText => _info;
        [PreviewWith(0.62f, 0.35f, 0.48f, 0f)]
        public float Usage => _usage;
        public BackgroundBrush UsageBrush => _usageBrush;
        [PreviewWith("#C5EDFF")]
        public Color TextColor => _textColor;
        [PreviewWith("#3A203F", "#3A203F", "#3A203F", "#602020")]
        public Color CardBackground => _cardBackground;
        [PreviewWith("#0C060D", "#0C060D", "#0C060D", "#130606")]
        public Color CardShadow => _cardShadow;
        [PreviewWith("#C5EDFF")]
        public Color LineColor => _lineColor;
        public Vector4 LineThickness => _lineThickness;

        public void Update(PowerApp.PowerEntry power, string maxLabel, string currentLabel, bool drawAsCard, bool listLines,
            bool gridLines, Color header, Color error, Color foreground, Color accent)
        {
            RaiseAndSetIfChanged(ref _label, power.Name ?? string.Empty, nameof(Label));
            RaiseAndSetIfChanged(ref _info, maxLabel + FormatingHelper.WattsToString(power.Max) + "\n" +
                                             currentLabel + FormatingHelper.WattsToString(power.Current) + "\n" +
                                             (power.UsageLine ?? string.Empty), nameof(InfoText));
            RaiseAndSetIfChanged(ref _usage, MathHelper.Clamp(power.Usage, 0f, 1f), nameof(Usage));
            var brush = new SolidColorBrush(accent);
            if (!brush.Equals(_usageBrush))
            {
                _usageBrush = brush;
                RaisePropertyChanged<BackgroundBrush>(nameof(UsageBrush));
            }

            bool idle = power.Current <= 0;
            Color background = drawAsCard ? (idle ? error : header) : Transparent;
            Vector3 hsv = background.ColorToHSV();
            hsv.Z *= 0.2f;
            RaiseAndSetIfChanged(ref _cardBackground, background, nameof(CardBackground));
            RaiseAndSetIfChanged(ref _cardShadow, drawAsCard ? hsv.HSVtoColor() : Transparent, nameof(CardShadow));
            RaiseAndSetIfChanged(ref _textColor, idle && !drawAsCard ? error : foreground, nameof(TextColor));
            RaiseAndSetIfChanged(ref _lineColor, gridLines ? new Color(header.R, header.G, header.B) : foreground, nameof(LineColor));
            RaiseAndSetIfChanged(ref _lineThickness,
                gridLines ? new Vector4(1f) : listLines ? new Vector4(0f, 1f, 0f, 1f) : Vector4.Zero, nameof(LineThickness));
        }
    }
}
