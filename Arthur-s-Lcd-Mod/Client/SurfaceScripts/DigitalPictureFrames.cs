using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
#if EXPERIMENTAL
    [MyTextSurfaceScript(ID, TITLE)]
#endif
    public sealed class DigitalPictureFramesSurfaceScript : InteractiveSurfaceScript
    {
        public const string ID = "LcdMod_DigitalPictureFrames";
        public const string TITLE = "LcdMod_DigitalPictureFrames";

        readonly List<ControlBase> _interactiveListFallback = new List<ControlBase>();
        DigitalPictureFramesApp _app;

        protected override ConfigKind ConfigKind => ConfigKind.DigitalPictureFrames;
        protected override string DefaultTitle => TITLE;
        public override IApp App => _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override List<ControlBase> InteractiveList => _app != null ? _app.InteractiveList : _interactiveListFallback;

        public DigitalPictureFramesSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
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
            var appConfig = Config as ScreenConfigDigitalPictureFrames;
            if (appConfig == null)
                return;

            if (_app == null)
                _app = new DigitalPictureFramesApp(appConfig, this);

            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            if (_app == null || Config == null)
                return sprites;

            AddBackground(sprites);
            DrawTitle(sprites);
            sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
