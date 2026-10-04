using Adk.Gui.Styling;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class StyleSourceCacheTests
{
    [Fact]
    public void SourcesAreReadOnceUntilTheCacheIsDisabled()
    {
        var reads = new Dictionary<string, int>();
        var files = new Dictionary<string, string>
        {
            ["Root.xaml"] = """<Styles xmlns="https://github.com/ArthurGamerHD/Adk"><StyleInclude Source="./Inner.xaml" /></Styles>""",
            ["Inner.xaml"] = """<Styles xmlns="https://github.com/ArthurGamerHD/Adk"><Style Selector="Border.a"><Setter Property="Padding" Value="4" /></Style></Styles>"""
        };
        var previousLoader = StyleSheet.SourceLoader;
        bool previousCache = StyleSheet.CacheSources;
        StyleSheet.SourceLoader = path =>
        {
            reads[path] = reads.GetValueOrDefault(path) + 1;
            return files[path];
        };
        try
        {
            StyleSheet.CacheSources = true;
            StyleSheet.LoadFromSource("/Root.xaml");
            StyleSheet.LoadFromSource("Root.xaml");
            StyleSheet.LoadFromSource("/Inner.xaml");
            Assert.Equal(1, reads["Root.xaml"]);
            Assert.Equal(1, reads["Inner.xaml"]);

            StyleSheet.CacheSources = false;
            StyleSheet.LoadFromSource("/Root.xaml");
            StyleSheet.LoadFromSource("/Root.xaml");
            Assert.Equal(3, reads["Root.xaml"]);
            Assert.Equal(3, reads["Inner.xaml"]);

            StyleSheet.CacheSources = true;
            StyleSheet.LoadFromSource("/Root.xaml");
            StyleSheet.LoadFromSource("/Root.xaml");
            Assert.Equal(4, reads["Root.xaml"]);
        }
        finally
        {
            StyleSheet.SourceLoader = previousLoader;
            StyleSheet.CacheSources = previousCache;
        }
    }
}
