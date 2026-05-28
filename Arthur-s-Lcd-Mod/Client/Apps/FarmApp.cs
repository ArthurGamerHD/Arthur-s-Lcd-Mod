using System;
using System.Collections.Generic;
using System.Globalization;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Gui.UserControls.Power;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using ScreenConfigPower = LcdMod.Common.Config.Models.Apps.ScreenConfigPower;

namespace LcdMod.Client.Apps
{
    internal sealed class FarmApp : AppBase, IAppInteractive
    {
        const float SLOT_W = 100f;
        const float SLOT_H = 100f;
        const float SCROLLER_W = 8f;
        const int SCROLL_TICK = 12;
        const int REMAINING_TIME_SAMPLE_FRAMES = 18;
        const float REMAINING_TIME_RATIO_RESET_THRESHOLD = 0.002f;
        const string FARM_PLOT_MASK_TEXTURE = "FarmPlotMask";
        static readonly FillableTexture FarmPlotTexture = new FillableTexture("FarmPlot", 0f, 0f, 0f, 0f, 70f);
        static readonly FillableTexture FarmPlotMaskTexture = new FillableTexture(FARM_PLOT_MASK_TEXTURE, 0f, 0f, 0f, 63f, 9f);

        sealed class FarmEntry
        {
            readonly List<ITooltipLine> _details = new List<ITooltipLine>();
            readonly float[] _remainingSecondsSamples = new float[REMAINING_TIME_SAMPLE_FRAMES];
            int _remainingSecondsSampleIndex;
            int _remainingSecondsSampleCount;
            float _remainingSecondsSampleTotal;
            float _lastRemainingSampleRatio = -1f;
            MyDefinitionId _lastRemainingSampleOutputItem;

            public long EntryId { get; private set; }
            public FarmPlotEntry Plot { get; private set; }
            public float Ratio { get; private set; }
            public string PercentText { get; private set; }
            public string RemainingText { get; private set; }
            public string WaterText { get; private set; }
            public string OutputSprite { get; private set; }
            public string OutputName { get; private set; }
            public Color StatusColor { get; private set; }

            public void Update(
                FarmPlotEntry plot,
                float ratio,
                string percentText,
                float growTimeSeconds,
                string waterText,
                string outputSprite,
                string outputName,
                Color statusColor)
            {
                Plot = plot;
                EntryId = plot != null && plot.Block != null ? plot.Block.EntityId : 0L;
                Ratio = MathHelper.Clamp(ratio, 0f, 1f);
                PercentText = percentText ?? string.Empty;
                RemainingText = GetStableRemainingText(plot, Ratio, growTimeSeconds, PercentText);
                WaterText = waterText ?? string.Empty;
                OutputSprite = outputSprite ?? string.Empty;
                OutputName = outputName ?? string.Empty;
                StatusColor = statusColor;
                RefreshDetails();
            }

            string GetStableRemainingText(FarmPlotEntry plot, float ratio, float growTimeSeconds, string percentText)
            {
                float remainingSeconds;
                string fallbackText;
                if (!TryGetRemainingSecondsEstimate(plot, ratio, growTimeSeconds, percentText,
                        out remainingSeconds, out fallbackText))
                {
                    ResetRemainingSamples();
                    return fallbackText;
                }

                var logic = plot != null ? plot.Logic : null;
                var outputItem = logic != null ? logic.OutputItem : default(MyDefinitionId);
                if (ShouldResetRemainingSamples(outputItem, ratio))
                    ResetRemainingSamples();

                AddRemainingSecondsSample(remainingSeconds);
                _lastRemainingSampleRatio = ratio;
                _lastRemainingSampleOutputItem = outputItem;

                return FormatRemainingSeconds(GetAverageRemainingSeconds());
            }

            bool ShouldResetRemainingSamples(MyDefinitionId outputItem, float ratio)
            {
                if (_remainingSecondsSampleCount <= 0)
                    return false;

                if (!_lastRemainingSampleOutputItem.Equals(outputItem))
                    return true;

                return _lastRemainingSampleRatio >= 0f &&
                       ratio + REMAINING_TIME_RATIO_RESET_THRESHOLD < _lastRemainingSampleRatio;
            }

