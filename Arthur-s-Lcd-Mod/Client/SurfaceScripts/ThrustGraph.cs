using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.ThrustApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class ThrustSurfaceScript : SurfaceScriptBase
    {
        public const string ID = MOD_PREFIX + "Thrust";
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
            if (_app == null)
                _app = new ThrustApp(this);

            _app.Update();

            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            if (_app == null || !_app.HasData)
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
