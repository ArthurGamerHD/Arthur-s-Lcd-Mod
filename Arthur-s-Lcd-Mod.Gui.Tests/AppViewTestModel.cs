using System.ComponentModel;
using Adk.Gui.Core;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Thrust;
using VRageMath;

namespace LcdMod.Client.Apps.Abstract;

// Test-only data-context contract for compiling the real AppView XAML without
// the game's grid/config services. The view, templates and LCD host are real.
public sealed class App : INotifyPropertyChanged, IResourceProvider
{
    readonly ThrustViewModel _thrust = new();
    public event PropertyChangedEventHandler PropertyChanged;
    public event Action<string> ResourceChanged;
    public string Title => "Thrust title";
    public float SurfaceFontSize { get; private set; } = 1;
    public bool ChromeVisible => true;
    public VisualElement Content { get; }
    public VisualElement Footer => null;
    public VisualElement BackgroundContent { get; }
    public int ContentRow => 1;
    public int RowSpan => 1;

    public App(VisualElement content = null, VisualElement background = null)
    {
        Content = content ?? new ThrustView(_thrust);
        BackgroundContent = background;
        ChangeFontSize(1);
    }

    public void ChangeFontSize(float size)
    {
        SurfaceFontSize = size;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SurfaceFontSize)));
        ResourceChanged?.Invoke(null);
        _thrust.RefreshLayout(1, size, "White", Color.White, Color.Black,
            new[] { "Forward:", "Backward:", "Left:", "Right:", "Up:", "Down:" });
    }

    public bool TryGetResource<T>(string key, out T value)
    {
        object resource = key switch
        {
            "fontScale" => SurfaceFontSize,
            "textFont" => "White",
            "factionIcon" => "Triangle",
            "factionIconMask" => new Adk.Gui.Styling.ImageBrush("Triangle") { Stretch = Adk.Gui.Styling.Stretch.Uniform },
            "accentColor" => Color.White,
            "headerColor" => Color.White,
            _ => null
        };
        if (resource is T typed) { value = typed; return true; }
        value = default;
        return false;
    }
}
