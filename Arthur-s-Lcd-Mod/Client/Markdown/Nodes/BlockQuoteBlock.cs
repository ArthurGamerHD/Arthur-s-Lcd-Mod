using System.Collections.Generic;

namespace LcdMod.Client.Markdown.Nodes
{
    public sealed class BlockQuoteBlock : BlockNode
    {
        public List<BlockNode> Children { get; } = new List<BlockNode>();
    }
}