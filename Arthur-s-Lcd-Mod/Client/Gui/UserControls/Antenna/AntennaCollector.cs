using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Grid;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI;
using VRageMath;
using ScreenConfigWithBlocks = LcdMod.Common.Config.Models.Apps.ScreenConfigWithBlocks;

namespace LcdMod.Client.Gui.UserControls.Antenna
{
    internal abstract class AntennaCollector
    {
        protected readonly Color ForegroundColor;
        protected readonly Color WarningColor;
        protected readonly ScreenConfigWithBlocks ScreenConfigGeneral;
        readonly Dictionary<string, string> _locCache = new Dictionary<string, string>();
        
        public abstract void Collect(GridLogic grid, List<AntennaEntry> entries);

        protected AntennaCollector(IAppHost antennaSurfaceScript)
        {
            var appConfig = (ScreenConfigWithBlocks)antennaSurfaceScript.Config;
            ForegroundColor = antennaSurfaceScript.ForegroundColor;
            WarningColor = appConfig.WarningColor;
            ScreenConfigGeneral = appConfig;
        }
        
        protected string GetLocCached(string key)
        {
            string value;
            if (_locCache.TryGetValue(key, out value))
                return value;

            value = LocHelper.GetLoc(key);
            _locCache[key] = value;
            return value;
        }
        
        
        protected bool IsValid(IMyTerminalBlock block) => block != null && !block.Closed && (!ScreenConfigGeneral.SelectedBlocks.Any() || ScreenConfigGeneral.SelectedBlocks.Contains(block.EntityId));
    }
}
