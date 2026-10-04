using Adk.Gui.Controls;
using Adk.Gui.Styling;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

// A style file missing from a deployment used to throw out of the view constructor and crash the game.
public sealed class StyleLoadFailureTests
{
    [Fact]
    public void AnUnreadableIncludeIsReportedAndSkippedWhileTheRestOfTheSheetApplies()
    {
        var files = new Dictionary<string, string>
        {
            ["Root.xaml"] = """
                <Styles xmlns="https://github.com/ArthurGamerHD/Adk">
                  <StyleInclude Source="./Missing.xaml" />
                  <StyleInclude Source="./Empty.xaml" />
                  <Style Selector="Border.kept"><Setter Property="Padding" Value="4" /></Style>
                </Styles>
                """,
            ["Empty.xaml"] = " "
        };
        var failures = new List<string>();
        var previousLoader = StyleSheet.SourceLoader;
        var previousReport = StyleSheet.SourceLoadFailed;
        bool previousCache = StyleSheet.CacheSources;
        StyleSheet.SourceLoader = path => files.TryGetValue(path, out var xml) ? xml : throw new FileNotFoundException(path);
        StyleSheet.SourceLoadFailed = (source, _) => failures.Add(source);
        StyleSheet.CacheSources = false;
        try
        {
            var sheet = StyleSheet.LoadFromSource("/Root.xaml");
            Assert.Equal(new[] { "Missing.xaml", "Empty.xaml" }, failures);
            using var border = new Border { StyleClass = "kept", Styles = sheet };
            var tree = new Adk.Gui.Core.VisualTree(border, new PillRenderingTests.Renderer());
            tree.ArrangeViewport(new RectangleF(0, 0, 50, 50), 1);
            Assert.Equal(new Vector4(4), border.Padding);

            var missing = StyleSheet.LoadFromSource("/Gone.xaml");
            Assert.NotNull(missing);
            Assert.Equal("Gone.xaml", failures.Last());

            StyleSheet.SourceLoadFailed = (_, _) => throw new InvalidOperationException("reporter failed");
            StyleSheet.LoadFromSource("/Gone.xaml");
        }
        finally
        {
            StyleSheet.SourceLoader = previousLoader;
            StyleSheet.SourceLoadFailed = previousReport;
            StyleSheet.CacheSources = previousCache;
        }
    }
}
