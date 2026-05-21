using System.Collections.Generic;

namespace LcdMod.Client.Markdown.Nodes
{
    public sealed class HeadingBlock : BlockNode
    {
        public int Level { get; set; }
        public List<InlineNode> Inlines { get; } = new List<InlineNode>();
        public string RawText { get; set; }
    }
}