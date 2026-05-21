using System.Collections.Generic;

namespace LcdMod.Client.Markdown.Nodes.Inline
{
    public sealed class StrongInline : InlineNode
    {
        public List<InlineNode> Children { get; } = new List<InlineNode>();
    }
}