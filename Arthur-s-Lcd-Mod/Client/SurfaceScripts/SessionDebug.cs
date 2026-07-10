using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.SessionDebugApp))]
#if DEBUG
    [MyTextSurfaceScript(ID, TITLE)]
#endif
    public partial class SessionDebugSurfaceScript : InteractiveSurfaceScript
    {
        public const string ID = "SessionDebug";
        public const string TITLE = "LcdMod Session Debug";

        SessionDebugApp _app;

        public override CursorType CursorType { get; protected set; } = CursorType.Default;

        public override List<Control> InteractiveList => _app?.InteractiveEntries ?? new List<Control>();

        public SessionDebugSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            LcdModSessionComponent.OnAfterSimulationUpdate += HandleAfterSimulationUpdate;
        }

        public override IApp App => _app;

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

        public override List<MySprite> GetSprites() => _app?.GetSprites() ?? new List<MySprite>();

        public override void SafeRun()
        {
            base.SafeRun();

            if (_app == null) _app = new SessionDebugApp(this);
        }
    }
}
