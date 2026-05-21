using System.Collections.Generic;

namespace LcdMod.Client.Markdown.Nodes.Inline
{
    public sealed class StrikethroughInline : InlineNode
    {
        public List<InlineNode> Children { get; } = new List<InlineNode>();
    }
}
