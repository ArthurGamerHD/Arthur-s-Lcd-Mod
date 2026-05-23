using System.Collections.Generic;
using LcdMod.Client.Markdown;

namespace LcdMod.Client.Markdown.Inline
{
    public sealed class StrikethroughInline : InlineNode
    {
        public List<InlineNode> Children { get; } = new List<InlineNode>();
    }
}
