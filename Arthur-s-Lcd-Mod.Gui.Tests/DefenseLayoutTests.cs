using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using Adk.Gui.Rendering;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Controls;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class DefenseLayoutTests
{
    [Fact]
    public void ViewboxScalesItsChildUniformlyIntoTheAvailableSpace()
    {
        var content = new Border { Width = 100, Height = 50, BackgroundColor = Color.Red, Interactive = true };
        var viewbox = new Viewbox { Child = content };
        var renderer = new Recorder();
        var tree = new VisualTree(viewbox, renderer);
        tree.ArrangeViewport(new RectangleF(10, 20, 50, 200), 2);
        tree.Draw(1);

        var fill = Assert.Single(renderer.Fills, f => f.Color == Color.Red);
        // The 50x200 px viewport is 25x100 logical at scaling 2, so the 100x50 child scales by 0.25.
        Assert.Equal(new RectangleF(10, 20, 50, 25), fill.Bounds);
        Assert.Equal(new Vector2(25, 12.5f), viewbox.DesiredSize);
        Assert.Same(content, viewbox.HitTest(new Vector2(12, 22)));
        Assert.Null(viewbox.HitTest(new Vector2(12, 40)));
    }

    [Theory]
    [InlineData(4, 320, 200, 2)]
    [InlineData(3, 320, 200, 2)]
    [InlineData(6, 600, 200, 3)]
    public void TilingPanelFillsItsBoundsWithoutOverflow(int count, float width, float height, int expectedColumns)
    {
        var panel = new TilingPanel { Spacing = 6 };
        for (int i = 0; i < count; i++)
            panel.AddChild(new Border());
        var tree = new VisualTree(panel, new Recorder());
        tree.ArrangeViewport(new RectangleF(0, 0, width, height), 1);

        var bounds = panel.LogicalChildren.Select(child => child.Bounds).ToList();
        Assert.All(bounds, rect =>
        {
            Assert.InRange(rect.X, -0.01f, width);
            Assert.InRange(rect.Right, 0, width + 0.01f);
            Assert.InRange(rect.Bottom, 0, height + 0.01f);
        });
        Assert.Equal(expectedColumns, bounds.Select(rect => rect.Y).Count(y => Math.Abs(y - bounds[0].Y) < 0.01f));
        float cellWidth = (width - 6 * (expectedColumns - 1)) / expectedColumns;
        Assert.All(bounds, rect => Assert.Equal(cellWidth, rect.Width, 2));
        if (count % expectedColumns != 0)
            Assert.Equal(width / 2f, (bounds[^1].X + bounds[^1].Right) / 2f, 2);
    }

    [Fact]
    public void HiddenSectionGivesItsRowToTheOtherOne()
    {
        var shields = new Border();
        var weapons = new Border();
        var grid = new UniformGrid { Columns = 1 };
        grid.AddChild(shields);
        grid.AddChild(weapons);
        var tree = new VisualTree(grid, new Recorder());
        tree.ArrangeViewport(new RectangleF(0, 0, 200, 100), 1);
        Assert.Equal(50, weapons.Bounds.Height, 2);

        shields.Visible = false;
        tree.ArrangeViewport(new RectangleF(0, 0, 200, 100), 1);
        Assert.Equal(new RectangleF(0, 0, 200, 100), weapons.Bounds);
    }

    sealed class Recorder : IUiRenderer, IDynamicTextureFactory, ITextureResolver
    {
        public readonly List<(RectangleF Bounds, Color Color)> Fills = new();
        public Recorder() { BorderTextures = new BorderTextureCache(this); }
        public ITextMetrics TextMetrics => null;
        public ITextureResolver Textures => this;
        public IDynamicTextureFactory DynamicTextures => this;
        public BorderTextureCache BorderTextures { get; }
        public TextureSource MissingTexture => default;
        public bool IsAvailable(TextureSource source) => true;
        public bool TryGetSize(TextureSource source, out Vector2 size) { size = Vector2.Zero; return false; }
        public bool TryResolve(TextureSource source, out object value) { value = source.Key; return true; }
        public Vector2I FitToSupportedSize(Vector2I requested) => requested;
        public IDynamicTexture Create(string name, Vector2I size) => null;
        public IDisposable PushClip(RectangleF bounds) => null;
        public void FillRectangle(RectangleF bounds, Color color) => Fills.Add((bounds, color));
        public void DrawTexture(TextureSource texture, RectangleF bounds, Color color, float rotation) { }
        public void DrawText(string font, string text, Vector2 position, float scale, Color color,
            HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) { }
    }
}

