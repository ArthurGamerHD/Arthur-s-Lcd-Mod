using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
#if EXPERIMENTAL
    [MyTextSurfaceScript(ID, "Input / Output")]
#endif
    public partial class InputOutputLcdSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControlGroup<BlocksFilterTerminalControlGroup>
    {
        public const string ID = "InputOutputCharts";
        public const string NAME = InputOutputApp.NAME;


        private InputOutputApp _app;

        public InputOutputLcdSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface,
            block, size)
        {
        }

        protected override ConfigKind ConfigKind => ConfigKind.WithItems;

        public override IApp App => _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        
        public override List<Control> InteractiveList => _app.Children as List<Control>;

        public override string Title => _app != null ? _app.Title : base.Title;
        protected override string DefaultTitle => NAME;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            base.SafeRun();

            if (_app == null)
                _app = new InputOutputApp(AppConfig, this);

            _app.Update();

            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();

            if (_app == null || !_app.HasBlocks)
            {
                AddEmptySprites(sprites);
                return sprites;
            }

            AddBackground(sprites);
            DrawTitle(sprites);
            sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