            void AddRemainingSecondsSample(float remainingSeconds)
            {
                remainingSeconds = Math.Max(0f, remainingSeconds);
                if (_remainingSecondsSampleCount < _remainingSecondsSamples.Length)
                {
                    _remainingSecondsSamples[_remainingSecondsSampleIndex] = remainingSeconds;
                    _remainingSecondsSampleTotal += remainingSeconds;
                    _remainingSecondsSampleCount++;
                    _remainingSecondsSampleIndex = (_remainingSecondsSampleIndex + 1) % _remainingSecondsSamples.Length;
                    return;
                }

                _remainingSecondsSampleTotal -= _remainingSecondsSamples[_remainingSecondsSampleIndex];
                _remainingSecondsSamples[_remainingSecondsSampleIndex] = remainingSeconds;
                _remainingSecondsSampleTotal += remainingSeconds;
                _remainingSecondsSampleIndex = (_remainingSecondsSampleIndex + 1) % _remainingSecondsSamples.Length;
            }

            float GetAverageRemainingSeconds()
            {
                return _remainingSecondsSampleCount > 0
                    ? _remainingSecondsSampleTotal / _remainingSecondsSampleCount
                    : 0f;
            }

            void ResetRemainingSamples()
            {
                _remainingSecondsSampleIndex = 0;
                _remainingSecondsSampleCount = 0;
                _remainingSecondsSampleTotal = 0f;
                _lastRemainingSampleRatio = -1f;
                _lastRemainingSampleOutputItem = default(MyDefinitionId);
            }

            public IList<ITooltipLine> GetDetails()
            {
                return _details;
            }

