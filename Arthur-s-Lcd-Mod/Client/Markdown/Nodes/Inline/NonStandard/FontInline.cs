using System.Collections.Generic;

namespace LcdMod.Client.Markdown.Nodes.Inline.NonStandard
{
    public sealed class FontInline : InlineNode
    {
        public FontInline()
        {
            Children = new List<InlineNode>();
            FontName = string.Empty;
        }

        public string FontName { get; set; }
        public List<InlineNode> Children { get; private set; }
    }
}