using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Custom;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Power;
using LcdMod.Client.Utility;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using ControlGrid = LcdMod.Client.Gui.ControlsTemplates.Panels.Grid;
using ScreenConfigPower = LcdMod.Common.Config.Models.Apps.ScreenConfigPower;

namespace LcdMod.Client.Apps
{
    internal sealed class EnergyDashboardApp : AppBase, IAppInteractive
    {
        const int SCALE_TIER_COUNT = 6;
        const int GRAPH_BUCKET_COUNT = 10;
        const float LIST_COLUMN_WRAP_WIDTH_PIXELS = 220f;
        const float PROGRESS_CELL_MARGIN_X_PIXELS = 3f;
        const float PROGRESS_CELL_MARGIN_Y_PIXELS = 2f;
        const float BUTTON_CELL_MARGIN_X_PIXELS = 2f;
        const float BUTTON_CELL_MARGIN_Y_PIXELS = 2f;
        const float PROGRESS_ROW_WEIGHT = 24f;
        const float BUTTON_ROW_WEIGHT = 16f;

        readonly ScreenConfigPower _config;
        readonly List<ControlBase> _interactiveList = new List<ControlBase>();
        readonly List<EnergyDashboardPowerRow> _producerRows = new List<EnergyDashboardPowerRow>();
        readonly List<EnergyDashboardPowerRow> _consumerRows = new List<EnergyDashboardPowerRow>();
        readonly List<EnergyDashboardPowerRow> _chargeRows = new List<EnergyDashboardPowerRow>();
        readonly Dictionary<string, string> _spriteCache = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly ToggleButton[] _scaleButtons = new ToggleButton[SCALE_TIER_COUNT];
        readonly ControlGrid _rootGrid;
        readonly EnergyStatBarControl _consumptionBar;
        readonly EnergyStatBarControl _productionBar;
        readonly EnergyStatBarControl _chargeBar;
        readonly EnergySubtypeGraphControl _consumerGraph;
        readonly EnergySubtypeGraphControl _producerGraph;
        readonly EnergySubtypeGraphControl _chargeGraph;
        readonly CarouselPanel _contentCarousel;
        readonly ScrollPanel _producerScrollPanel;
        readonly ScrollPanel _consumerScrollPanel;
        readonly ScrollPanel _chargeScrollPanel;
        readonly VirtualizedWrapPanel<EnergyDashboardPowerRow> _producerWrapPanel;
        readonly VirtualizedWrapPanel<EnergyDashboardPowerRow> _consumerWrapPanel;
        readonly VirtualizedWrapPanel<EnergyDashboardPowerRow> _chargeWrapPanel;
        PowerDataLease _lease;
        PowerSnapshot _latest;

