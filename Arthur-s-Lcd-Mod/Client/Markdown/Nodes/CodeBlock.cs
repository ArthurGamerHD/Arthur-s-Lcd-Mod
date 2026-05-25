namespace LcdMod.Client.Markdown
{
    public sealed class CodeBlock : BlockNode
    {
        public string Language { get; set; } = "cs";
        public string Text { get; set; } = "";
    }
}