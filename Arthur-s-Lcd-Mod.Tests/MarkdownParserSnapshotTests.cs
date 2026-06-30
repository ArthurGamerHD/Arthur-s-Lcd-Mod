using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using LcdMod.Client.Markdown;
using LcdMod.Client.Markdown.Inline;
using LcdMod.Client.Markdown.Inline.NonStandard;

namespace Arthur_s_Lcd_Mod.Tests;

public sealed class MarkdownParserSnapshotTests
{
    public static TheoryData<string, string> MarkdownCases()
    {
        return new TheoryData<string, string>
        {
            {
                "heading-paragraph-inline",
                """
                # Ship Status

                Battery is **online** and *stable* with `42%`.
                """
            },
            {
                "links-and-images",
                """
                Open [manual **now**](https://example.test/manual) and [color:#FFAA00]![reactor](sprite:Textures/Sprites/Reactor.dds)[/color].
                """
            },
            {
                "code-break-paragraph",
                """
                ```cs
                var power = 42;
                ```

                ---

                Plain text after the break.
                """
            }
            ,
            {
                "custom-elements",
                """
                Colors [color:#FF0000]Red[/color] [color:#00FF00]Green[/color] [color:#0000FF]Blue[/color]
                Font [font:"Monospace"]monospace[/font] [font:"white"]white[/font] [font:"debug"]debug[/font]
                Font & Colors [font:"Monospace"][color:#FF0000]Red Monospace[/color][/font] [font:"white"][color:#00FF00]Green "White"[/color][/font] [font:"debug"][color:#0000FF]Blue Debug[/color][/font]
                """
            }
        };
    }

    [Fact]
    public void ParseColorWrappedImage()
    {
        MarkdownDocument document = new MarkdownParser().Parse("[color:#12ABEF]![Arrow](sprite:Arrow)[/color]");
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        ColorInline color = Assert.IsType<ColorInline>(Assert.Single(paragraph.Inlines));
        ImageInline image = Assert.IsType<ImageInline>(Assert.Single(color.Children));

        Assert.Equal("#12ABEF", color.Color);
        Assert.Equal(ImageType.Sprite, image.Kind);
        Assert.Equal("Arrow", image.Source);
    }

    [Fact]
    public void ParseCustomInfoArgbColorTag()
    {
        MarkdownDocument document = new MarkdownParser().Parse("[Color=#8012ABEF]Tinted[/Color]");
        ParagraphBlock paragraph = Assert.IsType<ParagraphBlock>(Assert.Single(document.Blocks));
        ColorInline color = Assert.IsType<ColorInline>(Assert.Single(paragraph.Inlines));
        TextInline text = Assert.IsType<TextInline>(Assert.Single(color.Children));

        Assert.Equal("#8012ABEF", color.Color);
        Assert.Equal("Tinted", text.Text);
    }

    [Theory]
    [MemberData(nameof(MarkdownCases))]
    public void ParseMarkdown(string name, string markdown)
    {
        try
        {
            MarkdownDocument document = new MarkdownParser().Parse(markdown);
            XDocument xml = MarkdownXmlSnapshotSerializer.Serialize(document);

            WriteSnapshot(name, xml);
        }
        catch (Exception ex)
        {
            WriteSnapshot(name, MarkdownXmlSnapshotSerializer.SerializeError(ex));
        }

        Assert.True(true);
    }

    static void WriteSnapshot(string name, XDocument xml)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "MarkdownSnapshots");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, name + ".xml");
        xml.Declaration = new XDeclaration("1.0", "utf-8", null);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true
        };

        using var writer = XmlWriter.Create(path, settings);
        xml.Save(writer);
    }
}

static partial class MarkdownXmlSnapshotSerializer
{
    static partial void AddGeneratedKnownTypes(ICollection<Type> knownTypes);

    public static XDocument Serialize(MarkdownDocument document)
    {
        var serializer = new XmlSerializer(typeof(MarkdownDocument), GetKnownTypes());
        using var writer = new StringWriter();
        serializer.Serialize(writer, document);
        return XDocument.Parse(writer.ToString());
    }

    public static XDocument SerializeError(Exception exception)
    {
        return new XDocument(
            new XElement(
                "markdownParseError",
                new XAttribute("type", exception.GetType().FullName ?? exception.GetType().Name),
                new XElement("message", exception.Message),
                new XElement("stackTrace", exception.ToString())));
    }

    static Type[] GetKnownTypes()
    {
        var knownTypes = new List<Type>();
        AddGeneratedKnownTypes(knownTypes);
        return knownTypes.ToArray();
    }
}
