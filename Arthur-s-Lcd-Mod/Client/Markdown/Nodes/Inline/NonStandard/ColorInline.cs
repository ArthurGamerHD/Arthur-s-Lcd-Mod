// ReSharper disable RedundantUsingDirective
// ReSharper disable ArrangeObjectCreationWhenTypeEvident
using System.Collections.Generic;

namespace LcdMod.Client.Markdown.Inline.NonStandard
{
    public sealed class ColorInline : InlineNode
    {
        public ColorInline()
        {
            Children = new List<InlineNode>();
            Color = string.Empty;
        }

        public string Color { get; set; }
        public List<InlineNode> Children { get; private set; }
    }
}