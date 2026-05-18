using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class ThrustSurfaceScript : SurfaceScriptBase
    {
        protected override ConfigKind ConfigKind => ConfigKind.Colorable;
        public const string ID = "LcdMod_Thrust";
        public const string TITLE = "HelpScreen_JoystickThrust";

        ThrustApp _app;

        public override IApp App => _app;

        protected override string DefaultTitle => TITLE;

        public ThrustSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        public override void SafeRun()
        {
            if (AppConfig == null)
                return;

            if (_app == null)
                _app = new ThrustApp(AppConfig, this);

            _app.Update();

            if (!_app.HasData)
            {
                Empty();
                return;
            }

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                sprites.AddRange(_app.GetSprites());
                frame.AddRange(sprites);
            }
        }
    }
}
