using Acrux.Styling;
using Adk.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Views
{
    /// <summary>A centered status shown instead of, or over, an app's content: an icon or a loading wheel above a text.</summary>
    [PreviewWith("Text", "No blocks found on this grid")]
    [PreviewWith("IsVisible", true)]
    [PreviewWith("HasIcon", true)]
    [PreviewWith("Color", "#E0A010")]
    internal sealed class StatusMessage : ObservableObject
    {
        string _text = string.Empty;
        BackgroundBrush _iconMask;
        string _icon;
        Color _color = Color.White;
        bool _isLoading;
        bool _isVisible;

        public string Text => _text;
        public BackgroundBrush IconMask => _iconMask;
        [PreviewWith("#FFFFFF")]
        public Color Color => _color;
        public bool IsLoading => _isLoading;
        public bool HasIcon => !_isLoading && _iconMask != null;
        public bool IsVisible => _isVisible;

        public void Show(string text, string icon, Color color)
        {
            Set(text, icon, color, false);
        }

        public void ShowLoading(string text, Color color)
        {
            Set(text, null, color, true);
        }

        public void Hide()
        {
            RaiseAndSetIfChanged(ref _isVisible, false, nameof(IsVisible));
        }

        void Set(string text, string icon, Color color, bool loading)
        {
            RaiseAndSetIfChanged(ref _text, text ?? string.Empty, nameof(Text));
            if (_icon != icon)
            {
                _icon = icon;
                _iconMask = string.IsNullOrEmpty(icon) ? null : new ImageBrush(icon) { Stretch = Stretch.Uniform };
                RaisePropertyChanged<BackgroundBrush>(nameof(IconMask));
            }
            RaiseAndSetIfChanged(ref _color, color, nameof(Color));
            RaiseAndSetIfChanged(ref _isLoading, loading, nameof(IsLoading));
            RaisePropertyChanged<bool>(nameof(HasIcon));
            RaiseAndSetIfChanged(ref _isVisible, true, nameof(IsVisible));
        }
    }
}
