using Generated;
using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Terminal.Controls.Generic;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using SliderRadarRange = LcdMod.Client.Terminal.Controls.Generic.SliderRadarRange;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed class RadarSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<SliderRadarRange>,
        IUsesTerminalControl<ComboboxReferenceMode>
    {
        public const string ID = MOD_PREFIX + "Radar";
        public const string TITLE = MOD_PREFIX + "Radar";

        protected override ConfigKind ConfigKind => ConfigKind.Radar;
        protected override string DefaultTitle => TITLE;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;

        public override IApp App => _app;       
        RadarApp _app;

        public override List<Control> InteractiveList => _app.Children as List<Control>;

        public RadarSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface, block, size)
        {
        }

        public override void SafeRun()
        {
            var appConfig = Config as ScreenConfigRadar;
            if (appConfig == null)
                return;

            if (_app == null)
                _app = new RadarApp(appConfig, this);

            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            AddBackground(sprites);
            DrawTitle(sprites);
            if (_app != null)
                sprites.AddRange(_app.GetSprites());
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
