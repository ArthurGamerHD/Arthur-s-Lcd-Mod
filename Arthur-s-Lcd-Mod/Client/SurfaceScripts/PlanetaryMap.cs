using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Generation;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(PlanetaryMapApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed partial class PlanetaryMapSurfaceScript : InteractiveSurfaceScript
    {
        public const string ID = "LcdMod_PlanetaryMap";
        public const string TITLE = "Planetary Map";

        public override bool AsyncRender => true;
        
        readonly List<Control> _emptyInteractive = new List<Control>();
        PlanetaryMapApp _app;
        Matrix _rotationTransform = Matrix.Identity;

        protected override string DefaultTitle
        {
            get { return TITLE; }
        }

        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        protected override bool RendersInteractiveEntriesInGetSprites { get { return true; } }

        public override IApp App
        {
            get { return _app; }
        }

        public override List<Control> InteractiveList
        {
            get
            {
                return _app != null
                    ? _app.VisualChildren as List<Control> ?? _emptyInteractive
                    : _emptyInteractive;
            }
        }

        public PlanetaryMapSurfaceScript(
            IMyTextSurface surface,
            IMyCubeBlock block,
            Vector2 size)
            : base((Sandbox.ModAPI.IMyTextSurface)surface, block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            if (_app != null)
                _app.LayoutChanged();
        }

        public override void SafeRun()
        {
            if (_app == null)
            {
                _app = new PlanetaryMapApp(this, _rotationTransform);
                _app.LayoutChanged();
            }

            _app.Update();
            CursorType = _app.RequestedCursorType;
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            if (_app != null)
                sprites.AddRange(_app.GetSprites());
            DrawTitle(sprites);
            return sprites;
        }

        protected override void OnMouseScroll(int delta, ref bool handled)
        {
            base.OnMouseScroll(delta, ref handled);
            if (_app != null)
                _app.OnMouseScroll(delta, ref handled);
        }
    }
}
