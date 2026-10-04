using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Controls;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

[Collection(LcdTextureHooks.Name)]
public sealed class ActionButtonStyleTests
{
    static readonly Color Disabled = new(70, 70, 70);

    [Fact]
    public void TheActionIconIsDrawnInsideTheButton()
    {
        var sprites = Render(new ActionButton { Title = "Doors", ActionIcon = "Circle", Width = 92, Height = 92 });
        MySprite icon = Assert.Single(sprites, s => s.Data == "Circle");
        Assert.True(icon.Size!.Value.X > 20 && icon.Size.Value.Y > 20, "icon size " + icon.Size);
        Assert.Contains(sprites, s => s.Type == SpriteType.TEXT && s.Data == "Doors");
    }

    [Fact]
    public void ALongTitleShrinksOnItsOwnWithoutResizingTheIcon()
    {
        var shortTitle = Render(new ActionButton { Title = "Doors", ActionIcon = "Circle", Width = 92, Height = 92 });
        var longTitle = Render(new ActionButton
            { Title = "Hangar doors and every light on deck", ActionIcon = "Circle", Width = 92, Height = 92 });
        Assert.Equal(Assert.Single(shortTitle, s => s.Data == "Circle").Size,
            Assert.Single(longTitle, s => s.Data == "Circle").Size);
        float shortScale = Assert.Single(shortTitle, s => s.Type == SpriteType.TEXT && s.Data == "Doors").RotationOrScale;
        float longScale = Assert.Single(longTitle, s => s.Type == SpriteType.TEXT && s.Data.StartsWith("Hangar")).RotationOrScale;
        Assert.True(longScale < shortScale * .7f, "long title scale " + longScale + " vs " + shortScale);
    }

    [Fact]
    public void TheConfiguredColorFillsThePanelOrOutlinesTheBorderStyle()
    {
        var red = new Color(200, 20, 20);
        var filled = Render(new ActionButton { Title = "Doors", ActionIcon = "Circle", PanelColor = red, Width = 92, Height = 92 });
        Assert.Contains(filled, s => s.Type == SpriteType.TEXTURE && s.Color == red && s.Size!.Value.X > 80);
        Assert.Equal(Color.White, Assert.Single(filled, s => s.Type == SpriteType.TEXT && s.Data == "Doors").Color);

        var outlined = Render(new ActionButton
            { Title = "Doors", ActionIcon = "Circle", PanelColor = red, Variant = "border", Width = 92, Height = 92 });
        Assert.Contains(outlined, s => s.Type == SpriteType.TEXTURE && s.Color == red);
        Assert.DoesNotContain(outlined, s => s.Type == SpriteType.TEXTURE && s.Color == red &&
                                             s.Size!.Value.X > 80 && s.Size.Value.Y > 80 && s.Data == "SquareSimple");

        var unconfigured = Render(new ActionButton { ActionIcon = "Plus", PanelColor = red, IsConfigured = false, Width = 92, Height = 92 });
        Assert.DoesNotContain(unconfigured, s => s.Color == red);
    }

    [Fact]
    public void TheIconTakesMostOfTheTile()
    {
        var sprites = Render(new ActionButton { Title = "Doors", Status = "Open", ActionIcon = "Circle", Width = 100, Height = 100 });
        var icon = Assert.Single(sprites, s => s.Data == "Circle");
        Assert.True(icon.Size!.Value.Y >= 50f, "icon " + icon.Size);
    }

    [Fact]
    public void AnUnconfiguredSlotShowsTheMutedPlusIcon()
    {
        var sprites = Render(new ActionButton { ActionIcon = "Plus", IsConfigured = false, Width = 92, Height = 92 });
        Assert.Equal(Disabled, Assert.Single(sprites, s => s.Data == "Plus").Color);
    }

    [Fact]
    public void AConfiguredSlotKeepsTheRegularForeground()
    {
        var sprites = Render(new ActionButton { ActionIcon = "Circle", Width = 92, Height = 92 });
        Assert.NotEqual(Disabled, Assert.Single(sprites, s => s.Data == "Circle").Color);
    }

    static List<MySprite> Render(ActionButton button)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string repo = directory!.FullName;
        var previousLoader = StyleSheet.SourceLoader;
        var previousDefined = LcdTextureResolver.IsTextureDefined;
        LcdTextureResolver.IsTextureDefined = name => name == "Plus";
        StyleSheet.SourceLoader = path =>
        {
            path = path.TrimStart('/');
            return File.ReadAllText(path.StartsWith("Data/Gui/Styles/Adk/")
                ? Path.Combine(repo, "External/Adk/Adk.Gui/Styles", path.Substring("Data/Gui/Styles/Adk/".Length))
                : Path.Combine(repo, "Arthur-s-Lcd-Mod/Content", path));
        };
        try
        {
            button.HorizontalAlignment = Adk.Gui.Core.HorizontalAlignment.Left;
            button.VerticalAlignment = Adk.Gui.Core.VerticalAlignment.Top;
            var root = new Window { WindowState = WindowState.FullScreen, Content = button, Styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/FluentStyle.xaml") };
            foreach (string key in new[] { "surfaceContainerColor", "onSurfaceColor", "borderVariantColor", "accentColor",
                         "accentContainerColor", "onAccentColor", "secondaryContainerColor", "fontColor",
                         "surfaceColor" })
                root.Resources.Add(key, Color.Gray);
            root.Resources.Add("disabledColor", Disabled);
            root.Resources.Add("textFont", "White");
            root.Resources.Add("fontScale", 1f);
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), root);
            host.Arrange(new RectangleF(0, 0, 400, 300));
            var sprites = new List<MySprite>();
            host.Draw(sprites);
            return sprites;
        }
        finally
        {
            StyleSheet.SourceLoader = previousLoader;
            LcdTextureResolver.IsTextureDefined = previousDefined;
        }
    }
}
