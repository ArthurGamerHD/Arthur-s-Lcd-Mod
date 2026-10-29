using System;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using Adk.Gui.Styling;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.DigitalPictureFrames
{
    internal sealed class DigitalPictureFramesViewModel : ObservableObject
    {
        readonly DigitalPictureFramesApp _app;
        readonly List<PicturePage> _pages = new List<PicturePage>();
        int _selectedPage;
        bool _canEdit;
        string _pickText = string.Empty;
        int _displayMode = -1;

        public DigitalPictureFramesViewModel(DigitalPictureFramesApp app)
        {
            _app = app;
            OpenPickerCommand = new DelegateCommand(_ => _app.OpenNativePicker());
        }

        public IReadOnlyList<PicturePage> Pages => _pages;
        public int SelectedPage
        {
            get { return _selectedPage; }
            set { RaiseAndSetIfChanged(ref _selectedPage, value, nameof(SelectedPage)); }
        }
        public ICommand OpenPickerCommand { get; }
        public bool HasImage => _pages.Count > 0;
        public bool ShowPickerButton => _pages.Count == 0;
        public bool CanEdit => _canEdit;
        public string PickText => _pickText;

        public void Refresh()
        {
            IReadOnlyList<string> sprites = _app.NativeSprites;
            if (!SameSprites(sprites))
            {
                bool hadImage = HasImage;
                _pages.Clear();
                for (int i = 0; i < sprites.Count; i++)
                    _pages.Add(new PicturePage(sprites[i]));
                RaisePropertyChanged<IReadOnlyList<PicturePage>>(nameof(Pages));
                if (hadImage != HasImage)
                {
                    RaisePropertyChanged<bool>(nameof(HasImage));
                    RaisePropertyChanged<bool>(nameof(ShowPickerButton));
                }
            }

            int displayMode = _app.NativeDisplayMode;
            bool styleChanged = displayMode != _displayMode;
            _displayMode = displayMode;

            for (int i = 0; i < _pages.Count; i++)
            {
                PicturePage page = _pages[i];
                page.Update(_app.NativeIsLoading(page.Sprite), styleChanged, _app.CreateNativeBrush);
            }

            SelectedPage = _app.NativeSelectedIndex(_pages.Count);
            RaiseAndSetIfChanged(ref _canEdit, _app.NativeCanEdit, nameof(CanEdit));
            RaiseAndSetIfChanged(ref _pickText, _app.NativePickText, nameof(PickText));
        }

        bool SameSprites(IReadOnlyList<string> sprites)
        {
            if (sprites.Count != _pages.Count)
                return false;
            for (int i = 0; i < sprites.Count; i++)
                if (!string.Equals(sprites[i], _pages[i].Sprite, StringComparison.OrdinalIgnoreCase))
                    return false;
            return true;
        }
    }

    internal sealed class PicturePage : ObservableObject
    {
        BackgroundBrush _image;
        bool _isLoading;

        public PicturePage(string sprite)
        {
            Sprite = sprite;
        }

        public string Sprite { get; }
        public BackgroundBrush Image => _image;
        public bool IsLoading => _isLoading;

        public void Update(bool isLoading, bool styleChanged, Func<string, BackgroundBrush> createBrush)
        {
            RaiseAndSetIfChanged(ref _isLoading, isLoading, nameof(IsLoading));
            BackgroundBrush image = isLoading ? null : _image == null || styleChanged ? createBrush(Sprite) : _image;
            RaiseAndSetIfChanged(ref _image, image, nameof(Image));
        }
    }
}
