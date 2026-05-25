// ReSharper disable RedundantUsingDirective
// ReSharper disable ArrangeObjectCreationWhenTypeEvident
using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Markdown.Inline;
using LcdMod.Client.Markdown.Inline.NonStandard;

namespace LcdMod.Client.Markdown
{
    public sealed class InlineParser
    {
        string _text;
        int _position;

        delegate InlineNode ContainerFactory(List<InlineNode> children);

        public List<InlineNode> Parse(string text)
        {
            if (text == null)
                text = string.Empty;

            _text = text;
            _position = 0;

            List<InlineNode> nodes;
            ParseUntil(null, out nodes);

            return nodes;
        }

        bool ParseUntil(string closingDelimiter, out List<InlineNode> nodes)
        {
            nodes = new List<InlineNode>();

            while (!End)
            {
                if (closingDelimiter != null && StartsWith(closingDelimiter))
                {
                    _position += closingDelimiter.Length;
                    return true;
                }

                InlineNode node;

                if (TryParseCode(out node))
                {
                    AddNode(nodes, node);
                    continue;
                }

                if (TryParseImage(out node))
                {
                    AddNode(nodes, node);
                    continue;
                }
                
                if (TryParseColor(out node))
                {
                    AddNode(nodes, node);
                    continue;
                }

                if (TryParseFont(out node))
                {
                    AddNode(nodes, node);
                    continue;
                }

                if (TryParseLoc(out node))
                {
                    AddNode(nodes, node);
                    continue;
                }

                if (TryParseUnderline(out node))
                {
                    AddNode(nodes, node);
                    continue;
                }

                if (TryParseStrikethrough(out node))
                {
                    AddNode(nodes, node);
                    continue;
                }

                if (TryParseLink(out node))
                {
                    AddNode(nodes, node);
                    continue;
                }

                if (TryParseStrong(out node))
                {
                    AddNode(nodes, node);
                    continue;
                }

                if (TryParseEmphasis(out node))
                {
                    AddNode(nodes, node);
                    continue;
                }

                if (TryParseEscape(out node))
                {
                    AddNode(nodes, node);
                    continue;
                }

                AddNode(nodes, ParseText(closingDelimiter));
            }

            return closingDelimiter == null;
        }

        bool End => _position >= _text.Length;

        char Current => _text[_position];

        bool StartsWith(string value)
        {
            if (value == null)
                return false;

            if (_position + value.Length > _text.Length)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                if (_text[_position + i] != value[i])
                    return false;
            }

            return true;
        }

        private static void AddNode(List<InlineNode> nodes, InlineNode node)
        {
            if (node == null)
                return;

            TextInline text = node as TextInline;

            if (text != null)
            {
                text.Text = text.Text ?? string.Empty;

                if (text.Text.Length == 0)
                    return;

                if (nodes.Count > 0)
                {
                    TextInline previous = nodes[nodes.Count - 1] as TextInline;

                    if (previous != null)
                    {
                        previous.Text += text.Text;
                        return;
                    }
                }
            }

            nodes.Add(node);
        }

        bool TryParseCode(out InlineNode node)
        {
            node = null;

            if (End || Current != '`')
                return false;

            int start = _position + 1;
            int end = _text.IndexOf('`', start);

            if (end < 0)
                return false;

            CodeInline code = new CodeInline();
            code.Code = _text.Substring(start, end - start);

            _position = end + 1;
            node = code;

            return true;
        }

        bool TryParseStrong(out InlineNode node)
        {
            return TryParseContainer(
                "**",
                delegate(List<InlineNode> children)
                {
                    StrongInline strong = new StrongInline();
                    strong.Children.AddRange(children);
                    return strong;
                },
                out node);
        }

        bool TryParseEmphasis(out InlineNode node)
        {
            return TryParseContainer(
                "*",
                delegate(List<InlineNode> children)
                {
                    EmphasisInline emphasis = new EmphasisInline();
                    emphasis.Children.AddRange(children);
                    return emphasis;
                },
                out node);
        }

