using System;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.UserControls;
using LcdMod.Client.Markdown;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.Tooltip
{
    public sealed class MarkdownTooltipPanel : RectangleControl
    {
        readonly IAppHost _context;
        readonly Func<string> _textGetter;
        readonly MarkdownParser _parser = new MarkdownParser();

        MarkdownDocument _document;
        string _parsedText;

        public MarkdownTooltipPanel(IAppHost context, Func<string> textGetter)
            : base(default(RectangleF))
        {
            _context = context;
            _textGetter = textGetter;
        }

        public override Vector2 Measure(Vector2 availableSize)
        {
            EnsureDocument();
            if (_document == null || _context == null)
                return Vector2.Zero;

            float width = Math.Max(1f, availableSize.X);
            Vector2 size = MarkdownPanel.MeasureContent(_document, width, _context);
            return new Vector2(width, Math.Max(1f, Math.Min(size.Y, availableSize.Y)));
        }

        protected override void RenderDefault(System.Collections.Generic.List<MySprite> sprites)
        {
            EnsureDocument();
            if (_document == null || _context == null)
                return;

            MarkdownPanel.CreateSprites(
                _document,
                Bounds,
                _context.ForegroundColor,
                _context.ForegroundColor,
                sprites,
                _context);
        }

        void EnsureDocument()
        {
            string text = _textGetter != null ? _textGetter() : string.Empty;
            text = text ?? string.Empty;
            if (string.Equals(_parsedText, text, StringComparison.Ordinal))
                return;

            _parsedText = text;
            _document = string.IsNullOrWhiteSpace(text) ? null : _parser.Parse(text);
        }
    }
}
