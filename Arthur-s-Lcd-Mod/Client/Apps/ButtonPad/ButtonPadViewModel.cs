using System.Collections.Generic;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.ButtonPad
{
    internal sealed class ButtonPadViewModel : ObservableObject
    {
        readonly ButtonPadApp _app;
        readonly List<ActionButtonModel> _entries = new List<ActionButtonModel>();
        float _buttonSize = 92f;
        float _cellWidth = 96f;
        float _cellHeight = 96f;

        public ButtonPadViewModel(ButtonPadApp app)
        {
            _app = app;
        }

        public IReadOnlyList<ActionButtonModel> Entries => _entries;
        public float ButtonSize => _buttonSize;
        public float CellWidth => _cellWidth;
        public float CellHeight => _cellHeight;

        public void Refresh()
        {
            int previousCount = _entries.Count;
            float buttonSize, cellWidth, cellHeight;
            _app.BuildNativeEntries(_entries, out buttonSize, out cellWidth, out cellHeight);
            if (_entries.Count != previousCount)
                RaisePropertyChanged<IReadOnlyList<ActionButtonModel>>(nameof(Entries));
            RaiseAndSetIfChanged(ref _buttonSize, buttonSize, nameof(ButtonSize));
            RaiseAndSetIfChanged(ref _cellWidth, cellWidth, nameof(CellWidth));
            RaiseAndSetIfChanged(ref _cellHeight, cellHeight, nameof(CellHeight));
        }
    }
}
