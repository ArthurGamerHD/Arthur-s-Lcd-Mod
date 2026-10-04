using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Core.Binding;
using Adk.Gui.Layout;
using Adk.Gui.Lcd;
using Adk.Gui.Markup;
using Adk.Gui.Styling;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed partial class HotReloadView : UserControl
{
    public HotReloadView(HotReloadModel model)
    {
        Resources.Add("Multiply", new LcdMod.Client.Apps.Views.MultiplyConverter());
        InitializeComponent();
        DataContext = model;
    }

    public TextBlock TitleBlock => Title;
    public int Reloads { get; private set; }

    partial void OnHotReloaded()
    {
        Reloads++;
    }
}

public sealed class HotReloadModel : ObservableObject
{
    string _title = "Title";
    string _subtitle = "Subtitle";
    float _size = 2f;

    public string Title { get => _title; set => RaiseAndSetIfChanged(ref _title, value, nameof(Title)); }
    public string Subtitle { get => _subtitle; set => RaiseAndSetIfChanged(ref _subtitle, value, nameof(Subtitle)); }
    public float Size { get => _size; set => RaiseAndSetIfChanged(ref _size, value, nameof(Size)); }
    public List<HotReloadItem> Items { get; } = new() { new HotReloadItem("Iron"), new HotReloadItem("Gold") };
    public VisualElement Hosted { get; } = new TextBlock { Text = "Hosted" };
}

public sealed class HotReloadItem
{
    public HotReloadItem(string name) { Name = name; }
    public string Name { get; }
    public string Upper => Name.ToUpperInvariant();
}

public sealed class HotReloadTests : IDisposable
{
    const string Reloaded = """
        <UserControl xmlns="https://github.com/ArthurGamerHD/Adk"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:local="using:Arthur_s_Lcd_Mod.Gui.Tests"
                     x:Class="Arthur_s_Lcd_Mod.Gui.Tests.HotReloadView"
                     x:DataType="local:HotReloadModel">
          <Grid RowDefinitions="Auto Auto Auto">
            <!-- moved into a grid and rebound -->
            <ContentPresenter Grid.Row="2" Content="{Binding Hosted}" />
            <TextBlock x:Name="Title" Grid.Row="1" Text="{Binding Subtitle}" Foreground="Red"
                       FontSize="{Binding Size, Converter={StaticResource Multiply}, ConverterParameter=0.5, ConverterCulture=en-US}" />
            <ItemsControl ItemsSource="{Binding Items}">
              <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                  <StackPanel />
                </ItemsPanelTemplate>
              </ItemsControl.ItemsPanel>
              <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="local:HotReloadItem">
                  <Border Background="{DynamicResource accentColor}">
                    <TextBlock Text="{Binding Upper}" />
                  </Border>
                </DataTemplate>
              </ItemsControl.ItemTemplate>
            </ItemsControl>
          </Grid>
        </UserControl>
        """;

    readonly Dictionary<string, string> _files = new();
    readonly List<string> _log = new();
    readonly Func<string, string> _previousLoader;
    readonly HotReloadModel _model = new();
    readonly HotReloadView _view;
    readonly LcdGuiHost _host;

