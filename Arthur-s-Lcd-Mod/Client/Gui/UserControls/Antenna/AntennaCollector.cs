using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using ScreenConfigWithBlocks = LcdMod.Common.Config.Models.Apps.ScreenConfigWithBlocks;

namespace LcdMod.Client.Gui.UserControls.Antenna
{
    internal abstract class AntennaCollector
    {
        protected readonly Color ForegroundColor;
        protected readonly Color WarningColor;
        protected readonly ScreenConfigWithBlocks ScreenConfigGeneral;
        readonly Dictionary<string, string> _locCache = new Dictionary<string, string>();
        
        public abstract void Collect(
            GridLogic grid,
            List<AntennaEntry> entries,
            Dictionary<long, AntennaEntry> models,
            HashSet<long> activeEntryIds);

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

        protected string FormatLabelWithColon(string label)
        {
            return string.Format(FormatingHelper.Culture, GetLocCached(MOD_PREFIX + "Common_Label_WithColon"), label);
        }

        protected AntennaEntry GetOrCreateEntry(
            long entryId,
            List<AntennaEntry> entries,
            Dictionary<long, AntennaEntry> models,
            HashSet<long> activeEntryIds)
        {
            if (activeEntryIds != null)
                activeEntryIds.Add(entryId);

            AntennaEntry entry;
            if (models == null || !models.TryGetValue(entryId, out entry) || entry == null)
            {
                entry = new AntennaEntry(entryId);
                if (models != null)
                    models[entryId] = entry;
            }

            if (entries != null)
                entries.Add(entry);

            return entry;
        }
        
        protected bool IsValid(IMyTerminalBlock block) => block != null && !block.Closed && (!ScreenConfigGeneral.SelectedBlocks.Any() || ScreenConfigGeneral.SelectedBlocks.Contains(block.EntityId));
    }
}
