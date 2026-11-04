using System.Collections.Generic;
using Adk.Mvvm;

namespace LcdMod.Client.Apps.ButtonPad
{
    /// <summary>What the button pad view model asks its app for (<see cref="ButtonPadApp"/>; a design-time host in the preview).</summary>
    internal interface IButtonPadHost
    {
        // Fills destination with the buttons to show (reusing its models) and gives their size.
        void BuildNativeEntries(List<ActionButtonModel> destination, out float buttonSize, out float cellWidth, out float cellHeight);
    }

    internal sealed class ButtonPadViewModel : ObservableObject
    {
        readonly IButtonPadHost _app;
        readonly List<ActionButtonModel> _entries = new List<ActionButtonModel>();
        float _buttonSize = 92f;
        float _cellWidth = 96f;
        float _cellHeight = 96f;

        public ButtonPadViewModel(IButtonPadHost app)
        {
            _app = app;
        }

        [PreviewWith(6)]
        public IReadOnlyList<ActionButtonModel> Entries => _entries;
        [PreviewWith(140f)]
        public float ButtonSize => _buttonSize;
        [PreviewWith(166f)]
        public float CellWidth => _cellWidth;
        [PreviewWith(160f)]
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
