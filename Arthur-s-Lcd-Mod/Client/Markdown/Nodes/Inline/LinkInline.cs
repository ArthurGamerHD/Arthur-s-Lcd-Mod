// ReSharper disable RedundantUsingDirective
// ReSharper disable ArrangeObjectCreationWhenTypeEvident
using System.Collections.Generic;

namespace LcdMod.Client.Markdown.Inline
{
    public sealed class LinkInline : InlineNode
    {
        public string Url { get; set; } = "";
        public string Title { get; set; }
        public List<InlineNode> Children { get; } = new List<InlineNode>();
    }
}