    public HotReloadTests()
    {
        _previousLoader = StyleSheet.SourceLoader;
        StyleSheet.SourceLoader = path => _files.TryGetValue(path, out var text) ? text : throw new FileNotFoundException(path);
        HotReload.Log = _log.Add;
        _view = new HotReloadView(_model);
        var root = new Window { WindowState = WindowState.FullScreen, Content = _view };
        root.Resources.Add("accentColor", Color.Blue);
        _host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), root);
        _host.Arrange(new RectangleF(0, 0, 400, 400));
        _host.Draw(new List<MySprite>());
    }

    public void Dispose()
    {
        HotReload.Enabled = false;
        HotReload.Log = null;
        _host.Dispose();
        StyleSheet.SourceLoader = _previousLoader;
    }

    [Fact]
    public void GeneratedViewsTrackThemselvesWithTheirSource()
    {
        var reloadable = Assert.IsAssignableFrom<IHotReloadableView>(_view);
        Assert.Equal("HotReloadView.xaml", reloadable.HotReloadSource);
    }

    [Fact]
    public void ReloadRebuildsBindingsTemplatesAndNamedElements()
    {
        TextBlock oldTitle = _view.TitleBlock;
        _files["HotReloadView.xaml"] = Reloaded;

        Assert.True(HotReload.Reload(_view), string.Join("\n", _log));
        Redraw();

        Assert.NotSame(oldTitle, _view.TitleBlock);
        Assert.Null(oldTitle.Parent);
        Assert.Equal("Subtitle", _view.TitleBlock.Text);
        Assert.Equal(Color.Red, _view.TitleBlock.Foreground);
        Assert.Equal(1f, _view.TitleBlock.FontSize);
        Assert.Equal(1, _view.TitleBlock.GridRow);
        Assert.Equal(1, _view.Reloads);

        _model.Subtitle = "Changed";
        _model.Size = 4f;
        Assert.Equal("Changed", _view.TitleBlock.Text);
        Assert.Equal(2f, _view.TitleBlock.FontSize);

        var texts = Nodes(_view).OfType<TextBlock>().Select(text => text.Text).ToList();
        Assert.Contains("IRON", texts);
        Assert.Contains("GOLD", texts);
        Assert.DoesNotContain("Iron", texts);
        Assert.Contains(Nodes(_view).OfType<Border>(), border => border.Background?.FallbackColor == Color.Blue);

        Assert.IsType<ContentPresenter>(_model.Hosted.Parent);
        Assert.Contains(_model.Hosted, Nodes(_view));
    }

    [Fact]
    public void FailedReloadKeepsTheCurrentTree()
    {
        VisualElement content = _view.Content;
        TextBlock title = _view.TitleBlock;
        _files["HotReloadView.xaml"] = Reloaded.Replace("x:Name=\"Title\" ", string.Empty);

        Assert.False(HotReload.Reload(_view));

        Assert.Same(content, _view.Content);
        Assert.Same(title, _view.TitleBlock);
        Assert.Contains(_log, line => line.Contains("'Title'"));
        Assert.Equal(0, _view.Reloads);
    }

    [Fact]
    public void PollingRebuildsChangedViews()
    {
        _files["HotReloadView.xaml"] = File.ReadAllText(Path.Combine(RepoDirectory(), "Arthur-s-Lcd-Mod.Gui.Tests/HotReloadView.xaml"));
        HotReload.Enabled = true;
        Tick(60);
        Assert.Equal(0, _view.Reloads);

        _files["HotReloadView.xaml"] = Reloaded;
        Tick(60);

        Assert.Equal(1, _view.Reloads);
        Assert.Equal("Subtitle", _view.TitleBlock.Text);
    }

    [Fact]
    public void DisposedViewsAreUntracked()
    {
        int tracked = HotReload.TrackedViewCount;
        var view = new HotReloadView(new HotReloadModel());
        Assert.Equal(tracked + 1, HotReload.TrackedViewCount);
        view.Dispose();
        Assert.Equal(tracked, HotReload.TrackedViewCount);
    }

    public static IEnumerable<object[]> ProductionViews()
    {
        yield return new object[] { "AppView" };
        yield return new object[] { "InventoryView" };
        yield return new object[] { "NpcMarketView" };
        yield return new object[] { "ThrustView" };
        yield return new object[] { "NamedColorView" };
    }

    [Theory]
    [MemberData(nameof(ProductionViews))]
    public void ProductionViewsRebuildFromTheirOwnXaml(string name)
    {
        string repo = RepoDirectory();
        StyleSheet.SourceLoader = path =>
        {
            path = path.TrimStart('/');
            if (path.StartsWith("Data/Gui/Styles/Adk/"))
                return File.ReadAllText(Path.Combine(repo, "External/Adk/Adk.Gui/Styles", path.Substring("Data/Gui/Styles/Adk/".Length)));
            if (path.StartsWith("Data/"))
                return File.ReadAllText(Path.Combine(repo, "Arthur-s-Lcd-Mod/Content", path));
            return File.ReadAllText(Directory.EnumerateFiles(repo, path, SearchOption.AllDirectories)
                .First(file => !file.Contains("/obj/") && !file.Contains("/bin/")));
        };
        VisualElement view = name switch
        {
            "AppView" => new LcdMod.Client.Apps.Views.AppView(),
            "InventoryView" => new LcdMod.Client.Apps.Inventory.InventoryView(),
            "NpcMarketView" => new LcdMod.Client.Apps.NpcMarket.NpcMarketView(),
            "ThrustView" => new LcdMod.Client.Apps.Thrust.ThrustView(new LcdMod.Client.Apps.Thrust.ThrustViewModel()),
            _ => new NamedColorView()
        };
        using (view)
        {
            var reloadable = (IHotReloadableView)view;
            VisualElement before = ((Decorator)view).Child;
            HotReload.Apply(reloadable, StyleSheet.SourceLoader(reloadable.HotReloadSource));
            Assert.NotSame(before, ((Decorator)view).Child);
        }
    }

    [Fact]
    public void RuntimeParserAcceptsEveryModView()
    {
        string apps = Path.Combine(RepoDirectory(), "Arthur-s-Lcd-Mod/Client");
        var failures = new List<string>();
        foreach (string file in Directory.EnumerateFiles(apps, "*View.xaml", SearchOption.AllDirectories))
        {
            try { UiXmlLoader.ParseNode(File.ReadAllText(file)); }
            catch (FormatException error) { failures.Add(Path.GetFileName(file) + ": " + error.Message); }
        }
        Assert.Empty(failures);
    }

    void Redraw()
    {
        _host.Arrange(new RectangleF(0, 0, 400, 400));
        _host.Draw(new List<MySprite>());
    }

    static void Tick(int count)
    {
        for (int i = 0; i < count; i++)
            HotReload.Update();
    }

    static string RepoDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        return directory!.FullName;
    }

    static IEnumerable<VisualElement> Nodes(VisualElement node)
    {
        yield return node;
        foreach (var child in node.LogicalChildren)
            foreach (var descendant in Nodes(child)) yield return descendant;
    }
}
