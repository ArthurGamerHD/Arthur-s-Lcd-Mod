using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Markdown;
using LcdMod.Client.Utility;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class MarkdownSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControl<ButtonEditMarkdown>, IInputBlock
    {
        List<MySprite> _sprites = new List<MySprite>();
        
        protected override ConfigKind ConfigKind => ConfigKind.Markdown;
        public override IApp App => _app;
        MarkdownApp _app;

        public const string ID = "Markdown";
        public const string TITLE = "Markdown";

        protected override string DefaultTitle => TITLE;

        public MarkdownSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _app?.LayoutChanged();
        }

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            if (_app == null)
                _app = new MarkdownApp(AppConfig, this);

            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();
            if (_app?.Document == null)
            {
                AddEmptySprites(_sprites);
                return _sprites;
            }
            
            AddBackground(_sprites);
            DrawTitle(_sprites);
            _sprites.AddRange(_app.GetSprites());
            return _sprites;
        }
    }
}