        bool TryParseUnderline(out InlineNode node)
        {
            return TryParseContainer(
                "<u>",
                "</u>",
                delegate(List<InlineNode> children)
                {
                    UnderlineInline underline = new UnderlineInline();
                    underline.Children.AddRange(children);
                    return underline;
                },
                out node);
        }

        bool TryParseStrikethrough(out InlineNode node)
        {
            return TryParseContainer(
                "~~",
                delegate(List<InlineNode> children)
                {
                    StrikethroughInline strikethrough = new StrikethroughInline();
                    strikethrough.Children.AddRange(children);
                    return strikethrough;
                },
                out node);
        }

        bool TryParseContainer(
            string delimiter,
            ContainerFactory factory,
            out InlineNode node)
        {
            return TryParseContainer(delimiter, delimiter, factory, out node);
        }

        bool TryParseContainer(
            string openingDelimiter,
            string closingDelimiter,
            ContainerFactory factory,
            out InlineNode node)
        {
            node = null;

            if (!StartsWith(openingDelimiter))
                return false;

            int originalPosition = _position;

            _position += openingDelimiter.Length;

            List<InlineNode> children;
            bool closed = ParseUntil(closingDelimiter, out children);

            if (!closed)
            {
                _position = originalPosition;
                return false;
            }

            node = factory(children);
            return true;
        }

        bool TryParseImage(out InlineNode node)
        {
            node = null;

            if (!StartsWith("!["))
                return false;

            int originalPosition = _position;

            _position += 2;

            int altStart = _position;
            int altEnd = _text.IndexOf(']', altStart);

            if (altEnd < 0)
            {
                _position = originalPosition;
                return false;
            }

            string altText = _text.Substring(altStart, altEnd - altStart);

            _position = altEnd + 1;

            if (End || Current != '(')
            {
                _position = originalPosition;
                return false;
            }

            _position++;

            int sourceStart = _position;
            int sourceEnd = _text.IndexOf(')', sourceStart);

            if (sourceEnd < 0)
            {
                _position = originalPosition;
                return false;
            }

            string source = _text.Substring(sourceStart, sourceEnd - sourceStart).Trim();

            _position = sourceEnd + 1;

            ImageInline image = new ImageInline();
            image.AltText = altText;

            // not exactly Markdown compliant, but we do what we have to do
            if (source.StartsWith("sprite:"))
            {
                image.Kind = ImageType.Sprite;
                image.Source = source.Substring("sprite:".Length);
            }
            else if (source.StartsWith("monospace:"))
            {
                image.Kind = ImageType.Monospace;
                image.Source = source.Substring("monospace:".Length);
            }
            else
            {
                image.Kind = ImageType.Unknown; // later on the code assumes is a sprite
                image.Source = source;
            }

            node = image;
            return true;
        }

        bool TryParseLoc(out InlineNode node)
        {
            node = null;

            const string prefix = "[loc]";
            const string close = "[/loc]";

            if (!StartsWith(prefix))
                return false;

            int originalPosition = _position;
            int keyStart = _position + prefix.Length;
            int keyEnd = _text.IndexOf(close, keyStart, StringComparison.Ordinal);

            if (keyEnd < 0)
            {
                _position = originalPosition;
                return false;
            }

            string key = _text.Substring(keyStart, keyEnd - keyStart).Trim();
            if (key.Length == 0)
            {
                _position = originalPosition;
                return false;
            }

            _position = keyEnd + close.Length;

            LocInline locInline = new LocInline();
            locInline.Key = key;
            node = locInline;
            return true;
        }

        bool TryParseColor(out InlineNode node)
        {
            node = null;

            const string prefix = "[color:";
            const string close = "[/color]";

            if (!StartsWith(prefix))
                return false;

            int originalPosition = _position;

            int colorStart = _position + prefix.Length;
            int colorEnd = _text.IndexOf(']', colorStart);

            if (colorEnd < 0)
            {
                _position = originalPosition;
                return false;
            }

            string color = _text.Substring(colorStart, colorEnd - colorStart).Trim();

            if (!IsValidColorValue(color))
            {
                _position = originalPosition;
                return false;
            }

            _position = colorEnd + 1;

            List<InlineNode> children;
            bool closed = ParseUntil(close, out children);

            if (!closed)
            {
                _position = originalPosition;
                return false;
            }

            ColorInline colorInline = new ColorInline();
            colorInline.Color = color;
            colorInline.Children.AddRange(children);

            node = colorInline;
            return true;
        }

