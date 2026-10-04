using System;
using System.Collections.Generic;
using System.Globalization;
using Adk.Gui.Controls;
using Adk.Gui.Controls.Documents;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Layout;
using Adk.Gui.Rendering;
using LcdMod.Client.Helpers;
using LcdMod.Client.Markdown;
using LcdMod.Client.Markdown.Inline;
using LcdMod.Client.Markdown.Inline.NonStandard;
using Inline = Adk.Gui.Controls.Documents.Inline;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps.Markdown
{
    /// <summary>
    /// Renders a parsed Markdown document as ADK elements: one wrapping TextBlock with inlines per paragraph, plus
    /// panels for lists, quotes, code and rules. Colors come from the md-* style classes.
    /// </summary>
    internal sealed class MarkdownPresenter : Decorator
    {
        public static readonly StyledProperty<MarkdownDocument> DocumentProperty =
            StyledProperty.Register<MarkdownPresenter, MarkdownDocument>(nameof(Document), null, UiInvalidation.Measure);
        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<MarkdownPresenter, float>(nameof(FontSize), .8f, UiInvalidation.Measure);

        const string CODE_FONT = "DEBUG";
        const string MONOSPACE_FONT = MOD_PREFIX + "Monospace";

        MarkdownDocument _built;
        float _builtSize = -1f;

        public MarkdownDocument Document
        {
            get { return GetValue(DocumentProperty); }
            set { SetValue(DocumentProperty, value); }
        }

        public float FontSize
        {
            get { return GetValue(FontSizeProperty); }
            set { SetValue(FontSizeProperty, value); }
        }

        protected override void OnPropertyChanged(IStyledProperty property)
        {
            base.OnPropertyChanged(property);
            if (property == DocumentProperty || property == FontSizeProperty)
                Rebuild();
        }

        void Rebuild()
        {
            MarkdownDocument document = Document;
            if (ReferenceEquals(document, _built) && Math.Abs(_builtSize - FontSize) < .0001f)
                return;
            _built = document;
            _builtSize = FontSize;
            var root = new StackPanel { StyleClass = "md-document" };
            if (document != null)
                AddBlocks(root, document.Blocks);
            Child = root;
        }

        void AddBlocks(StackPanel target, IList<BlockNode> blocks)
        {
            if (blocks == null)
                return;
            for (int i = 0; i < blocks.Count; i++)
            {
                VisualElement element = CreateBlock(blocks[i]);
                if (element != null)
                    target.AddChild(element);
            }
        }

        VisualElement CreateBlock(BlockNode block)
        {
            var heading = block as HeadingBlock;
            if (heading != null)
                return Paragraph(heading.Inlines, "md-h" + Math.Max(1, Math.Min(6, heading.Level)), HeadingScale(heading.Level));
            var paragraph = block as ParagraphBlock;
            if (paragraph != null)
                return Paragraph(paragraph.Inlines, "md-paragraph", 1f);
            var subtext = block as SubtextBlock;
            if (subtext != null)
                return Paragraph(subtext.Inlines, "md-subtext", .78f);
            var code = block as CodeBlock;
            if (code != null)
                return new Border
                {
                    StyleClass = "md-code",
                    Child = new TextBlock
                    {
                        StyleClass = "md-code-text",
                        Text = (code.Text ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\n'),
                        FontFamily = CODE_FONT,
                        FontSize = FontSize * .82f,
                        TextWrapping = TextWrapping.Wrap
                    }
                };
            var quote = block as BlockQuoteBlock;
            if (quote != null)
            {
                var content = new StackPanel { StyleClass = "md-quote-content" };
                AddBlocks(content, quote.Children);
                var grid = new Grid { StyleClass = "md-quote" };
                grid.SetColumnDefinitions(GridLength.Auto, new GridLength(1, GridUnitType.Star));
                grid.Set(new Border { StyleClass = "md-quote-bar" }, 0, 0);
                grid.Set(content, 1, 0);
                return grid;
            }
            var list = block as ListBlock;
            if (list != null)
                return List(list);
            if (block is ThematicBreakBlock)
                return new Border { StyleClass = "md-rule" };
            return null;
        }

        VisualElement List(ListBlock list)
        {
            var panel = new StackPanel { StyleClass = "md-list" };
            for (int i = 0; i < list.Items.Count; i++)
            {
                var content = new StackPanel { StyleClass = "md-list-item" };
                AddBlocks(content, list.Items[i].Children);
                var row = new Grid();
                row.SetColumnDefinitions(GridLength.Auto, new GridLength(1, GridUnitType.Star));
                row.Set(new TextBlock
                {
                    StyleClass = "md-bullet",
                    Text = list.Ordered ? (i + 1).ToString(CultureInfo.InvariantCulture) + "." : "•",
                    FontSize = FontSize
                }, 0, 0);
                row.Set(content, 1, 0);
                panel.AddChild(row);
            }
            return panel;
        }

        TextBlock Paragraph(IList<InlineNode> inlines, string styleClass, float scale)
        {
            var text = new TextBlock
            {
                StyleClass = styleClass,
                FontFamily = "White",
                FontSize = FontSize * scale,
                TextWrapping = TextWrapping.Wrap
            };
            AddInlines(text.Inlines, inlines);
            text.InvalidateInlines();
            return text;
        }

        void AddInlines(InlineCollection target, IList<InlineNode> inlines)
        {
            if (inlines == null)
                return;
            for (int i = 0; i < inlines.Count; i++)
            {
                Inline inline = CreateInline(inlines[i]);
                if (inline != null)
                    target.Add(inline);
            }
        }

        Inline CreateInline(InlineNode node)
        {
            var text = node as TextInline;
            if (text != null)
                return new Run(text.Text);
            if (node is LineBreakInline)
                return new LineBreak();
            var emphasis = node as EmphasisInline;
            if (emphasis != null)
                return WithChildren(new Italic(), emphasis.Children);
            var strong = node as StrongInline;
            if (strong != null)
                return WithChildren(new Bold(), strong.Children);
            var underline = node as UnderlineInline;
            if (underline != null)
                return WithChildren(new Underline(), underline.Children);
            var strike = node as StrikethroughInline;
            if (strike != null)
                return WithChildren(new Span { TextDecorations = TextDecorations.Strikethrough }, strike.Children);
            var link = node as LinkInline;
            if (link != null)
                return WithChildren(new Span { TextDecorations = TextDecorations.Underline }, link.Children);
            var code = node as CodeInline;
            if (code != null)
                return new Run(code.Code) { FontFamily = CODE_FONT };
            var color = node as ColorInline;
            if (color != null)
            {
                Color parsed;
                var span = new Span();
                if (TryParseColor(color.Color, out parsed))
                    span.Foreground = parsed;
                return WithChildren(span, color.Children);
            }
            var font = node as FontInline;
            if (font != null)
                return WithChildren(new Span { FontFamily = string.IsNullOrEmpty(font.FontName) ? null : NormalizeFont(font.FontName) },
                    font.Children);
            var loc = node as LocInline;
            if (loc != null)
                return new Run(LocHelper.GetLoc(loc.Key));
            var image = node as ImageInline;
            if (image != null)
                return new InlineUIContainer(new MarkdownImage(image, FontSize));
            return null;
        }

        Span WithChildren(Span span, IList<InlineNode> children)
        {
            AddInlines(span.Inlines, children);
            return span;
        }

        static string NormalizeFont(string font)
        {
            if (string.Equals(font, "white", StringComparison.OrdinalIgnoreCase))
                return "White";
            if (string.Equals(font, "debug", StringComparison.OrdinalIgnoreCase))
                return CODE_FONT;
            if (string.Equals(font, "monospace", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(font, MONOSPACE_FONT, StringComparison.OrdinalIgnoreCase))
                return MONOSPACE_FONT;
            return font;
        }

        static float HeadingScale(int level)
        {
            switch (level)
            {
                case 1: return 1.45f;
                case 2: return 1.3f;
                case 3: return 1.16f;
                case 4: return 1.05f;
                case 5: return .96f;
                default: return .9f;
            }
        }

        internal static bool TryParseColor(string value, out Color color)
        {
            color = Color.White;
            if (string.IsNullOrEmpty(value) || value.Length != 7 && value.Length != 9 || value[0] != '#')
                return false;
            int a = 255;
            int offset = 1;
            if (value.Length == 9)
            {
                if (!int.TryParse(value.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
                    return false;
                offset = 3;
            }
            int r, g, b;
            if (!int.TryParse(value.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
                !int.TryParse(value.Substring(offset + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
                !int.TryParse(value.Substring(offset + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                return false;
            color = new Color(r, g, b, a);
            return true;
        }
    }

    /// <summary>
    /// An inline Markdown image: a sprite, or monospace text art scaled into its box. Sizes are pixels at a font size of
    /// one, or a percentage of the available width.
    /// </summary>
    internal sealed class MarkdownImage : VisualElement
    {
        const string MONOSPACE_FONT = MOD_PREFIX + "Monospace";

        readonly ImageInline _image;
        readonly float _fontSize;
        readonly string _sprite;
        readonly string _monospace;

        public MarkdownImage(ImageInline image, float fontSize)
        {
            _image = image;
            _fontSize = Math.Max(.05f, fontSize);
            if (image.Kind == ImageType.Monospace)
                _monospace = (image.Source ?? string.Empty).Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\t", "\t");
            else
                _sprite = ResolveSprite(image.Source);
        }

        /// <summary>Maps a block definition name to a registered sprite; set by the client once textures are available.</summary>
        internal static Func<string, string> BlockSpriteResolver { get; set; }

        static string ResolveSprite(string sprite)
        {
            Func<string, string> resolver = BlockSpriteResolver;
            if (resolver != null && !string.IsNullOrEmpty(sprite) && sprite.StartsWith("MyObjectBuilder_", StringComparison.Ordinal))
                return resolver(sprite) ?? sprite;
            return sprite;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            float width = Dimension(_image.Width, availableSize.X);
            float height = Dimension(_image.Height, availableSize.X);
            if (!float.IsPositiveInfinity(availableSize.X) && width > availableSize.X)
            {
                height *= availableSize.X / width;
                width = availableSize.X;
            }
            return new Vector2(width, height);
        }

        float Dimension(float value, float availableWidth)
        {
            value = Math.Max(1f, value);
            if (_image.SizeType == SizeType.Percent)
                return float.IsPositiveInfinity(availableWidth) ? value : Math.Max(1f, availableWidth * value / 100f);
            return value * _fontSize;
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (_monospace != null)
            {
                if (_monospace.Length == 0 || context.TextMetrics == null)
                    return;
                Vector2 measured = context.TextMetrics.Measure(MONOSPACE_FONT, _monospace, 1f);
                if (measured.X <= 0f || measured.Y <= 0f)
                    return;
                float scale = Math.Max(.01f, Math.Min(Bounds.Width / measured.X, Bounds.Height / measured.Y));
                context.Text(MONOSPACE_FONT, _monospace, Bounds.Position, scale, Color.White,
                    HorizontalAlignment.Left, VerticalAlignment.Top);
                return;
            }
            if (!string.IsNullOrEmpty(_sprite))
                context.Texture(_sprite, Bounds, Color.White, true);
        }
    }
}
