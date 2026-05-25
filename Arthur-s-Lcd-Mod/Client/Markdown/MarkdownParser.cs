namespace LcdMod.Client.Markdown
{
    public sealed class MarkdownParser
    {
        public BlockParser BlockParser { get; }
        public InlineParser InlineParser { get; }

        public MarkdownParser()
        {
            InlineParser = new InlineParser();
            BlockParser = new BlockParser(InlineParser);
        }

        public MarkdownDocument Parse(string text)
        {
            if (text == null)
                text = string.Empty;

            MarkdownDocument document = BlockParser.Parse(text);

            return document;
        }
    }
}