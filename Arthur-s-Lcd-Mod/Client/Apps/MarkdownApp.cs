using LcdMod.Common.Config.Components;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Markdown;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(14)]
    [ConfigComponent(Constants.APP, typeof(MarkdownConfigComponent), PropertyName = "MarkdownComponent")]
    public sealed partial class MarkdownApp : App
    {
        readonly MarkdownParser _parser = new MarkdownParser();
        readonly List<MySprite> _noSprites = new List<MySprite>();
        string _loadedText;
        readonly Markdown.MarkdownViewModel _viewModel;

        public MarkdownApp(IAppHost host) : base(host)
        {
            _viewModel = new Markdown.MarkdownViewModel(this);
            SetContent(new Markdown.MarkdownView { DataContext = _viewModel });
        }

        public MarkdownDocument Document { get; private set; }
        internal override float WindowRenderScaling => CardScale;

        public override void Update()
        {
            EnsureDocument();
            _viewModel.Refresh();
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            _loadedText = null;
        }

        public override List<MySprite> GetSprites()
        {
            return _noSprites;
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
