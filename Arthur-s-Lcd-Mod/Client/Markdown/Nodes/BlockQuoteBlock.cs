using System.Collections.Generic;
using LcdMod.Client.Markdown;

namespace LcdMod.Client.Markdown
{
    public sealed class BlockQuoteBlock : BlockNode
    {
        public List<BlockNode> Children { get; } = new List<BlockNode>();
    }
}