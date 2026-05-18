using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
#if DEBUG
    [MyTextSurfaceScript(ID, TITLE)]
#endif
    public partial class SessionDebugSurfaceScript : SurfaceScriptBase
    {
        protected override ConfigKind ConfigKind => ConfigKind.Colorable;

        public const string ID = "SessionDebug";
        public const string TITLE = "LcdMod Session Debug";
        readonly SessionDebugApp _app = new SessionDebugApp();
        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public SessionDebugSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
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
            if (Surface == null)
                return;

            RenderSprites();
        }

        protected override List<MySprite> GetSprites()
        {
            return _app.GetSprites(this);
        }

        public override void SafeRun()
        {
        }
    }
}