        public EnergyDashboardApp(ScreenConfigPower config, IAppHost host) : base(config, host)
        {
            _config = config;
            _rootGrid = new ControlGrid(default(RectangleF), new[] { 1f }, new[] { PROGRESS_ROW_WEIGHT, BUTTON_ROW_WEIGHT, 90f, 140f });
            var progressGrid = new ControlGrid(default(RectangleF), new[] { 1f, 1f, 1f }, new[] { 1f });
            var columns = new float[SCALE_TIER_COUNT];
            var buttonGrid = new ControlGrid(default(RectangleF), columns, new[] { 1f });
            _consumptionBar = new EnergyStatBarControl(host) { Label = "Consumption" };
            _productionBar = new EnergyStatBarControl(host) { Label = "Production" };
            _chargeBar = new EnergyStatBarControl(host) { Label = "Charge", ShowPercentage = true };
            progressGrid.Set(CreateInsetCell(_consumptionBar, PROGRESS_CELL_MARGIN_X_PIXELS, PROGRESS_CELL_MARGIN_Y_PIXELS), 0, 0);
            progressGrid.Set(CreateInsetCell(_productionBar, PROGRESS_CELL_MARGIN_X_PIXELS, PROGRESS_CELL_MARGIN_Y_PIXELS), 1, 0);
            progressGrid.Set(CreateInsetCell(_chargeBar, PROGRESS_CELL_MARGIN_X_PIXELS, PROGRESS_CELL_MARGIN_Y_PIXELS), 2, 0);

            
            for (int i = 0; i < SCALE_TIER_COUNT; i++)
            {
                columns[i] = 1;
                int tier = i;
                _scaleButtons[i] = new ToggleButton(default(RectangleF), GetScaleLabel(i),
                    delegate { return GetScaleTierIndex() == tier; },
                    delegate { SetScaleTierIndex(tier); });
                buttonGrid.Set(CreateInsetCell(_scaleButtons[i], BUTTON_CELL_MARGIN_X_PIXELS, BUTTON_CELL_MARGIN_Y_PIXELS), i, 0);
                _interactiveList.Add(_scaleButtons[i]);
            }

            _consumerGraph = new EnergySubtypeGraphControl(host) { Producers = false, Title = "Consumption" };
            _producerGraph = new EnergySubtypeGraphControl(host) { Producers = true, Title = "Production" };
            _chargeGraph = new EnergySubtypeGraphControl(host) { Charge = true, Title = "Charge" };

            _producerWrapPanel = CreatePowerWrapPanel(_producerRows);
            _consumerWrapPanel = CreatePowerWrapPanel(_consumerRows);
            _chargeWrapPanel = CreatePowerWrapPanel(_chargeRows);
            _producerScrollPanel = new ScrollPanel(null, this);
            _consumerScrollPanel = new ScrollPanel(null, this);
            _chargeScrollPanel = new ScrollPanel(null, this);
            _producerScrollPanel.SetContent(_producerWrapPanel);
            _consumerScrollPanel.SetContent(_consumerWrapPanel);
            _chargeScrollPanel.SetContent(_chargeWrapPanel);
            _contentCarousel = new CarouselPanel();
            _contentCarousel.AddChild(CreatePage(_consumerGraph, _consumerScrollPanel));
            _contentCarousel.AddChild(CreatePage(_producerGraph, _producerScrollPanel));
            _contentCarousel.AddChild(CreatePage(_chargeGraph, _chargeScrollPanel));
            _interactiveList.Add(_contentCarousel);

            _rootGrid.Set(progressGrid, 0, 0);
            _rootGrid.Set(buttonGrid, 0, 1);
            _rootGrid.Set(_contentCarousel, 0, 2, 1, 2);
            CaptureLease();
        }

        InsetCellPanel CreateInsetCell(ControlBase child, float horizontalMarginPixels, float verticalMarginPixels)
        {
            return new InsetCellPanel(child, Host, horizontalMarginPixels, verticalMarginPixels);
        }

        static ControlGrid CreatePage(ControlBase graph, ControlBase list)
        {
            var page = new ControlGrid(default(RectangleF), new[] { 1f }, new[] { 90f, 140f });
            page.Set(graph, 0, 0);
            page.Set(list, 0, 1);
            return page;
        }

        public List<ControlBase> InteractiveList { get { return _interactiveList; } }
        public ScreenConfigPower Config { get { return _config; } }

        public bool HasVisibleItems()
        {
            return true;
        }

        public void OnMouseScroll(int delta, ref bool handled)
        {
        }

        public override void Update()
        {
            CaptureLease();
            _latest = _lease != null ? _lease.Latest : new PowerSnapshot();
            BuildRows(_latest.ProducerSubtypes, _producerRows, EnergyDashboardPowerMetrics.GetCurrentProducerTotal(_latest));
            BuildRows(_latest.ConsumerSubtypes, _consumerRows, EnergyDashboardPowerMetrics.GetCurrentConsumerTotal(_latest));
            BuildRows(_latest.ChargeSubtypes, _chargeRows, _latest.MaxStoredEnergyWh, true);
            _producerWrapPanel.InvalidateLayout();
            _consumerWrapPanel.InvalidateLayout();
            _chargeWrapPanel.InvalidateLayout();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            float top = GetContentTop();
            var bounds = new RectangleF(Host.ViewBox.X, top, Host.ViewBox.Width, Math.Max(0f, Host.ViewBox.Bottom - top));
            BindControls(GetSnapshots(_lease != null ? _lease.History : null));
            _rootGrid.Arrange(bounds);
            _rootGrid.Render(CreateControlRenderContext(Host.Surface, Host.Proportion, Host.Surface.FontSize, GetCursorPosition()), sprites);
            return sprites;
        }

