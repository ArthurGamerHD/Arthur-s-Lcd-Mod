using System.Collections.Generic;
using LcdMod.Client.Markdown;

namespace LcdMod.Client.Markdown
{
    public sealed class ListBlock : BlockNode
    {
        public bool Ordered { get; set; }
        public List<ListItemBlock> Items { get; } = new List<ListItemBlock>();
    }
}