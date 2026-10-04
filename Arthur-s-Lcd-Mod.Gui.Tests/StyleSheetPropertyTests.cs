using System.Xml.Linq;
using Adk.Gui.Core;
using Adk.Gui.Styling;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class StyleSheetPropertyTests
{
    public static IEnumerable<object[]> Setters()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LcdMod.sln"))) directory = directory.Parent;
        string repo = directory!.FullName;
        var files = Directory.GetFiles(Path.Combine(repo, "Arthur-s-Lcd-Mod/Content/Data/Gui"), "*.xaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(repo, "External/Adk/Adk.Gui/Styles"), "*.xaml", SearchOption.AllDirectories));
        foreach (string file in files)
        {
            var document = XDocument.Load(file);
            if (document.Root == null || document.Root.Name.LocalName != "Styles")
                continue;
            var namespaces = document.Root.Attributes()
                .Where(a => a.IsNamespaceDeclaration && a.Name.Namespace == XNamespace.Xmlns)
                .ToDictionary(a => a.Name.LocalName, a => a.Value);
            foreach (var style in document.Root.Elements().Where(e => e.Name.LocalName == "Style"))
            {
                string type = SelectorType((string)style.Attribute("Selector")!, namespaces);
                foreach (var setter in style.Elements().Where(e => e.Name.LocalName == "Setter"))
                    yield return new object[] { Path.GetFileName(file), type, (string)setter.Attribute("Property")! };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Setters))]
    public void EveryShippedSetterResolvesOnItsSelectorType(string file, string type, string property)
    {
        if (!ControlTemplate.TemplateTypes.Contains(type))
            return;
        Assert.NotNull(StyleSheet.Empty);
        var control = ControlTemplate.TemplateTypes.Create(type);
        Assert.True(StylePropertyRegistry.Global.CanApply(type, property, control),
            file + ": '" + type + "." + property + "' is not a styled property.");
    }

    [Fact]
    public void BorderPaddingIsStyleable()
    {
        var border = new Adk.Gui.Controls.Border
        {
            StyleClass = "card",
            Styles = StyleSheet.Load("<Styles><Style Selector=\"Border.card\"><Setter Property=\"Padding\" Value=\"14\" /></Style></Styles>")
        };
        border.Measure(new VRageMath.Vector2(100, 100));
        Assert.Equal(new VRageMath.Vector4(14), border.Padding);
    }

    static string SelectorType(string selector, IDictionary<string, string> namespaces)
    {
        string head = selector.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Last();
        int bar = head.IndexOf('|');
        int start = bar + 1;
        int end = start;
        while (end < head.Length && head[end] != '.' && head[end] != ':' && head[end] != ' ') end++;
        string local = head.Substring(start, end - start);
        if (bar < 0)
            return local;
        string ns = namespaces[head.Substring(0, bar)];
        ns = ns.StartsWith("using:") ? ns.Substring(6) : ns.Substring("clr-namespace:".Length).Split(';')[0];
        return ns + "." + local;
    }
}