        public override void Close()
        {
            if (_lease != null)
            {
                _lease.Dispose();
                _lease = null;
            }
        }

        void BindControls(List<PowerSnapshot> snapshots)
        {
            double maxConsumptionW = EnergyDashboardPowerMetrics.GetMaxConsumerTotal(_latest);
            float loadRatio = EnergyDashboardPowerMetrics.Ratio(_latest.TotalRequiredInputW, maxConsumptionW);
            float chargeRatio = EnergyDashboardPowerMetrics.Ratio(_latest.StoredEnergyWh, _latest.MaxStoredEnergyWh);
            _consumptionBar.Current = _latest.TotalRequiredInputW;
            _consumptionBar.Max = maxConsumptionW;
            _consumptionBar.FillColor = GetLoadColor(loadRatio);
            _productionBar.Current = _latest.Producers.KnownCurrentOutputW;
            _productionBar.Max = _latest.MaxAvailableW;
            _productionBar.FillColor = _config.HeaderColor;
            _chargeBar.Current = _latest.StoredEnergyWh;
            _chargeBar.Max = _latest.MaxStoredEnergyWh;
            _chargeBar.FillColor = GetBatteryIconColor(chargeRatio);

            int selectedScale = GetScaleTierIndex();
            bool showAll = selectedScale == SCALE_TIER_COUNT - 1;
            _consumerGraph.WindowSeconds = showAll ? 0f : GetScaleWindowSeconds(selectedScale);
            _producerGraph.WindowSeconds = showAll ? 0f : GetScaleWindowSeconds(selectedScale);
            _chargeGraph.WindowSeconds = showAll ? 0f : GetScaleWindowSeconds(selectedScale);
            _consumerGraph.UseTimeSpacing = showAll;
            _producerGraph.UseTimeSpacing = showAll;
            _chargeGraph.UseTimeSpacing = showAll;
            _consumerGraph.Bind(snapshots, _consumerRows);
            _producerGraph.Bind(snapshots, _producerRows);
            _chargeGraph.Bind(snapshots, _chargeRows);
            _contentCarousel.LayoutScale = Host.Proportion;
            BindList(_consumerScrollPanel, _consumerWrapPanel);
            BindList(_producerScrollPanel, _producerWrapPanel);
            BindList(_chargeScrollPanel, _chargeWrapPanel);
        }

        void BindList(ScrollPanel scrollPanel, VirtualizedWrapPanel<EnergyDashboardPowerRow> wrapPanel)
        {
            float scale = Host.Proportion;
            float rowH = 44f * scale;
            wrapPanel.RowHeight = rowH;
            wrapPanel.MinimumColumnWidth = LIST_COLUMN_WRAP_WIDTH_PIXELS * scale;
            wrapPanel.HorizontalGap = 6f * scale;
            wrapPanel.VerticalGap = 0f;
            scrollPanel.AutomaticScrollerWidthPixels = ScrollPanel.DefaultScrollerWidthPixels * scale;
            scrollPanel.ScrollStepPixels = rowH;
            Color fg = Host.Surface.ScriptForegroundColor;
            scrollPanel.SetScrollBarColors(new Color(fg.R, fg.G, fg.B, 60), fg);
            scrollPanel.SetVisible(true);
        }

        void CaptureLease()
        {
            if (_lease != null || LcdModSessionComponent.Client == null || LcdModSessionComponent.Client.PowerData == null)
                return;

            _lease = LcdModSessionComponent.Client.PowerData.Capture(Host.GridLogic, _config.GridLinkType);
        }

