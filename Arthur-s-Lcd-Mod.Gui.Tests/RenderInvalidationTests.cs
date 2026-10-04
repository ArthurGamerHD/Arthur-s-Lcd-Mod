using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Lcd;
using Adk.Gui.Layout;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class RenderInvalidationTests
{
    static LcdGuiHost CreateHost(VisualElement root)
    {
        var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), root);
        host.Arrange(new RectangleF(0, 0, 400, 300));
        host.Draw(new List<MySprite>());
        return host;
    }

    [Fact]
    public void NestedRenderChangeReachesTheRoot()
    {
        var text = new TextBlock { Text = "a" };
        var panel = new StackPanel();
        panel.Add(new Border { Child = text });
        var root = new Border { Child = panel };
        using var host = CreateHost(root);
        Assert.False(host.NeedsRender);

        text.Foreground = Color.Red;
        Assert.NotEqual(UiInvalidation.None, root.PendingInvalidation & UiInvalidation.Render);
        Assert.True(host.NeedsRender);
        host.Draw(new List<MySprite>());
        Assert.False(host.NeedsRender);
    }

    [Fact]
    public void ChangesInsideAHiddenSubtreeDoNotRedraw()
    {
        var text = new TextBlock { Text = "a" };
        var hidden = new Border { Child = text, Visible = false };
        var root = new StackPanel();
        root.Add(new TextBlock { Text = "shown" });
        root.Add(hidden);
        using var host = CreateHost(root);

        text.Foreground = Color.Red;
        Assert.False(host.NeedsRender);

        hidden.Visible = true;
        Assert.True(host.NeedsRender);
        var sprites = new List<MySprite>();
        host.Draw(sprites);
        Assert.Contains(sprites, sprite => sprite.Data == "a" && sprite.Color == Color.Red);
    }

    [Fact]
    public void SpriteCanvasRedrawsEveryFrame()
    {
        int calls = 0;
        var canvas = new LcdSpriteCanvas
        {
            SpriteProvider = (context, bounds) =>
            {
                calls++;
                return new[] { new MySprite(SpriteType.TEXT, calls.ToString()) };
            }
        };
        using var host = CreateHost(new Border { Child = canvas });
        Assert.True(host.NeedsRender);
        var sprites = new List<MySprite>();
        host.Draw(sprites);
        Assert.Equal(2, calls);
        Assert.Contains(sprites, sprite => sprite.Data == "2");
    }

    [Fact]
    public void ReusedFrameMatchesTheTree()
    {
        var root = new StackPanel();
        root.Add(new TextBlock { Text = "a" });
        root.Add(new Button { Content = "b" });
        using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), root);
        host.Arrange(new RectangleF(0, 0, 400, 300));
        var first = new List<MySprite>();
        host.Draw(first);
        var second = new List<MySprite> { new MySprite(SpriteType.TEXTURE, "Background") };
        host.Draw(second);
        Assert.Equal(first, second.Skip(1));
    }

    [Fact]
    public void ResizingRedraws()
    {
        var root = new Border { Background = new Adk.Gui.Styling.SolidColorBrush(Color.Red) };
        using var host = CreateHost(root);
        host.Arrange(new RectangleF(0, 0, 200, 100));
        Assert.True(host.NeedsRender);
    }
}

public sealed class ItemsControlInvalidationTests
{
    [Fact]
    public void ReassigningAnUnchangedPlainListDoesNotInvalidate()
    {
        var items = new List<string> { "a", "b" };
        var control = new ItemsControl
        {
            ItemTemplate = new Adk.Gui.Styling.DataTemplate(item => new TextBlock { Text = (string)item }),
            ItemsSource = items
        };
        using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), control);
        host.Arrange(new RectangleF(0, 0, 400, 300));
        host.Draw(new List<MySprite>());

        control.ItemsSource = items;
        Assert.False(host.NeedsRender);

        items.Add("c");
        control.ItemsSource = items;
        Assert.True(host.NeedsRender);
        var sprites = new List<MySprite>();
        host.Draw(sprites);
        Assert.Contains(sprites, sprite => sprite.Data == "c");

        items.Reverse();
        control.ItemsSource = items;
        Assert.True(host.NeedsRender);
    }
}

