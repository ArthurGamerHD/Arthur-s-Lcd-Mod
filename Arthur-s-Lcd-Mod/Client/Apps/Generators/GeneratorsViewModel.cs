using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Helpers;
using LcdMod.Common.Mvvm;
using Adk.Gui.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.Generators
{
    internal sealed class GeneratorsViewModel : Views.CardGridViewModel
    {
        readonly GeneratorsApp _app;
        readonly List<GeneratorDisplayEntry> _entries = new List<GeneratorDisplayEntry>();
        readonly Dictionary<string, GeneratorDisplayEntry> _byKey = new Dictionary<string, GeneratorDisplayEntry>();
        int _rangeTier = -1;
        float _fontScale = 1f;

        public GeneratorsViewModel(GeneratorsApp app)
        {
            _app = app;
        }

        public IReadOnlyList<GeneratorDisplayEntry> Entries => _entries;
        public string GraphWindowLabel => LocHelper.GetLoc("LcdMod_GraphWindow") + ":";
        public int RangeTier => _rangeTier;
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
        public string Key { get; private set; }
        public string Label => _label;
        public string InfoText => _info;
        public float Usage => _usage;
        public BackgroundBrush UsageBrush => _usageBrush;
        public Color TextColor => _textColor;
        public Color CardBackground => _cardBackground;
        public Color CardShadow => _cardShadow;
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
