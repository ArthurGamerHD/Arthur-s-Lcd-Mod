using System.ComponentModel;
using Acrux.Core;

namespace LcdMod.Client.Apps.Views
{
    /// <summary>
    /// What an app's shell (<see cref="AppShellView"/>) shows around its content: the title row, a background layer
    /// beneath everything and an optional footer. <see cref="Abstract.App"/> in game; a design-time shell in the IDE
    /// preview, so a previewed view sits in the same chrome as on the LCD.
    /// </summary>
    internal interface IAppShell : INotifyPropertyChanged
    {
        string Title { get; }
        float SurfaceFontSize { get; }
        bool ChromeVisible { get; }
        VisualElement Content { get; }
        VisualElement BackgroundContent { get; }
        VisualElement Footer { get; }
        bool FooterVisible { get; }
        int ContentRow { get; }
        int RowSpan { get; }
    }
}
