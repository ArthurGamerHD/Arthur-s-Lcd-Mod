#if EXPERIMENTAL
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Generation;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyTextSurface = Sandbox.ModAPI.IMyTextSurface;

namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.MediaPlayerApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class MediaPlayerSurfaceScript : InteractiveSurfaceScript
    {
        public const string ID = "MediaPlayer";
        public const string TITLE = "Media Player";

        MediaPlayerApp _app;

        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override IApp App => _app;
        public override List<Control> InteractiveList => _app == null ? new List<Control>() : _app.VisualChildren as List<Control>;
        public override string Title => _app != null ? _app.Title : base.Title;
        protected override bool RendersInteractiveEntriesInGetSprites => true;

        public MediaPlayerSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        public override void Dispose()
        {
            if (_app != null)
                _app.Close();
            base.Dispose();
        }

        public override void SafeRun()
        {
            base.SafeRun();

            if (_app == null)
                _app = new MediaPlayerApp(this);

            UpdateViewBox();
            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            DrawTitle(sprites);
            if (_app == null)
                DrawLoading(sprites);
            else
                sprites.AddRange(_app.GetSprites());
            return sprites;
        }
    }
}
#endif
