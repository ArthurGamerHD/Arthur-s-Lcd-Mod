using LcdMod.Common.Mvvm;
using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Config;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Power;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Client.Utility;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using ColorExtensions = LcdMod.Client.Extensions.ColorExtensions;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps.Abstract
{
    [ConfigComponent(Constants.APP, typeof(PowerConfigComponent), PropertyName = "PowerComponent")]
    public abstract partial class PowerApp : App, IApp
    {
        protected const float LINE = 22f;
        protected const float MINIMUM_COL_WIDTH = 400f;
        protected const float GRID_CELL_LINES = 6f;
        internal const int RANGE_TIER_COUNT = 6;
        const int MAX_RANGE_WINDOW_TICKS = 5 * 60 * 60;

        protected struct PowerEntryDefinition
        {
            public readonly string Key;
            public readonly string DisplayNameToken;
            public readonly string FallbackName;

            public PowerEntryDefinition(string key, string displayNameToken, string fallbackName)
            {
                Key = key;
                DisplayNameToken = displayNameToken;
                FallbackName = fallbackName;
            }
        }

        internal sealed class PowerEntry : ObservableObject
        {
            public PowerEntry(string key, string name)
            {
                Key = key;
                Name = name ?? string.Empty;
                UsageLine = string.Empty;
            }

            public string Key { get; private set; }
            public string Name { get; set; }
            public float Usage { get; set; }
            public double Current { get; set; }
            public double Max { get; set; }
            public string UsageLine { get; set; }
            public int DetectedBlocks { get; set; }
            public string MaxLabel { get; private set; }
            public string CurrentLabel { get; private set; }
            public bool DrawAsLines { get; private set; }

            public void UpdateValues(float usage, double current, double max, string usageLine, int detectedBlocks)
            {
                Usage = usage;
                Current = current;
                Max = max;
                UsageLine = usageLine ?? string.Empty;
                DetectedBlocks = detectedBlocks;
            }

            public void ConfigureRender(string maxLabel, string currentLabel, bool drawAsLines)
            {
                MaxLabel = maxLabel ?? string.Empty;
                CurrentLabel = currentLabel ?? string.Empty;
                DrawAsLines = drawAsLines;
            }
        }

        struct PowerTotals
        {
            public double Current;
            public double Max;
            public int Count;
        }

        readonly Dictionary<string, PowerEntry> _entriesByKey = new Dictionary<string, PowerEntry>();
        readonly Dictionary<string, PowerTotals> _totalsByKey = new Dictionary<string, PowerTotals>();
        readonly Dictionary<string, TickWindowAverage> _currentAverages = new Dictionary<string, TickWindowAverage>();
        readonly Dictionary<string, TickWindowAverage> _maxAverages = new Dictionary<string, TickWindowAverage>();
        string[] _entryOrder;
        PowerEntry[] _entriesOrdered;
        readonly List<PowerEntry> _visibleEntries = new List<PowerEntry>();
        readonly LinkedTypedBlockSourceSet<IMyPowerProducer> _producerSources =
            new LinkedTypedBlockSourceSet<IMyPowerProducer>(blocks => blocks.PowerProducers);

        Color _ascentColor = Color.White;
        string _usagePrefix = string.Empty;
        string _maxLabelCache = string.Empty;
        string _currentLabelCache = string.Empty;
        internal IReadOnlyList<PowerEntry> NativeEntries => _visibleEntries;
        internal string NativeMaxLabel => _maxLabelCache ?? string.Empty;
        internal string NativeCurrentLabel => _currentLabelCache ?? string.Empty;
        internal Color NativeAccentFill => _ascentColor;
        internal Color NativeHeaderColor => GetHeaderColor();
        internal Color NativeErrorColor => ColorComponent.ResolveErrorColor();
        internal Color NativeForeground => Host.Surface.ScriptForegroundColor;
        internal bool NativeDrawLines => GeneralComponent.DrawLines;

        protected abstract PowerEntryDefinition[] EntryDefinitions { get; }

        protected PowerApp(IAppHost host) : base(host)
        {
        }

        public List<MyTerminalControlComboBoxItem> GetDisplayModes()
        {
            return DisplayModes.GridAndLegacy;
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            _ascentColor = ColorExtensions.DeriveAccentColor(GetHeaderColor(), .4f, 0.5);

            RefreshEntryLabels();
            _maxLabelCache = string.Empty;
            _currentLabelCache = string.Empty;
        }

        public override void Update()
        {
            _producerSources.Bind(Host.GridLogic, PowerScopeResolver.NormalizeLinkType(PowerComponent.GridLinkTypeInternal));
            SumPowerSources(_totalsByKey);
            UpdateEntryValues();
            BuildVisibleEntries();

            if (string.IsNullOrEmpty(_maxLabelCache))
                _maxLabelCache = MyTexts.Get(MyStringId.GetOrCompute("BlockPropertiesText_MaxOutput")).ToString();
            if (string.IsNullOrEmpty(_currentLabelCache))
                _currentLabelCache = MyTexts.Get(MyStringId.GetOrCompute("BlockPropertyProperties_CurrentOutput"))
                    .ToString();
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        protected abstract bool TryMapProducerType(string typeId, IMyPowerProducer producer, out string entryKey);

        protected void InitializeEntries()
        {
            var definitions = EntryDefinitions;

            _entryOrder = new string[definitions.Length];
            _entriesOrdered = new PowerEntry[definitions.Length];

            _entriesByKey.Clear();
            _totalsByKey.Clear();

            for (int i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                _entryOrder[i] = definition.Key;
                _entriesByKey[definition.Key] = new PowerEntry(definition.Key, ResolveDisplayName(definition));
                _totalsByKey[definition.Key] = new PowerTotals();
            }

            _usagePrefix = MyTexts.Get(MyStringId.GetOrCompute("HudInfoNamePowerUsage")) + " ";
            SyncOrderedEntries();
        }

        void RefreshEntryLabels()
        {
            if (_entriesByKey.Count == 0)
                InitializeEntries();

            var definitions = EntryDefinitions;
            for (int i = 0; i < definitions.Length; i++)
            {
                var definition = definitions[i];
                var entry = _entriesByKey[definition.Key];
                entry.Name = ResolveDisplayName(definition);
            }

            _usagePrefix = MyTexts.Get(MyStringId.GetOrCompute("HudInfoNamePowerUsage")) + " ";
            SyncOrderedEntries();
        }

        string ResolveDisplayName(PowerEntryDefinition definition)
        {
            var localized = MyTexts.GetString(definition.DisplayNameToken);
            if (string.IsNullOrEmpty(localized) || localized == definition.DisplayNameToken)
                return definition.FallbackName;
            return localized;
        }

        void UpdateEntryValues()
        {
            if (_entriesByKey.Count == 0)
                InitializeEntries();

            int windowTicks = GetRangeWindowTicks(RangeTier);
            for (int i = 0; i < _entryOrder.Length; i++)
            {
                var key = _entryOrder[i];
                var totals = _totalsByKey[key];
                totals.Current = GetAverage(_currentAverages, key).Add(totals.Current, windowTicks);
                totals.Max = GetAverage(_maxAverages, key).Add(totals.Max, windowTicks);

                var usage = totals.Max > 0 ? (float)Math.Min(Math.Max(totals.Current / totals.Max, 0), 1) : 0f;

                var entry = _entriesByKey[key];
                entry.UpdateValues(
                    usage,
                    totals.Current,
                    totals.Max,
                    _usagePrefix + FormatingHelper.PercentageToString(usage),
                    totals.Count);
            }

            SyncOrderedEntries();
        }

        /// <summary>
        /// Index into the dashboard ranges (1s, 5s, 30s, 1m, 5m, all) the output is averaged over. Vanilla batteries make
        /// generator output swing between idle and full load every 100 ticks, so a single sample is rarely representative.
        /// </summary>
        internal int RangeTier
        {
            get
            {
                int tier = PowerComponent.PowerHistoryTier >= 0 ? PowerComponent.PowerHistoryTier : PowerComponent.GraphWindowIndex;
                return Math.Max(0, Math.Min(tier, RANGE_TIER_COUNT - 1));
            }
        }

        internal void SetRangeTier(int tier)
        {
            tier = Math.Max(0, Math.Min(tier, RANGE_TIER_COUNT - 1));
            if (tier == RangeTier)
                return;

            PowerComponent.PowerHistoryTier = tier;
            PowerComponent.GraphWindowIndex = tier;
            if (Host.Block != null && Host.ProviderConfig != null)
                ConfigManager.Sync(Host.Block, Host.ProviderConfig);
            ClearAverages();
        }

        static int GetRangeWindowTicks(int tier)
        {
            return tier >= RANGE_TIER_COUNT - 1 ? 0 : (int)(EnergyDashboardApp.GetScaleWindowSeconds(tier) * 60f);
        }

        void ClearAverages()
        {
            foreach (var average in _currentAverages.Values)
                average.Clear();
            foreach (var average in _maxAverages.Values)
                average.Clear();
        }

        static TickWindowAverage GetAverage(Dictionary<string, TickWindowAverage> averages, string key)
        {
            TickWindowAverage average;
            if (!averages.TryGetValue(key, out average))
                averages[key] = average = new TickWindowAverage(MAX_RANGE_WINDOW_TICKS);
            return average;
        }

        void BuildVisibleEntries()
        {
            _visibleEntries.Clear();
            var hideEmpty = PowerComponent.HideEmpty;
            for (int i = 0; i < _entriesOrdered.Length; i++)
            {
                if (!hideEmpty || _entriesOrdered[i].DetectedBlocks > 0)
                    _visibleEntries.Add(_entriesOrdered[i]);
            }
        }

        void SyncOrderedEntries()
        {
            for (int i = 0; i < _entryOrder.Length; i++)
                _entriesOrdered[i] = _entriesByKey[_entryOrder[i]];
        }

        void SumPowerSources(Dictionary<string, PowerTotals> totals)
        {
            for (int i = 0; i < _entryOrder.Length; i++)
            {
                var key = _entryOrder[i];
                totals[key] = new PowerTotals();
            }

            var sources = _producerSources.Sources;
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var producers = sources[sourceIndex];
                for (int i = 0; i < producers.Count; i++)
                {
                    var prod = producers[i];
                    var typeId = string.Empty;

                    try
                    {
                        typeId = prod.BlockDefinition.TypeIdString ?? string.Empty;
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, GetType());
                    }

                    string key;
                    if (!TryMapProducerType(typeId, prod, out key))
                        continue;
                    if (!totals.ContainsKey(key))
                        continue;

                    var values = totals[key];
                    values.Current += ToWatts(prod != null ? prod.CurrentOutput : 0);
                    values.Max += ToWatts(PowerBlockMath.AvailableOutput(prod));
                    values.Count++;
                    totals[key] = values;
                }
            }
        }

        public override void Close()
        {
            _producerSources.Dispose();
            base.Close();
        }


        public override bool HasVisibleItems() => _visibleEntries.Count > 0;

        protected static double ToWatts(float powerUnit)
        {
            return powerUnit * 1000000;
        }
    }
}