public sealed class FooterControlTests
{
    static LcdGuiHost CreateHost(VisualElement root)
    {
        var window = new Window { WindowState = WindowState.FullScreen, Content = root, Styles = Adk.Gui.Styling.StyleSheet.LoadFromSource("/Data/Gui/Styles/Adk/Fluent/FluentStyle.xaml") };
        var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), window);
        host.Arrange(new RectangleF(0, 0, 400, 300));
        host.Draw(new List<MySprite>());
        return host;
    }

    [Fact]
    public void ComboBoxHeaderOpensAndSelects()
    {
        string selected = null;
        var combo = new ComboBox<string>(new[] { "Buy", "Sell", "Both" }, value => value, value => selected = value)
        {
            Width = 120,
            Height = 28,
            OpenDirection = ComboBoxOpenDirection.Down,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var previousLoader = Adk.Gui.Styling.StyleSheet.SourceLoader;
        Adk.Gui.Styling.StyleSheet.SourceLoader = path => File.ReadAllText(Path.Combine(RepoRoot(), "External/Adk/Adk.Gui/Styles",
            path.TrimStart('/').Substring("Data/Gui/Styles/Adk/".Length)));
        try
        {
            using var host = CreateHost(combo);
            host.MovePointer(combo.Bounds.Center);
            host.ClickPrimary();
            host.Draw(new List<MySprite>());
            Assert.True(combo.IsOpen);

            host.MovePointer(new Vector2(combo.Bounds.Center.X, combo.Bounds.Bottom + combo.OptionHeight * 1.5f));
            host.ClickPrimary();
            Assert.Equal("Sell", selected);
        }
        finally
        {
            Adk.Gui.Styling.StyleSheet.SourceLoader = previousLoader;
        }
    }

    [Fact]
    public void TextArrangedAtItsMeasuredWidthIsNotTrimmed()
    {
        var text = new TextBlock { Text = "Name", TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Orientation = StackOrientation.Horizontal };
        panel.Add(new Border { Child = text, Margin = new Vector4(0.7f, 0f, -0.7f, 0f) });
        var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), panel);
        host.Arrange(new RectangleF(0, 0, 400, 300));
        var sprites = new List<MySprite>();
        host.Draw(sprites);
        Assert.Contains(sprites, sprite => sprite.Data == "Name");
        host.Dispose();
    }

    static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }
}