            void RefreshDetails()
            {
                _details.Clear();

                var logic = Plot != null ? Plot.Logic : null;
                if (logic == null)
                {
                    _details.Add(new StaticTooltipLine(PercentText));
                    return;
                }

                if (!string.IsNullOrEmpty(OutputName))
                {
                    var amount = logic.OutputItemAmount;
                    _details.Add(new StaticTooltipLine(amount > 0
                        ? string.Format(FormatingHelper.Culture, LocHelper.GetLoc("LcdMod_Farm_OutputAmount"),
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
        readonly List<ControlBase> _interactiveList = new List<ControlBase>();
        readonly Dictionary<long, FarmEntry> _entryById = new Dictionary<long, FarmEntry>();
        readonly Dictionary<long, FarmEntry> _entryModelsById = new Dictionary<long, FarmEntry>();
        readonly Dictionary<long, RectangleControl> _entryHitboxById = new Dictionary<long, RectangleControl>();
        readonly HashSet<long> _activeEntryIds = new HashSet<long>();
        readonly List<long> _entryIdsToRemove = new List<long>();
        readonly ScrollPanel _scrollPanel;
        readonly ScreenConfigPower _config;

        public FarmApp(ScreenConfigPower config, IAppHost surfaceHost) : base(config, surfaceHost)
        {
            _surfaceHost = surfaceHost;
            _interactiveHost = surfaceHost as InteractiveSurfaceScript;
            if (_interactiveHost == null)
                throw new ArgumentException("FarmApp requires an InteractiveSurfaceScript host.", "surfaceHost");

            _config = config;
            _scrollPanel = new ScrollPanel(CursorType.Default, this);
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _scrollPanel.SetVisible(false);
        }

        public List<ControlBase> InteractiveList
        {
            get { return _interactiveList; }
        }

        public override void LayoutChanged()
        {
            _entries.Clear();
            _entryById.Clear();
            _entryModelsById.Clear();
            ClearFarmEntryHitboxes();
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

        public bool HasVisibleItems()
        {
            return _entries.Count > 0;
        }

        public void OnMouseScroll(int delta, ref bool handled)
        {
        }

        FarmEntry GetFarmEntry(long entryId)
        {
            FarmEntry entry;
            return _entryById.TryGetValue(entryId, out entry) ? entry : null;
        }

        public override List<MySprite> GetSprites()
        {
            BeginFarmEntryHitboxFrame();
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

            float growTimeSeconds;
            var ratio = GetGrowthRatio(plot, out growTimeSeconds);
            var outputItem = plot.Logic.OutputItem;
            var outputSprite = ResolveOutputSprite(outputItem);
            var outputName = ResolveOutputName(outputItem);
            var percentText = FormatingHelper.PercentageToString(ratio);

            entry.Update(
                plot,
                ratio,
                percentText,
                growTimeSeconds,
                GetWaterText(plot),
                outputSprite,
                outputName,
                GetStatusColor(plot, ratio));
            return entry;
        }

        static string GetWaterText(FarmPlotEntry plot)
        {
            var storage = plot != null ? plot.StorageComponent : null;
            if (storage == null)
                return string.Empty;

            var ratio = MathHelper.Clamp((float)storage.FilledRatio, 0f, 1f);
            return string.Format(FormatingHelper.Culture, LocHelper.GetLoc("LcdMod_Farm_Water"),
                FormatingHelper.PercentageToString(ratio));
        }

        string ResolveOutputSprite(MyDefinitionId outputItem)
        {
            if (outputItem.Equals(default(MyDefinitionId)) || MyDefinitionManager.Static == null)
                return string.Empty;

            MyPhysicalItemDefinition definition;
            if (!MyDefinitionManager.Static.TryGetPhysicalItemDefinition(outputItem, out definition))
                return string.Empty;

            return BlockIconHelper.GetOrAddTextureForItem(definition);
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
            if (_config == null)
                return _surfaceHost.ForegroundColor;

            if (plot?.Logic == null || !plot.Logic.IsPlantPlanted)
                return _surfaceHost.ForegroundColor;

            if (!plot.Logic.IsAlive)
                return _config.ErrorColor;

            if (ratio >= 1f || plot.Logic.IsHarvestable)
                return _config.HeaderColor;

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
            float minW = SLOT_W * owner.Scale;
            float minH = SLOT_H * owner.Scale;
            float contentTop = GetContentTop(owner) + 6f * owner.Scale;
            float availW = owner.ViewBox.Width;
            float xLeft = owner.ViewBox.X;
            float xRight = owner.ViewBox.X + owner.ViewBox.Width;

            int count = _entries.Count;
            if (count <= 0)
                return;

            int cols = Math.Min(count, Math.Max(1, (int)Math.Floor(availW / minW)));
            int totalRows = (int)Math.Ceiling(count / (float)cols);
            ConfigureFarmScrollPanel(owner, contentTop, minH, totalRows);

            if (_scrollPanel.IsScrollable)
            {
                xRight -= SCROLLER_W * owner.Scale;
                availW = xRight - xLeft;
                cols = Math.Min(count, Math.Max(1, (int)Math.Floor(availW / minW)));
                totalRows = (int)Math.Ceiling(count / (float)cols);
                ConfigureFarmScrollPanel(owner, contentTop, minH, totalRows);
            }

            float slotW = availW / cols;
            float slotH = minH;
            int startIdx = _scrollPanel.GetStartIndex(cols);
            int renderRows = _scrollPanel.VisibleRows + (_scrollPanel.IsScrollable ? 1 : 0);
            int show = Math.Min(renderRows * cols, count - startIdx);

            BeginScrollPanelClip(sprites);
            var renderContext = CreateRenderContext(owner);

            for (int i = 0; i < show; i++)
            {
                int col = i % cols;
                int row = i / cols;
                float xStart = xLeft + col * slotW;
                float yStart = _scrollPanel.ContentBounds.Y + row * slotH;
                var bounds = new RectangleF(xStart, yStart, slotW, slotH);
                var control = RegisterFarmEntryHitbox(_entries[startIdx + i], bounds);
                if (control != null)
                    control.Render(renderContext, sprites);
            }

            EndClip(sprites);
            _scrollPanel.Render(renderContext, sprites);
        }

        void ConfigureFarmScrollPanel(IAppHost owner, float contentTop, float rowHeight, int totalRows)
        {
            _scrollPanel.Configure(owner.ViewBox, contentTop, 0f, rowHeight, totalRows, SCROLLER_W * owner.Scale, SCROLL_TICK / 6f);
            var headerColor = _config != null ? _config.HeaderColor : owner.ForegroundColor;
            _scrollPanel.SetScrollBarColors(
                new Color(owner.Surface.ScriptForegroundColor.R, owner.Surface.ScriptForegroundColor.G, owner.Surface.ScriptForegroundColor.B, 127),
                new Color(headerColor.R, headerColor.G, headerColor.B, 250));
            _scrollPanel.SetVisible(true);
            if (!_interactiveList.Contains(_scrollPanel))
                _interactiveList.Add(_scrollPanel);
        }

        void BeginFarmEntryHitboxFrame()
        {
            _interactiveList.Clear();
            _scrollPanel.ClearChildren();
            _scrollPanel.SetVisible(false);
            foreach (var kv in _entryHitboxById)
                if (kv.Value != null)
                    kv.Value.SetVisible(false);
        }

        void ClearFarmEntryHitboxes()
        {
            foreach (var kv in _entryHitboxById)
                if (kv.Value != null)
                    kv.Value.SetVisible(false);

            _entryHitboxById.Clear();
            _interactiveList.Clear();
        }

        ControlRenderContext CreateRenderContext(IAppHost owner)
        {
            return CreateControlRenderContext(
                owner.Surface,
                owner.Scale,
                owner.Surface.FontSize,
                new Vector2(float.NaN, float.NaN));
        }

        RectangleControl RegisterFarmEntryHitbox(FarmEntry entry, RectangleF bounds)
        {
            if (entry == null)
                return null;

            RectangleControl hitbox;
            if (!_entryHitboxById.TryGetValue(entry.EntryId, out hitbox) || hitbox == null)
            {
                hitbox = new RectangleControl(bounds, CursorType.Hand, entry, null, BuildFarmEntryTooltip(entry.EntryId))
                {
                    ClickSound = AudioHelper.HudClick,
                    CustomRender = RenderFarmEntryHitbox
                };
                _entryHitboxById[entry.EntryId] = hitbox;
            }
            else
            {
                hitbox.SetRect(bounds);
                hitbox.SetDataContext(entry);
                hitbox.SetCursor(CursorType.Hand);
                hitbox.SetTooltip(BuildFarmEntryTooltip(entry.EntryId));
                hitbox.CustomRender = RenderFarmEntryHitbox;
            }

            hitbox.SetVisible(true);
            _scrollPanel.AddChild(hitbox);
            return hitbox;
        }

        void OnScrollPanelChanged(ScrollPanel panel)
        {
            _interactiveHost.RenderSprites();
        }

        void RenderFarmEntryHitbox(ControlBase hitbox, ControlRenderContext context, List<MySprite> sprites)
        {
            if (hitbox == null)
                return;

            var entry = hitbox.DataContext as FarmEntry;
            if (entry == null)
                return;

            entry = GetFarmEntry(entry.EntryId);
            if (entry == null)
                return;

            DrawFarmSlotVisual(_surfaceHost, sprites, entry, hitbox.Bounds);
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

        void DrawFarmSlotVisual(IAppHost owner, List<MySprite> sprites, FarmEntry entry, RectangleF bounds)
        {
            float width = bounds.Width;
            float height = bounds.Height;
            float labelGap = Math.Max(1f, owner.Scale * 2f);
            var label = entry.RemainingText;
            Vector2 labelRef = FormatingHelper.GetSizeInPixel(label, "White", 1f, owner.Surface);
            float labelScale = Math.Min((width * 0.82f) / Math.Max(1f, labelRef.X), (height * 0.22f) / Math.Max(1f, labelRef.Y)) * Math.Min(owner.Surface.FontSize, 1f);
            float labelH = labelRef.Y * labelScale;
            float iconSize = Math.Max(0f, Math.Min(width, height - labelH - labelGap));
            float centerX = bounds.X + width / 2f;
            float centerY = bounds.Y + iconSize / 2f;
            var center = new Vector2(centerX, centerY);

            DrawFarmIcon(sprites, entry, center, iconSize, owner.Surface.ScriptForegroundColor, GetHeaderColor());

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = label,
                Position = new Vector2(centerX, bounds.Y + iconSize + labelGap),
                RotationOrScale = labelScale,
                Color = entry.StatusColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
            });
        }

        void DrawFarmIcon(List<MySprite> sprites, FarmEntry entry, Vector2 center, float iconSize, Color foreground, Color fillColor)
        {
            var frameSize = iconSize * 0.84f;
            var itemSize = iconSize * 0.58f;
            var frameColor = foreground;
            var itemCenter = FarmPlotTexture.GetInnerRect(center, frameSize).Center;
            var ratio = entry != null ? MathHelper.Clamp(entry.Ratio, 0f, 1f) : 0f;
            var fillRect = FarmPlotMaskTexture.GetInnerRect(center, frameSize);

            if (ratio > 0.005f && fillRect.Width > 0f && fillRect.Height > 0f)
            {
                float fillH = fillRect.Height * ratio;
                var clip = new RectangleF(fillRect.X, fillRect.Bottom - fillH, fillRect.Width, fillH);
                if (BeginLocalClip(sprites, clip))
                {
                    sprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXTURE,
                        Data = FARM_PLOT_MASK_TEXTURE,
                        Position = center,
                        Size = new Vector2(frameSize),
                        Color = fillColor,
                        Alignment = TextAlignment.CENTER
                    });

                    EndClip(sprites);
                    BeginScrollPanelClip(sprites);
                }
            }

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = FarmPlotTexture.Name,
                Position = center,
                Size = new Vector2(frameSize),
                Color = frameColor,
                Alignment = TextAlignment.CENTER
            });

            if (entry == null || string.IsNullOrEmpty(entry.OutputSprite) || itemSize <= 0f)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = entry.OutputSprite,
                Position = itemCenter,
                Size = new Vector2(itemSize),
                Color = Color.White,
                Alignment = TextAlignment.CENTER
            });
        }

