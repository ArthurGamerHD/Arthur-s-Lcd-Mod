// ReSharper disable once RedundantUsingDirective
using System.Collections.Generic;

namespace LcdMod.Client.Markdown
{
    public sealed class ParagraphBlock : BlockNode
    {
        // ReSharper disable once ArrangeObjectCreationWhenTypeEvident
        public List<InlineNode> Inlines { get; } = new List<InlineNode>();
        public string RawText { get; set; }
    }
}