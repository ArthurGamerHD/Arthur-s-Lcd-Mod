using System.Collections.Generic;
using Generated;
using LcdMod.Client.Apps;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Terminal.Controls;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Client.SurfaceScripts
{
#if DEBUG
    [MyTextSurfaceScript(ID, TITLE)]
#endif
    public partial class VisibleTreeDebugSurfaceScript : SurfaceScriptBase, IReferenceBlockSelection
    {
        public const string ID = "VisibleTreeDebug";
        public const string TITLE = "LcdMod Visible Tree Debug";

        readonly VisibleTreeDebugApp _app = new VisibleTreeDebugApp();

        protected override ConfigKind ConfigKind => ConfigKind.RenderProxy;
        protected override string DefaultTitle => TITLE;
        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;
        public override IApp App => _app;

        public VisibleTreeDebugSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            LcdModSessionComponent.OnAfterSimulationUpdate += HandleAfterSimulationUpdate;
        }

        public bool IsReferenceBlockCandidate(IMyTerminalBlock block)
        {
            if (!(block is IMyTextPanel) || block.MarkedForClose || block.Equals(Block))
                return false;

            var apps = ConfigManager.GetAppsForBlock(block);
            if (apps == null)
                return true;

            bool hasAnyApp = false;
            foreach (var app in apps)
            {
                hasAnyApp = true;
                if (!(app is VisibleTreeDebugSurfaceScript))
                    return true;
            }

            return !hasAnyApp;
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

            int rotationIndex = GetSelectedTextPanelRotationIndex(targetBlock);
            target = instances.GetInstance(rotationIndex);
            if (target == null)
            {
                status = "No active script for rotation " + rotationIndex;
                return false;
            }

            return true;
        }

        static int GetSelectedTextPanelRotationIndex(IMyTerminalBlock block)
        {
            var panel = block as IMyTextPanel;
            if (panel == null)
                return 0;

            foreach (var component in panel.Components)
            {
                var lcdSurfaceComponent = component as IMyLcdSurfaceComponent;
                if (lcdSurfaceComponent == null)
                    continue;

                return lcdSurfaceComponent.SelectedRotationIndex;
            }

            return 0;
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
            return _app.GetSprites(this, target, status);
        }

        public override void SafeRun()
        {
        }

        public override void Dispose()
        {
            LcdModSessionComponent.OnAfterSimulationUpdate -= HandleAfterSimulationUpdate;
            base.Dispose();
        }
    }
}
