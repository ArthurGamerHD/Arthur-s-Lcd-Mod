using LcdMod.Common.Config.Components;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.UserControls;
using LcdMod.Client.Markdown;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;
using VRageMath;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(14)]
    [ConfigComponent(Constants.APP, typeof(MarkdownConfigComponent), PropertyName = "MarkdownComponent")]
    public sealed partial class MarkdownApp : App
    {
        readonly MarkdownParser _parser = new MarkdownParser();
        readonly List<MySprite> _cachedSprites = new List<MySprite>();
        bool _spritesLoaded;
        string _loadedText;

        // todo: convert to interactive app
        public override IReadOnlyList<Control> Children { get; } = new Control[]{};
        
        public MarkdownApp(IAppHost host) : base(host)
        {
        }

        public MarkdownDocument Document { get; private set; }

        public override void Update()
        {
            EnsureDocument();
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            _loadedText = null;
            _spritesLoaded = false;
            _cachedSprites.Clear();
        }

        public override List<MySprite> GetSprites()
        {
            if (Document == null)
                return _cachedSprites;

            if (_spritesLoaded)
                return _cachedSprites;

            _cachedSprites.Clear();

            Color headerColor = GetHeaderColor();
            RectangleF contentViewBox = MarkdownPanel.GetContentViewBox(Host);
            MarkdownPanel.CreateSprites(
                Document,
                contentViewBox,
                headerColor,
                Host.ForegroundColor,
                _cachedSprites,
                Host);

            _spritesLoaded = true;
            return _cachedSprites;
        }

        void EnsureDocument()
        {
            var text = MarkdownComponent.RawText ?? string.Empty;
            if (_loadedText == text)
                return;

            ParseDocument(text);
        }

        void ParseDocument(string text)
        {
            text = text ?? string.Empty;
            _loadedText = text;
            _spritesLoaded = false;
            _cachedSprites.Clear();

            if (string.IsNullOrWhiteSpace(text))
            {
                Document = null;
                return;
            }

            Document = _parser.Parse(text);
            if (Document.Blocks.Count != 0)
                return;

            LogHelper.Log(MyLogSeverity.Warning, $"Parsing the markdown text failed.\n{text}");
            Document = null;
        }
    }
}
