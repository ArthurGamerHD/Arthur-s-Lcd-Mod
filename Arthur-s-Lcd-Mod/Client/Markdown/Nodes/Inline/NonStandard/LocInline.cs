namespace LcdMod.Client.Markdown.Nodes.Inline.NonStandard
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
