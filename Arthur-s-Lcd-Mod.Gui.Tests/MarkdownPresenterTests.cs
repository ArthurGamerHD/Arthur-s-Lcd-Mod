using Adk.Gui.Styling;
using LcdMod.Client.Apps.Markdown;
using LcdMod.Client.Markdown;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class MarkdownPresenterTests
{
    static List<MySprite> Render(string markdown)
    {
        var harness = new DialogHarness(500, 600);
        var presenter = new MarkdownPresenter
        {
            Styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/Controls/Markdown.xaml"),
            Document = new MarkdownParser().Parse(markdown)
        };
        harness.Owner.Content = presenter;
        harness.Host.Arrange(new RectangleF(0, 0, 500, 600));
        using (harness)
        {
            harness.Draw();
            return harness.Draw();
        }
    }

    static List<MySprite> Texts(List<MySprite> sprites) => sprites.Where(s => s.Type == SpriteType.TEXT).ToList();

    [Fact]
    public void InlineMarkersBecomeStylesInsteadOfLiteralText()
    {
        var texts = Texts(Render("Some **bold** and *italic* words"));
        Assert.DoesNotContain(texts, s => s.Data.Contains("**") || s.Data.Contains('*'));
        Assert.Equal("White-Bold", texts.Single(s => s.Data.StartsWith("bold")).FontId);
        Assert.Equal("White-Italic", texts.Single(s => s.Data.StartsWith("italic")).FontId);
        Assert.Equal("White", texts.Single(s => s.Data.StartsWith("Some")).FontId);
    }

    [Fact]
    public void HeadingsAreAccentColouredAndLargerThanParagraphs()
    {
        var texts = Texts(Render("# Title\n\nBody"));
        MySprite title = texts.Single(s => s.Data == "Title");
        MySprite body = texts.Single(s => s.Data == "Body");
        Assert.Equal(DialogHarness.Accent, title.Color);
        Assert.Equal(DialogHarness.OnSurface, body.Color);
        Assert.True(title.RotationOrScale > body.RotationOrScale);
        Assert.True(body.Position!.Value.Y > title.Position!.Value.Y);
    }

    [Fact]
    public void ListsGetBulletsAndNumbersAndQuotesGetABar()
    {
        var sprites = Render("- one\n- two\n\n1. first\n2. second\n\n> quoted");
        var texts = Texts(sprites);
        Assert.Equal(2, texts.Count(s => s.Data == "•"));
        Assert.Contains(texts, s => s.Data == "1.");
        Assert.Contains(texts, s => s.Data == "2.");
        MySprite quoted = texts.Single(s => s.Data == "quoted");
        Assert.Contains(sprites, s => s.Type == SpriteType.TEXTURE && s.Color == DialogHarness.Accent &&
                                      s.Position!.Value.X < quoted.Position!.Value.X);
        MySprite bullet = texts.First(s => s.Data == "•");
        Assert.True(texts.Single(s => s.Data == "one").Position!.Value.X > bullet.Position!.Value.X);
    }

    [Fact]
    public void ColorSpansOverrideTheThemeColour()
    {
        var texts = Texts(Render("[color:#FF00FF00]green[/color] text"));
        Assert.Equal(new Color(0, 255, 0, 255), texts.Single(s => s.Data.StartsWith("green")).Color);
    }
}

public sealed class FarmPlotFlyoutTests
{
    [Fact]
    public void ThePlotFlyoutInheritsTheViewStylesAndStaysOpenWhenClickedInside()
    {
        using var harness = new DialogHarness(500, 400);
        var plot = new LcdMod.Client.Apps.Farm.FarmPlotButton
        {
            Width = 120,
            Height = 80,
            HorizontalAlignment = Adk.Gui.Core.HorizontalAlignment.Left,
            VerticalAlignment = Adk.Gui.Core.VerticalAlignment.Top,
            Title = "Farm Plot 7",
            Details = new MarkdownParser().Parse("**Wheat** growing\n\n- 42%")
        };
        var styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/Controls/Farm.xaml");
        var view = new Adk.Gui.Layout.Grid { Styles = styles };
        view.AddChild(plot);
        harness.Owner.Content = view;
        harness.Host.Arrange(new RectangleF(0, 0, 500, 400));
        Assert.DoesNotContain("Farm Plot 7", harness.Texts());

        harness.Click(plot);
        var sprites = harness.Draw();
        MySprite title = sprites.Single(s => s.Type == SpriteType.TEXT && s.Data == "Farm Plot 7");
        Assert.Equal(DialogHarness.Accent, title.Color);
        Assert.Contains(sprites, s => s.Type == SpriteType.TEXTURE && s.Color == DialogHarness.Surface &&
                                      s.Position!.Value.Y > plot.Bounds.Bottom);
        var texts = sprites.Where(s => s.Type == SpriteType.TEXT).Select(s => s.Data).ToList();
        Assert.Contains(texts, t => t.StartsWith("Wheat"));
        Assert.Contains("42%", texts);
        MySprite background = sprites.Where(s => s.Type == SpriteType.TEXTURE && s.Color == DialogHarness.Surface &&
                                                  s.Position!.Value.Y > plot.Bounds.Bottom)
            .OrderByDescending(s => s.Size!.Value.X * s.Size!.Value.Y).First();
        Assert.True(background.Size!.Value.X > 150f && background.Size!.Value.Y > 60f, "the flyout must be sized to its content");
        MySprite wheat = sprites.Single(s => s.Type == SpriteType.TEXT && s.Data.StartsWith("Wheat"));
        MySprite percent = sprites.Single(s => s.Type == SpriteType.TEXT && s.Data == "42%");
        Assert.True(wheat.Position!.Value.Y > title.Position!.Value.Y + 10f);
        Assert.True(percent.Position!.Value.Y > wheat.Position!.Value.Y + 10f);

        harness.Host.MovePointer(title.Position!.Value + new Vector2(4, 4));
        harness.Host.ClickPrimary();
        Assert.Contains("Farm Plot 7", harness.Texts());

        harness.Host.MovePointer(new Vector2(480, 380));
        harness.Host.ClickPrimary();
        Assert.DoesNotContain("Farm Plot 7", harness.Texts());
    }
}
