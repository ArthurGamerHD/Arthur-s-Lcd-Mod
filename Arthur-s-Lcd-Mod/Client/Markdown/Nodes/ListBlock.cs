using System.Collections.Generic;

namespace LcdMod.Client.Markdown.Nodes
{
    public sealed class ListBlock : BlockNode
    {
        public bool Ordered { get; set; }
        public List<ListItemBlock> Items { get; } = new List<ListItemBlock>();
    }
}