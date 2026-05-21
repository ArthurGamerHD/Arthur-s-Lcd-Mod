using System.Collections.Generic;

namespace LcdMod.Client.Markdown.Nodes
{
    public sealed class ParagraphBlock : BlockNode
    {
        public List<InlineNode> Inlines { get; } = new List<InlineNode>();
        public string RawText { get; set; }
    }
}