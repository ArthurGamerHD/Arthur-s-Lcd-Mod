using Generated;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.Gps;
using LcdMod.Client.Terminal.Controls.Groups;
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
    public sealed partial class PlanetaryMapSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SwitchDisplayMyGps>,
        IUsesTerminalControl<SwitchIncludeRadioSignals>,
        IUsesTerminalControlGroup<GpsAlwaysDisplayTerminalControlGroup>
    {
        public const string ID = "LcdMod_PlanetaryMap";
        public const string TITLE = "Planetary Map";

        public override bool AsyncRender => false;
        
        PlanetaryMapApp _app;

        protected override string DefaultTitle
        {
            get { return TITLE; }
        }

        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override IApp App
        {
            get { return _app; }
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
                _app = new PlanetaryMapApp(this);
                _app.LayoutChanged();
            }

            EnsureRetainedAppView(_app);
            _app.Update();
            CursorType = ResolveRetainedCursorType();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            if (_app != null)
                DrawRetainedAppView(sprites);
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
