using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.UserControls;
using LcdMod.Client.Markdown;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Models;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;
using VRageMath;

namespace LcdMod.Client.Apps
{
    public sealed class MarkdownApp : AppBase
    {
        readonly MarkdownParser _parser = new MarkdownParser();
        readonly List<MySprite> _cachedSprites = new List<MySprite>();
        bool _spritesLoaded;
        string _loadedText;

        public MarkdownApp(ScreenConfigMarkdown config, IAppHost host) : base(config, host)
        {
        }

        ScreenConfigMarkdown Config => (ScreenConfigMarkdown)AppConfig;

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

            var colorable = AppConfig as ScreenConfigColorable;
            Color headerColor = colorable != null ? colorable.HeaderColor : Host.ForegroundColor;
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
            var text = Config.RawText ?? string.Empty;
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
