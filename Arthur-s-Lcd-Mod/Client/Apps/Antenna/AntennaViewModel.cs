using System.Collections.Generic;
using LcdMod.Client.Apps.Views;
using LcdMod.Client.Gui.UserControls.Antenna;
using Adk.Mvvm;

namespace LcdMod.Client.Apps.Antenna
{
    /// <summary>What the antenna view model reads from its app (<see cref="AntennaApp"/>; a design-time host in the preview).</summary>
    internal interface IAntennaHost
    {
        IReadOnlyList<AntennaEntry> Antennas { get; }
        bool IsGridMode { get; }
    }

    internal sealed class AntennaViewModel : ObservableObject
    {
        const float MINIMUM_COLUMN_WIDTH = 400f;
        readonly IAntennaHost _app;
        float _viewportWidth;
        int _columns = 1;

        public AntennaViewModel(IAntennaHost app)
        {
            _app = app;
        }

        [PreviewWith(4)]
        public IReadOnlyList<AntennaEntry> Entries => _app.Antennas;
        [PreviewWith(1)]
        public int Columns => _columns;

        public void SetViewportWidth(float width)
        {
            if (_viewportWidth == width)
                return;
            _viewportWidth = width;
            UpdateColumns();
        }

        public void Refresh()
        {
            RaisePropertyChanged<IReadOnlyList<AntennaEntry>>(nameof(Entries));
            UpdateColumns();
        }

        void UpdateColumns()
        {
            int columns = _app.IsGridMode
                ? UniformColumns.Calculate(_viewportWidth, MINIMUM_COLUMN_WIDTH, _app.Antennas.Count)
                : 1;
            RaiseAndSetIfChanged(ref _columns, columns, nameof(Columns));
        }
    }
}