        void BuildRows(List<PowerSubtypeSnapshot> entries, List<EnergyDashboardPowerRow> rows, double ratioDenominatorW)
        {
            BuildRows(entries, rows, ratioDenominatorW, false);
        }

        void BuildRows(List<PowerSubtypeSnapshot> entries, List<EnergyDashboardPowerRow> rows, double ratioDenominatorW, bool charge)
        {
            rows.Clear();
            if (entries == null)
                return;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || (entry.CurrentW <= 0 && entry.MaxW <= 0))
                    continue;

                rows.Add(new EnergyDashboardPowerRow
                {
                    Entry = entry,
                    SpriteName = ResolveSubtypeSprite(entry),
                    CurrentW = entry.CurrentW,
                    MaxW = entry.MaxW,
                    RatioDenominatorW = charge ? entry.MaxW : ratioDenominatorW,
                    IsCharge = charge
                });
            }

            rows.Sort(ComparePowerRowsDescending);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                row.Color = ColorForSubtypeIndex(i);
                rows[i] = row;
            }
        }

        VirtualizedWrapPanel<EnergyDashboardPowerRow> CreatePowerWrapPanel(List<EnergyDashboardPowerRow> rows)
        {
            return new VirtualizedWrapPanel<EnergyDashboardPowerRow>
            {
                ItemsSource = rows,
                CreateControl = CreatePowerRowControl,
                BindControl = BindPowerRowControl
            };
        }

        ControlBase CreatePowerRowControl(EnergyDashboardPowerRow row)
        {
            return new EnergyPowerRowControl(Host);
        }

        static void BindPowerRowControl(ControlBase control, EnergyDashboardPowerRow row, int index)
        {
            var rowControl = control as EnergyPowerRowControl;
            if (rowControl != null)
                rowControl.SetRow(row);
        }

        static int ComparePowerRowsDescending(EnergyDashboardPowerRow a, EnergyDashboardPowerRow b)
        {
            int current = b.CurrentW.CompareTo(a.CurrentW);
            if (current != 0)
                return current;
            return string.Compare(GetRowLabel(a.Entry), GetRowLabel(b.Entry), StringComparison.OrdinalIgnoreCase);
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

        List<PowerSnapshot> GetSnapshots(PowerHistory history)
        {
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
                    return GetAllNonZeroSnapshots(history, _latest);
            }

            return PadSnapshots(snapshots, GetScaleWindowSeconds(tier), _latest);
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

        static List<PowerSnapshot> PadSnapshots(List<PowerSnapshot> snapshots, float windowSeconds, PowerSnapshot latest)
        {
            if (snapshots == null)
                snapshots = new List<PowerSnapshot>();

            if (snapshots.Count == 0 || latest.GameplayFrame > snapshots[snapshots.Count - 1].GameplayFrame)
            {
                snapshots = new List<PowerSnapshot>(snapshots);
                snapshots.Add(latest);
            }

            if (snapshots.Count > GRAPH_BUCKET_COUNT)
                snapshots = snapshots.GetRange(snapshots.Count - GRAPH_BUCKET_COUNT, GRAPH_BUCKET_COUNT);

            if (snapshots.Count >= GRAPH_BUCKET_COUNT)
                return snapshots;

            long endFrame = snapshots.Count > 0 ? snapshots[snapshots.Count - 1].GameplayFrame : latest.GameplayFrame;
            long stepFrames = Math.Max(1L, (long)Math.Round(Math.Max(0.1f, windowSeconds) * 60f / GRAPH_BUCKET_COUNT));
            var padded = new List<PowerSnapshot>(GRAPH_BUCKET_COUNT);
            int missing = GRAPH_BUCKET_COUNT - snapshots.Count;

            for (int i = missing; i > 0; i--)
                padded.Add(PowerSnapshot.Empty(endFrame - stepFrames * i));

            padded.AddRange(snapshots);
            return padded;
        }

        PowerHistoryTier GetHistoryTier()
        {
            return (PowerHistoryTier)GetScaleTierIndex();
        }

        int GetScaleTierIndex()
        {
            int tier = _config.PowerHistoryTier >= 0 ? _config.PowerHistoryTier : _config.GraphWindowIndex;
            return Math.Max(0, Math.Min(tier, SCALE_TIER_COUNT - 1));
        }

        void SetScaleTierIndex(int tier)
        {
            tier = Math.Max(0, Math.Min(tier, SCALE_TIER_COUNT - 1));
            _config.PowerHistoryTier = tier;
            _config.GraphWindowIndex = tier;
            Host.RenderSprites();
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
            if (ratio >= 0.90f) return _config.ErrorColor;
            if (ratio >= 0.70f) return _config.WarningColor;
            return _config.HeaderColor;
        }

        Color GetBatteryIconColor(float ratio)
        {
            if (ratio < 0.15f) return _config.ErrorColor;
            if (ratio < 0.35f) return _config.WarningColor;
            return _config.HeaderColor;
        }

        static Color ColorForSubtypeIndex(int index)
        {
            // I didn't check how factorio does, but this is similar enough for initial numbers:
            // 240 starts at blue, then * by the prime 137 so it scales out of phase with the HUE
            // but still deterministic based on the index
            return HsvToColor((240 + index * 137) % 360, 0.85f, 0.75f);
        }

        static Color HsvToColor(int hueDegrees, float saturation, float value)
        {
            float h = ((hueDegrees % 360) + 360) % 360 / 60f;
            float c = value * saturation;
            float x = c * (1f - Math.Abs(h % 2f - 1f));
            float m = value - c;
            float r = 0f;
            float g = 0f;
            float b = 0f;

            if (h < 1f)
            {
                r = c;
                g = x;
            }
            else if (h < 2f)
            {
                r = x;
                g = c;
            }
            else if (h < 3f)
            {
                g = c;
                b = x;
            }
            else if (h < 4f)
            {
                g = x;
                b = c;
            }
            else if (h < 5f)
            {
                r = x;
                b = c;
            }
            else
            {
                r = c;
                b = x;
            }

            return new Color(
                (int)Math.Round((r + m) * 255f),
                (int)Math.Round((g + m) * 255f),
                (int)Math.Round((b + m) * 255f));
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

        Vector2 GetCursorPosition()
        {
            var interactive = Host as IEyeTracking;
            return interactive != null ? interactive.CursorPosition : new Vector2(float.NaN, float.NaN);
        }

        float GetContentTop()
        {
            return Host.TitleVisible ? Host.ViewBox.Y + (40f * Host.Proportion * Host.Surface.FontSize) : Host.ViewBox.Y;
        }

        // todo: remove this when implement style support
        sealed class InsetCellPanel : Panel
        {
            readonly ControlBase _child;
            readonly IAppHost _host;
            readonly float _horizontalMarginPixels;
            readonly float _verticalMarginPixels;

            public InsetCellPanel(ControlBase child, IAppHost host, float horizontalMarginPixels, float verticalMarginPixels)
                : base(default(RectangleF))
            {
                _child = child;
                _host = host;
                _horizontalMarginPixels = Math.Max(0f, horizontalMarginPixels);
                _verticalMarginPixels = Math.Max(0f, verticalMarginPixels);
                AddChild(child);
            }

            protected override void ArrangeChildren()
            {
                if (_child == null)
                    return;

                float scale = _host != null ? _host.Proportion : 1f;
                float x = Math.Min(Rect.Width * 0.5f, _horizontalMarginPixels * scale);
                float y = Math.Min(Rect.Height * 0.5f, _verticalMarginPixels * scale);

                _child.Arrange(new RectangleF(
                    Rect.X + x,
                    Rect.Y + y,
                    Math.Max(0f, Rect.Width - x * 2f),
                    Math.Max(0f, Rect.Height - y * 2f)));
            }
        }
    }
}
