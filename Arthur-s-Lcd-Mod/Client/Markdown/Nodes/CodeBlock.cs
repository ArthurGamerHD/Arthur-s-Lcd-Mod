namespace LcdMod.Client.Markdown.Nodes
{
    public sealed class CodeBlock : BlockNode
    {
        public string Language { get; set; } = "cs";
        public string Text { get; set; } = "";
    }
}