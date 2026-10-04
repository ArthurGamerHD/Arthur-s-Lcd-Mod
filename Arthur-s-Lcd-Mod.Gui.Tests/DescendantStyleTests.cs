using Adk.Gui.Controls;
using Adk.Gui.Layout;
using Adk.Gui.Styling;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class DescendantStyleTests
{
    [Fact]
    public void NamespacedDescendantStylesFollowAncestorClassesAndReparenting()
    {
        var root = new StackPanel
        {
            Styles = StyleSheet.Load("""
                <Styles xmlns:ui="using:Adk.Gui.Controls">
                  <Style Selector="ui|TextBlock"><Setter Property="Foreground" Value="White" /></Style>
                  <Style Selector="ui|Border.active ui|TextBlock.caption">
                    <Setter Property="Foreground" Value="Red" />
                  </Style>
                </Styles>
                """)
        };
        var label = new TextBlock { StyleClass = "caption", Text = "Label" };
        var inner = new Border { Child = label };
        var active = new Border { StyleClass = "active", Child = inner };
        var inactive = new Border();
        root.AddChild(active);
        root.AddChild(inactive);
        root.Measure(new Vector2(300));
        Assert.Equal(Color.Red, label.Foreground);
        active.StyleClass = "inactive";
        root.Measure(new Vector2(300));
        Assert.Equal(Color.White, label.Foreground);
        active.StyleClass = "active";
        root.Measure(new Vector2(300));
        Assert.Equal(Color.Red, label.Foreground);
        inactive.Child = inner;
        root.Measure(new Vector2(300));
        Assert.Equal(Color.White, label.Foreground);
        root.Dispose();
    }

    [Fact]
    public void AncestorPseudoClassesUpdateNestedDescendantsAndReset()
    {
        var label = new TextBlock { Text = "Label" };
        var middle = new Border { Child = label };
        using var root = new Button
        {
            Content = middle,
            Styles = StyleSheet.Load("""
                <Styles>
                  <Style Selector="TextBlock"><Setter Property="Foreground" Value="White" /></Style>
                  <Style Selector="Button:pointerover Border TextBlock">
                    <Setter Property="Foreground" Value="Red" />
                  </Style>
                </Styles>
                """)
        };
        root.Measure(new Vector2(300));
        Assert.Equal(Color.White, label.Foreground);
        root.IsPointerOver = true;
        root.Measure(new Vector2(300));
        Assert.Equal(Color.Red, label.Foreground);
        root.IsPointerOver = false;
        root.Measure(new Vector2(300));
        Assert.Equal(Color.White, label.Foreground);
    }
}