public sealed class DefenseViewboxContentTests
{
    [Fact]
    public void ViewboxDrawsAFixedSizeGridWithIconAndRows()
    {
        var grid = new Adk.Gui.Layout.Grid { Width = 150, Height = 43 };
        grid.SetColumnDefinitions(Adk.Gui.Layout.GridLength.Auto, new Adk.Gui.Layout.GridLength(1, Adk.Gui.Layout.GridUnitType.Star));
        var icon = new Adk.Gui.Controls.Image { Source = "Textures\\Icon.dds", Width = 43, Height = 43 };
        var rows = new Adk.Gui.Layout.StackPanel { VerticalAlignment = VerticalAlignment.Center };
        rows.AddChild(new Adk.Gui.Controls.TextBlock { Text = "(2) Ready", Height = 10.75f });
        grid.Set(icon, 0, 0);
        grid.Set(rows, 1, 0);
        var viewbox = new Adk.Gui.Layout.Viewbox { Child = grid };
        var root = new Adk.Gui.Layout.Grid();
        root.SetRowDefinitions(new Adk.Gui.Layout.GridLength(22, Adk.Gui.Layout.GridUnitType.Star), new Adk.Gui.Layout.GridLength(66, Adk.Gui.Layout.GridUnitType.Star), new Adk.Gui.Layout.GridLength(12, Adk.Gui.Layout.GridUnitType.Star));
        root.Set(viewbox, 0, 1);
        using var host = new Adk.Gui.Lcd.LcdGuiHost(
            System.Reflection.DispatchProxy.Create<Sandbox.ModAPI.IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(),
            new Adk.Gui.Controls.Window { Content = root });
        host.Arrange(new RectangleF(0, 0, 200, 120));
        var sprites = new List<VRage.Game.GUI.TextPanel.MySprite>();
        host.Draw(sprites);
        Assert.True(viewbox.Bounds.Width > 0 && viewbox.Bounds.Height > 0, "viewbox " + viewbox.Bounds);
        Assert.True(grid.Bounds.Width > 0, "grid " + grid.Bounds + " desired " + grid.DesiredSize);
        string dump = string.Join("\n", sprites.Select(s => s.Type + " " + s.Data + " " + s.Position + " " + s.Size + " " + s.RotationOrScale)) +
                      "\ngrid " + grid.Bounds + " icon " + icon.Bounds + " rows " + rows.Bounds + " viewbox " + viewbox.Bounds;
        Assert.True(sprites.Any(s => s.Type == VRage.Game.GUI.TextPanel.SpriteType.TEXT && s.Data == "(2) Ready"), dump);
        Assert.True(sprites.Any(s => s.Type == VRage.Game.GUI.TextPanel.SpriteType.TEXTURE), dump);
    }
}

public sealed class DefenseDashboardXamlTests
{
    sealed class Dashboard : Adk.Gui.Core.Binding.ObservableObject
    {
        public List<object> Shields { get; } = new();
        public List<Weapon> Weapons { get; } = new() { new Weapon() };
        public bool HasShields => false;
        public bool HasWeapons => true;
    }

