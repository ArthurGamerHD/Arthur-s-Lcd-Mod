using System.Text;
using LcdMod.Client.Markdown.Nodes;
using LcdMod.Common;

namespace LcdMod.Client.Markdown
{
    public class BlockParser
    {
        InlineParser _inlineParser;

        public BlockParser(InlineParser inlineParser)
        {
            _inlineParser = inlineParser;
        }

        public MarkdownDocument Parse(string text)
        {
            if (text == null)
                text = string.Empty;

            string[] lines = text.Split('\n');

            MarkdownDocument document = new MarkdownDocument();

            int index = 0;

            while (index < lines.Length)
            {
                string line = lines[index];

                if (IsBlank(line))
                {
                    index++;
                    continue;
                }

                HeadingBlock heading;
                if (TryParseHeading(line, out heading))
                {
                    heading.Inlines.AddRange(_inlineParser.Parse(heading.RawText));
                    document.Blocks.Add(heading);
                    index++;
                    continue;
                }

                if (IsThematicBreak(line))
                {
                    document.Blocks.Add(new ThematicBreakBlock());
                    index++;
                    continue;
                }

                CodeBlock codeBlock;
                if (TryParseFencedCodeBlock(lines, ref index, out codeBlock))
                {
                    document.Blocks.Add(codeBlock);
                    continue;
                }

                ParagraphBlock paragraph = ParseParagraph(lines, ref index);
                paragraph.Inlines.AddRange(_inlineParser.Parse(paragraph.RawText));
                document.Blocks.Add(paragraph);
            }

            return document;
        }

        static bool IsBlank(string line)
        {
            return line == null || line.Trim().Length == 0;
        }

        static bool TryParseHeading(string line, out HeadingBlock heading)
        {
            heading = null;

            string trimmed = line.TrimStart();

            int level = 0;

            while (level < trimmed.Length && trimmed[level] == '#')
            {
                level++;
            }

            if (level == 0 || level > 6)
                return false;

            if (level >= trimmed.Length)
                return false;

            if (trimmed[level] != ' ')
                return false;

            string rawText = trimmed.Substring(level).Trim();

            heading = new HeadingBlock();
            heading.Level = level;
            heading.RawText = rawText;

            return true;
        }

        static bool IsThematicBreak(string line)
        {
            string trimmed = line.Trim();

            if (trimmed.Length < 3)
                return false;

            char marker = trimmed[0];

            if (marker != '-' && marker != '*' && marker != '_')
                return false;

            for (int i = 0; i < trimmed.Length; i++)
            {
                if (trimmed[i] != marker)
                    return false;
            }

            return true;
        }

        static bool TryParseFencedCodeBlock(
            string[] lines,
            ref int index,
            out CodeBlock block)
        {
            block = null;

            string line = lines[index].TrimStart();

            if (!line.StartsWith("```"))
                return false;

            string language = string.Empty;

            if (line.Length > 3)
                language = line.Substring(3).Trim();

            index++;

            StringBuilder builder = new StringBuilder();

            while (index < lines.Length)
            {
                string current = lines[index];

                if (current.TrimStart().StartsWith("```"))
                {
                    index++;

                    block = new CodeBlock();
                    block.Language = language;
                    block.Text = builder.ToString();

                    return true;
                }

                builder.AppendLine(current);
                index++;
            }

            block = new CodeBlock();
            block.Language = language;
            block.Text = builder.ToString();

            return true;
        }

        static ParagraphBlock ParseParagraph(string[] lines, ref int index)
        {
            StringBuilder builder = new StringBuilder();

            while (index < lines.Length)
            {
                string line = lines[index];

                if (IsBlank(line))
                    break;

                HeadingBlock ignoredHeading;
                if (TryParseHeading(line, out ignoredHeading))
                    break;

                if (IsThematicBreak(line))
                    break;

                CodeBlock ignoredCodeBlock;
                int tempIndex = index;
                if (TryParseFencedCodeBlock(lines, ref tempIndex, out ignoredCodeBlock))
                    break;

                if (builder.Length > 0)
                    builder.Append(" ");

                builder.Append(line.Trim());

                index++;
            }

            ParagraphBlock paragraph = new ParagraphBlock();
            paragraph.RawText = builder.ToString();

            return paragraph;
        }
    }
}