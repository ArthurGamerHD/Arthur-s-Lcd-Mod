using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI;
using VRageMath;
using VRage.Game.ModAPI;
using static LcdMod.Common.Helpers.Constants;
namespace LcdMod.Client.Gui.UserControls.Antenna
{
    internal abstract class AntennaCollector : IDisposable
    {
        readonly IAppHost _host;
        readonly Func<BlockSelectionConfigComponent> _getConfig;
        readonly Func<ColorConfigComponent> _getColors;
        readonly Dictionary<string, string> _locCache = new Dictionary<string, string>();

        protected Color ForegroundColor => _host.ForegroundColor;
        protected Color WarningColor => _getColors().ResolveWarningColor();
        protected BlockSelectionConfigComponent AntennaConfig => _getConfig();
        
        public abstract void Collect(
            GridLogic grid,
            List<AntennaEntry> entries,
            Dictionary<long, AntennaEntry> models,
            HashSet<long> activeEntryIds);

        public abstract void Dispose();

        protected AntennaCollector(
            IAppHost antennaSurfaceScript,
            Func<BlockSelectionConfigComponent> getConfig,
            Func<ColorConfigComponent> getColors)
        {
            _host = antennaSurfaceScript;
            if (getConfig == null) throw new ArgumentNullException(nameof(getConfig));
            if (getColors == null) throw new ArgumentNullException(nameof(getColors));
            _getConfig = getConfig;
            _getColors = getColors;
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
        
        protected GridLinkTypeEnum GridLinkType => (GridLinkTypeEnum)AntennaConfig.GridLinkTypeInternal;

        protected bool IsValid(IMyTerminalBlock block) => block != null && !block.Closed && (!AntennaConfig.SelectedBlocks.Any() || AntennaConfig.SelectedBlocks.Contains(block.EntityId));
    }
}
