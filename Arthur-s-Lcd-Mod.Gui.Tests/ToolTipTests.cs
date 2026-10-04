using Adk.Gui.Controls;
using Adk.Gui.Controls.Documents;
using Adk.Gui.Controls.Primitives;
using Adk.Gui.Core;
using Adk.Gui.Layout;
using LcdMod.Common.Mvvm;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed partial class ToolTipView : UserControl
{
    public ToolTipView(ToolTipModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    public Border LiteralTarget => Literal;
    public Border BoundTarget => Bound;
    public Border SlowTarget => Slow;
    public Border PlainTarget => Plain;
}

public sealed class ToolTipModel : ObservableObject
{
    string _tip = "Bound tip";
    public string Tip { get => _tip; set => RaiseAndSetIfChanged(ref _tip, value, nameof(Tip)); }
}

public sealed class ToolTipTests
{
    static (DialogHarness Harness, ToolTipView View, ToolTipModel Model) Show()
    {
        var harness = new DialogHarness(400, 300);
        var model = new ToolTipModel();
        var view = new ToolTipView(model);
        harness.Owner.Content = view;
        harness.Host.Arrange(new RectangleF(0, 0, 400, 300));
        harness.Draw();
        return (harness, view, model);
    }

    [Fact]
    public void XamlAttachedPropertiesReachTheTarget()
    {
        var (harness, view, model) = Show();
        using var _ = harness;
        Assert.Equal("Static tip", ToolTip.GetTip(view.LiteralTarget));
        Assert.Equal(PlacementMode.Bottom, ToolTip.GetPlacement(view.LiteralTarget));
        Assert.Equal(0, ToolTip.GetShowDelay(view.LiteralTarget));
        Assert.Equal("Bound tip", ToolTip.GetTip(view.BoundTarget));
        model.Tip = "Changed";
        Assert.Equal("Changed", ToolTip.GetTip(view.BoundTarget));
    }

    [Fact]
    public void HoveringANonInteractiveElementShowsItsTipAndLeavingHidesIt()
    {
        var (harness, view, _) = Show();
        using var _h = harness;
        harness.Host.MovePointer(view.BoundTarget.Bounds.Center);
        Assert.Contains("Bound tip", harness.Texts());
        Assert.True(ToolTip.GetIsOpen(view.BoundTarget));

        harness.Host.MovePointer(view.PlainTarget.Bounds.Center);
        Assert.DoesNotContain("Bound tip", harness.Texts());
        Assert.False(ToolTip.GetIsOpen(view.BoundTarget));
    }

    [Fact]
    public void TheTipWaitsForItsDelayAndAClickDismissesIt()
    {
        var (harness, view, _) = Show();
        using var _h = harness;
        harness.Host.MovePointer(view.SlowTarget.Bounds.Center);
        Assert.DoesNotContain("Slow tip", harness.Texts());

        harness.Host.MovePointer(view.BoundTarget.Bounds.Center);
        Assert.Contains("Bound tip", harness.Texts());
        harness.Host.ClickPrimary();
        Assert.DoesNotContain("Bound tip", harness.Texts());
        harness.Host.MovePointer(view.BoundTarget.Bounds.Center + new Vector2(1, 0));
        Assert.DoesNotContain("Bound tip", harness.Texts());
    }

    [Fact]
    public void BottomPlacementPutsTheTipUnderItsTarget()
    {
        var (harness, view, _) = Show();
        using var _h = harness;
        harness.Host.MovePointer(view.LiteralTarget.Bounds.Center);
        MySprite tip = harness.Draw().Single(s => s.Type == SpriteType.TEXT && s.Data == "Static tip");
        Assert.True(tip.Position!.Value.Y >= view.LiteralTarget.Bounds.Bottom);
    }
}

public sealed class InlineTextTests
{
    static (DialogHarness Harness, TextBlock Text) Show(float width, params Inline[] inlines)
    {
        var harness = new DialogHarness(400, 300);
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Width = width, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top };
        foreach (Inline inline in inlines)
            text.Inlines.Add(inline);
        harness.Owner.Content = text;
        harness.Host.Arrange(new RectangleF(0, 0, 400, 300));
        return (harness, text);
    }

    [Fact]
    public void RunsKeepTheirStyleAndBoldUsesTheBoldFont()
    {
        var bold = new Bold();
        bold.Inlines.Add("strong ");
        var colored = new Span { Foreground = Color.Red };
        colored.Inlines.Add("red");
        var (harness, _) = Show(400, new Run("plain "), bold, colored);
        using var _h = harness;
        var sprites = harness.Draw().Where(s => s.Type == SpriteType.TEXT).ToList();
        Assert.Equal("White-Bold", sprites.Single(s => s.Data.StartsWith("strong")).FontId);
        Assert.Equal(Color.Red, sprites.Single(s => s.Data == "red").Color);
        Assert.Equal("White", sprites.Single(s => s.Data.StartsWith("plain")).FontId);
    }

    [Fact]
    public void TextWrapsWordByWordAcrossStyledRuns()
    {
        var (harness, text) = Show(120, new Run("alpha beta gamma delta epsilon zeta eta theta"));
        using var _h = harness;
        var sprites = harness.Draw().Where(s => s.Type == SpriteType.TEXT).ToList();
        Assert.True(sprites.Select(s => s.Position!.Value.Y).Distinct().Count() > 1, "long text must wrap onto several lines");
        Assert.All(sprites, s => Assert.True(s.Position!.Value.X < text.Bounds.Right));
        Assert.True(text.Bounds.Height > sprites.Max(s => s.Position!.Value.Y) - text.Bounds.Y);
    }

    [Fact]
    public void LineBreaksAndDecorationsAreHonoured()
    {
        var under = new Underline();
        under.Inlines.Add("under");
        var (harness, _) = Show(400, new Run("first"), new LineBreak(), under);
        using var _h = harness;
        var sprites = harness.Draw();
        var first = sprites.Single(s => s.Type == SpriteType.TEXT && s.Data == "first");
        var second = sprites.Single(s => s.Type == SpriteType.TEXT && s.Data == "under");
        Assert.True(second.Position!.Value.Y > first.Position!.Value.Y);
        Assert.Contains(sprites, s => s.Type == SpriteType.TEXTURE && s.Position!.Value.Y > second.Position!.Value.Y);
    }

    [Fact]
    public void InlineVisualsAreLaidOutInTheTextFlow()
    {
        var image = new Border { Width = 30, Height = 20, Background = new Adk.Gui.Styling.SolidColorBrush(Color.Blue) };
        var (harness, _) = Show(400, new Run("before "), new InlineUIContainer(image), new Run(" after"));
        using var _h = harness;
        var sprites = harness.Draw();
        float before = sprites.Single(s => s.Type == SpriteType.TEXT && s.Data.StartsWith("before")).Position!.Value.X;
        float after = sprites.Single(s => s.Type == SpriteType.TEXT && s.Data.Contains("after")).Position!.Value.X;
        Assert.InRange(image.Bounds.X, before, after);
        Assert.Equal(new Vector2(30, 20), image.Bounds.Size);
    }
}