public sealed class OverlayStyleTests
{
    [Fact]
    public void FluentButtonsShowTheHandCursor()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string root = directory!.FullName;
        var previousLoader = Adk.Gui.Styling.StyleSheet.SourceLoader;
        Adk.Gui.Styling.StyleSheet.SourceLoader = path => File.ReadAllText(Path.Combine(root, "External/Adk/Adk.Gui/Styles",
            path.TrimStart('/').Substring("Data/Gui/Styles/Adk/".Length)));
        try
        {
            var button = new Button { Content = "Go", Width = 80, Height = 30, HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top };
            var window = new Window { WindowState = WindowState.FullScreen, Content = button, Styles = Adk.Gui.Styling.StyleSheet.LoadFromSource("/Data/Gui/Styles/Adk/Fluent/FluentStyle.xaml") };
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), window);
            host.Arrange(new RectangleF(0, 0, 400, 300));
            host.Draw(new List<MySprite>());
            host.MovePointer(button.Bounds.Center);
            Assert.Equal(Adk.Gui.Core.Input.PointerCursor.Hand, host.PointerCursor);
            host.MovePointer(new Vector2(300, 250));
            Assert.NotEqual(Adk.Gui.Core.Input.PointerCursor.Hand, host.PointerCursor);
        }
        finally
        {
            Adk.Gui.Styling.StyleSheet.SourceLoader = previousLoader;
        }
    }

    [Fact]
    public void ComboBoxPopupKeepsItemsInsideItsCornersAndUsesTheForeground()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string root = directory!.FullName;
        var previousLoader = Adk.Gui.Styling.StyleSheet.SourceLoader;
        Adk.Gui.Styling.StyleSheet.SourceLoader = path => File.ReadAllText(Path.Combine(root, "External/Adk/Adk.Gui/Styles",
            path.TrimStart('/').Substring("Data/Gui/Styles/Adk/".Length)));
        try
        {
            var combo = new ComboBox<string>(new[] { "Buy", "Sell", "Both" }, value => value, value => { })
            {
                Width = 120, Height = 28, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left
            };
            var window = new Window { WindowState = WindowState.FullScreen, Content = combo, Styles = Adk.Gui.Styling.StyleSheet.LoadFromSource("/Data/Gui/Styles/Adk/Fluent/FluentStyle.xaml") };
            window.Resources.Add("surfaceColor", Color.Gray);
            window.Resources.Add("onSurfaceColor", Color.Cyan);
            window.Resources.Add("accentContainerColor", Color.Orange);
            var popupBackground = new Color(10, 20, 30, 230);
            window.Resources.Add("popupBackgroundColor", popupBackground);
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), window);
            host.Arrange(new RectangleF(0, 0, 400, 300));
            host.Draw(new List<MySprite>());
            host.MovePointer(combo.Bounds.Center);
            host.ClickPrimary();
            var sprites = new List<MySprite>();
            host.Draw(sprites);

            Assert.All(new[] { "Sell", "Both" }, label => Assert.Equal(Color.Cyan, sprites.Single(sprite => sprite.Data == label).Color));
            var popupBounds = ((Adk.Gui.Core.IOverlayProvider)combo).OverlayElement.Bounds;
            Assert.Contains(sprites, sprite => sprite.Color == popupBackground && sprite.Position.HasValue &&
                                               Math.Abs(sprite.Position.Value.Y - popupBounds.Center.Y) < 1);
            Assert.DoesNotContain(sprites, sprite => sprite.Data == "SquareSimple" && sprite.Color == Color.Gray &&
                                                     sprite.Position.Value.Y > combo.Bounds.Bottom);

            // Hover the first and last items: their square highlight stays clear of the popup's rounded corners.
            var popup = ((Adk.Gui.Core.IOverlayProvider)combo).OverlayElement.Bounds;
            float popupTop = popup.Y;
            float popupBottom = popup.Bottom;
            foreach (float y in new[] { popupTop + combo.CornerRadius.X + combo.OptionHeight * .5f + 1,
                         popupBottom - combo.CornerRadius.W - combo.OptionHeight * .5f - 1 })
            {
                host.MovePointer(new Vector2(combo.Bounds.Center.X, y));
                sprites.Clear();
                host.Draw(sprites);
                var highlight = sprites.Single(sprite => sprite.Data == "SquareSimple" && sprite.Color == Color.Orange);
                float top = highlight.Position.Value.Y - highlight.Size.Value.Y / 2;
                float bottom = highlight.Position.Value.Y + highlight.Size.Value.Y / 2;
                Assert.True(top >= popupTop + combo.CornerRadius.X - .01f, "highlight top " + top);
                Assert.True(bottom <= popupBottom - combo.CornerRadius.W + .01f, "highlight bottom " + bottom + " popup bottom " + popupBottom);
            }
        }
        finally
        {
            Adk.Gui.Styling.StyleSheet.SourceLoader = previousLoader;
        }
    }

    [Fact]
    public void ComboBoxItemsInheritOwnerStylesForHoverAndInvalidateTheTree()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string root = directory!.FullName;
        var previousLoader = Adk.Gui.Styling.StyleSheet.SourceLoader;
        Adk.Gui.Styling.StyleSheet.SourceLoader = path => File.ReadAllText(Path.Combine(root, "External/Adk/Adk.Gui/Styles",
            path.TrimStart('/').Substring("Data/Gui/Styles/Adk/".Length)));
        try
        {
            var combo = new ComboBox<string>(new[] { "Buy", "Sell", "Both" }, value => value, value => { })
            {
                Width = 120, Height = 28, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left
            };
            var window = new Window { WindowState = WindowState.FullScreen, Content = combo, Styles = Adk.Gui.Styling.StyleSheet.LoadFromSource("/Data/Gui/Styles/Adk/Fluent/FluentStyle.xaml") };
            window.Resources.Add("surfaceColor", Color.Gray);
            window.Resources.Add("accentContainerColor", Color.Orange);
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), window);
            host.Arrange(new RectangleF(0, 0, 400, 300));
            host.Draw(new List<MySprite>());
            host.MovePointer(combo.Bounds.Center);
            host.ClickPrimary();
            host.Draw(new List<MySprite>());
            Assert.True(combo.IsOpen);

            var item = new Vector2(combo.Bounds.Center.X, combo.Bounds.Bottom + combo.OptionHeight * 1.5f);
            host.MovePointer(item);
            Assert.True(host.NeedsRender);
            var sprites = new List<MySprite>();
            host.Draw(sprites);
            Assert.Contains(sprites, sprite => sprite.Color == Color.Orange && sprite.Position.HasValue &&
                                               Math.Abs(sprite.Position.Value.Y - item.Y) < combo.OptionHeight);
        }
        finally
        {
            Adk.Gui.Styling.StyleSheet.SourceLoader = previousLoader;
        }
    }
}
