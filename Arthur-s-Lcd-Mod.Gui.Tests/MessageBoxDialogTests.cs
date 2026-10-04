using Adk.Gui.Controls;
using LcdMod.Client.Apps.Dialogs;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

[Collection(LcdTextureHooks.Name)]
public sealed class MessageBoxDialogTests
{
    [Fact]
    public void ShowsTheMessageAndRunsTheChosenButtonAfterClosing()
    {
        using var harness = new DialogHarness();
        string chosen = null;
        var model = new MessageBoxViewModel("Delete file", "Delete song.wav?", "Delete", () => chosen = "delete",
            "Cancel", () => chosen = "cancel", "Danger");
        var dialog = harness.Show(new MessageBoxView(model));

        var texts = harness.Texts();
        Assert.Contains("Delete file", texts);
        Assert.Contains("Delete song.wav?", texts);
        Assert.Contains("Owner content", texts);

        harness.Click(DialogHarness.ButtonWithText(dialog, "Delete"));
        Assert.Equal("delete", chosen);
        Assert.False(dialog.IsOpen);
        Assert.DoesNotContain("Delete file", harness.Texts());
    }

    [Fact]
    public void TheWindowSizesToItsContentWithinItsWidth()
    {
        using var harness = new DialogHarness(1000, 600);
        var dialog = harness.Show(new MessageBoxView(new MessageBoxViewModel("Title", "Message", "OK", null)));
        var frame = dialog.FrameBounds;
        Assert.Equal(440, frame.Width, 1);
        Assert.InRange(frame.Height, 60, 300);
        Assert.Equal(500, frame.Center.X, 1);
        Assert.Equal(300, frame.Center.Y, 1);
    }

    [Fact]
    public void ALongMessageScrollsInsideTheScreen()
    {
        using var harness = new DialogHarness(1000, 400);
        var text = string.Join(" ", Enumerable.Repeat("This message keeps going.", 200));
        var dialog = harness.Show(new MessageBoxView(new MessageBoxViewModel("Title", text, "OK", null)));
        Assert.Equal(376, dialog.FrameBounds.Height, 1);
        Assert.Contains("OK", harness.Texts());
    }

    [Fact]
    public void AThreeButtonBoxShowsItsButtonsInOrderWithOnlyThePrimaryStyled()
    {
        using var harness = new DialogHarness();
        string chosen = null;
        var model = new MessageBoxViewModel("Unsaved queue", "Save the queue first?", new[]
        {
            new DialogButton("Cancel", () => chosen = "cancel"),
            new DialogButton("Discard", () => chosen = "discard"),
            new DialogButton("Save", () => chosen = "save", true)
        });
        var dialog = harness.Show(new MessageBoxView(model));

        var buttons = new[] { "Cancel", "Discard", "Save" }.Select(text => DialogHarness.ButtonWithText(dialog, text)).ToList();
        Assert.True(buttons[0].Bounds.X < buttons[1].Bounds.X && buttons[1].Bounds.X < buttons[2].Bounds.X);
        Assert.Equal(new[] { "dialog-button", "dialog-button", "dialog-button primary" }, buttons.Select(b => b.StyleClass));

        harness.Click(buttons[1]);
        Assert.Equal("discard", chosen);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void ATextPromptReturnsTheEditedTextOnlyWhenConfirmed()
    {
        using var harness = new DialogHarness();
        string saved = null;
        var model = new TextPromptViewModel("Save playlist", "Name the playlist", "playlist_1", text => saved = text);
        var dialog = harness.Show(new TextPromptView(model));
        var box = DialogHarness.Find<TextBox>(dialog, _ => true);
        Assert.Equal("playlist_1", box.Text);

        box.Text = "Road trip";
        harness.Click(DialogHarness.ButtonWithText(dialog, "LcdMod_Common_Button_OK"));
        Assert.Equal("Road trip", saved);
        Assert.False(dialog.IsOpen);

        saved = null;
        var cancelled = harness.Show(new TextPromptView(new TextPromptViewModel("Save playlist", null, "x", text => saved = text)));
        harness.Click(DialogHarness.ButtonWithText(cancelled, "LcdMod_Common_Button_Cancel"));
        Assert.Null(saved);
        Assert.False(cancelled.IsOpen);
    }
}
