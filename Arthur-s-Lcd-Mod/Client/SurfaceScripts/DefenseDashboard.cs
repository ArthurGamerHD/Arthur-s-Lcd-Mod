using System.Collections.Generic;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Generation;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(DefenseDashboardApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class DefenseDashboardSurfaceScript : SurfaceScriptBase
    {
        public const string ID = MOD_PREFIX + "DefenseDashboard";
        public const string TITLE = MOD_PREFIX + "DefenseDashboard";

        DefenseDashboardApp _app;

        public override IApp App => _app;
        protected override string DefaultTitle => TITLE;

        public DefenseDashboardSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base((Sandbox.ModAPI.IMyTextSurface)surface, block, size)
        {
        }

        public override void SafeRun()
        {
            if (_app == null)
                _app = new DefenseDashboardApp(this);

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
