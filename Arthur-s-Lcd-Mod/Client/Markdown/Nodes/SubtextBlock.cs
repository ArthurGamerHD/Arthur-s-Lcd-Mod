// ReSharper disable RedundantUsingDirective
// ReSharper disable ArrangeObjectCreationWhenTypeEvident
using System.Collections.Generic;

namespace LcdMod.Client.Markdown
{
    public sealed class SubtextBlock : BlockNode
    {
        public List<InlineNode> Inlines { get; } = new List<InlineNode>();
        public string RawText { get; set; }
    }
}
