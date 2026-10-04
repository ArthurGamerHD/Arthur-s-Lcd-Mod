using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using Adk.Gui.Core.Input;
using Adk.Gui.Extensions;
using Adk.Gui.Layout;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.Inventory;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class ModalWindowFrameTests
{
    [Fact]
    public void ClickingInsideAModalKeepsOwnerAndModalInEveryFrame()
    {
        var previousLoader = StyleSheet.SourceLoader;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string repo = directory!.FullName;
        StyleSheet.SourceLoader = source =>
        {
            source = source.TrimStart('/');
            return File.ReadAllText(source.StartsWith("Data/Gui/Styles/Adk/", StringComparison.Ordinal)
                ? Path.Combine(repo, "External/Adk/Adk.Gui/Styles", source.Substring("Data/Gui/Styles/Adk/".Length))
                : Path.Combine(repo, "Arthur-s-Lcd-Mod/Content", source));
        };
        try
        {
            var owner = new Window
            {
                WindowState = WindowState.FullScreen,
                Platform = new OpenPlatform(),
                Content = new TextBlock { Text = "Owner content" },
                Styles = StyleSheet.LoadFromSource("/Data/Gui/Styles/Fluent/FluentStyle.xaml")
            };
            foreach (string key in new[] { "fontColor", "onSurfaceColor", "accentColor", "surfaceColor", "surfaceContainerColor",
                         "surfaceContainerHighColor", "surfaceContainerHighestColor", "secondaryContainerColor", "onAccentColor",
                         "borderColor", "borderVariantColor", "dividerColor", "accentContainerColor", "onAccentContainerColor",
                         "scrollBarTrackColor", "scrollBarThumbColor", "disabledColor", "surfaceContainerLowColor" })
                owner.Resources.Add(key, Color.Gray);
            owner.Resources.Add("textFont", "White");
            owner.Resources.Add("fontScale", 1f);
            owner.Open();
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), owner);
            host.Arrange(new RectangleF(0, 0, 512, 512));

            var model = new CraftDialogViewModel(1) { Title = "Craft dialog", FontSize = .65f };
            var dialog = new CraftDialogView(model);
            dialog.ShowModal(owner);

            var frame = Draw(host);
            AssertFrame(frame);
            var plus = Nodes(dialog).OfType<Button>().First(button => button.Command == model.IncreaseCommand);

            host.MovePointer(plus.Bounds.Center);
            AssertFrame(Draw(host));
            host.SetPrimaryButton(true);
            AssertFrame(Draw(host));
            host.SetPrimaryButton(false);
            AssertFrame(Draw(host));
            AssertFrame(Draw(host));
            Assert.Equal("2", model.Amount);
        }
        finally
        {
            StyleSheet.SourceLoader = previousLoader;
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.3f)]
    public void ClickingEveryControlOfACraftDialogOverAppViewKeepsItDrawn(float scaling)
    {
        var previousLoader = StyleSheet.SourceLoader;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string repo = directory!.FullName;
        StyleSheet.SourceLoader = source =>
        {
            source = source.TrimStart('/');
            return File.ReadAllText(source.StartsWith("Data/Gui/Styles/Adk/", StringComparison.Ordinal)
                ? Path.Combine(repo, "External/Adk/Adk.Gui/Styles", source.Substring("Data/Gui/Styles/Adk/".Length))
                : Path.Combine(repo, "Arthur-s-Lcd-Mod/Content", source));
        };
        try
        {
            var app = new LcdMod.Client.Apps.Abstract.App(new TextBlock { Text = "Owner content" });
            using var owner = new LcdMod.Client.Apps.Views.AppView { DataContext = app, Platform = new OpenPlatform() };
            foreach (string key in new[] { "fontColor", "onSurfaceColor", "surfaceColor", "surfaceContainerColor",
                         "surfaceContainerHighColor", "surfaceContainerHighestColor", "secondaryContainerColor", "onAccentColor",
                         "borderColor", "borderVariantColor", "dividerColor", "accentContainerColor", "onAccentContainerColor",
                         "scrollBarTrackColor", "scrollBarThumbColor", "disabledColor", "surfaceContainerLowColor", "warningColor", "errorColor" })
                owner.Resources.Add(key, Color.Gray);
            owner.Open();
            using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), owner,
                ownsRoot: false) { RenderScaling = scaling };
            var origin = new Vector2(32, 48);
            host.Arrange(new RectangleF(origin.X, origin.Y, 512, 512));
            Draw(host);

            var model = new CraftDialogViewModel(1)
            {
                Title = "Craft dialog", FontSize = .65f,
                AmountLabel = "Amount", ItemName = "Steel"
            };
            model.SetAssemblers(new[]
            {
                new CraftAssemblerModel { Label = "Assembler A", Queue = (_, _) => { } },
                new CraftAssemblerModel { Label = "Assembler B", Queue = (_, _) => { } }
            });
            var dialog = new CraftDialogView(model);
            dialog.ShowModal(owner);
            AssertFrame(Draw(host));

            var closing = Nodes(dialog).OfType<Button>()
                .Where(button => button.Command == model.CancelCommand || button.Command == model.CraftCommand ||
                                 button.Command == model.CloseCommand)
                .SelectMany(Nodes).ToHashSet();
            var targets = Nodes(dialog).Where(node => node.Visible && !closing.Contains(node) &&
                                                      !node.StyleClass.Contains("caption-button") &&
                                                      (node is Button || node is CheckBox || node is TextBox ||
                                                       node is TextBlock || node is ScrollViewer || node is Image))
                .ToList();
            Assert.NotEmpty(targets);
            foreach (var target in targets)
            {
                Vector2 pixel = origin + (target.Bounds.Center - origin) * scaling;
                host.MovePointer(pixel);
                AssertFrame(Draw(host), target);
                host.SetPrimaryButton(true);
                AssertFrame(Draw(host), target);
                host.MovePointer(pixel + new Vector2(1, 1));
                AssertFrame(Draw(host), target);
                host.SetPrimaryButton(false);
                AssertFrame(Draw(host), target);
                AssertFrame(Draw(host), target);
            }
        }
        finally
        {
            StyleSheet.SourceLoader = previousLoader;
        }
    }

    static List<MySprite> Draw(LcdGuiHost host)
    {
        var sprites = new List<MySprite>();
        host.Draw(sprites);
        return sprites;
    }

    static void AssertFrame(List<MySprite> sprites, VisualElement target = null)
    {
        Assert.True(sprites.Any(sprite => sprite.Type == SpriteType.TEXT && sprite.Data == "Craft dialog"),
            "Dialog missing after interacting with " + Describe(target));
        foreach (MySprite sprite in sprites)
        {
            Assert.True(Finite(sprite.Position) && Finite(sprite.Size) && !float.IsNaN(sprite.RotationOrScale) &&
                        !float.IsInfinity(sprite.RotationOrScale),
                "Non-finite sprite " + sprite.Type + " '" + sprite.Data + "' " + sprite.Position + " " + sprite.Size +
                " after interacting with " + target);
        }
        RectangleF? clip = null;
        foreach (MySprite sprite in sprites)
        {
            if (sprite.Type == SpriteType.CLIP_RECT)
                clip = sprite.Position.HasValue && sprite.Size.HasValue
                    ? new RectangleF(sprite.Position.Value, sprite.Size.Value)
                    : (RectangleF?)null;
            else if (sprite.Type == SpriteType.TEXT && sprite.Data == "Craft dialog" && clip.HasValue)
                Assert.True(clip.Value.Contains(sprite.Position.Value),
                    "Dialog title clipped by " + clip.Value + " after interacting with " + target);
        }
        Assert.Contains(sprites, sprite => sprite.Type == SpriteType.TEXT && sprite.Data == "Owner content");
        Assert.Contains(sprites, sprite => sprite.Type == SpriteType.TEXT && sprite.Data == "Craft dialog");
    }

    static string Describe(VisualElement target)
    {
        return target == null ? "<none>" : target.GetType().Name + (target is TextBlock text ? " '" + text.Text + "'" : string.Empty) +
                                         " at " + target.Bounds;
    }

    static bool Finite(Vector2? value)
    {
        return !value.HasValue || !float.IsNaN(value.Value.X) && !float.IsNaN(value.Value.Y) &&
               !float.IsInfinity(value.Value.X) && !float.IsInfinity(value.Value.Y);
    }

    static IEnumerable<VisualElement> Nodes(VisualElement node)
    {
        yield return node;
        foreach (var child in node.VisualChildren)
            foreach (var descendant in Nodes(child)) yield return descendant;
    }

    sealed class OpenPlatform : IWindowPlatform
    {
        public IWindowImpl CreateWindowImpl(Window window) => new Impl();

        sealed class Impl : IWindowImpl
        {
            public bool IsOpen { get; private set; }
            public void Open() => IsOpen = true;
            public void Close() => IsOpen = false;
            public void BindOpen(Key key) { }
            public bool UnbindOpen(Key key) => true;
            public void Dispose() { }
        }
    }
}
