using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;
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
    public partial class SessionDebugSurfaceScript : InteractiveSurfaceScript
    {
        protected override ConfigKind ConfigKind => ConfigKind.Interactive;

        public const string ID = "SessionDebug";
        public const string TITLE = "LcdMod Session Debug";

        readonly SessionDebugApp _app = new SessionDebugApp();

        public override CursorType CursorType { get; protected set; }

        public override List<ControlBase> InteractiveList
        {
            get { return _app.InteractiveEntries; }
        }

        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;

        public SessionDebugSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            CursorType = CursorType.Default;
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

        public override List<MySprite> GetSprites()
        {
            return _app.GetSprites(this);
        }

        public override void SafeRun()
        {
            base.SafeRun();
        }
    }
}
