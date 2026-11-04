using System.Linq;
using LcdMod.Client.Config;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Client.Utility;
using Sandbox.ModAPI;

namespace LcdMod.Client.Terminal.Controls.Snapshots
{
    public abstract class GridSnapshotButton : TerminalControlsWrapper
    {
        // The screen of the surface selected in the terminal.
        protected T Find<T>(IMyTerminalBlock block) where T : class
        {
            if (block == null)
                return null;
            var textPanelInstances = SurfaceScriptBase.Instances.GetInstances(block) as TextPanelSurfaceTssInstances;
            if (textPanelInstances != null)
                return textPanelInstances.GetActiveInstance() as T;
            int surfaceIndex = GetThisSurfaceIndex(block);
            return ConfigManager.GetAppsForBlock(block).FirstOrDefault(app => app.RotationOrSurfaceIndex == surfaceIndex) as T;
        }
    }
}
