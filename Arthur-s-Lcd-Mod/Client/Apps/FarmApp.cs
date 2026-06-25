using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Globalization;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Farm;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Custom;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using VisualWrapPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel.WrapPanel;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(9)]
    internal sealed partial class FarmApp : App, IApp
    {
        const float SLOT_W = 100f;
        const float SLOT_H = 100f;
        const float SCROLLER_W = 8f;
        internal sealed class FarmEntry
        {
            readonly List<ITooltipLine> _details = new List<ITooltipLine>();

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

            public void Update(
                FarmPlotEntry plot,
                float ratio,
                string percentText,
                string remainingText,
                string waterText,
                float waterRatio,
                string outputSprite,
                string outputName,
                Color statusColor)
            {
                Plot = plot;
                EntryId = plot != null && plot.Block != null ? plot.Block.EntityId : 0L;
                Ratio = MathHelper.Clamp(ratio, 0f, 1f);
                PercentText = percentText ?? string.Empty;
                RemainingText = remainingText ?? string.Empty;
                WaterText = waterText ?? string.Empty;
                WaterRatio = MathHelper.Clamp(waterRatio, 0f, 1f);
                OutputSprite = outputSprite ?? string.Empty;
                OutputName = outputName ?? string.Empty;
                StatusColor = statusColor;
                RefreshDetails();
            }

            public IList<ITooltipLine> GetDetails()
            {
                return _details;
            }

            void RefreshDetails()
            {
                _details.Clear();

                var logic = Plot?.Logic;
                if (logic == null)
                {
                    _details.Add(new StaticTooltipLine(PercentText));
                    return;
                }

                if (!string.IsNullOrEmpty(OutputName))
                {
                    var amount = logic.OutputItemAmount;
                    _details.Add(new StaticTooltipLine(amount > 0
                        ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "Farm_OutputAmount"),
                            OutputName, amount)
                        : OutputName));
                }

                if (!string.IsNullOrEmpty(WaterText))
                    _details.Add(new StaticTooltipLine(WaterText));

                AppendDetailedInfoLines(logic);

                if (_details.Count == 0)
                    _details.Add(new StaticTooltipLine(PercentText));
            }

            void AppendDetailedInfoLines(Sandbox.ModAPI.IMyFarmPlotLogic logic)
            {
                try
                {
                    var detailInfo = logic.GetDetailedInfoWithoutRequiredInput();
                    if (string.IsNullOrEmpty(detailInfo))
                        return;

                    detailInfo = detailInfo.Replace("\r\n", "\n").Replace('\r', '\n');
                    var lines = detailInfo.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i == lines.Length - 1 && string.IsNullOrEmpty(lines[i]))
                            continue;

                        _details.Add(new StaticTooltipLine(lines[i]));
                    }
                }
                catch (Exception e)
                {
                    ErrorHandlerHelper.LogError(e, typeof(FarmApp));
                }
            }
        }

        readonly IAppHost _surfaceHost;
        readonly InteractiveSurfaceScript _interactiveHost;
        readonly List<FarmEntry> _entries = new List<FarmEntry>();
        readonly List<Control> _children = new List<Control>();
        readonly Dictionary<long, FarmEntry> _entryById = new Dictionary<long, FarmEntry>();
        readonly Dictionary<long, FarmEntry> _entryModelsById = new Dictionary<long, FarmEntry>();
        readonly Dictionary<long, FarmPlotControl> _entryControlById = new Dictionary<long, FarmPlotControl>();
        readonly HashSet<long> _activeEntryIds = new HashSet<long>();
        readonly List<long> _entryIdsToRemove = new List<long>();
        readonly FarmGrowthHelper _growthDefinitions = new FarmGrowthHelper();
        readonly ScrollPanel _scrollPanel;
        readonly VisualWrapPanel _gridPanel;
        internal ColorConfigComponent FarmColors => ColorComponent;

        public FarmApp(IAppHost surfaceHost) : base(surfaceHost)
        {
            _surfaceHost = surfaceHost;
            _interactiveHost = surfaceHost as InteractiveSurfaceScript;
            if (_interactiveHost == null)
                throw new ArgumentException("FarmApp requires an InteractiveSurfaceScript host.", "surfaceHost");

            _scrollPanel = AddChild(new ScrollPanel(CursorType.Default, this));
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _scrollPanel.SetVisible(false);
            _gridPanel = new VisualWrapPanel();
        }

        public override IReadOnlyList<Control> Children => _children;

        public override void LayoutChanged()
        {
            _entries.Clear();
            _entryById.Clear();
            _entryModelsById.Clear();
            ClearFarmEntryControls();
        }

        public override void Update()
        {
            _entries.Clear();
            _entryById.Clear();
            _activeEntryIds.Clear();

            var gridLogic = _surfaceHost.GridLogic;
            if (gridLogic == null)
                return;

            var farmPlots = gridLogic.GetFarmPlots();
            for (int i = 0; i < farmPlots.Count; i++)
            {
                var plot = farmPlots[i];
                if (plot == null || plot.Block == null || plot.Logic == null)
                    continue;

                var entry = GetOrUpdateEntry(plot);
                _entries.Add(entry);
                _entryById[entry.EntryId] = entry;
                _activeEntryIds.Add(entry.EntryId);
            }

            RemoveStaleEntryModels();
        }

        public override bool HasVisibleItems()
        {
            return _entries.Count > 0;
        }

        FarmEntry GetFarmEntry(long entryId)
        {
            FarmEntry entry;
            return _entryById.TryGetValue(entryId, out entry) ? entry : null;
        }

        public override List<MySprite> GetSprites()
        {
            BeginFarmEntryControlFrame();
            var sprites = new List<MySprite>();
            DrawFarmPlots(_surfaceHost, sprites);
            return sprites;
        }

        FarmEntry GetOrUpdateEntry(FarmPlotEntry plot)
        {
            var entryId = plot.Block.EntityId;
            FarmEntry entry;
            if (!_entryModelsById.TryGetValue(entryId, out entry) || entry == null)
            {
                entry = new FarmEntry();
                _entryModelsById[entryId] = entry;
            }

            float growthPercent;
            var ratio = GetGrowthRatio(plot, out growthPercent);
            var outputItem = plot.Logic.OutputItem;
            var outputSprite = ResolveOutputSprite(outputItem);
            var outputName = ResolveOutputName(outputItem);
            var percentText = FormatingHelper.PercentageToString(ratio);
            var remainingText = GetRemainingText(plot, growthPercent, ratio, percentText);
            var waterRatio = GetWaterRatio(plot);

            entry.Update(
                plot,
                ratio,
                percentText,
                remainingText,
                GetWaterText(waterRatio, plot),
                waterRatio,
                outputSprite,
                outputName,
                GetStatusColor(plot, ratio));
            return entry;
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

        void RemoveStaleEntryModels()
        {
            _entryIdsToRemove.Clear();
            foreach (var kv in _entryModelsById)
            {
                if (!_activeEntryIds.Contains(kv.Key))
                    _entryIdsToRemove.Add(kv.Key);
            }

            for (int i = 0; i < _entryIdsToRemove.Count; i++)
                _entryModelsById.Remove(_entryIdsToRemove[i]);
        }

        void DrawFarmPlots(IAppHost owner, List<MySprite> sprites)
        {
            float minW = SLOT_W * owner.ConfiguredScale;
            float minH = SLOT_H * owner.ConfiguredScale;
            float contentTop = GetContentTop(owner) + 6f * owner.ConfiguredScale;

            int count = _entries.Count;
            if (count <= 0)
                return;

            _scrollPanel.SetContent(_gridPanel);
            _gridPanel.RowHeight = minH;
            _gridPanel.MinimumColumnWidth = minW;
            _gridPanel.HorizontalGap = 0f;
            _gridPanel.VerticalGap = 0f;
            SyncFarmEntryControls(_gridPanel);
            ConfigureFarmScrollPanel(owner, contentTop, minH);
            _scrollPanel.Render(sprites);
        }

        void ConfigureFarmScrollPanel(IAppHost owner, float contentTop, float rowHeight)
        {
            var viewportHeight = Math.Max(0f, owner.ViewBox.Bottom - contentTop);
            _scrollPanel.ConfigureAutomatic(
                new RectangleF(owner.ViewBox.X, contentTop, owner.ViewBox.Width, viewportHeight),
                SCROLLER_W * owner.ConfiguredScale,
                rowHeight);
            _scrollPanel.SetVisible(true);
            if (!_children.Contains(_scrollPanel))
                _children.Add(_scrollPanel);
        }

        void BeginFarmEntryControlFrame()
        {
            _children.Clear();
            _scrollPanel.SetVisible(false);
            foreach (var kv in _entryControlById)
                if (kv.Value != null)
                    kv.Value.SetVisible(false);
        }

        void ClearFarmEntryControls()
        {
            foreach (var kv in _entryControlById)
                if (kv.Value != null)
                    kv.Value.SetVisible(false);

            _entryControlById.Clear();
            _gridPanel.ClearChildren();
            _children.Clear();
        }

        void SyncFarmEntryControls(Panel panel)
        {
            if (panel == null)
                return;

            var desiredIds = new Dictionary<long, bool>();
            var desired = new List<Control>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry == null)
                    continue;

                desiredIds[entry.EntryId] = true;
                desired.Add(GetOrCreateFarmPlotControl(entry));
            }

            RemoveStalePanelChildren(panel, desiredIds);
            EnsurePanelChildOrder(panel, desired);
        }

        FarmPlotControl GetOrCreateFarmPlotControl(FarmEntry entry)
        {
            if (entry == null)
                return null;

            FarmPlotControl control;
            if (!_entryControlById.TryGetValue(entry.EntryId, out control) || control == null)
            {
                control = new FarmPlotControl(default(RectangleF), entry, BuildFarmEntryTooltip(entry.EntryId));
                _entryControlById[entry.EntryId] = control;
            }
            else
            {
                control.Bind(entry, BuildFarmEntryTooltip(entry.EntryId));
            }

            control.SetVisible(true);
            return control;
        }

        static void RemoveStalePanelChildren(Panel panel, Dictionary<long, bool> desiredIds)
        {
            var children = panel.Children;
            if (children == null)
                return;

            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i];
                var entry = child?.DataContext as FarmEntry;
                if (entry == null || desiredIds.ContainsKey(entry.EntryId))
                    continue;

                panel.RemoveChild(child);
            }
        }

        static void EnsurePanelChildOrder(Panel panel, List<Control> desired)
        {
            if (panel == null || desired == null)
                return;

            var children = panel.Children;
            bool changed = false;
            for (int i = 0; i < desired.Count; i++)
            {
                var child = desired[i] as ControlTemplate;
                if (child == null)
                    continue;

                if (!ReferenceEquals(child.Parent, panel))
                {
                    panel.AddChild(child);
                    children = panel.Children;
                    changed = true;
                }

                if (children == null || i >= children.Count || ReferenceEquals(children[i], child))
                    continue;

                int currentIndex = IndexOfChild(children, child);
                if (currentIndex < 0)
                    continue;

                if (panel.MoveChild(child, i))
                    changed = true;
            }

            if (changed)
                panel.InvalidateLayout();
        }

        static int IndexOfChild(IReadOnlyList<Control> children, ControlTemplate child)
        {
            if (children == null || child == null)
                return -1;

            for (int i = 0; i < children.Count; i++)
            {
                if (ReferenceEquals(children[i], child))
                    return i;
            }

            return -1;
        }

        void OnScrollPanelChanged(ScrollPanel panel)
        {
            _interactiveHost.RenderSprites();
        }

        InteractiveTooltip BuildFarmEntryTooltip(long entryId)
        {
            return new InteractiveTooltip(
                delegate
                {
                    var entry = GetFarmEntry(entryId);
                    var block = entry != null && entry.Plot != null ? entry.Plot.Block : null;
                    if (block != null && !string.IsNullOrEmpty(block.CustomName))
                        return block.CustomName;
                    return block != null ? block.DisplayNameText : string.Empty;
                },
                delegate
                {
                    var entry = GetFarmEntry(entryId);
                    return entry != null ? entry.GetDetails() : new List<ITooltipLine>();
                },
                null,
                null,
                TooltipActivationMode.Click,
                TooltipActivationMode.Click,
                delegate
                {
                    var entry = GetFarmEntry(entryId);
                    return entry != null ? entry.OutputSprite : string.Empty;
                });
        }

        float GetContentTop(IAppHost owner)
        {
            return owner.TitleVisible ? owner.ViewBox.Y + (40f * owner.ConfiguredScale * owner.Surface.FontSize) : owner.ViewBox.Y;
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

        static float GetGrowthRatio(FarmPlotEntry plot, out float growthPercent)
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

            try
            {
                float percent;
                int percentIndex;
                // Growth progress is not exposed as a typed IMyFarmPlotLogic property.
                var detailInfo = logic.GetDetailedInfoWithoutRequiredInput();
                if (TryParseFirstPercent(detailInfo, out percent, out percentIndex))
                {
                    growthPercent = MathHelper.Clamp(percent, 0f, 100f);
                    return growthPercent / 100f;
                }
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, typeof(FarmApp));
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
