using System.Collections.Generic;
using LcdMod.Client.Markdown;

namespace LcdMod.Client.Markdown.Inline.NonStandard
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