        void BeginScrollPanelClip(List<MySprite> sprites)
        {
            BeginClip(sprites, _scrollPanel.ContentViewportBounds);
        }

        bool BeginLocalClip(List<MySprite> sprites, RectangleF bounds)
        {
            return BeginClip(sprites, Intersect(bounds, _scrollPanel.ContentViewportBounds));
        }

        static bool BeginClip(List<MySprite> sprites, RectangleF bounds)
        {
            if (sprites == null || bounds.Width <= 0f || bounds.Height <= 0f)
                return false;

            int x = (int)Math.Floor(bounds.X);
            int y = (int)Math.Floor(bounds.Y);
            int right = (int)Math.Ceiling(bounds.Right);
            int bottom = (int)Math.Ceiling(bounds.Bottom);
            if (right <= x || bottom <= y)
                return false;

            sprites.Add(MySprite.CreateClipRect(new Rectangle(x, y, right - x, bottom - y)));
            return true;
        }

        static void EndClip(List<MySprite> sprites)
        {
            if (sprites != null)
                sprites.Add(MySprite.CreateClearClipRect());
        }

        static RectangleF Intersect(RectangleF a, RectangleF b)
        {
            var x = Math.Max(a.X, b.X);
            var y = Math.Max(a.Y, b.Y);
            var right = Math.Min(a.Right, b.Right);
            var bottom = Math.Min(a.Bottom, b.Bottom);
            return new RectangleF(x, y, Math.Max(0f, right - x), Math.Max(0f, bottom - y));
        }

