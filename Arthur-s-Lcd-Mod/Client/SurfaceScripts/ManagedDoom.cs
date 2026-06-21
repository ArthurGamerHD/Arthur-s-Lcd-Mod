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

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class ManagedDoomSurfaceScript : SurfaceScriptBase
    {
        public const string ID = "ManagedDoom";
        public const string TITLE = "Doom - Demo";

        ManagedDoomApp _app;

        protected override ConfigKind ConfigKind => ConfigKind.Interactive;
        protected override string DefaultTitle => TITLE;
        public override IApp App => _app;

        public ManagedDoomSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            LcdModSessionComponent.OnAfterSimulationUpdate += HandleAfterSimulationUpdate;
        }

        public override void Dispose()
        {
            LcdModSessionComponent.OnAfterSimulationUpdate -= HandleAfterSimulationUpdate;
            base.Dispose();
        }

        void HandleAfterSimulationUpdate()
        {
            if (Surface == null || MyAPIGateway.Session == null)
                return;

            if (LastRunTick == MyAPIGateway.Session.GameplayFrameCounter)
                return;

            Run();
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
                _app = new ManagedDoomApp(AppConfig, this);

            _app.Update();
            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            if (_app == null)
                return new List<MySprite>();

            return _app.GetSprites();
        }
    }
}