        static bool IsValidColorValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            if (value.Length != 7)
                return false;

            if (value[0] != '#')
                return false;

            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];

                bool isDigit = c >= '0' && c <= '9';
                bool isLowerHex = c >= 'a' && c <= 'f';
                bool isUpperHex = c >= 'A' && c <= 'F';

                if (!isDigit && !isLowerHex && !isUpperHex)
                    return false;
            }

            return true;
        }

        bool TryParseFont(out InlineNode node)
        {
            node = null;

            const string prefix = "[font:";
            const string close = "[/font]";

            if (!StartsWith(prefix))
                return false;

            int originalPosition = _position;

            int fontStart = _position + prefix.Length;
            int fontEnd = _text.IndexOf(']', fontStart);

            if (fontEnd < 0)
            {
                _position = originalPosition;
                return false;
            }

            string fontExpression = _text.Substring(fontStart, fontEnd - fontStart).Trim();
            string fontName;

            if (!TryParseQuotedValue(fontExpression, out fontName))
            {
                _position = originalPosition;
                return false;
            }

            _position = fontEnd + 1;

            List<InlineNode> children;
            bool closed = ParseUntil(close, out children);

            if (!closed)
            {
                _position = originalPosition;
                return false;
            }

            FontInline fontInline = new FontInline();
            fontInline.FontName = fontName;
            fontInline.Children.AddRange(children);

            node = fontInline;
            return true;
        }

        static bool TryParseQuotedValue(string value, out string result)
        {
            result = string.Empty;

            if (string.IsNullOrEmpty(value))
                return false;

            if (value.Length < 2)
                return false;

            if (value[0] != '"' || value[value.Length - 1] != '"')
                return false;

            result = value.Substring(1, value.Length - 2);

            return result.Length > 0;
        }
        
        bool TryParseLink(out InlineNode node)
        {
            node = null;

            if (End || Current != '[')
                return false;

            int originalPosition = _position;

            // Skip '['
            _position++;

            List<InlineNode> labelNodes;
            bool closedLabel = ParseUntil("]", out labelNodes);

            if (!closedLabel)
            {
                _position = originalPosition;
                return false;
            }

            if (End || Current != '(')
            {
                _position = originalPosition;
                return false;
            }

            // Skip '('
            _position++;

            int urlStart = _position;
            int urlEnd = _text.IndexOf(')', urlStart);

            if (urlEnd < 0)
            {
                _position = originalPosition;
                return false;
            }

            string url = _text.Substring(urlStart, urlEnd - urlStart).Trim();

            _position = urlEnd + 1;

            LinkInline link = new LinkInline();
            link.Url = url;
            link.Children.AddRange(labelNodes);

            node = link;
            return true;
        }

        bool TryParseEscape(out InlineNode node)
        {
            node = null;

            if (End || Current != '\\')
                return false;

            if (_position + 1 >= _text.Length)
                return false;

            char escaped = _text[_position + 1];

            TextInline text = new TextInline();
            text.Text = escaped.ToString();

            _position += 2;

            node = text;
            return true;
        }

        InlineNode ParseText(string closingDelimiter)
        {
            StringBuilder builder = new StringBuilder();

            while (!End)
            {
                if (closingDelimiter != null && StartsWith(closingDelimiter))
                    break;

                if (StartsWith("!["))
                    break;

                if (StartsWith("**"))
                    break;

                if (Current == '*')
                    break;

                if (Current == '`')
                    break;

                if (Current == '[')
                    break;

                if (Current == '\\')
                    break;

                builder.Append(Current);
                _position++;
            }

            if (builder.Length == 0 && !End)
            {
                builder.Append(Current);
                _position++;
            }

            TextInline text = new TextInline();
            text.Text = builder.ToString();

            return text;
        }
    }
}
