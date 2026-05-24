using System.Collections.Generic;
using LcdMod.Client.Markdown;

namespace LcdMod.Client.Markdown
{
    public sealed class HeadingBlock : BlockNode
    {
        public int Level { get; set; }
        public List<InlineNode> Inlines { get; } = new List<InlineNode>();
        public string RawText { get; set; }
    }
}