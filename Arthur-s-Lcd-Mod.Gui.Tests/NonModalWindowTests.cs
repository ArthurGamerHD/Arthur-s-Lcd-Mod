using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Core.Input;
using LcdMod.Client.Ftue;
using LcdMod.Client.Markdown;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class NonModalWindowTests
{
    sealed class Counter : ICommand
    {
        public int Count;
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => Count++;
    }

    [Fact]
    public void AShownWindowStaysOnTopWithoutBlockingOrDimmingItsOwner()
    {
        using var harness = new DialogHarness(500, 400);
        var clicks = new Counter();
        var button = new Button { Content = "Owner", Command = clicks, Width = 100, Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        harness.Owner.Content = button;
        var window = new Window { Width = 200, Height = 100, WindowState = WindowState.Normal,
            Content = new TextBlock { Text = "Floating" } };
        window.Show(harness.Owner);
        harness.Host.Arrange(new RectangleF(0, 0, 500, 400));

        var sprites = harness.Draw();
        Assert.Contains("Floating", sprites.Where(s => s.Type == SpriteType.TEXT).Select(s => s.Data));
        Assert.DoesNotContain(sprites, s => s.Type == SpriteType.TEXTURE && s.Size == new Vector2(500, 400));

        harness.Click(button);
        Assert.Equal(1, clicks.Count);

        harness.Host.MovePointer(window.FrameBounds.Center);
        Assert.True(harness.Host.OwnsPointer);
        harness.Host.ClickPrimary();
        Assert.True(window.IsOpen);
    }

    [Fact]
    public void TheHintIsDismissedByItsCloseButtonButNotWhenHidden()
    {
        using var harness = new DialogHarness(500, 400);
        harness.Owner.Content = new TextBlock { Text = "App" };
        harness.Host.Arrange(new RectangleF(0, 0, 500, 400));
        harness.Draw();
        int dismissed = 0;
        var hint = new FtueHintWindow(HintPlacement.Bottom)
        {
            Document = new MarkdownParser().Parse("# Farm\n\nClick a plot to see its **details**."),
            Dismissed = () => dismissed++
        };
        hint.ShowOver(harness.Owner);
        var texts = harness.Texts();
        Assert.Contains(texts, t => t.StartsWith("details"));
        Assert.True(hint.FrameBounds.Bottom > 300f, "a bottom hint sits at the bottom of the app");
        var sprites = harness.Draw();
        var closeButton = DialogHarness.Nodes(hint).OfType<Adk.Gui.Controls.Chrome.CaptionButton>().Single(b => b.Visible);
        var title = sprites.Single(s => s.Type == VRage.Game.GUI.TextPanel.SpriteType.TEXT && s.Data == "Farm");
        var description = sprites.First(s => s.Type == VRage.Game.GUI.TextPanel.SpriteType.TEXT && s.Data.StartsWith("Click"));
        Assert.True(title.Position!.Value.Y < closeButton.Bounds.Bottom, "the title shares the close button's row");
        Assert.False(closeButton.Bounds.Contains(title.Position!.Value), "the title does not overlap the close button");
        Assert.True(description.Position!.Value.Y > closeButton.Bounds.Bottom, "the description goes below the title row");
        var text = DialogHarness.Find<LcdMod.Client.Apps.Markdown.MarkdownPresenter>(hint, _ => true).Bounds;
        Assert.True(text.X >= hint.FrameBounds.X + 10f && text.Right <= hint.FrameBounds.Right - 10f &&
                    text.Bottom <= hint.FrameBounds.Bottom - 6f, "the hint text needs inner padding from the frame");

        Assert.True(hint.FrameBounds.Width > 450f, "the hint spans the app window");
        harness.Host.Arrange(new RectangleF(0, 0, 900, 400));
        harness.Draw();
        Assert.True(hint.FrameBounds.Width > 850f, "the hint follows the window when it is resized");

        hint.Hide();
        Assert.Equal(0, dismissed);
        Assert.DoesNotContain(harness.Texts(), t => t.StartsWith("details"));

        hint.ShowOver(harness.Owner);
        harness.Draw();
        var close = DialogHarness.Nodes(hint).OfType<Adk.Gui.Controls.Chrome.CaptionButton>().Single(b => b.Visible);
        harness.Click(close);
        Assert.Equal(1, dismissed);
        Assert.False(hint.IsOpen);
    }
}
