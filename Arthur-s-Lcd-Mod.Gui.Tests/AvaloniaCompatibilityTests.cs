using System.Reflection;
using Adk.Gui.Controls;
using Adk.Gui.Controls.Primitives;
using Adk.Gui.Core;
using Adk.Gui.Core.Input;
using Adk.Gui.Lcd;
using Adk.Gui.Styling;
using LcdMod.Client.Apps.EnergyDashboard;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class AvaloniaCompatibilityTests
{
    [Fact]
    public void ClipToBoundsClipsTheElementAndItsChildren()
    {
        var inner = new Border { Background = new SolidColorBrush(Color.Red), Margin = new Vector4(0, 0, -40, 0) };
        var outer = new Border { Width = 20, Height = 10, ClipToBounds = true, Child = inner,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var sprites = Draw(outer);
        int clip = sprites.FindIndex(s => s.Type == SpriteType.CLIP_RECT && s.Size == new Vector2(20, 10));
        int fill = sprites.FindIndex(s => s.Color == Color.Red);
        Assert.InRange(clip, 0, fill - 1);
        Assert.Equal(60, sprites[fill].Size.Value.X);
        Assert.Contains(sprites.Skip(fill), s => s.Type == SpriteType.CLIP_RECT && !s.Size.HasValue);
    }

    [Theory]
    [InlineData(TextTrimming.None, "Large Hydrogen Tank")]
    [InlineData(TextTrimming.CharacterEllipsis, "Large Hydroge…")]
    [InlineData(TextTrimming.WordEllipsis, "Large…")]
    public void TextTrimmingFitsSingleLineTextWithAnEllipsis(TextTrimming trimming, string expected)
    {
        var text = new TextBlock { Text = "Large Hydrogen Tank", FontSize = 1, Width = 140, TextTrimming = trimming,
            HorizontalAlignment = HorizontalAlignment.Left };
        var sprite = Draw(text).Single(s => s.Type == SpriteType.TEXT);
        Assert.Equal(expected, sprite.Data);
    }

    [Fact]
    public void ToggleButtonTogglesBeforeExecutingItsCommandLikeAvalonia()
    {
        bool? observed = null;
        var toggle = new ToggleButton { Width = 40, Height = 20,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        toggle.Command = new DelegateCommand(_ => observed = toggle.IsChecked);
        var window = new Window { WindowState = WindowState.FullScreen, Content = toggle };
        using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), window);
        host.Arrange(new RectangleF(0, 0, 100, 100));
        host.Draw(new List<MySprite>());
        host.MovePointer(new Vector2(10, 10));
        host.ClickPrimary();
        Assert.True(observed);
        Assert.True(toggle.IsChecked);
    }

    [Fact]
    public void RuntimeTemplatesCreateAppControlsThroughXmlNamespaces()
    {
        var sheet = StyleSheet.Load("""
            <Styles xmlns="https://github.com/ArthurGamerHD/Adk"
                    xmlns:energy="using:LcdMod.Client.Apps.EnergyDashboard">
              <Style Selector="Button.runtime">
                <Setter Property="Template">
                  <ControlTemplate>
                    <Grid xmlns:inner="clr-namespace:LcdMod.Client.Apps.EnergyDashboard;assembly=LcdMod">
                      <energy:EnergyStatBar x:Name="Bar" Label="Load" Ratio="0.25" FillColor="#FF102030" />
                      <inner:EnergyTierButton Label="5s" Foreground="{TemplateBinding Foreground}" IsChecked="True" />
                      <Image Source="Circle" Stretch="Fill" />
                    </Grid>
                  </ControlTemplate>
                </Setter>
              </Style>
              <Style Selector="energy|EnergyTierButton:checked">
                <Setter Property="FontSize" Value="2.5" />
              </Style>
            </Styles>
            """);
        var button = new Button { StyleClass = "runtime", Styles = sheet, Foreground = Color.Red };
        using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(),
            new Window { WindowState = WindowState.FullScreen, Content = button });
        host.Arrange(new RectangleF(0, 0, 200, 100));
        host.Draw(new List<MySprite>());
        var bar = Nodes(button).OfType<EnergyStatBar>().Single();
        Assert.Equal("Load", bar.Label);
        Assert.Equal(.25f, bar.Ratio);
        Assert.Equal(new Color(16, 32, 48), bar.FillColor);
        Assert.Same(bar, button.GetTemplateChild("Bar"));
        var tier = Nodes(button).OfType<EnergyTierButton>().Single();
        Assert.Equal("5s", tier.Label);
        Assert.Equal(Color.Red, tier.Foreground);
        Assert.Equal(2.5f, tier.FontSize);
        Assert.Equal(Stretch.Fill, Nodes(button).OfType<Image>().Single().Stretch);
    }

    [Theory]
    [InlineData("<Button Text=\"Legacy\" />", "Text")]
    [InlineData("<energy:EnergyPowerRowControl Text=\"Legacy\" />", "Text")]
    [InlineData("<Border BackgroundColor=\"#FFFFFFFF\" />", "BackgroundColor")]
    public void RuntimeTemplatesRejectMembersAvaloniaDoesNotHave(string element, string property)
    {
        var template = ControlTemplate.Parse(
            "<ControlTemplate xmlns:energy=\"using:LcdMod.Client.Apps.EnergyDashboard\">" + element + "</ControlTemplate>");
        var error = Assert.Throws<InvalidOperationException>(() => template.BuildInstance(new Button()));
        Assert.Contains("." + property + "'", error.Message);
    }

    [Fact]
    public void RuntimeTemplatesAcceptAvaloniaMembersAndAppControlProperties()
    {
        var template = ControlTemplate.Parse("""
            <ControlTemplate xmlns:energy="using:LcdMod.Client.Apps.EnergyDashboard">
              <Grid>
                <Button ClickMode="Press" HorizontalContentAlignment="Left" />
                <energy:EnergyPowerRowControl Label="Reactor" ClickMode="Press" />
              </Grid>
            </ControlTemplate>
            """);
        var root = template.BuildInstance(new Button()).Root;
        var row = Nodes(root).OfType<EnergyPowerRowControl>().Single();
        Assert.Equal("Reactor", row.Label);
        Assert.Equal(ClickMode.Press, row.ClickMode);
    }

    [Theory]
    [InlineData("<Styles><Style Selector=\"missing|Control\"><Setter Property=\"Width\" Value=\"1\" /></Style></Styles>")]
    [InlineData("<Styles><Style Selector=\"Button\"><Setter Property=\"Template\"><ControlTemplate><missing:Control /></ControlTemplate></Setter></Style></Styles>")]
    public void UndeclaredXmlNamespacePrefixesAreRejected(string xml)
    {
        var error = Assert.Throws<FormatException>(() => StyleSheet.Load(xml));
        Assert.Contains("Undeclared XML namespace prefix 'missing'", error.Message);
    }

    static IEnumerable<VisualElement> Nodes(VisualElement node)
    {
        yield return node;
        foreach (var child in node.VisualChildren)
            foreach (var descendant in Nodes(child)) yield return descendant;
    }

    static List<MySprite> Draw(Adk.Gui.Core.VisualElement content)
    {
        var window = new Window { WindowState = WindowState.FullScreen, Content = content };
        using var host = new LcdGuiHost(DispatchProxy.Create<IMyTextSurface, ThrustLcdHostTests.TextSurfaceProxy>(), window);
        host.Arrange(new RectangleF(0, 0, 200, 100));
        var sprites = new List<MySprite>();
        host.Draw(sprites);
        return sprites;
    }
}
