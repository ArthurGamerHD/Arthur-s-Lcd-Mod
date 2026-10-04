using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.EnergyDashboard;
using LcdMod.Client.Config;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Power;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Apps
{
    [LcdApp(7)]
    [ConfigComponent(Constants.APP, typeof(PowerConfigComponent), PropertyName = "PowerComponent")]
    internal sealed partial class EnergyDashboardApp : App, IApp
    {
        const int SCALE_TIER_COUNT = EnergyDashboardViewModel.TIER_COUNT;
        const int GRAPH_BUCKET_COUNT = 10;
        const int ROW_REFRESH_RUN_INTERVAL = 6;
        const int GRAPH_1_SECOND_REFRESH_RUN_INTERVAL = 1;
        const int GRAPH_5_SECOND_REFRESH_RUN_INTERVAL = 3;
        const int GRAPH_LONG_REFRESH_RUN_INTERVAL = 6;
        const string NO_DATA = "No data";

        readonly List<EnergyDashboardPowerRow> _producerRows = new List<EnergyDashboardPowerRow>();
        readonly List<EnergyDashboardPowerRow> _consumerRows = new List<EnergyDashboardPowerRow>();
        readonly List<EnergyDashboardPowerRow> _chargeRows = new List<EnergyDashboardPowerRow>();
        readonly Dictionary<EnergyDashboardPowerCategory, Dictionary<string, EnergyDashboardPowerRow>>
            _rowsByCategory =
                new Dictionary<EnergyDashboardPowerCategory, Dictionary<string, EnergyDashboardPowerRow>>
                {
                    {
                        EnergyDashboardPowerCategory.Producer,
                        new Dictionary<string, EnergyDashboardPowerRow>(StringComparer.Ordinal)
                    },
                    {
                        EnergyDashboardPowerCategory.Consumer,
                        new Dictionary<string, EnergyDashboardPowerRow>(StringComparer.Ordinal)
                    },
                    {
                        EnergyDashboardPowerCategory.Charge,
                        new Dictionary<string, EnergyDashboardPowerRow>(StringComparer.Ordinal)
                    }
                };
        readonly Dictionary<EnergyDashboardPowerCategory, EnergyDashboardPowerRow> _selectedRows =
            new Dictionary<EnergyDashboardPowerCategory, EnergyDashboardPowerRow>();
        readonly Dictionary<EnergyDashboardPowerCategory, EnergyDashboardPowerRow> _hoverRows =
            new Dictionary<EnergyDashboardPowerCategory, EnergyDashboardPowerRow>();
        readonly Dictionary<string, string> _spriteCache = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<string> _rowKeysToRemove = new List<string>();
        readonly List<EnergyPowerRowModel> _rowModels = new List<EnergyPowerRowModel>();
        readonly List<EnergyGraphSeries> _series = new List<EnergyGraphSeries>();
        readonly EnergyDashboardViewModel _viewModel;
        readonly EnergyDashboardView _view;
        long _powerRowUpdateToken;
        long _lastGraphPresentedFrame = long.MinValue;
        int _runsUntilRowRefresh;
        int _runsUntilGraphRefresh;
        int _presentationTier = -1;
        bool _rowRefreshPending = true;
        bool _hoverRefreshQueued;
        bool _closed;
        PowerDataLease _lease;
        PowerDataService _presentedService;
        PowerSnapshot _latest;
        List<PowerSnapshot> _presentationSnapshots = new List<PowerSnapshot>();

        internal override float WindowRenderScaling => GeneralComponent.GetScale() > 0f ? GeneralComponent.GetScale() : 1f;

        public EnergyDashboardApp(IAppHost host) : base(host)
        {
            var labels = new string[SCALE_TIER_COUNT];
            for (int i = 0; i < labels.Length; i++)
                labels[i] = GetScaleLabel(i);
            _viewModel = new EnergyDashboardViewModel(labels, SetScaleTierIndex);
            _view = new EnergyDashboardView(_viewModel);
            SetContent(_view);
            CaptureLease();
            RefreshColors();
            Present();
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            _runsUntilRowRefresh = 0;
            _runsUntilGraphRefresh = 0;
            _rowRefreshPending = true;
            RefreshColors();
            Present();
        }

        public override void Update()
        {
            CaptureLease();
            bool listPinned = _view.IsPointerOverList();

            var currentService = _lease?.Service;
            int currentTier = GetScaleTierIndex();
            bool serviceChanged = !ReferenceEquals(_presentedService, currentService);
            bool tierChanged = _presentationTier != currentTier;
            if (serviceChanged)
            {
                _runsUntilRowRefresh = 0;
                _runsUntilGraphRefresh = 0;
                _rowRefreshPending = true;
            }
            else if (tierChanged)
            {
                _runsUntilGraphRefresh = 0;
                _rowRefreshPending = true;
            }

            if (TakeRefreshTurn(ref _runsUntilRowRefresh, ROW_REFRESH_RUN_INTERVAL))
                _rowRefreshPending = true;

            bool refreshGraph = TakeRefreshTurn(
                ref _runsUntilGraphRefresh,
                GetGraphRefreshRunInterval(currentTier));
            var latest = _lease?.Latest ?? new PowerSnapshot();
            bool presentationChanged = false;

            if (refreshGraph)
            {
                long graphRevision;
                var graphSnapshots = GetSnapshots(_lease?.History, out graphRevision);
                if (serviceChanged || tierChanged || _lastGraphPresentedFrame != graphRevision)
                {
                    _lastGraphPresentedFrame = graphRevision;
                    _presentationTier = currentTier;
                    _latest = latest;
                    _presentationSnapshots = graphSnapshots;
                    presentationChanged = true;
                }
            }

            if (presentationChanged && _rowRefreshPending && !listPinned)
            {
                RefreshRowsFromGraphPresentation();
                _rowRefreshPending = false;
            }

            _presentedService = currentService;
            if (presentationChanged)
                Present();
        }

        public override List<MySprite> GetSprites() { return new List<MySprite>(); }

        public override void Close()
        {
            _closed = true;
            DisposeLease();
            base.Close();
        }

        void RefreshColors()
        {
            _viewModel.RefreshColors(Host.Surface.ScriptForegroundColor,
                ResolveResource(ThemeResources.FontColor, Color.White),
                ResolveResource(ThemeResources.AccentColor, Color.White),
                ResolveResource(ThemeResources.SecondaryContainerColor, Color.White));
        }

        void Present()
        {
            Color foreground = Host.Surface.ScriptForegroundColor;
            double maxConsumptionW = EnergyDashboardPowerMetrics.GetMaxConsumerTotal(_latest);
            float loadRatio = EnergyDashboardPowerMetrics.Ratio(_latest.TotalRequiredInputW, maxConsumptionW);
            float chargeRatio = EnergyDashboardPowerMetrics.Ratio(_latest.StoredEnergyWh, _latest.MaxStoredEnergyWh);
            PresentBar(_viewModel.Consumption, "Consumption", _latest.TotalRequiredInputW, maxConsumptionW, false,
                GetLoadColor(loadRatio), foreground);
            PresentBar(_viewModel.Production, "Production", _latest.Producers.KnownCurrentOutputW,
                _latest.MaxAvailableW, false, GetHeaderColor(), foreground);
            PresentBar(_viewModel.Charge, "Charge", _latest.StoredEnergyWh, _latest.MaxStoredEnergyWh, true,
                GetBatteryIconColor(chargeRatio), foreground);

            int selectedScale = GetScaleTierIndex();
            bool showAll = selectedScale == SCALE_TIER_COUNT - 1;
            float windowSeconds = showAll ? 0f : GetScaleWindowSeconds(selectedScale);
            _viewModel.SetSelectedTier(selectedScale);
            ApplyRowState(_consumerRows, EnergyDashboardPowerCategory.Consumer, foreground);
            ApplyRowState(_producerRows, EnergyDashboardPowerCategory.Producer, foreground);
            ApplyRowState(_chargeRows, EnergyDashboardPowerCategory.Charge, foreground);
            PresentGraph(_viewModel.ConsumerPage, "Consumption", _consumerRows, false, false,
                GetActiveSelection(EnergyDashboardPowerCategory.Consumer), windowSeconds, showAll, foreground);
            PresentGraph(_viewModel.ProducerPage, "Production", _producerRows, true, false,
                GetActiveSelection(EnergyDashboardPowerCategory.Producer), windowSeconds, showAll, foreground);
            PresentGraph(_viewModel.ChargePage, "Charge", _chargeRows, false, true,
                GetActiveSelection(EnergyDashboardPowerCategory.Charge), windowSeconds, showAll, foreground);
        }

        static void PresentBar(EnergyStatBarModel bar, string label, double current, double max, bool energy,
            Color fill, Color foreground)
        {
            float ratio = EnergyDashboardPowerMetrics.Ratio(current, max);
            string value = energy
                ? FormatWattHours(current) + "/" + FormatWattHours(max) + " (" +
                  FormatingHelper.PercentageToString(ratio) + ")"
                : FormatingHelper.WattsToString(current) + "/" + FormatingHelper.WattsToString(max);
            bar.Update(label, value, ratio, fill, foreground);
        }

        static string FormatWattHours(double wattHours)
        {
            return Math.Abs(wattHours) < 1e-12 ? "0 Wh" : FormatingHelper.WattHoursToString((float)wattHours);
        }

        void PresentGraph(EnergyPageModel page, string title, List<EnergyDashboardPowerRow> rows, bool producers,
            bool charge, EnergyDashboardPowerRow selected, float windowSeconds, bool useTimeSpacing, Color foreground)
        {
            var snapshots = _presentationSnapshots ?? new List<PowerSnapshot>();
            double max = EnergyDashboardPowerMetrics.GetGraphMax(snapshots, rows, producers, charge, selected);
            bool empty = max <= 0 || snapshots.Count <= 0 || rows.Count == 0;
            var axis = new string[4];
            for (int i = 0; i < axis.Length; i++)
                axis[i] = empty
                    ? string.Empty
                    : charge
                        ? FormatingHelper.WattHoursToString(max * (i / 3.0), "0.#")
                        : FormatingHelper.WattsToString(max * (i / 3.0), "0.#");

            string left = string.Empty, middle = string.Empty, right = string.Empty;
            if (snapshots.Count > 0)
            {
                long latestFrame = snapshots[snapshots.Count - 1].GameplayFrame;
                left = windowSeconds > 0f
                    ? FormatingHelper.FormatRelativeTime(windowSeconds)
                    : FormatingHelper.FormatRelativeTime(latestFrame, GetInterpolatedFrame(snapshots, 0f));
                middle = windowSeconds > 0f
                    ? FormatingHelper.FormatRelativeTime(windowSeconds * .5f)
                    : FormatingHelper.FormatRelativeTime(latestFrame, GetInterpolatedFrame(snapshots, .5f));
                right = FormatingHelper.FormatRelativeTime(0f);
            }

            _series.Clear();
            if (!empty)
            {
                int seriesCount = Math.Min(EnergyDashboardPowerMetrics.MAX_GRAPH_SERIES, rows.Count);
                for (int r = 0; r < seriesCount; r++)
                    if (!ReferenceEquals(rows[r], selected))
                        AddSeries(snapshots, rows[r], producers, charge, max, useTimeSpacing);
                if (selected != null)
                    AddSeries(snapshots, selected, producers, charge, max, useTimeSpacing);
            }

            page.UpdateGraph(title, axis, left, middle, right, snapshots.Count > 0, empty, NO_DATA,
                _series.ToArray(), foreground);
        }

        void AddSeries(List<PowerSnapshot> snapshots, EnergyDashboardPowerRow row, bool producers, bool charge,
            double max, bool useTimeSpacing)
        {
            if (row.Entry == null || snapshots.Count == 0)
                return;

            var points = new Vector2[snapshots.Count];
            for (int i = 0; i < snapshots.Count; i++)
            {
                double value = EnergyDashboardPowerMetrics.GetSubtypeValue(snapshots[i], row.Entry.Key, producers, charge);
                float x = useTimeSpacing ? GetTimeSpacedX(snapshots, i) : GetIndexSpacedX(snapshots.Count, i);
                points[i] = new Vector2(x, (float)Math.Min(1.0, value / max));
            }

            _series.Add(new EnergyGraphSeries(points, row.Color));
        }

        static float GetIndexSpacedX(int count, int index)
        {
            return count <= 1 ? 0f : (float)index / (count - 1);
        }

        static float GetTimeSpacedX(List<PowerSnapshot> snapshots, int index)
        {
            int count = snapshots.Count;
            if (count <= 1)
                return 0f;

            long first = snapshots[0].GameplayFrame;
            long last = snapshots[count - 1].GameplayFrame;
            if (last <= first)
                return GetIndexSpacedX(count, index);

            double ratio = (snapshots[index].GameplayFrame - first) / (double)(last - first);
            return (float)Math.Max(0.0, Math.Min(1.0, ratio));
        }

        static long GetInterpolatedFrame(List<PowerSnapshot> snapshots, float ratio)
        {
            int count = snapshots.Count;
            if (count <= 0)
                return 0L;

            long first = snapshots[0].GameplayFrame;
            if (count == 1)
                return first;

            long last = snapshots[count - 1].GameplayFrame;
            return first + (long)Math.Round((last - first) * Math.Max(0f, Math.Min(1f, ratio)));
        }

        void RefreshRowsFromGraphPresentation()
        {
            var rowSnapshot = GetNewestDisplayedSnapshot();
            BuildRows(rowSnapshot.ProducerSubtypes, _producerRows,
                EnergyDashboardPowerMetrics.GetCurrentProducerTotal(rowSnapshot),
                EnergyDashboardPowerCategory.Producer, false);
            BuildRows(rowSnapshot.ConsumerSubtypes, _consumerRows,
                EnergyDashboardPowerMetrics.GetCurrentConsumerTotal(rowSnapshot),
                EnergyDashboardPowerCategory.Consumer, false);
            BuildRows(rowSnapshot.ChargeSubtypes, _chargeRows, rowSnapshot.MaxStoredEnergyWh,
                EnergyDashboardPowerCategory.Charge, true);
            SetChargeControlsVisible(HasChargeableDevices(_latest));
            PublishRows(_viewModel.ProducerPage, _producerRows);
            PublishRows(_viewModel.ConsumerPage, _consumerRows);
            PublishRows(_viewModel.ChargePage, _chargeRows);
        }

        void PublishRows(EnergyPageModel page, List<EnergyDashboardPowerRow> rows)
        {
            _rowModels.Clear();
            for (int i = 0; i < rows.Count; i++)
                _rowModels.Add(rows[i].Model);
            page.SetRows(_rowModels);
        }

        void SetChargeControlsVisible(bool visible)
        {
            if (!visible)
            {
                _selectedRows.Remove(EnergyDashboardPowerCategory.Charge);
                _hoverRows.Remove(EnergyDashboardPowerCategory.Charge);
            }

            _viewModel.SetHasCharge(visible);
        }

        void BuildRows(List<PowerSubtypeSnapshot> entries, List<EnergyDashboardPowerRow> rows, double ratioDenominatorW,
            EnergyDashboardPowerCategory category, bool charge)
        {
            rows.Clear();
            var rowsByKey = _rowsByCategory[category];
            long updateToken = ++_powerRowUpdateToken;

            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null || string.IsNullOrEmpty(entry.Key) ||
                        entry.CurrentW <= 0 && entry.MaxW <= 0)
                        continue;

                    EnergyDashboardPowerRow row;
                    if (!rowsByKey.TryGetValue(entry.Key, out row))
                    {
                        row = new EnergyDashboardPowerRow();
                        var created = row;
                        row.Model = new EnergyPowerRowModel(() => OnPowerRowClicked(created),
                            hovered => OnPowerRowHovered(created, hovered));
                        rowsByKey.Add(entry.Key, row);
                    }
                    else if (row.UpdateToken == updateToken)
                    {
                        continue;
                    }

                    row.Entry = entry;
                    row.Category = category;
                    row.SpriteName = ResolveSubtypeSprite(entry);
                    row.CurrentW = entry.CurrentW;
                    row.MaxW = entry.MaxW;
                    row.RatioDenominatorW = charge ? entry.MaxW : ratioDenominatorW;
                    row.IsCharge = charge;
                    row.UpdateToken = updateToken;
                    string label = GetRowLabel(entry);
                    row.Model.Update(row.SpriteName,
                        string.IsNullOrEmpty(row.SpriteName) ? GetInitial(label) : string.Empty,
                        entry.BlockCount.ToString(FormatingHelper.Culture), label,
                        charge ? FormatingHelper.WattHoursToString(row.CurrentW) : FormatingHelper.WattsToString(row.CurrentW),
                        EnergyDashboardPowerMetrics.Ratio(row.CurrentW, row.RatioDenominatorW));
                    rows.Add(row);
                }
            }

            rows.Sort(ComparePowerRowsDescending);
            for (int i = 0; i < rows.Count; i++)
                rows[i].Color = ColorForSubtypeIndex(i);

            _rowKeysToRemove.Clear();
            foreach (var kv in rowsByKey)
            {
                if (kv.Value == null || kv.Value.UpdateToken != updateToken)
                    _rowKeysToRemove.Add(kv.Key);
            }

            for (int i = 0; i < _rowKeysToRemove.Count; i++)
                rowsByKey.Remove(_rowKeysToRemove[i]);
        }

        static string GetInitial(string label)
        {
            if (string.IsNullOrEmpty(label))
                return "?";
            for (int i = 0; i < label.Length; i++)
            {
                if (!char.IsWhiteSpace(label[i]))
                    return char.ToUpperInvariant(label[i]).ToString();
            }

            return "?";
        }

        void ApplyRowState(List<EnergyDashboardPowerRow> rows, EnergyDashboardPowerCategory category, Color foreground)
        {
            EnsureTrackedRowExists(rows, category, _selectedRows);
            EnsureTrackedRowExists(rows, category, _hoverRows);
            var selected = GetTrackedRow(_selectedRows, category);
            var hover = GetTrackedRow(_hoverRows, category);
            var active = selected ?? hover;

            for (int i = 0; rows != null && i < rows.Count; i++)
            {
                var row = rows[i];
                row.Selected = ReferenceEquals(row, selected);
                row.Hover = ReferenceEquals(row, hover);
                row.Color = active != null
                    ? ColorForSubtypeIndex(i, ReferenceEquals(row, active))
                    : ColorForSubtypeIndex(i);
                row.Model.UpdateState(row.Color, foreground, row.Selected);
            }
        }

        void OnPowerRowClicked(EnergyDashboardPowerRow row)
        {
            if (row == null || row.Entry == null || string.IsNullOrEmpty(row.Entry.Key))
                return;

            var selected = GetTrackedRow(_selectedRows, row.Category);
            if (ReferenceEquals(selected, row))
                _selectedRows.Remove(row.Category);
            else
                _selectedRows[row.Category] = row;
            Present();
            Host.RenderSprites();
        }

        void OnPowerRowHovered(EnergyDashboardPowerRow row, bool hovered)
        {
            if (row == null || row.Entry == null || string.IsNullOrEmpty(row.Entry.Key))
                return;

            var previous = GetTrackedRow(_hoverRows, row.Category);
            if (hovered)
            {
                if (ReferenceEquals(previous, row))
                    return;
                _hoverRows.Clear();
                _hoverRows[row.Category] = row;
            }
            else
            {
                if (!ReferenceEquals(previous, row))
                    return;
                _hoverRows.Remove(row.Category);
            }

            if (_hoverRefreshQueued)
                return;
            _hoverRefreshQueued = true;
            LcdModClientComponent.RunNextFrame.Add(() =>
            {
                _hoverRefreshQueued = false;
                if (_closed)
                    return;
                Present();
                Host.RenderSprites();
            });
        }

        EnergyDashboardPowerRow GetActiveSelection(EnergyDashboardPowerCategory category)
        {
            return GetTrackedRow(_selectedRows, category) ?? GetTrackedRow(_hoverRows, category);
        }

        void SetScaleTierIndex(int tier)
        {
            tier = Math.Max(0, Math.Min(tier, SCALE_TIER_COUNT - 1));
            if (PowerComponent.PowerHistoryTier == tier && PowerComponent.GraphWindowIndex == tier)
                return;

            PowerComponent.PowerHistoryTier = tier;
            PowerComponent.GraphWindowIndex = tier;
            if (Host.Block != null && Host.ProviderConfig != null)
                ConfigManager.Sync(Host.Block, Host.ProviderConfig);

            _latest = _lease?.Latest ?? _latest;
            _presentationTier = tier;
            _runsUntilGraphRefresh = GetGraphRefreshRunInterval(tier) - 1;
            _presentationSnapshots = GetSnapshots(_lease?.History, out _lastGraphPresentedFrame);
            _rowRefreshPending = true;
            if (!_view.IsPointerOverList())
            {
                RefreshRowsFromGraphPresentation();
                _rowRefreshPending = false;
            }
            Present();
            Host.RenderSprites();
        }

        static bool TakeRefreshTurn(ref int runsUntilRefresh, int interval)
        {
            if (runsUntilRefresh > 0)
            {
                runsUntilRefresh--;
                return false;
            }

            runsUntilRefresh = Math.Max(1, interval) - 1;
            return true;
        }

        static int GetGraphRefreshRunInterval(int tier)
        {
            switch ((PowerHistoryTier)tier)
            {
                case PowerHistoryTier.Average1Second:
                    return GRAPH_1_SECOND_REFRESH_RUN_INTERVAL;
                case PowerHistoryTier.Average5Seconds:
                    return GRAPH_5_SECOND_REFRESH_RUN_INTERVAL;
                default:
                    return GRAPH_LONG_REFRESH_RUN_INTERVAL;
            }
        }

        void CaptureLease()
        {
            var client = LcdModSessionComponent.Client;
            var powerData = client?.PowerData;
            if (powerData == null)
                return;

            var requester = Host.GridLogic;
            var linkType = (GridLinkTypeEnum)PowerComponent.GridLinkTypeInternal;
            if (_lease != null && _lease.Service != null &&
                ReferenceEquals(_lease.Service.Requester, requester) &&
                _lease.Service.LinkType == linkType)
                return;

            DisposeLease();
            _lease = powerData.Capture(requester, linkType);
        }

        void DisposeLease()
        {
            if (_lease == null)
                return;

            _lease.Dispose();
            _lease = null;
        }

        PowerSnapshot GetNewestDisplayedSnapshot()
        {
            if (_presentationSnapshots != null)
            {
                for (int i = _presentationSnapshots.Count - 1; i >= 0; i--)
                {
                    if (HasPowerData(_presentationSnapshots[i]))
                        return _presentationSnapshots[i];
                }
            }

            return _latest;
        }

        static int ComparePowerRowsDescending(EnergyDashboardPowerRow a, EnergyDashboardPowerRow b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            int current = b.CurrentW.CompareTo(a.CurrentW);
            if (current != 0)
                return current;
            return string.Compare(GetRowLabel(a.Entry), GetRowLabel(b.Entry), StringComparison.OrdinalIgnoreCase);
        }

        static EnergyDashboardPowerRow GetTrackedRow(
            Dictionary<EnergyDashboardPowerCategory, EnergyDashboardPowerRow> trackedRows,
            EnergyDashboardPowerCategory category)
        {
            EnergyDashboardPowerRow row;
            return trackedRows.TryGetValue(category, out row) ? row : null;
        }

        static void EnsureTrackedRowExists(List<EnergyDashboardPowerRow> rows, EnergyDashboardPowerCategory category,
            Dictionary<EnergyDashboardPowerCategory, EnergyDashboardPowerRow> trackedRows)
        {
            var tracked = GetTrackedRow(trackedRows, category);
            if (tracked == null || rows != null && rows.Contains(tracked))
                return;

            trackedRows.Remove(category);
        }

        string ResolveSubtypeSprite(PowerSubtypeSnapshot entry)
        {
            if (entry == null)
                return string.Empty;

            var spriteKey = !string.IsNullOrEmpty(entry.SpriteKey) ? entry.SpriteKey : entry.Key;
            if (string.IsNullOrEmpty(spriteKey))
                return string.Empty;

            string cached;
            if (_spriteCache.TryGetValue(spriteKey, out cached))
                return cached;

            string spriteName;
            if (!TextureHelper.TryGetOrAddTextureForBlockName(spriteKey, out spriteName))
                spriteName = string.Empty;

            _spriteCache[spriteKey] = spriteName;
            return spriteName;
        }

        List<PowerSnapshot> GetSnapshots(PowerHistory history, out long revision)
        {
            revision = long.MinValue;
            if (history == null)
                return new List<PowerSnapshot>();

            int tier = GetScaleTierIndex();
            List<PowerSnapshot> snapshots;
            switch ((PowerHistoryTier)tier)
            {
                case PowerHistoryTier.Average1Second:
                    snapshots = history.RawSamples.ToListOldestFirst();
                    break;
                case PowerHistoryTier.Average5Seconds:
                    snapshots = history.Average5Seconds.ToListOldestFirst();
                    break;
                case PowerHistoryTier.Average30Seconds:
                    snapshots = history.Average30Seconds.ToListOldestFirst();
                    break;
                case PowerHistoryTier.Average1Minute:
                    snapshots = history.Average1Minute.ToListOldestFirst();
                    break;
                case PowerHistoryTier.Average5Minutes:
                    snapshots = history.Average5Minutes.ToListOldestFirst();
                    break;
                default:
                    snapshots = GetAllNonZeroSnapshots(history, _latest);
                    revision = GetNewestSnapshotFrame(snapshots);
                    return snapshots;
            }

            // RawSamples already contains the current complete 1-second-tier point. Averaged histories
            // contain only drained, complete buckets; do not append _latest as a partial right-edge bucket.
            revision = GetNewestSnapshotFrame(snapshots);
            return PadSnapshots(snapshots, GetScaleWindowSeconds(tier), _latest.GameplayFrame);
        }

        static long GetNewestSnapshotFrame(List<PowerSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
                return long.MinValue;

            return snapshots[snapshots.Count - 1].GameplayFrame;
        }

        static List<PowerSnapshot> GetAllNonZeroSnapshots(PowerHistory history, PowerSnapshot latest)
        {
            var byFrame = new Dictionary<long, PowerSnapshot>();
            AddNonZeroSnapshots(byFrame, history.Average30Minutes.ToListOldestFirst());
            AddNonZeroSnapshots(byFrame, history.Average5Minutes.ToListOldestFirst());
            AddNonZeroSnapshots(byFrame, history.Average1Minute.ToListOldestFirst());
            AddNonZeroSnapshots(byFrame, history.Average30Seconds.ToListOldestFirst());
            AddNonZeroSnapshots(byFrame, history.Average5Seconds.ToListOldestFirst());
            AddNonZeroSnapshots(byFrame, history.RawSamples.ToListOldestFirst());
            AddNonZeroSnapshot(byFrame, latest);

            var result = new List<PowerSnapshot>(byFrame.Values);
            result.Sort(CompareSnapshotsByFrame);
            return result;
        }

        static void AddNonZeroSnapshots(Dictionary<long, PowerSnapshot> byFrame, List<PowerSnapshot> snapshots)
        {
            if (snapshots == null)
                return;

            for (int i = 0; i < snapshots.Count; i++)
                AddNonZeroSnapshot(byFrame, snapshots[i]);
        }

        static void AddNonZeroSnapshot(Dictionary<long, PowerSnapshot> byFrame, PowerSnapshot snapshot)
        {
            if (!HasPowerData(snapshot))
                return;

            byFrame[snapshot.GameplayFrame] = snapshot;
        }

        static bool HasPowerData(PowerSnapshot snapshot)
        {
            if (snapshot.TotalRequiredInputW > 0.0 ||
                snapshot.MaxAvailableW > 0.0 ||
                snapshot.Producers.KnownCurrentOutputW > 0.0 ||
                snapshot.StoredEnergyWh > 0.0 ||
                snapshot.MaxStoredEnergyWh > 0.0)
                return true;

            return HasSubtypePowerData(snapshot.ProducerSubtypes) ||
                   HasSubtypePowerData(snapshot.ConsumerSubtypes) ||
                   HasSubtypePowerData(snapshot.ChargeSubtypes);
        }

        static bool HasChargeableDevices(PowerSnapshot snapshot)
        {
            if (snapshot.MaxStoredEnergyWh > 0.0)
                return true;

            var entries = snapshot.ChargeSubtypes;
            if (entries == null)
                return false;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && (entry.MaxW > 0.0 || entry.BlockCount > 0))
                    return true;
            }

            return false;
        }

        static bool HasSubtypePowerData(List<PowerSubtypeSnapshot> entries)
        {
            if (entries == null)
                return false;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry != null && (entry.CurrentW > 0.0 || entry.RequiredW > 0.0 || entry.MaxW > 0.0))
                    return true;
            }

            return false;
        }

        static int CompareSnapshotsByFrame(PowerSnapshot a, PowerSnapshot b)
        {
            return a.GameplayFrame.CompareTo(b.GameplayFrame);
        }

        static List<PowerSnapshot> PadSnapshots(List<PowerSnapshot> snapshots, float windowSeconds,
            long fallbackEndFrame)
        {
            if (snapshots == null)
                snapshots = new List<PowerSnapshot>();

            if (snapshots.Count > GRAPH_BUCKET_COUNT)
                snapshots = snapshots.GetRange(snapshots.Count - GRAPH_BUCKET_COUNT, GRAPH_BUCKET_COUNT);

            if (snapshots.Count >= GRAPH_BUCKET_COUNT)
                return snapshots;

            long endFrame = snapshots.Count > 0
                ? snapshots[snapshots.Count - 1].GameplayFrame
                : fallbackEndFrame;
            long stepFrames = Math.Max(1L, (long)Math.Round(Math.Max(0.1f, windowSeconds) * 60f / GRAPH_BUCKET_COUNT));
            var padded = new List<PowerSnapshot>(GRAPH_BUCKET_COUNT);
            int missing = GRAPH_BUCKET_COUNT - snapshots.Count;

            for (int i = missing; i > 0; i--)
                padded.Add(PowerSnapshot.Empty(endFrame - stepFrames * i));

            padded.AddRange(snapshots);
            return padded;
        }

        int GetScaleTierIndex()
        {
            int tier = PowerComponent.PowerHistoryTier >= 0 ? PowerComponent.PowerHistoryTier : PowerComponent.GraphWindowIndex;
            return Math.Max(0, Math.Min(tier, SCALE_TIER_COUNT - 1));
        }

        static string GetScaleLabel(int index)
        {
            switch (index)
            {
                case 0: return "1s";
                case 1: return "5s";
                case 2: return "30s";
                case 3: return "1m";
                case 4: return "5m";
                default: return "all";
            }
        }

        static float GetScaleWindowSeconds(int index)
        {
            switch (index)
            {
                case 0: return 1f;
                case 1: return 5f;
                case 2: return 30f;
                case 3: return 60f;
                case 4: return 300f;
                default: return 1800f;
            }
        }

        Color GetLoadColor(float ratio)
        {
            if (ratio >= 0.90f) return ColorComponent.ResolveErrorColor();
            if (ratio >= 0.70f) return ColorComponent.ResolveWarningColor();
            return GetHeaderColor();
        }

        Color GetBatteryIconColor(float ratio)
        {
            if (ratio < 0.15f) return ColorComponent.ResolveErrorColor();
            if (ratio < 0.35f) return ColorComponent.ResolveWarningColor();
            return GetHeaderColor();
        }

        static float GetSubtypeHue(int index)
        {
            // I didn't check how factorio does, but this is similar enough for initial numbers:
            // 240 starts at blue, then * by the prime 137 so it scales out of phase with the HUE
            // but still deterministic based on the index
            return (240 + index * 137) % 360 / 360f;
        }

        static string GetRowLabel(PowerSubtypeSnapshot entry)
        {
            if (entry == null)
                return "Unknown";
            if (!string.IsNullOrEmpty(entry.DisplayName))
                return entry.DisplayName;
            if (!string.IsNullOrEmpty(entry.SubtypeId))
                return entry.SubtypeId;
            return "Unknown";
        }

        static Color ColorForSubtypeIndex(int index) => new Vector3(GetSubtypeHue(index), 0.85f, 0.75f).HSVtoColor();

        static Color ColorForSubtypeIndex(int index, bool selected) => selected
            ? ColorForSubtypeIndex(index)
            : new Vector3(GetSubtypeHue(index), 0.4f, 0.2f).HSVtoColor();
    }
}
