// ReSharper disable RedundantUsingDirective
// ReSharper disable ArrangeObjectCreationWhenTypeEvident
using System.Collections.Generic;

namespace LcdMod.Client.Markdown.Inline
{
    public sealed class EmphasisInline : InlineNode
    {
        public List<InlineNode> Children { get; } = new List<InlineNode>();
    }
}