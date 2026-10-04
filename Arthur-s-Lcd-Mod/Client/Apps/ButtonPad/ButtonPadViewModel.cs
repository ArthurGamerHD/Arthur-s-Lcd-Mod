using System.Collections.Generic;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.ButtonPad
{
    internal sealed class ButtonPadViewModel : ObservableObject
    {
        readonly ButtonPadApp _app;
        readonly List<ActionButtonModel> _entries = new List<ActionButtonModel>();
        float _itemSize = 92f;
        float _spacing = 4f;

        public ButtonPadViewModel(ButtonPadApp app)
        {
            _app = app;
        }

        public IReadOnlyList<ActionButtonModel> Entries => _entries;
        public float ItemSize => _itemSize;
        public float Spacing => _spacing;

        public void Refresh()
        {
            int previousCount = _entries.Count;
            float itemSize, spacing;
            _app.BuildNativeEntries(_entries, out itemSize, out spacing);
            if (_entries.Count != previousCount)
                RaisePropertyChanged<IReadOnlyList<ActionButtonModel>>(nameof(Entries));
            RaiseAndSetIfChanged(ref _itemSize, itemSize, nameof(ItemSize));
            RaiseAndSetIfChanged(ref _spacing, spacing, nameof(Spacing));
        }
    }
}
