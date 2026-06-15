#if DEBUG
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls.VisibleTreeDebug;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class VisibleTreeDebugSurfaceScript : InteractiveSurfaceScript,
        IUsesTerminalControl<ListboxVisibleTreeDebugBlockSelection>,
        IUsesTerminalControl<ListboxVisibleTreeDebugScreenSelection>
    {
        public const string ID = "VisibleTreeDebug";
        public const string TITLE = "LcdMod Visible Tree Debug";

        readonly List<Control> _interactiveList = new List<Control>();
        VisibleTreeDebugApp _app;

        protected override ConfigKind ConfigKind => ConfigKind.VisibleTreeDebug;
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

        public bool TryGetDebugTarget(out SurfaceScriptBase target, out string status)
        {
            target = null;
            status = null;

            var config = AppConfig;
            if (config == null)
            {
                status = "Missing reference config";
                return false;
            }

            if (config.ReferenceBlock == 0L)
            {
                status = "Screen Not Linked";
                return false;
            }

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(config.ReferenceBlock, out entity))
            {
                status = "Reference block not found";
                return false;
            }

            var targetBlock = entity as IMyTerminalBlock;
            if (targetBlock == null || targetBlock.MarkedForClose)
            {
                status = "Invalid reference block";
                return false;
            }

            var instances = SurfaceScriptBase.Instances.GetInstances(targetBlock);
            if (instances == null)
            {
                status = "No LcdMod script instance";
                return false;
            }

            target = instances.GetInstance(config.ReferenceScreenIndex);
            if (target == null)
            {
                status = "No active script for screen " + config.ReferenceScreenIndex;
                return false;
            }

            return true;
        }

        void HandleAfterSimulationUpdate()
        {
            if (Surface == null)
                return;

            RenderSprites();
        }

        public override List<MySprite> GetSprites()
        {
            SurfaceScriptBase target;
            string status;
            TryGetDebugTarget(out target, out status);
            if (_app == null)
                return new List<MySprite>();
            return _app.GetSprites(this, target, status);
        }

        public override void SafeRun()
        {
            var appConfig = AppConfig;
            if (appConfig == null)
                return;

            base.SafeRun();

            if (_app == null)
                _app = new VisibleTreeDebugApp(appConfig, this);

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
