using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LcdMod.Client.Markdown;
using LcdMod.Client.Markdown.Inline;
using LcdMod.Client.Markdown.Inline.NonStandard;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.UserControls
{
    public static class MarkdownPanel
    {
        const string DEFAULT_FONT = "White";
        const string DEFAULT_BOLD_FONT = "White-Bold";
        const string DEFAULT_ITALIC_FONT = "White-Italic";
        const string DEFAULT_BOLD_ITALIC_FONT = "White-Bold-Italic";
        const string CODE_FONT = "DEBUG";
        const string MONOSPACE_FONT = "LcdMod_Monospace";
        const float TITLE_BAR_HEIGHT_BASE = 40f;

        public static void CreateSprites(
            MarkdownDocument document,
            RectangleF viewBox,
            Color headerColor,
            Color textColor,
            List<MySprite> sprites,
            IAppHost context)
        {
            if (document == null || sprites == null || context == null)
                return;

            if (viewBox.Width <= 1f || viewBox.Height <= 1f)
                return;

            var renderer = new Renderer(context, viewBox, headerColor, textColor, sprites);
            renderer.Render(document);
        }

        public static RectangleF GetContentViewBox(IAppHost context)
        {
            if (context == null)
                return new RectangleF();

            var viewBox = context.ViewBox;
            if (!context.TitleVisible)
                return viewBox;

            float layoutScale = context.Scale * context.Surface.FontSize;
            float contentTop = viewBox.Y + TITLE_BAR_HEIGHT_BASE * layoutScale;
            return new RectangleF(
                viewBox.X,
                contentTop,
                viewBox.Width,
                Math.Max(0f, viewBox.Bottom - contentTop));
        }

        sealed class Renderer
        {
            readonly IAppHost _context;
            readonly RectangleF _viewBox;
            readonly Color _headerColor;
            readonly Color _textColor;
            readonly List<MySprite> _sprites;
            readonly StringBuilder _measureBuffer = new StringBuilder();
            readonly List<string> _spriteLibrary = new List<string>();
            readonly List<TextRun> _runs = new List<TextRun>();
            readonly LineState _line = new LineState();

            float _cursorY;
            float _contentLeft;
            float _contentRight;
            float _contentBottom;
            float _layoutScale;

            public Renderer(
                IAppHost context,
                RectangleF viewBox,
                Color headerColor,
                Color textColor,
                List<MySprite> sprites)
            {
                _context = context;
                _viewBox = viewBox;
                _headerColor = headerColor;
                _textColor = textColor;
                _sprites = sprites;
                _layoutScale = Math.Max(0.1f, _context.Scale * _context.Surface.FontSize);
            }

            public void Render(MarkdownDocument document)
            {
                float padding = Math.Max(2f, 5f * _layoutScale);
                _contentLeft = _viewBox.X + padding;
                _contentRight = _viewBox.Right - padding;
                _contentBottom = _viewBox.Bottom - padding;
                _cursorY = _viewBox.Y + padding;

                if (_contentRight <= _contentLeft || _contentBottom <= _cursorY)
                    return;

                _sprites.Add(MySprite.CreateClipRect(new Rectangle(
                    (int)_viewBox.X,
                    (int)_viewBox.Y,
                    Math.Max(1, (int)_viewBox.Width),
                    Math.Max(1, (int)_viewBox.Height))));

                for (int i = 0; i < document.Blocks.Count; i++)
                {
                    if (_cursorY > _contentBottom)
                        break;

                    RenderBlock(document.Blocks[i], 0f);
                }

                _sprites.Add(MySprite.CreateClearClipRect());
            }

            void RenderBlock(BlockNode block, float indent)
            {
                var heading = block as HeadingBlock;
                if (heading != null)
                {
                    RenderInlineBlock(heading.Inlines, indent, HeadingScale(heading.Level), _headerColor);
                    AddBlockGap(heading.Level <= 2 ? 8f : 6f);
                    return;
                }

                var paragraph = block as ParagraphBlock;
                if (paragraph != null)
                {
                    RenderInlineBlock(paragraph.Inlines, indent, 1f, _textColor);
                    AddBlockGap(7f);
                    return;
                }

                var code = block as CodeBlock;
                if (code != null)
                {
                    RenderCodeBlock(code, indent);
                    AddBlockGap(8f);
                    return;
                }

                var list = block as ListBlock;
                if (list != null)
                {
                    RenderListBlock(list, indent);
                    AddBlockGap(5f);
                    return;
                }

                var quote = block as BlockQuoteBlock;
                if (quote != null)
                {
                    float quoteIndent = Math.Max(10f, 14f * _layoutScale);
                    for (int i = 0; i < quote.Children.Count; i++)
                        RenderBlock(quote.Children[i], indent + quoteIndent);
                    AddBlockGap(5f);
                    return;
                }

                if (block is ThematicBreakBlock)
                {
                    RenderThematicBreak(indent);
                    AddBlockGap(8f);
                }
            }

            void RenderInlineBlock(
                List<InlineNode> inlines,
                float indent,
                float relativeScale,
                Color defaultColor)
            {
                _runs.Clear();
                var style = new TextStyle(
                    defaultColor,
                    DEFAULT_FONT,
                    Math.Max(0.05f, relativeScale * _context.Scale * _context.Surface.FontSize));
                CollectRuns(inlines, style, _runs);

                float x = _contentLeft + indent;
                float availableWidth = Math.Max(1f, _contentRight - x);
                RenderRuns(_runs, x, availableWidth, false);
            }

            void RenderCodeBlock(CodeBlock block, float indent)
            {
                var style = new TextStyle(
                    _textColor,
                    CODE_FONT,
                    Math.Max(0.05f, 0.82f * _context.Scale * _context.Surface.FontSize));

                float x = _contentLeft + indent;
                float availableWidth = Math.Max(1f, _contentRight - x);

                string text = TextWrappingHelper.NormalizeNewlines(block.Text ?? string.Empty);
                string[] lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    _runs.Clear();
                    _runs.Add(new TextRun(lines[i].TrimEnd('\r'), style));
                    RenderRuns(_runs, x, availableWidth, true);
                }
            }

            void RenderListBlock(ListBlock block, float indent)
            {
                float scale = _context.Scale * _context.Surface.FontSize;
                float markerWidth = GetListMarkerWidth(block, scale);
                float markerGap = Math.Max(6f, 8f * _layoutScale);
                float markerLeft = _contentLeft + indent ;
                float textX = markerLeft + markerWidth + markerGap;
                float availableWidth = Math.Max(1f, _contentRight - textX);
                TextStyle textStyle = new TextStyle(_textColor, DEFAULT_FONT, scale);

                for (int i = 0; i < block.Items.Count; i++)
                {
                    if (_cursorY > _contentBottom)
                        break;

                    string marker = block.Ordered
                        ? (i + 1).ToString(CultureInfo.InvariantCulture) + "."
                        : "-";

                    DrawTextRun(
                        new TextRun(marker, new TextStyle(_textColor, DEFAULT_FONT, scale)),
                        markerLeft,
                        _cursorY,
                        TextAlignment.LEFT);

                    var item = block.Items[i];
                    CollectListItemRuns(item, textStyle, _runs);
                    if (_runs.Count == 0)
                    {
                        _cursorY += GetLineHeight(DEFAULT_FONT, scale);
                    }
                    else
                    {
                        RenderRuns(_runs, textX, availableWidth, false);
                    }

                    AddBlockGap(2f);
                }
            }

            float GetListMarkerWidth(ListBlock block, float scale)
            {
                if (block == null || block.Items.Count == 0)
                    return Math.Max(8f, 10f * _layoutScale);

                string marker = block.Ordered
                    ? block.Items.Count.ToString(CultureInfo.InvariantCulture) + "."
                    : "-";

                return Math.Max(8f, Measure(marker, DEFAULT_FONT, scale).X);
            }

            void CollectListItemRuns(ListItemBlock item, TextStyle style, List<TextRun> result)
            {
                result.Clear();
                if (item == null || item.Children.Count == 0)
                    return;

                for (int i = 0; i < item.Children.Count; i++)
                {
                    if (result.Count > 0)
                        result.Add(TextRun.Break(style));

                    CollectBlockRuns(item.Children[i], style, result);
                }
            }

            void CollectBlockRuns(BlockNode block, TextStyle style, List<TextRun> result)
            {
                var heading = block as HeadingBlock;
                if (heading != null)
                {
                    CollectRuns(heading.Inlines, style, result);
                    return;
                }

                var paragraph = block as ParagraphBlock;
                if (paragraph != null)
                {
                    CollectRuns(paragraph.Inlines, style, result);
                    return;
                }

                var code = block as CodeBlock;
                if (code != null)
                {
                    AddTextRun(result, new TextRun(TextWrappingHelper.NormalizeNewlines(code.Text ?? string.Empty).Trim(), style.WithFont(CODE_FONT)));
                    return;
                }
            }

            void RenderThematicBreak(float indent)
            {
                float height = Math.Max(1f, 2f * _layoutScale);
                float x = _contentLeft + indent;
                float width = Math.Max(1f, _contentRight - x);
                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(x + width * 0.5f, _cursorY + height * 0.5f),
                    Size = new Vector2(width, height),
                    Color = _textColor,
                    Alignment = TextAlignment.CENTER
                });

                _cursorY += height;
            }

            void RenderRuns(List<TextRun> runs, float x, float availableWidth, bool preserveWhitespace)
            {
                _line.Reset(x, availableWidth);

                for (int i = 0; i < runs.Count; i++)
                {
                    var run = runs[i];
                    if (run.LineBreak)
                    {
                        FlushLine();
                        continue;
                    }

                    if (run.IsImage)
                    {
                        AppendImageRun(run);
                        continue;
                    }

                    if (string.IsNullOrEmpty(run.Text))
                        continue;

                    if (preserveWhitespace)
                        AppendPreformattedRun(run);
                    else
                        AppendWrappedRun(run);
                }

                FlushLine();
            }

            void AppendImageRun(TextRun run)
            {
                Vector2 size = GetImageBoxSize(run, _line.AvailableWidth);

                if (_line.HasText && _line.Width + size.X > _line.AvailableWidth)
                    FlushLine();

                _line.Add(run, size.X, size.Y);
            }

            void AppendWrappedRun(TextRun run)
            {
                TextWrappingHelper.AppendWrappedWords(
                    run.Text,
                    () => _line.HasText,
                    token => AppendToken(new TextRun(token, run.Style), true),
                    FlushLine);
            }

            void AppendPreformattedRun(TextRun run)
            {
                TextWrappingHelper.AppendPreformattedCharacters(
                    run.Text,
                    token => AppendToken(new TextRun(token, run.Style), false),
                    FlushLine);
            }

            void AppendToken(TextRun token, bool trimOnWrap)
            {
                if (string.IsNullOrEmpty(token.Text))
                    return;

                float tokenWidth = Measure(token.Text, token.FontId, token.Scale).X;

                if (_line.HasText && _line.Width + tokenWidth > _line.AvailableWidth)
                {
                    FlushLine();
                    if (trimOnWrap)
                    {
                        token = new TextRun(token.Text.TrimStart(), token.Style);
                        tokenWidth = Measure(token.Text, token.FontId, token.Scale).X;
                    }
                }

                if (tokenWidth > _line.AvailableWidth && token.Text.Length > 1)
                {
                    AppendOversizedToken(token);
                    return;
                }

                _line.Add(token, tokenWidth, GetLineHeight(token.FontId, token.Scale));
            }

            void AppendOversizedToken(TextRun token)
            {
                for (int i = 0; i < token.Text.Length; i++)
                {
                    string value = token.Text[i].ToString();
                    float width = Measure(value, token.FontId, token.Scale).X;

                    if (_line.HasText && _line.Width + width > _line.AvailableWidth)
                        FlushLine();

                    _line.Add(new TextRun(value, token.Style), width, GetLineHeight(token.FontId, token.Scale));
                }
            }

            void FlushLine()
            {
                if (!_line.HasText)
                {
                    _cursorY += GetLineHeight(DEFAULT_FONT, _context.Scale * _context.Surface.FontSize);
                    return;
                }

                if (_cursorY <= _contentBottom)
                {
                    float x = _line.X;
                    for (int i = 0; i < _line.Runs.Count; i++)
                    {
                        var segment = _line.Runs[i];
                        if (segment.Run.IsImage)
                            DrawImageRun(segment.Run, x, _cursorY);
                        else
                            DrawTextRun(segment.Run, x, _cursorY);

                        x += segment.Width;
                    }
                }

                _cursorY += Math.Max(1f, _line.Height) + Math.Max(1f, 2f * _layoutScale);
                _line.Clear();
            }

            void DrawImageRun(TextRun run, float x, float y)
            {
                Vector2 size = GetImageBoxSize(run, _line.AvailableWidth);
                if (run.ImageKind == ImageType.Monospace)
                {
                    string text = DecodeMonospaceImageText(run.Text);
                    if (string.IsNullOrEmpty(text))
                        return;

                    _sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXT,
                        Data = text,
                        Position = new Vector2(x, y),
                        Color = run.Color,
                        Alignment = TextAlignment.LEFT,
                        FontId = MONOSPACE_FONT,
                        RotationOrScale = GetMonospaceImageScale(text, size)
                    });
                    return;
                }

                string spriteName = ResolveSpriteName(run.Text);
                if (string.IsNullOrEmpty(spriteName))
                    return;

                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = spriteName,
                    Position = new Vector2(x + size.X * 0.5f, y + size.Y * 0.5f),
                    Size = size,
                    Color = Color.White,
                    Alignment = TextAlignment.CENTER
                });
            }

            string ResolveSpriteName(string spriteName)
            {
                if (string.IsNullOrEmpty(spriteName))
                    return string.Empty;

                if (!IsBlockIconSpriteName(spriteName) || SurfaceHasSprite(spriteName))
                    return spriteName;

                string registeredSpriteName;
                return BlockIconHelper.TryGetOrAddTextureForBlockName(spriteName, out registeredSpriteName)
                    ? registeredSpriteName
                    : spriteName;
            }

            bool SurfaceHasSprite(string spriteName)
            {
                _spriteLibrary.Clear();
                _context.Surface.GetSprites(_spriteLibrary);
                return _spriteLibrary.Contains(spriteName);
            }

            static bool IsBlockIconSpriteName(string spriteName)
            {
                return !string.IsNullOrEmpty(spriteName) &&
                       spriteName.StartsWith("MyObjectBuilder_", StringComparison.Ordinal);
            }

            void DrawTextRun(TextRun run, float x, float y)
            {
                DrawTextRun(run, x, y, TextAlignment.LEFT);
            }

            void DrawTextRun(TextRun run, float x, float y, TextAlignment alignment)
            {
                if (string.IsNullOrEmpty(run.Text))
                    return;

                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = run.Text,
                    Position = new Vector2(x, y),
                    Color = run.Color,
                    Alignment = alignment,
                    FontId = run.FontId,
                    RotationOrScale = run.Scale
                });

                if (run.Underline)
                    DrawTextDecorationLine(run, x, y, alignment, 0.88f);

                if (run.Strikethrough)
                    DrawTextDecorationLine(run, x, y, alignment, 0.52f);
            }

            void DrawTextDecorationLine(TextRun run, float x, float y, TextAlignment alignment, float lineHeightFactor)
            {
                float width = Measure(run.Text, run.FontId, run.Scale).X;
                if (width <= 0f)
                    return;

                float height = GetLineHeight(run.FontId, run.Scale);
                float lineX = x + width * 0.5f;
                if (alignment == TextAlignment.RIGHT)
                    lineX = x - width * 0.5f;
                else if (alignment == TextAlignment.CENTER)
                    lineX = x;

                float thickness = Math.Max(1f, _layoutScale);
                _sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(lineX, y + height * lineHeightFactor),
                    Size = new Vector2(width, thickness),
                    Color = run.Color,
                    Alignment = TextAlignment.CENTER
                });
            }

            void CollectRuns(List<InlineNode> inlines, TextStyle style, List<TextRun> result)
            {
                if (inlines == null)
                    return;

                for (int i = 0; i < inlines.Count; i++)
                    CollectRun(inlines[i], style, result);
            }

            void CollectRun(InlineNode inline, TextStyle style, List<TextRun> result)
            {
                var text = inline as TextInline;
                if (text != null)
                {
                    AddTextRun(result, new TextRun(text.Text, style));
                    return;
                }

                var code = inline as CodeInline;
                if (code != null)
                {
                    AddTextRun(result, new TextRun(code.Code, style.WithFont(CODE_FONT).WithScale(style.Scale * 0.95f)));
                    return;
                }

                var lineBreak = inline as LineBreakInline;
                if (lineBreak != null)
                {
                    result.Add(TextRun.Break(style));
                    return;
                }

                var emphasis = inline as EmphasisInline;
                if (emphasis != null)
                {
                    CollectRuns(emphasis.Children, style.WithItalic(), result);
                    return;
                }

                var strong = inline as StrongInline;
                if (strong != null)
                {
                    CollectRuns(strong.Children, style.WithBold(), result);
                    return;
                }

                var underline = inline as UnderlineInline;
                if (underline != null)
                {
                    CollectRuns(underline.Children, style.WithUnderline(), result);
                    return;
                }

                var strikethrough = inline as StrikethroughInline;
                if (strikethrough != null)
                {
                    CollectRuns(strikethrough.Children, style.WithStrikethrough(), result);
                    return;
                }

                var link = inline as LinkInline;
                if (link != null)
                {
                    CollectRuns(link.Children, style, result);
                    return;
                }

                var image = inline as ImageInline;
                if (image != null)
                {
                    AddTextRun(result, new TextRun(image, style));
                    return;
                }

                var color = inline as ColorInline;
                if (color != null)
                {
                    Color parsed;
                    CollectRuns(color.Children, TryParseColor(color.Color, out parsed) ? style.WithColor(parsed) : style, result);
                    return;
                }

                var font = inline as FontInline;
                if (font != null)
                {
                    CollectRuns(font.Children, string.IsNullOrEmpty(font.FontName) ? style : style.WithFont(font.FontName), result);
                    return;
                }

                var loc = inline as LocInline;
                if (loc != null)
                {
                    AddTextRun(result, new TextRun(LocHelper.GetLoc(loc.Key), style));
                }
            }

            void AddTextRun(List<TextRun> result, TextRun run)
            {
                if (string.IsNullOrEmpty(run.Text))
                    return;

                if (run.IsImage)
                {
                    result.Add(run);
                    return;
                }

                if (result.Count > 0)
                {
                    var previous = result[result.Count - 1];
                    if (!previous.LineBreak && !previous.IsImage && previous.Style.Equals(run.Style))
                    {
                        result[result.Count - 1] = new TextRun(previous.Text + run.Text, previous.Style);
                        return;
                    }
                }

                result.Add(run);
            }

            Vector2 Measure(string text, string fontId, float scale)
            {
                _measureBuffer.Clear();
                _measureBuffer.Append(text);
                return _context.Surface.MeasureStringInPixels(_measureBuffer, fontId, scale);
            }

            Vector2 GetImageBoxSize(TextRun run, float availableWidth)
            {
                float width = ResolveImageDimension(run.ImageWidth, availableWidth, run.ImageSizeType);
                float height = ResolveImageDimension(run.ImageHeight, availableWidth, run.ImageSizeType);
                return new Vector2(width, height);
            }

            float ResolveImageDimension(float value, float availableWidth, SizeType sizeType)
            {
                value = Math.Max(1f, value);
                if (sizeType == SizeType.Percent)
                    return Math.Max(1f, availableWidth * value / 100f);

                return value * _layoutScale;
            }

            float GetMonospaceImageScale(string text, Vector2 imageBoxSize)
            {
                Vector2 measured = Measure(text, MONOSPACE_FONT, 1f);
                if (measured.X <= 0f || measured.Y <= 0f)
                    return _context.Scale * _context.Surface.FontSize;

                return Math.Max(0.01f, Math.Min(imageBoxSize.X / measured.X, imageBoxSize.Y / measured.Y));
            }

            static string DecodeMonospaceImageText(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return string.Empty;

                return value.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\t", "\t");
            }

            float GetLineHeight(string fontId, float scale)
            {
                return Math.Max(1f, FormatingHelper.LineHeight(scale, _context.Surface, fontId));
            }

            void AddBlockGap(float value)
            {
                _cursorY += Math.Max(1f, value * _layoutScale);
            }

            static float HeadingScale(int level)
            {
                switch (level)
                {
                    case 1:
                        return 1.45f;
                    case 2:
                        return 1.3f;
                    case 3:
                        return 1.16f;
                    case 4:
                        return 1.05f;
                    case 5:
                        return 0.96f;
                    default:
                        return 0.9f;
                }
            }

            static bool TryParseColor(string value, out Color color)
            {
                color = Color.White;
                if (string.IsNullOrEmpty(value) || value.Length != 7 || value[0] != '#')
                    return false;

                int r;
                int g;
                int b;
                if (!int.TryParse(value.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
                    !int.TryParse(value.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
                    !int.TryParse(value.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                    return false;

                color = new Color(r, g, b);
                return true;
            }
        }

        struct TextStyle : IEquatable<TextStyle>
        {
            public readonly Color Color;
            public readonly string FontId;
            public readonly float Scale;
            public readonly bool Underline;
            public readonly bool Strikethrough;

            public TextStyle(Color color, string fontId, float scale)
                : this(color, fontId, scale, false, false)
            {
            }

            TextStyle(Color color, string fontId, float scale, bool underline, bool strikethrough)
            {
                Color = color;
                FontId = NormalizeFontId(fontId);
                Scale = scale;
                Underline = underline;
                Strikethrough = strikethrough;
            }

            public TextStyle WithColor(Color color)
            {
                return new TextStyle(color, FontId, Scale, Underline, Strikethrough);
            }

            public TextStyle WithFont(string fontId)
            {
                return new TextStyle(Color, fontId, Scale, Underline, Strikethrough);
            }

            public TextStyle WithBold()
            {
                return new TextStyle(Color, GetBoldFont(FontId), Scale, Underline, Strikethrough);
            }

            public TextStyle WithItalic()
            {
                return new TextStyle(Color, GetItalicFont(FontId), Scale, Underline, Strikethrough);
            }

            public TextStyle WithUnderline()
            {
                return new TextStyle(Color, FontId, Scale, true, Strikethrough);
            }

            public TextStyle WithStrikethrough()
            {
                return new TextStyle(Color, FontId, Scale, Underline, true);
            }

            public TextStyle WithScale(float scale)
            {
                return new TextStyle(Color, FontId, scale, Underline, Strikethrough);
            }

            public bool Equals(TextStyle other)
            {
                return Color == other.Color &&
                       string.Equals(FontId, other.FontId, StringComparison.Ordinal) &&
                       Math.Abs(Scale - other.Scale) < 0.0001f &&
                       Underline == other.Underline &&
                       Strikethrough == other.Strikethrough;
            }

            static string GetBoldFont(string fontId)
            {
                if (string.Equals(fontId, DEFAULT_FONT, StringComparison.Ordinal))
                    return DEFAULT_BOLD_FONT;

                if (string.Equals(fontId, DEFAULT_ITALIC_FONT, StringComparison.Ordinal))
                    return DEFAULT_BOLD_ITALIC_FONT;

                return fontId;
            }

            static string GetItalicFont(string fontId)
            {
                if (string.Equals(fontId, DEFAULT_FONT, StringComparison.Ordinal))
                    return DEFAULT_ITALIC_FONT;

                if (string.Equals(fontId, DEFAULT_BOLD_FONT, StringComparison.Ordinal))
                    return DEFAULT_BOLD_ITALIC_FONT;

                return fontId;
            }

            static string NormalizeFontId(string fontId)
            {
                if (string.IsNullOrEmpty(fontId))
                    return DEFAULT_FONT;

                if (string.Equals(fontId, "white", StringComparison.OrdinalIgnoreCase))
                    return DEFAULT_FONT;

                if (string.Equals(fontId, "debug", StringComparison.OrdinalIgnoreCase))
                    return CODE_FONT;

                if (string.Equals(fontId, "monospace", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fontId, MONOSPACE_FONT, StringComparison.OrdinalIgnoreCase))
                    return MONOSPACE_FONT;

                return fontId;
            }
        }

        struct TextRun
        {
            public readonly string Text;
            public readonly TextStyle Style;
            public readonly bool LineBreak;
            public readonly ImageType ImageKind;
            public readonly float ImageWidth;
            public readonly float ImageHeight;
            public readonly SizeType ImageSizeType;
            public readonly bool IsImage;

            public TextRun(string text, TextStyle style)
            {
                Text = text ?? string.Empty;
                Style = style;
                LineBreak = false;
                ImageKind = ImageType.Unknown;
                ImageWidth = 0f;
                ImageHeight = 0f;
                ImageSizeType = SizeType.Pixel;
                IsImage = false;
            }

            public TextRun(ImageInline image, TextStyle style)
            {
                Text = image == null ? string.Empty : image.Source ?? string.Empty;
                Style = style;
                LineBreak = false;
                ImageKind = image == null ? ImageType.Unknown : image.Kind;
                ImageWidth = image == null ? 0f : image.Width;
                ImageHeight = image == null ? 0f : image.Height;
                ImageSizeType = image == null ? SizeType.Pixel : image.SizeType;
                IsImage = true;
            }

            TextRun(TextStyle style, bool lineBreak)
            {
                Text = string.Empty;
                Style = style;
                LineBreak = lineBreak;
                ImageKind = ImageType.Unknown;
                ImageWidth = 0f;
                ImageHeight = 0f;
                ImageSizeType = SizeType.Pixel;
                IsImage = false;
            }

            public Color Color { get { return Style.Color; } }
            public string FontId { get { return Style.FontId; } }
            public float Scale { get { return Style.Scale; } }
            public bool Underline { get { return Style.Underline; } }
            public bool Strikethrough { get { return Style.Strikethrough; } }

            public static TextRun Break(TextStyle style)
            {
                return new TextRun(style, true);
            }
        }

        struct LineSegment
        {
            public readonly TextRun Run;
            public readonly float Width;

            public LineSegment(TextRun run, float width)
            {
                Run = run;
                Width = width;
            }
        }

        sealed class LineState
        {
            public readonly List<LineSegment> Runs = new List<LineSegment>();
            public float X;
            public float AvailableWidth;
            public float Width;
            public float Height;
            public bool HasText { get { return Runs.Count > 0; } }

            public void Reset(float x, float availableWidth)
            {
                X = x;
                AvailableWidth = availableWidth;
                Clear();
            }

            public void Clear()
            {
                Runs.Clear();
                Width = 0f;
                Height = 0f;
            }

            public void Add(TextRun run, float width, float height)
            {
                if (Runs.Count > 0)
                {
                    var previous = Runs[Runs.Count - 1];
                    if (!previous.Run.LineBreak && !run.LineBreak &&
                        !previous.Run.IsImage && !run.IsImage &&
                        previous.Run.Style.Equals(run.Style))
                    {
                        Runs[Runs.Count - 1] = new LineSegment(
                            new TextRun(previous.Run.Text + run.Text, run.Style),
                            previous.Width + width);
                        Width += width;
                        Height = Math.Max(Height, height);
                        return;
                    }
                }

                Runs.Add(new LineSegment(run, width));
                Width += width;
                Height = Math.Max(Height, height);
            }
        }
    }
}