        float GetContentTop(IAppHost owner)
        {
            return owner.TitleVisible ? owner.ViewBox.Y + (40f * owner.Scale * owner.Surface.FontSize) : owner.ViewBox.Y;
        }

        static bool TryGetRemainingSecondsEstimate(
            FarmPlotEntry plot,
            float ratio,
            float growTimeSeconds,
            string percentText,
            out float remainingSeconds,
            out string fallbackText)
        {
            remainingSeconds = 0f;
            fallbackText = LocHelper.Empty;
            var logic = plot != null ? plot.Logic : null;
            if (logic == null || !logic.IsPlantPlanted)
                return false;

            if (!logic.IsAlive)
            {
                fallbackText = "--";
                return false;
            }

            if (logic.IsPlantFullyGrown || logic.IsHarvestable || ratio >= 0.999f)
            {
                fallbackText = FormatRemainingSeconds(0f);
                return false;
            }

            if (ratio <= 0.0001f || growTimeSeconds <= 0f)
            {
                fallbackText = percentText ?? string.Empty;
                return false;
            }

            remainingSeconds = growTimeSeconds * (1f - MathHelper.Clamp(ratio, 0f, 1f)) / ratio;
            if (float.IsNaN(remainingSeconds) || float.IsInfinity(remainingSeconds))
            {
                fallbackText = percentText ?? string.Empty;
                return false;
            }

            return true;
        }

