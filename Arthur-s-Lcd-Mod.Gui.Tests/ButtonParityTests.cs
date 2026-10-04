using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Extensions;
using Adk.Gui.Layout;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

// The pre-migration buttons centered and shrank their labels, shrank while pressed and clicked audibly.
[Collection(LcdTextureHooks.Name)]
public sealed class ButtonParityTests
{
    sealed class CountingEffect : IClickEffect
    {
        public int Played;
        public void Play(Button button) => Played++;
    }

    static Button Labelled(string text, float width) => new()
    {
        Width = width, Height = 44, HorizontalAlignment = HorizontalAlignment.Left,
        Content = new TextBlock { Text = text, FontSize = 1 }
    };

    static (DialogHarness Harness, Window Window, Button Short, Button Long, Button Stretch, TextBlock Plain) Show()
    {
        var harness = new DialogHarness(700, 700);
        var shortButton = Labelled("OK", 200);
        var longButton = Labelled("A label far too long for its narrow button", 60);
        var stretch = new Button
        {
            Width = 200, Height = 30, HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        var grid = new Grid();
        grid.SetColumnDefinitions(GridLength.Star, GridLength.Auto);
        grid.Set(new TextBlock { Text = "Left", FontSize = 1 }, 0, 0, 1, 1);
        grid.Set(new TextBlock { Text = "Right", FontSize = 1 }, 1, 0, 1, 1);
        stretch.Content = grid;
        var plain = new TextBlock { Text = "Plain", FontSize = 1 };
        var panel = new StackPanel { Spacing = 4 };
        panel.AddChild(shortButton);
        panel.AddChild(longButton);
        panel.AddChild(stretch);
        panel.AddChild(plain);
        var window = harness.Show(new Window { Width = 400, Height = 300, Content = panel });
        harness.Draw();
        return (harness, window, shortButton, longButton, stretch, plain);
    }

    static MySprite TextSprite(List<MySprite> sprites, string text) =>
        sprites.Single(s => s.Type == SpriteType.TEXT && s.Data == text);

    [Fact]
    public void LabelsAreCenteredAndOnlyShrinkToFit()
    {
        var (harness, _, shortButton, _, _, _) = Show();
        using var _ = harness;
        var label = DialogHarness.Nodes(shortButton).OfType<TextBlock>().Single();
        Assert.InRange(label.Bounds.Center.X, shortButton.Bounds.Center.X - 1, shortButton.Bounds.Center.X + 1);
        Assert.InRange(label.Bounds.Center.Y, shortButton.Bounds.Center.Y - 1, shortButton.Bounds.Center.Y + 1);

        var sprites = harness.Draw();
        float plain = TextSprite(sprites, "Plain").RotationOrScale;
        Assert.Equal(plain, TextSprite(sprites, "OK").RotationOrScale, 3);
        Assert.True(TextSprite(sprites, "A label far too long for its narrow button").RotationOrScale < plain * .8f);
    }

    [Fact]
    public void StretchedContentFillsTheButtonWithoutTheScalingViewbox()
    {
        var (harness, _, _, _, stretch, _) = Show();
        using var _ = harness;
        Assert.Empty(DialogHarness.Nodes(stretch).OfType<Viewbox>());
        var right = DialogHarness.Nodes(stretch).OfType<TextBlock>().Single(t => t.Text == "Right");
        Assert.InRange(right.Bounds.Right, stretch.Bounds.Right - stretch.Padding.Z - 1, stretch.Bounds.Right);

        stretch.HorizontalContentAlignment = HorizontalAlignment.Center;
        harness.Draw();
        Assert.Single(DialogHarness.Nodes(stretch).OfType<Viewbox>());
        Assert.Single(DialogHarness.Nodes(stretch).OfType<Grid>(), g => g.VisualChildren.OfType<TextBlock>().Any());
    }

    [Fact]
    public void TextContentIsMeasuredSoTheViewboxDoesNotClipIt()
    {
        using var harness = new DialogHarness(700, 700);
        var button = new Button { Content = "Refresh", Width = 96, Height = 28, HorizontalAlignment = HorizontalAlignment.Left };
        harness.Show(new Window { Width = 300, Height = 200, Content = button });
        var sprites = harness.Draw();
        int text = sprites.FindIndex(s => s.Type == SpriteType.TEXT && s.Data == "Refresh");
        Assert.True(text >= 0);
        var clip = sprites.Take(text).Last(s => s.Type == SpriteType.CLIP_RECT);
        Assert.True(clip.Size.HasValue && clip.Size.Value.X > 20 && clip.Size.Value.Y > 10, "clip " + clip.Size);
    }

    [Fact]
    public void APanelDrawsOnlyItsBackground()
    {
        using var harness = new DialogHarness(700, 700);
        var panel = new Panel { Width = 100, Height = 40, HorizontalAlignment = HorizontalAlignment.Left };
        harness.Show(new Window { Width = 300, Height = 200, Content = panel });
        Func<List<MySprite>, bool> drawsAt = sprites => sprites.Any(s => s.Type == SpriteType.TEXTURE && s.Size.HasValue &&
            Math.Abs(s.Size.Value.X - panel.Bounds.Width) < .5f && Math.Abs(s.Size.Value.Y - panel.Bounds.Height) < .5f);
        Assert.False(drawsAt(harness.Draw()));
        panel.Background = new Adk.Gui.Styling.SolidColorBrush(Color.Red);
        Assert.True(drawsAt(harness.Draw()));
    }

    [Fact]
    public void MapLayerButtonsKeepTheirFillingPreviewOutOfTheViewbox()
    {
        using var harness = new DialogHarness(700, 700);
        var preview = new Border { Background = new Adk.Gui.Styling.SolidColorBrush(Color.Red) };
        var button = new Button { StyleClass = "map-layer", Content = preview, HorizontalAlignment = HorizontalAlignment.Left };
        harness.Show(new Window
        {
            Width = 300, Height = 200, Content = button,
            Styles = Adk.Gui.Styling.StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/Controls/Maps.xaml")
        });
        harness.Draw();
        Assert.Empty(DialogHarness.Nodes(button).OfType<Viewbox>());
        Assert.Equal(button.Bounds.Width, preview.Bounds.Width, 1);
        Assert.Equal(button.Bounds.Height, preview.Bounds.Height, 1);
    }

    [Fact]
    public void ALongDialogTitleShrinksToItsCaptionBar()
    {
        using var harness = new DialogHarness(700, 700);
        var title = "A dialog title that is much too long for a narrow window to show";
        harness.Show(new Window { Title = title, Width = 220, Height = 160, Content = new TextBlock { Text = "Body" } });
        var sprites = harness.Draw();
        var titleSprite = sprites.Single(s => s.Type == SpriteType.TEXT && s.Data == title);
        float body = sprites.Single(s => s.Type == SpriteType.TEXT && s.Data == "Body").RotationOrScale;
        Assert.True(titleSprite.RotationOrScale < body * .7f, "title scale " + titleSprite.RotationOrScale);
    }

    [Fact]
    public void MapToggleIconsFillTheirButton()
    {
        using var harness = new DialogHarness(700, 700);
        var icon = new Border { StyleClass = "map-icon" };
        var toggle = new Adk.Gui.Controls.Primitives.ToggleButton
            { StyleClass = "map-button", Content = icon, HorizontalAlignment = HorizontalAlignment.Left };
        harness.Show(new Window
        {
            Width = 300, Height = 200, Content = toggle,
            Styles = Adk.Gui.Styling.StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/Controls/Maps.xaml")
        });
        harness.Draw();
        Assert.Equal(toggle.Bounds.Width - toggle.Padding.X - toggle.Padding.Z, icon.Bounds.Width, 1);
        Assert.Equal(toggle.Bounds.Height - toggle.Padding.Y - toggle.Padding.W, icon.Bounds.Height, 1);
    }

    [Fact]
    public void PressingShrinksTheWholeButtonUntilReleased()
    {
        var (harness, _, shortButton, _, _, _) = Show();
        using var _ = harness;
        float released = TextSprite(harness.Draw(), "OK").RotationOrScale;

        harness.Host.MovePointer(shortButton.Bounds.Center);
        harness.Host.SetPrimaryButton(true);
        var pressed = harness.Draw();
        Assert.Equal(released * .95f, TextSprite(pressed, "OK").RotationOrScale, 3);
        Assert.Contains(pressed, s => s.Type == SpriteType.TEXTURE && s.Size.HasValue &&
                                      Math.Abs(s.Size.Value.X - shortButton.Bounds.Width * .95f) < .5f);

        harness.Host.SetPrimaryButton(false);
        Assert.Equal(released, TextSprite(harness.Draw(), "OK").RotationOrScale, 3);
    }

    [Fact]
    public void ClicksPlayTheEffectFromTheButtonsResourcesUnlessTurnedOff()
    {
        var (harness, _, shortButton, _, _, _) = Show();
        using var _ = harness;
        var effect = new CountingEffect();
        harness.Owner.Resources.Add(ClickEffect.ResourceKey, effect);
        ClickEffect.Install();
        try
        {
            int commands = 0;
            shortButton.Command = new Adk.Gui.Core.Input.DelegateCommand(_ => commands++);
            harness.Click(shortButton);
            Assert.Equal(1, effect.Played);
            Assert.Equal(1, commands);

            ClickEffect.SetIsEnabled(shortButton, false);
            harness.Click(shortButton);
            Assert.Equal(1, effect.Played);
            Assert.Equal(2, commands);
        }
        finally
        {
            ClickEffect.Uninstall();
        }
    }
}
