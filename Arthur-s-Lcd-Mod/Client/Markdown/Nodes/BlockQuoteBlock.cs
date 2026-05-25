// ReSharper disable RedundantUsingDirective
// ReSharper disable ArrangeObjectCreationWhenTypeEvident
using System.Collections.Generic;

namespace LcdMod.Client.Markdown
{
    public sealed class BlockQuoteBlock : BlockNode
    {
        public List<BlockNode> Children { get; } = new List<BlockNode>();
    }
}