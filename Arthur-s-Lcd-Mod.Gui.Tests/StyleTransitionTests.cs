using Adk.Gui.Controls;
using LcdMod.Client.Apps.Dialogs;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

// SE skips a generated texture in the frame it is created, so a hover that needs a new rounded texture used to show
// the previous look for a frame. Hover colors now only tint cached masks.
[Collection(LcdTextureHooks.Name)]
public sealed class StyleTransitionTests
{
    [Fact]
    public void HoveringRowsShowsTheHoverLookInTheFirstFrameWithoutNewTextures()
    {
        using var textures = new FakeGeneratedTextures();
        using var harness = new DialogHarness(600, 520);
        var root = new FolderModel { Name = "Music" };
        root.Files.Add(new FileModel { Name = "a.wav" });
        root.Files.Add(new FileModel { Name = "b.wav" });
        var dialog = harness.Show(new FilePickerView(new FilePickerViewModel("Pick", FilePickerMode.PickFile,
            new[] { root }, _ => { }, "Music")));
        var outline = new Color(90, 160, 220);
        dialog.Resources.Add("borderColor", outline);
        var first = DialogHarness.ButtonWithText(dialog, "a.wav");
        var second = DialogHarness.ButtonWithText(dialog, "b.wav");
        harness.Draw();

        harness.Host.MovePointer(first.Bounds.Center);
        var firstFrame = Look(RowSprites(harness.Draw(), first));
        var settled = Look(RowSprites(harness.Draw(), first));
        Assert.Equal(settled, firstFrame);
        Assert.Contains(settled, look => look.EndsWith(outline.ToString()));

        harness.Host.MovePointer(new Vector2(2, 2));
        harness.Draw();
        int created = FakeGeneratedTextures.Textures.Count;

        harness.Host.MovePointer(second.Bounds.Center);
        var next = RowSprites(harness.Draw(), second);
        Assert.Equal(created, FakeGeneratedTextures.Textures.Count);
        Assert.Equal(Look(RowSprites(harness.Draw(), second)), Look(next));
        Assert.Contains(next, s => s.Data.Contains("_ring") && s.Color == outline);
        Assert.Contains(next, s => s.Data.Contains("_mask") && s.Color == DialogHarness.Surface);
    }

    static List<string> Look(List<MySprite> sprites) =>
        sprites.Select(s => s.Data.Substring(0, s.Data.IndexOf('#')) + " " + s.Color).ToList();

    static List<MySprite> RowSprites(List<MySprite> sprites, Button row) => sprites
        .Where(s => s.Type == SpriteType.TEXTURE && s.Position.HasValue && s.Size.HasValue &&
                    Vector2.Distance(s.Position.Value, row.Bounds.Center) < 1f &&
                    Math.Abs(s.Size.Value.X - row.Bounds.Width) < 1f)
        .ToList();
}
