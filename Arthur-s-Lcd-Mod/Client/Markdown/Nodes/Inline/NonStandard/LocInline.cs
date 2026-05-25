namespace LcdMod.Client.Markdown.Inline.NonStandard
{
    public sealed class LocInline : InlineNode
    {
        public LocInline()
        {
            Key = string.Empty;
        }

        public string Key { get; set; }
    }
}
