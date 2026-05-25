// ReSharper disable RedundantUsingDirective
// ReSharper disable ArrangeObjectCreationWhenTypeEvident
using System.Collections.Generic;

namespace LcdMod.Client.Markdown
{
    public sealed class MarkdownDocument : MarkdownNode
    {
        public List<BlockNode> Blocks { get; } = new List<BlockNode>();
    }

    public abstract class BlockNode : MarkdownNode
    {
    }

    public abstract class InlineNode : MarkdownNode
    {
    }
}