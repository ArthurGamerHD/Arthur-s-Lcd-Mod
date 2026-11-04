using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Farm;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(9)]
    // ReSharper disable once PartialTypeWithSinglePart
    internal sealed partial class FarmApp : App, IApp, Farm.IFarmHost
    {
        const int DATA_REFRESH_RUN_INTERVAL = 6;
        internal sealed class FarmEntry
        {
            readonly List<string> _details = new List<string>();

            public long EntryId { get; private set; }
            public FarmPlotEntry Plot { get; private set; }
            public float Ratio { get; private set; }
            public string PercentText { get; private set; }
            public string RemainingText { get; private set; }
            public string WaterText { get; private set; }
            public float WaterRatio { get; private set; }
            public string OutputSprite { get; private set; }
            public string OutputName { get; private set; }
            public Color StatusColor { get; private set; }
            public string DetailsMarkdown { get; private set; } = string.Empty;
            // The plot block's name (its custom name, else its display name); empty without a block.
            public string Title { get; private set; } = string.Empty;

            public bool Update(
                FarmPlotEntry plot,
                float ratio,
                string percentText,
                string remainingText,
                string waterText,
                float waterRatio,
                string outputSprite,
                string outputName,
                Color statusColor,
                string detailedInfo)
            {
                var block = plot != null ? plot.Block : null;
                return Update(plot, block != null ? block.EntityId : 0L,
                    block == null ? string.Empty : !string.IsNullOrEmpty(block.CustomName) ? block.CustomName : block.DisplayNameText,
                    ratio, percentText, remainingText, waterText, waterRatio, outputSprite, outputName, statusColor, detailedInfo);
            }

            // The entry's presentation from its values; plot may be null (details then list the growth).
            public bool Update(
                FarmPlotEntry plot,
                long entryId,
                string title,
                float ratio,
                string percentText,
                string remainingText,
                string waterText,
                float waterRatio,
                string outputSprite,
                string outputName,
                Color statusColor,
                string detailedInfo)
            {
                ratio = MathHelper.Clamp(ratio, 0f, 1f);
                waterRatio = MathHelper.Clamp(waterRatio, 0f, 1f);
                percentText = percentText ?? string.Empty;
                remainingText = remainingText ?? string.Empty;
                waterText = waterText ?? string.Empty;
                outputSprite = outputSprite ?? string.Empty;
                outputName = outputName ?? string.Empty;

                title = title ?? string.Empty;
                bool presentationChanged =
                    EntryId != entryId ||
                    !string.Equals(Title, title, StringComparison.Ordinal) ||
                    !Ratio.Equals(ratio) ||
                    !string.Equals(PercentText, percentText, StringComparison.Ordinal) ||
                    !string.Equals(RemainingText, remainingText, StringComparison.Ordinal) ||
                    !string.Equals(WaterText, waterText, StringComparison.Ordinal) ||
                    !WaterRatio.Equals(waterRatio) ||
                    !string.Equals(OutputSprite, outputSprite, StringComparison.Ordinal) ||
                    !string.Equals(OutputName, outputName, StringComparison.Ordinal) ||
                    !StatusColor.Equals(statusColor);
                string previousDetails = DetailsMarkdown;

                Plot = plot;
                EntryId = entryId;
                Title = title;
                Ratio = ratio;
                PercentText = percentText;
                RemainingText = remainingText;
                WaterText = waterText;
                WaterRatio = waterRatio;
                OutputSprite = outputSprite;
                OutputName = outputName;
                StatusColor = statusColor;

                RefreshDetails(detailedInfo);

                return presentationChanged ||
                       !string.Equals(previousDetails, DetailsMarkdown, StringComparison.Ordinal);
            }

            void RefreshDetails(string detailedInfo)
            {
                _details.Clear();

                var logic = Plot?.Logic;
                if (logic == null)
                {
                    _details.Add(PercentText);
                    DetailsMarkdown = BuildDetailsMarkdown();
                    return;
                }

                if (!string.IsNullOrEmpty(OutputName))
                {
                    var amount = logic.OutputItemAmount;
                    _details.Add(amount > 0
                        ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "Farm_OutputAmount"),
                            OutputName, amount)
                        : OutputName);
                }

                if (!string.IsNullOrEmpty(WaterText))
                    _details.Add(WaterText);

                AppendDetailedInfoLines(detailedInfo);

                if (_details.Count == 0)
                    _details.Add(PercentText);

                DetailsMarkdown = BuildDetailsMarkdown();
            }

            void AppendDetailedInfoLines(string detailInfo)
            {
                if (string.IsNullOrEmpty(detailInfo))
                    return;

                detailInfo = detailInfo.Replace("\r\n", "\n").Replace('\r', '\n');
                var lines = detailInfo.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i == lines.Length - 1 && string.IsNullOrEmpty(lines[i]))
                        continue;

                    _details.Add(lines[i]);
                }
            }

            string BuildDetailsMarkdown()
            {
                if (_details.Count == 0)
                    return string.Empty;

                var sb = new StringBuilder();
                for (int i = 0; i < _details.Count; i++)
                {
                    var text = _details[i];
                    if (string.IsNullOrEmpty(text))
                        continue;

                    if (sb.Length > 0)
                        sb.AppendLine().AppendLine();

                    sb.Append(text);
                }

                return sb.ToString();
            }
        }

        readonly IAppHost _surfaceHost;
        readonly List<FarmEntry> _entries = new List<FarmEntry>();
        readonly List<FarmEntry> _nextEntries = new List<FarmEntry>();
        readonly Dictionary<long, FarmEntry> _entryById = new Dictionary<long, FarmEntry>();
        readonly Dictionary<long, FarmEntry> _entryModelsById = new Dictionary<long, FarmEntry>();
        readonly HashSet<long> _activeEntryIds = new HashSet<long>();
        readonly List<long> _entryIdsToRemove = new List<long>();
        readonly FarmGrowthHelper _growthDefinitions = new FarmGrowthHelper();
        readonly Farm.FarmViewModel _viewModel;
        int _runsUntilDataRefresh;
        internal ColorConfigComponent FarmColors => ColorComponent;

        public event Action PlotDetailsOpened;

        public FarmApp(IAppHost surfaceHost) : base(surfaceHost)
        {
            _viewModel = new Farm.FarmViewModel(this);
            _viewModel.DetailsOpened += () => PlotDetailsOpened?.Invoke();
            SetContent(new Farm.FarmView { DataContext = _viewModel });
            NeedGridData(GridCapability.Blocks);
            _surfaceHost = surfaceHost;

        }

        internal override float WindowRenderScaling => CardScale;

        internal IReadOnlyList<FarmEntry> NativeEntries => _entries;
        internal Color NativeBackground => _surfaceHost.BackgroundColor;
        bool Farm.IFarmHost.IsGridDisplay => IsGridDisplay;
        Color Farm.IFarmHost.NativeBackground => NativeBackground;
        ColorConfigComponent Farm.IFarmHost.FarmColors => FarmColors;
        IReadOnlyList<FarmEntry> Farm.IFarmHost.NativeEntries => NativeEntries;

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            _runsUntilDataRefresh = 0;
            _entries.Clear();
            _nextEntries.Clear();
            _entryById.Clear();
            _entryModelsById.Clear();
            MarkDirty();
        }

        public override void Update()
        {
            if (_runsUntilDataRefresh > 0)
            {
                _runsUntilDataRefresh--;
                return;
            }

            _runsUntilDataRefresh = DATA_REFRESH_RUN_INTERVAL - 1;
            RefreshEntries();
        }

        void RefreshEntries()
        {
            _nextEntries.Clear();
            _entryById.Clear();
            _activeEntryIds.Clear();
            bool changed = false;

            var gridLogic = _surfaceHost.GridLogic;
            if (gridLogic == null)
            {
                changed = _entries.Count > 0 || _entryModelsById.Count > 0;
                _entries.Clear();
                _entryModelsById.Clear();
                if (changed)
                    MarkDirty();
                _viewModel.Refresh();
                return;
            }

            var farmPlots = gridLogic.FarmPlots;
            for (int i = 0; i < farmPlots.Count; i++)
            {
                var plot = farmPlots[i];
                if (plot == null || plot.Block == null || plot.Logic == null)
                    continue;

                bool entryChanged;
                var entry = GetOrUpdateEntry(plot, out entryChanged);
                changed |= entryChanged;
                _nextEntries.Add(entry);
                _entryById[entry.EntryId] = entry;
                _activeEntryIds.Add(entry.EntryId);
            }

            changed |= !EntryOrderEquals(_entries, _nextEntries);
            _entries.Clear();
            _entries.AddRange(_nextEntries);
            changed |= RemoveStaleEntryModels();

            if (changed)
                MarkDirty();
            _viewModel.Refresh();
        }

        public override bool HasVisibleItems()
        {
            return _entries.Count > 0;
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        public void CompleteHostRender()
        {
            ClearDirtyAfterRender();
        }

        FarmEntry GetOrUpdateEntry(FarmPlotEntry plot, out bool changed)
        {
            var entryId = plot.Block.EntityId;
            FarmEntry entry;
            if (!_entryModelsById.TryGetValue(entryId, out entry) || entry == null)
            {
                entry = new FarmEntry();
                _entryModelsById[entryId] = entry;
            }

            var detailedInfo = GetDetailedInfo(plot);
            float growthPercent;
            var ratio = GetGrowthRatio(plot, detailedInfo, out growthPercent);
            var outputItem = plot.Logic.OutputItem;
            var outputSprite = ResolveOutputSprite(outputItem);
            var outputName = ResolveOutputName(outputItem);
            var percentText = FormatingHelper.PercentageToString(ratio);
            var remainingText = GetRemainingText(plot, growthPercent, ratio, percentText);
            var waterRatio = GetWaterRatio(plot);

            changed = entry.Update(
                plot,
                ratio,
                percentText,
                remainingText,
                GetWaterText(waterRatio, plot),
                waterRatio,
                outputSprite,
                outputName,
                GetStatusColor(plot, ratio),
                detailedInfo);
            return entry;
        }

        static string GetDetailedInfo(FarmPlotEntry plot)
        {
            try
            {
                return plot?.Logic?.GetDetailedInfoWithoutRequiredInput() ?? string.Empty;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(FarmApp));
                return string.Empty;
            }
        }

        static bool EntryOrderEquals(List<FarmEntry> current, List<FarmEntry> next)
        {
            if (ReferenceEquals(current, next))
                return true;
            if (current == null || next == null || current.Count != next.Count)
                return false;

            for (int i = 0; i < current.Count; i++)
            {
                if (!ReferenceEquals(current[i], next[i]))
                    return false;
            }

            return true;
        }

        static float GetWaterRatio(FarmPlotEntry plot)
        {
            var storage = plot?.StorageComponent;
            if (storage == null)
                return 0f;

            return MathHelper.Clamp((float)storage.FilledRatio, 0f, 1f);
        }

        static string GetWaterText(float ratio, FarmPlotEntry plot)
        {
            if (plot == null || plot.StorageComponent == null)
                return string.Empty;

            return string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "Farm_Water"),
                FormatingHelper.PercentageToString(ratio));
        }

        string ResolveOutputSprite(MyDefinitionId outputItem)
        {
            if (outputItem.Equals(default(MyDefinitionId)) || MyDefinitionManager.Static == null)
                return string.Empty;

            MyPhysicalItemDefinition definition;
            if (!MyDefinitionManager.Static.TryGetPhysicalItemDefinition(outputItem, out definition))
                return string.Empty;

            return TextureHelper.ResolveItemSprite(definition, _surfaceHost.Surface);
        }

        static string ResolveOutputName(MyDefinitionId outputItem)
        {
            if (outputItem.Equals(default(MyDefinitionId)) || MyDefinitionManager.Static == null)
                return string.Empty;

            MyPhysicalItemDefinition definition;
            if (!MyDefinitionManager.Static.TryGetPhysicalItemDefinition(outputItem, out definition))
                return outputItem.SubtypeName;

            return !string.IsNullOrEmpty(definition.DisplayNameText) ? definition.DisplayNameText : outputItem.SubtypeName;
        }

        Color GetStatusColor(FarmPlotEntry plot, float ratio)
        {
            if (plot?.Logic == null || !plot.Logic.IsPlantPlanted)
                return _surfaceHost.ForegroundColor;

            if (!plot.Logic.IsAlive)
                return ColorComponent.ResolveErrorColor();

            if (ratio >= 1f || plot.Logic.IsHarvestable)
                return GetHeaderColor();

            return _surfaceHost.ForegroundColor;
        }

        bool RemoveStaleEntryModels()
        {
            _entryIdsToRemove.Clear();
            foreach (var kv in _entryModelsById)
            {
                if (!_activeEntryIds.Contains(kv.Key))
                    _entryIdsToRemove.Add(kv.Key);
            }

            for (int i = 0; i < _entryIdsToRemove.Count; i++)
            {
                long entryId = _entryIdsToRemove[i];
                _entryModelsById.Remove(entryId);

            }

            return _entryIdsToRemove.Count > 0;
        }

        string GetRemainingText(FarmPlotEntry plot, float growthPercent, float ratio, string percentText)
        {
            var logic = plot?.Logic;
            if (logic == null || !logic.IsPlantPlanted)
                return LocHelper.Empty;

            if (!logic.IsAlive)
                return "--";

            if (logic.IsPlantFullyGrown || logic.IsHarvestable || ratio >= 0.999f)
                return FormatRemainingSeconds(0d);

            double remainingSeconds;
            return TryGetRemainingSeconds(plot, growthPercent, out remainingSeconds)
                ? FormatRemainingSeconds(remainingSeconds)
                : percentText ?? string.Empty;
        }

        bool TryGetRemainingSeconds(
            FarmPlotEntry plot,
            float growthProgressPercent,
            out double seconds)
        {
            seconds = 0d;
            if (FarmGrowthHelper.TryGetRuntimeRemainingSeconds(plot, out seconds))
                return true;

            FarmGrowthProfile profile;
            return _growthDefinitions.TryResolveGrowthProfile(plot, out profile) &&
                   FarmGrowthHelper.TryGetRemainingSeconds(profile, growthProgressPercent, out seconds);
        }

        static string FormatRemainingSeconds(double remainingSeconds)
        {
            if (double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds))
                remainingSeconds = 0d;

            return FormatingHelper.FormatTimeHours((float)Math.Max(0d, remainingSeconds / 3600d));
        }

        static float GetGrowthRatio(FarmPlotEntry plot, string detailedInfo, out float growthPercent)
        {
            growthPercent = 0f;
            var logic = plot?.Logic;
            if (logic == null || !logic.IsPlantPlanted)
                return 0f;
            if (logic.IsPlantFullyGrown)
            {
                growthPercent = 100f;
                return 1f;
            }
            if (!logic.IsAlive)
                return 0f;

            if (!string.IsNullOrEmpty(detailedInfo))
            {
                float percent;
                int percentIndex;
                // Growth progress is not exposed as a typed IMyFarmPlotLogic property.
                if (TryParseFirstPercent(detailedInfo, out percent, out percentIndex))
                {
                    growthPercent = MathHelper.Clamp(percent, 0f, 100f);
                    return growthPercent / 100f;
                }
            }

            return 0f;
        }

        static bool TryParseFirstPercent(string text, out float value, out int percentIndex)
        {
            value = 0f;
            percentIndex = -1;
            if (string.IsNullOrEmpty(text))
                return false;

            percentIndex = text.IndexOf('%');
            if (percentIndex <= 0)
                return false;

            var start = percentIndex - 1;
            while (start >= 0)
            {
                var c = text[start];
                if (!char.IsDigit(c) && c != '.' && c != ',' && c != '-' && c != '+' && !char.IsWhiteSpace(c))
                    break;
                start--;
            }

            var token = text.Substring(start + 1, percentIndex - start - 1).Trim().Replace(" ", string.Empty);
            if (string.IsNullOrEmpty(token))
                return false;

            if (float.TryParse(token, NumberStyles.Float, FormatingHelper.Culture, out value))
                return true;

            token = token.Replace(',', '.');
            return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

    }
}
