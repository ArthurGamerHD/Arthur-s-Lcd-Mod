using LcdMod.Client.Markdown;

public sealed class MarkdownParser
{
    readonly BlockParser _blockParser;
    readonly InlineParser _inlineParser;

    public MarkdownParser()
    {
        _inlineParser = new InlineParser();
        _blockParser = new BlockParser(_inlineParser);
    }

    public MarkdownDocument Parse(string text)
    {
        if (text == null)
            text = string.Empty;

        MarkdownDocument document = _blockParser.Parse(text);

        return document;
    }
}