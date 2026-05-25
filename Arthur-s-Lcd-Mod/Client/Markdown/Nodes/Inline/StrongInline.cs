// ReSharper disable RedundantUsingDirective
// ReSharper disable ArrangeObjectCreationWhenTypeEvident
using System.Collections.Generic;

namespace LcdMod.Client.Markdown.Inline
{
    public sealed class StrongInline : InlineNode
    {
        public List<InlineNode> Children { get; } = new List<InlineNode>();
    }
}