    sealed class Weapon : Adk.Gui.Core.Binding.ObservableObject
    {
        public Color CardColor => new Color(40, 80, 120);
        public Color ShadowColor => new Color(8, 16, 24);
        public Color Foreground => Color.White;
        public Color TrackColor => Color.Gray;
        public string Title => "Gatling Turret";
        public string Icon => "Textures\\GUI\\Icons\\Cubes\\gatling_turret.dds";
        public float TitleFontSize => .56f;
        public List<Row> Rows { get; } = new() { new Row() };
        public IReadOnlyList<LcdMod.Client.Apps.Controls.BarSegment> Segments { get; } =
            new[] { new LcdMod.Client.Apps.Controls.BarSegment(2, Color.Green) };
        public float Total => 2;
    }

    sealed class Row : Adk.Gui.Core.Binding.ObservableObject
    {
        public string Text => "(2) Ready";
        public Color DotColor => new Color(40, 80, 120);
        public Color Foreground => Color.White;
        public float FontSize => .5f;
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.6f)]
    public void WeaponCardsDrawIconAndStatusRows(float scaling)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string repo = directory!.FullName;
        var previousLoader = Adk.Gui.Styling.StyleSheet.SourceLoader;
        Adk.Gui.Styling.StyleSheet.SourceLoader = source =>
        {
            source = source.TrimStart('/');
            return File.ReadAllText(source.StartsWith("Data/Gui/Styles/Adk/", StringComparison.Ordinal)
                ? Path.Combine(repo, "External/Adk/Adk.Gui/Styles", source.Substring("Data/Gui/Styles/Adk/".Length))
                : Path.Combine(repo, "Arthur-s-Lcd-Mod/Content", source));
        };
        try
        {
            const string ns = "LcdMod.Client.Apps.DefenseDashboard.";
            var context = new Adk.Gui.Markup.HotReloadContext(Adk.Gui.Styling.ControlTemplate.CreateTypeRegistry());
            context.Getter(ns + "DefenseDashboardViewModel", "Shields", s => ((Dashboard)s).Shields);
            context.Getter(ns + "DefenseDashboardViewModel", "Weapons", s => ((Dashboard)s).Weapons);
            context.Getter(ns + "DefenseDashboardViewModel", "HasShields", s => ((Dashboard)s).HasShields);
            context.Getter(ns + "DefenseDashboardViewModel", "HasWeapons", s => ((Dashboard)s).HasWeapons);
            foreach (string name in new[] { "CardColor", "ShadowColor", "Foreground", "TrackColor", "Title", "Icon", "TitleFontSize", "Rows", "Segments", "Total" })
            {
                string property = name;
                context.Getter(ns + "WeaponStatusEntry", property, s => typeof(Weapon).GetProperty(property)!.GetValue(s));
            }
            foreach (string name in new[] { "Text", "DotColor", "Foreground", "FontSize" })
            {
                string property = name;
                context.Getter(ns + "WeaponStatusRow", property, s => typeof(Row).GetProperty(property)!.GetValue(s));
            }
            string xaml = File.ReadAllText(Path.Combine(repo, "Arthur-s-Lcd-Mod/Client/Apps/DefenseDashboard/DefenseDashboardView.xaml"));
            var view = new Adk.Gui.Controls.UserControl();
            var builder = new Adk.Gui.Markup.HotReloadViewBuilder(context);
            var content = builder.BuildRoot(Adk.Gui.Markup.UiXmlLoader.ParseNode(xaml), view);
            builder.ApplyRoot(view);
            view.Content = content;
            view.DataContext = new Dashboard();

            var window = new Adk.Gui.Controls.Window { Content = view, Styles = Adk.Gui.Styling.StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/FluentStyle.xaml") };
            window.Resources.Add("textFont", "White");
            window.Resources.Add("fontScale", 1f);
            window.Resources.Add("MinimumContrast", new LcdMod.Client.Apps.Views.MinimumContrastConverter());
            using var host = new Adk.Gui.Lcd.LcdGuiHost(
                System.Reflection.DispatchProxy.Create<Sandbox.ModAPI.IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), window)
            { RenderScaling = scaling };
            host.Arrange(new RectangleF(0, 0, 512, 300));
            var sprites = new List<VRage.Game.GUI.TextPanel.MySprite>();
            host.Draw(sprites);

            string dump = string.Join("\n", sprites.Select(s => s.Type + " " + s.Data + " " + s.Position + " " + s.Size + " " + s.RotationOrScale)) +
                          "\n" + Describe(view, 0);
            Assert.True(sprites.Any(s => s.Data == "(2) Ready"), dump);
            var card = new Color(40, 80, 120);
            var dot = Nodes(view).OfType<Adk.Gui.Controls.Border>().Single(border => border.StyleClass == "weapon-dot");
            Assert.True(LcdMod.Client.Extensions.ColorExtensions.ContrastRatio(dot.BackgroundColor, card) >= 3.0,
                "Firing dot " + dot.BackgroundColor + " blends into card " + card);
            Assert.True(sprites.Any(s => s.Type == VRage.Game.GUI.TextPanel.SpriteType.TEXTURE && s.Data == "Circle" &&
                                         s.Color == Color.Green), "Segment bar missing\n" + dump);
            RectangleF? clip = null;
            foreach (var sprite in sprites)
            {
                if (sprite.Type == VRage.Game.GUI.TextPanel.SpriteType.CLIP_RECT)
                    clip = sprite.Position.HasValue && sprite.Size.HasValue
                        ? new RectangleF(sprite.Position.Value, sprite.Size.Value)
                        : (RectangleF?)null;
                else if ((sprite.Data == "(2) Ready" || sprite.Type == VRage.Game.GUI.TextPanel.SpriteType.TEXTURE &&
                             (sprite.Data == "MissingIcon" || sprite.Data.Contains("gatling"))) && clip.HasValue)
                    Assert.True(clip.Value.Contains(sprite.Position.Value), "Clipped by " + clip + "\n" + dump);
            }
            Assert.True(sprites.Any(s => s.Type == VRage.Game.GUI.TextPanel.SpriteType.TEXTURE &&
                                         (s.Data == "MissingIcon" || s.Data.Contains("gatling"))), dump);
        }
        finally
        {
            Adk.Gui.Styling.StyleSheet.SourceLoader = previousLoader;
        }
    }

    static IEnumerable<VisualElement> Nodes(VisualElement node)
    {
        yield return node;
        foreach (var child in node.LogicalChildren)
            foreach (var descendant in Nodes(child)) yield return descendant;
    }

    static string Describe(VisualElement element, int depth)
    {
        var text = new string(' ', depth * 2) + element.GetType().Name + " " + element.Bounds + " visible=" + element.Visible + "\n";
        foreach (var child in element.VisualChildren)
            text += Describe(child, depth + 1);
        return text;
    }
}

[Collection(LcdTextureHooks.Name)]
public sealed class TextureResolverTests
{
    [Fact]
    public void TexturesDefinedAfterTheSurfaceSnapshotResolveThroughTheHost()
    {
        var resolver = new Adk.Gui.Lcd.LcdTextureResolver(
            System.Reflection.DispatchProxy.Create<Sandbox.ModAPI.IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>());
        var icon = Adk.Gui.Rendering.TextureSource.FromKey("MyObjectBuilder_LargeGatlingTurret/");
        var previous = Adk.Gui.Lcd.LcdTextureResolver.IsTextureDefined;
        try
        {
            Adk.Gui.Lcd.LcdTextureResolver.IsTextureDefined = null;
            Assert.False(resolver.IsAvailable(icon));
            Adk.Gui.Lcd.LcdTextureResolver.IsTextureDefined = name => name == "MyObjectBuilder_LargeGatlingTurret/";
            Assert.True(resolver.IsAvailable(icon));
            Assert.False(resolver.IsAvailable(Adk.Gui.Rendering.TextureSource.FromKey("Undefined")));
        }
        finally
        {
            Adk.Gui.Lcd.LcdTextureResolver.IsTextureDefined = previous;
        }
    }
}