        static string FormatRemainingSeconds(float remainingSeconds)
        {
            return FormatingHelper.FormatTimeHours(Math.Max(0f, remainingSeconds / 3600f));
        }

        static float GetGrowthRatio(FarmPlotEntry plot, out float growTimeSeconds)
        {
            growTimeSeconds = 0f;
            var logic = plot != null ? plot.Logic : null;
            if (logic == null || !logic.IsPlantPlanted)
                return 0f;
            if (logic.IsPlantFullyGrown)
                return 1f;
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
                    TryParseFirstTimeAfter(detailInfo, percentIndex + 1, out growTimeSeconds);
                    return MathHelper.Clamp(percent / 100f, 0f, 1f);
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

        static void TryParseFirstTimeAfter(string text, int startIndex, out float seconds)
        {
            seconds = 0f;
            if (string.IsNullOrEmpty(text)) return;

            startIndex = Math.Max(0, Math.Min(startIndex, text.Length));
            for (int i = startIndex; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i]))
                    continue;

                int parsedSeconds;
                int endIndex;
                if (TryParseTimeAt(text, i, out parsedSeconds, out endIndex))
                {
                    seconds = parsedSeconds;
                    return;
                }
            }
        }

        static bool TryParseTimeAt(string text, int startIndex, out int seconds, out int endIndex)
        {
            seconds = 0;
            endIndex = startIndex;

            int index = startIndex;
            int first;
            if (!TryParseUnsignedInt(text, ref index, out first))
                return false;

            int days = 0;
            if (index < text.Length && (text[index] == 'd' || text[index] == 'D'))
            {
                days = first;
                index++;
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;

                if (!TryParseUnsignedInt(text, ref index, out first))
                    return false;
            }

            if (index >= text.Length || text[index] != ':')
                return false;

            index++;
            int second;
            if (!TryParseFixedTimePart(text, ref index, out second))
                return false;

            if (index >= text.Length || text[index] != ':')
                return false;

            index++;
            int third;
            if (!TryParseFixedTimePart(text, ref index, out third))
                return false;

            if (second >= 60 || third >= 60)
                return false;

            seconds = days * 86400 + first * 3600 + second * 60 + third;
            endIndex = index;
            return true;
        }

        static bool TryParseUnsignedInt(string text, ref int index, out int value)
        {
            value = 0;
            var start = index;
            while (index < text.Length && char.IsDigit(text[index]))
            {
                value = value * 10 + (text[index] - '0');
                index++;
            }

            return index > start;
        }

        static bool TryParseFixedTimePart(string text, ref int index, out int value)
        {
            value = 0;
            if (index + 1 >= text.Length || !char.IsDigit(text[index]) || !char.IsDigit(text[index + 1]))
                return false;

            value = (text[index] - '0') * 10 + (text[index + 1] - '0');
            index += 2;
            return true;
        }
    }
}
