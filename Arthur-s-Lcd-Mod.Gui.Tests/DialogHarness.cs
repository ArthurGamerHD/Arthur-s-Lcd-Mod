using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Core.Input;
using Adk.Gui.Extensions;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

/// <summary>Opens a dialog window modally over an owner styled with the real mod stylesheets.</summary>
sealed class DialogHarness : IDisposable
{
    public static readonly Color Accent = new(200, 120, 20);
    public static readonly Color Surface = new(30, 34, 40);
    public static readonly Color OnSurface = new(230, 230, 230);
    public static readonly Color Error = new(220, 40, 40);

    readonly Func<string, string> _previousLoader;
    readonly Func<string, bool> _previousDefined;
    public readonly Window Owner;
    public readonly LcdGuiHost Host;

    public DialogHarness(float width = 512, float height = 512)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string repo = directory!.FullName;
        _previousLoader = StyleSheet.SourceLoader;
        StyleSheet.SourceLoader = source =>
        {
            source = source.TrimStart('/');
            return File.ReadAllText(source.StartsWith("Data/Gui/Styles/Adk/", StringComparison.Ordinal)
                ? Path.Combine(repo, "External/Adk/Adk.Gui/Styles", source.Substring("Data/Gui/Styles/Adk/".Length))
                : Path.Combine(repo, "Arthur-s-Lcd-Mod/Content", source));
        };
        _previousDefined = LcdTextureResolver.IsTextureDefined;
        LcdTextureResolver.IsTextureDefined = _ => true;
        Owner = new Window
        {
            WindowState = WindowState.FullScreen,
            Platform = new OpenPlatform(),
            Content = new TextBlock { Text = "Owner content" },
            Styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/FluentStyle.xaml")
        };
        foreach (string key in new[] { "fontColor", "onSurfaceColor", "onSurfaceVariantColor", "onAccentColor", "onAccentContainerColor" })
            Owner.Resources.Add(key, OnSurface);
        foreach (string key in new[] { "surfaceColor", "surfaceContainerColor", "surfaceContainerHighColor", "surfaceContainerHighestColor",
                     "surfaceContainerLowColor", "surfaceContainerLowestColor", "secondaryContainerColor", "borderColor",
                     "borderVariantColor", "dividerColor", "scrollBarTrackColor", "scrollBarThumbColor", "disabledColor",
                     "controlBackgroundColor", "headerColor" })
            Owner.Resources.Add(key, Surface);
        Owner.Resources.Add("errorColor", Error);
        Owner.Resources.Add("accentColor", Accent);
        Owner.Resources.Add("accentContainerColor", Accent);
        Owner.Resources.Add("textFont", "White");
        Owner.Resources.Add("fontScale", 1f);
        Owner.Open();
        Host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), Owner);
        Host.Arrange(new RectangleF(0, 0, width, height));
    }

    public T Show<T>(T dialog) where T : Window
    {
        LcdMod.Client.Apps.Dialogs.DialogService.ShowModal(dialog, Owner, .65f);
        Draw();
        return dialog;
    }

    public List<MySprite> Draw()
    {
        var sprites = new List<MySprite>();
        Host.Draw(sprites);
        return sprites;
    }

    public List<string> Texts() => Draw().Where(s => s.Type == SpriteType.TEXT).Select(s => s.Data).ToList();

    public void Click(VisualElement element)
    {
        Draw();
        Host.MovePointer(element.Bounds.Center);
        Host.ClickPrimary();
        Draw();
    }

    public void ClickSecondary(VisualElement element)
    {
        Draw();
        Host.MovePointer(element.Bounds.Center);
        Host.ClickSecondary();
        Draw();
    }

    public static IEnumerable<VisualElement> Nodes(VisualElement root)
    {
        yield return root;
        foreach (var child in root.VisualChildren)
            foreach (var node in Nodes(child))
                yield return node;
    }

    public static T Find<T>(VisualElement root, Func<T, bool> predicate) where T : VisualElement =>
        Nodes(root).OfType<T>().First(node => IsShown(node) && predicate(node));

    static bool IsShown(VisualElement node)
    {
        for (var current = node; current != null; current = current.Parent)
            if (!current.Visible)
                return false;
        return true;
    }

    public static Button ButtonWithText(VisualElement root, string text) =>
        Find<Button>(root, button => Nodes(button).OfType<TextBlock>().Any(block => block.Text == text));

    public static TabItem TabWithText(VisualElement root, string text) =>
        Find<TabItem>(root, tab => Nodes(tab).OfType<TextBlock>().Any(block => block.Text == text));

    public void Dispose()
    {
        Host.Dispose();
        StyleSheet.SourceLoader = _previousLoader;
        LcdTextureResolver.IsTextureDefined = _previousDefined;
    }

    sealed class OpenPlatform : IWindowPlatform
    {
        public IWindowImpl CreateWindowImpl(Window window) => new Impl();

        sealed class Impl : IWindowImpl
        {
            public bool IsOpen { get; private set; }
            public void Open() => IsOpen = true;
            public void Close() => IsOpen = false;
            public void BindOpen(Key key) { }
            public bool UnbindOpen(Key key) => true;
            public void Dispose() { }
        }
    }
}
