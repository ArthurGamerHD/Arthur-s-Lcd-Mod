using System.Collections.Generic;
using LcdMod.Client.Apps.Views;
using LcdMod.Client.Gui.UserControls.Antenna;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.Antenna
{
    internal sealed class AntennaViewModel : ObservableObject
    {
        const float MINIMUM_COLUMN_WIDTH = 400f;
        readonly AntennaApp _app;
        float _viewportWidth;
        int _columns = 1;

        public AntennaViewModel(AntennaApp app)
        {
            _app = app;
        }

        public IReadOnlyList<AntennaEntry> Entries => _app.Antennas;
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
