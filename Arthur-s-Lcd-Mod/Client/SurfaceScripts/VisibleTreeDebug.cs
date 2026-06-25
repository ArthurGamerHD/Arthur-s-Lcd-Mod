#if DEBUG
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.VisibleTreeDebug;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.SurfaceScripts
{
    [LcdSurface(typeof(LcdMod.Client.Apps.VisibleTreeDebugApp))]
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class VisibleTreeDebugSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<ListboxVisibleTreeDebugBlockSelection>,
        IUsesTerminalControl<ListboxVisibleTreeDebugScreenSelection>
    {
        public const string ID = "VisibleTreeDebug";
        public const string TITLE = "LcdMod Visible Tree Debug";

        readonly List<Control> _interactiveList = new List<Control>();
        VisibleTreeDebugApp _app;
        protected override string DefaultTitle => TITLE;
        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;
        public override IApp App => _app;
        public override CursorType CursorType { get; protected set; } = CursorType.Default;
        public override List<Control> InteractiveList => _interactiveList;

        public VisibleTreeDebugSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            LcdModSessionComponent.OnAfterSimulationUpdate += HandleAfterSimulationUpdate;
        }

        void HandleAfterSimulationUpdate()
        {
            if (Surface == null)
                return;

            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            if (_app == null)
                return new List<MySprite>();

            SurfaceScriptBase target;
            string status;
            _app.TryGetDebugTarget(out target, out status);
            return _app.GetSprites(this, target, status);
        }

        public override void SafeRun()
        {
            base.SafeRun();

            if (_app == null)
                _app = new VisibleTreeDebugApp(this);

            _app.Update();
            RenderSprites();
        }

        public override void Dispose()
        {
            LcdModSessionComponent.OnAfterSimulationUpdate -= HandleAfterSimulationUpdate;
            base.Dispose();
        }
    }
}
#endif
