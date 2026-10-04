using System.Collections.Generic;
using LcdMod.Client.Apps.Views;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.IntegrityMonitor
{
    internal sealed class IntegrityMonitorViewModel : ObservableObject
    {
        readonly List<IntegrityLegendEntry> _legend = new List<IntegrityLegendEntry>();
        Color?[,] _cells;
        int _version;
        int _fitSide = 1;
        float _rotation;
        float _scale = 1f;
        Color _legendBackground;

        public StatusMessage Status { get; } = new StatusMessage();
        public Color?[,] Cells => _cells;
        public int Version => _version;
        public int FitSide => _fitSide;
        public float MapRotation => _rotation;
        public float MapScale => _scale;
        public bool HasMap => _cells != null;
        public IReadOnlyList<IntegrityLegendEntry> Legend => _legend;
        public bool HasLegend => _legend.Count > 0;
        public Color LegendBackground => _legendBackground;

        public void SetMap(Color?[,] cells, int fitSide, float rotation, float scale)
        {
            bool had = HasMap;
            _cells = cells;
            _version++;
            RaisePropertyChanged<Color?[,]>(nameof(Cells));
            RaisePropertyChanged<int>(nameof(Version));
            RaiseAndSetIfChanged(ref _fitSide, fitSide, nameof(FitSide));
            RaiseAndSetIfChanged(ref _rotation, rotation, nameof(MapRotation));
            RaiseAndSetIfChanged(ref _scale, scale, nameof(MapScale));
            if (had != HasMap)
                RaisePropertyChanged<bool>(nameof(HasMap));
        }

        public void ClearMap()
        {
            if (_cells == null)
                return;
            _cells = null;
            RaisePropertyChanged<Color?[,]>(nameof(Cells));
            RaisePropertyChanged<bool>(nameof(HasMap));
        }

        public void SetLegend(List<IntegrityLegendEntry> entries, Color background)
        {
            bool had = HasLegend;
            _legend.Clear();
            _legend.AddRange(entries);
            RaisePropertyChanged<IReadOnlyList<IntegrityLegendEntry>>(nameof(Legend));
            if (had != HasLegend)
                RaisePropertyChanged<bool>(nameof(HasLegend));
            RaiseAndSetIfChanged(ref _legendBackground, background, nameof(LegendBackground));
        }
    }

    internal sealed class IntegrityLegendEntry
    {
        public IntegrityLegendEntry(Color color, string caption)
        {
            Color = color;
            Caption = caption ?? string.Empty;
        }

        public Color Color { get; }
        public string Caption { get; }
    }
}
