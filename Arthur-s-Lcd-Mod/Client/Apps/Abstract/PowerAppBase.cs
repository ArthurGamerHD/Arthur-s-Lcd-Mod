using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
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
using VisualStackPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel.StackPanel;
using VisualWrapPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.WrapPanel.WrapPanel;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps.Abstract
{
    [ConfigComponent(Constants.APP, typeof(PowerConfigComponent), PropertyName = "PowerComponent")]
    public abstract partial class PowerApp : App, IApp
    {
        protected const float LINE = 22f;
        protected const float MINIMUM_COL_WIDTH = 400f;
        protected const float GRID_CELL_LINES = 6f;

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

        sealed class PowerEntry : ControlModelBase
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
        string[] _entryOrder;
        PowerEntry[] _entriesOrdered;
        readonly List<PowerEntry> _visibleEntries = new List<PowerEntry>();
        readonly List<IMyPowerProducer> _producers = new List<IMyPowerProducer>();
        readonly Dictionary<string, RectangleControl> _entryControls = new Dictionary<string, RectangleControl>();
        readonly List<Control> _children = new List<Control>();
        readonly ScrollPanel _scrollPanel;
        readonly VisualStackPanel _listPanel;
        readonly VisualWrapPanel _gridPanel;
        readonly InteractiveSurfaceScript _interactiveHost;

        Color _ascentColor = Color.White;
        string _usagePrefix = string.Empty;
        string _maxLabelCache = string.Empty;
        string _currentLabelCache = string.Empty;
        float _caretY;
        bool _drawGridLineSprites;
        bool _drawGridVerticalLines;
        const float FOOTER_HEIGHT = 0f;
        IMyCubeBlock Block => Host.Block;
        Sandbox.ModAPI.Ingame.IMyTextSurface Surface => Host.Surface;
        RectangleF ViewBox => Host.ViewBox;
        float Scale => GeneralComponent.GetScale();
        float FontScale => Host.Surface.FontSize;
        float LayoutScale => Scale * FontScale;
        Color ForegroundColor => Host.ForegroundColor;
        public override IReadOnlyList<Control> VisualChildren => _children;

        protected abstract PowerEntryDefinition[] EntryDefinitions { get; }

        protected PowerApp(IAppHost host) : base(host)
        {
            _interactiveHost = host as InteractiveSurfaceScript;
            if (_interactiveHost == null)
                throw new ArgumentException("PowerAppBase requires an InteractiveSurfaceScript host.", "host");

            _scrollPanel = AddLogicalChild(new ScrollPanel(CursorType.Default, this));
            _scrollPanel.ScrollChanged = OnScrollPanelChanged;
            _scrollPanel.SetVisible(false);
            _listPanel = new VisualStackPanel();
            _listPanel.CustomRender = RenderListPanelContent;
            _gridPanel = new VisualWrapPanel();
            _gridPanel.CustomRender = RenderGridPanelContent;
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
            SumPowerSources(Host.GridLogic, _totalsByKey);
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
            ClearInteractiveTree();
            var sprites = new List<MySprite>();
            _caretY = ContentTop();

            switch (GeneralComponent.DisplayMode)
            {
                case (int)DisplayMode.Grid:
                    DrawGridLike(
                        sprites,
                        _visibleEntries,
                        _maxLabelCache,
                        _currentLabelCache,
                        false,
                        GeneralComponent.DrawLines,
                        GeneralComponent.DrawLines,
                        GeneralComponent.DrawLines);
                    break;
                default:
                    DrawDefaultView(
                        sprites,
                        _visibleEntries,
                        _maxLabelCache,
                        _currentLabelCache);
                    break;
            }

            ClearDirtyAfterRender();
            return sprites;
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

            for (int i = 0; i < _entryOrder.Length; i++)
            {
                var key = _entryOrder[i];
                var totals = _totalsByKey[key];
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

        void SumPowerSources(GridLogic gridLogic, Dictionary<string, PowerTotals> totals)
        {
            for (int i = 0; i < _entryOrder.Length; i++)
            {
                var key = _entryOrder[i];
                totals[key] = new PowerTotals();
            }

            if (gridLogic == null)
                return;

            _producers.Clear();
            _producers.AddRange(gridLogic.GetTerminalBlocks<IMyPowerProducer>((GridLinkTypeEnum)PowerComponent.GridLinkTypeInternal));

            for (int i = 0; i < _producers.Count; i++)
            {
                var prod = _producers[i];
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
                values.Current += ToWatts(prod?.CurrentOutput ?? 0);
                values.Max += ToWatts(prod?.MaxOutput ?? 0);
                values.Count++;
                totals[key] = values;
            }
        }

        void DrawDefaultView(List<MySprite> sprites, List<PowerEntry> entries, string maxLabel, string currentLabel)
        {
            var rowHeight = GRID_CELL_LINES * LINE * Scale;
            _scrollPanel.SetContent(_listPanel);
            _listPanel.RowHeight = rowHeight;
            _listPanel.Gap = 0f;
            SyncPanelChildren(_listPanel, entries, maxLabel, currentLabel, true);
            ConfigureScrollPanel(_caretY, rowHeight);
            _scrollPanel.Render(sprites);
        }

        void DrawGridLike(List<MySprite> sprites, List<PowerEntry> entries, string maxLabel, string currentLabel,
            bool forceSingleColumn, bool drawLineSprites, bool drawVerticalLines, bool drawCellsAsLines)
        {
            var rowHeight = GRID_CELL_LINES * LINE * Scale;
            _scrollPanel.SetContent(_gridPanel);
            _gridPanel.RowHeight = rowHeight;
            _gridPanel.MinimumColumnWidth = MINIMUM_COL_WIDTH * Scale;
            _gridPanel.ForceSingleColumn = forceSingleColumn;
            _gridPanel.HorizontalGap = 0f;
            _gridPanel.VerticalGap = 0f;
            _drawGridLineSprites = drawLineSprites;
            _drawGridVerticalLines = drawVerticalLines;
            SyncPanelChildren(_gridPanel, entries, maxLabel, currentLabel, drawCellsAsLines);
            ConfigureScrollPanel(_caretY, rowHeight);
            _scrollPanel.Render(sprites);
        }


        void ClearInteractiveTree()
        {
            _scrollPanel.SetVisible(false);
            _children.Clear();

            foreach (var kv in _entryControls)
                kv.Value?.SetVisible(false);
        }

        void ConfigureScrollPanel(float contentTop, float rowHeight)
        {
            var viewportHeight = Math.Max(0f, ViewBox.Bottom - contentTop - FOOTER_HEIGHT);
            _scrollPanel.ConfigureAutomatic(
                new RectangleF(ViewBox.X, contentTop, ViewBox.Width, viewportHeight),
                ScrollPanel.DEFAULT_SCROLLER_WIDTH_PIXELS * Scale,
                rowHeight);
            _scrollPanel.SetVisible(true);
            if (!_children.Contains(_scrollPanel))
                _children.Add(_scrollPanel);
        }

        void SyncPanelChildren(
            Panel panel,
            List<PowerEntry> entries,
            string maxLabel,
            string currentLabel,
            bool drawAsLines)
        {
            if (panel == null)
                return;

            var desired = new List<Control>(entries?.Count ?? 0);
            var desiredKeys = new HashSet<string>();
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null)
                        continue;

                    desiredKeys.Add(entry.Key);
                    desired.Add(GetOrCreateEntryControl(entry, maxLabel, currentLabel, drawAsLines));
                }
            }

            RemoveStalePanelChildren(panel, desiredKeys);
            EnsurePanelChildOrder(panel, desired);
        }

        RectangleControl GetOrCreateEntryControl(
            PowerEntry dataContext,
            string maxLabel,
            string currentLabel,
            bool drawAsLines)
        {
            if (dataContext == null)
                return null;

            dataContext.ConfigureRender(maxLabel, currentLabel, drawAsLines);

            RectangleControl control;
            if (!_entryControls.TryGetValue(dataContext.Key, out control) || control == null)
            {
                control = new RectangleControl(default(RectangleF), CursorType.Default, dataContext)
                {
                    CustomRender = RenderPowerEntryControl
                };
                _entryControls[dataContext.Key] = control;
            }
            else
            {
                control.SetDataContext(dataContext);
                control.CustomRender = RenderPowerEntryControl;
            }

            control.SetVisible(true);
            return control;
        }

        void RenderListPanelContent(ControlTemplate control, List<MySprite> sprites)
        {
            var children = control?.VisualChildren;
            if (children == null)
                return;

            if (GeneralComponent.DrawLines)
                DrawHorizontalLines(sprites, ForegroundColor, "Circle", _listPanel.RowHeight);

            RenderPanelChildren(children, sprites);
        }

        void RenderGridPanelContent(ControlTemplate control, List<MySprite> sprites)
        {
            var children = control?.VisualChildren;
            if (children == null)
                return;

            if (_drawGridLineSprites)
            {
                var layout = WrapPanelLayout.Create(
                    control.Bounds,
                    _gridPanel.RowHeight,
                    _gridPanel.MinimumColumnWidth,
                    children.Count,
                    0,
                    _gridPanel.ForceSingleColumn);
                DrawWrapPanelLines(sprites, layout, _drawGridVerticalLines);
            }

            RenderPanelChildren(children, sprites);
        }

        void DrawHorizontalLines(List<MySprite> sprites, Color color, string texture, float rowHeight)
        {
            var contentStart = _scrollPanel.ContentBounds.X;
            var contentEnd = _scrollPanel.ContentBounds.Right;
            for (int row = 0; row <= _scrollPanel.MaxVisibleRows; row++)
            {
                var y = _scrollPanel.ContentBounds.Y + row * rowHeight;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = texture,
                    Position = new Vector2((contentStart + contentEnd) / 2f, y),
                    Size = new Vector2(contentEnd - contentStart, 2f),
                    Color = color,
                    Alignment = TextAlignment.CENTER
                });
            }
        }

        void DrawWrapPanelLines(List<MySprite> sprites, WrapPanelLayout layout, bool drawVerticalLines)
        {
            var lineColor = new Color(GetHeaderColor().R, GetHeaderColor().G, GetHeaderColor().B);
            DrawHorizontalLines(sprites, lineColor, "SquareSimple", layout.RowHeight);

            if (!drawVerticalLines)
                return;

            var contentStart = _scrollPanel.ContentBounds.X;
            var contentEnd = _scrollPanel.ContentBounds.Right;
            var gridHeight = _scrollPanel.ContentBounds.Height;
            for (int col = 0; col <= layout.Columns; col++)
            {
                var x = col == layout.Columns ? contentEnd : contentStart + col * layout.ColumnWidth;
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(x, _scrollPanel.ContentViewportBounds.Y + gridHeight / 2f),
                    Size = new Vector2(2f, gridHeight),
                    Color = lineColor,
                    Alignment = TextAlignment.CENTER
                });
            }
        }

        static void RenderPanelChildren(IReadOnlyList<Control> children, List<MySprite> sprites)
        {
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i] as ControlTemplate;
                child?.Render(sprites);
            }
        }

        void RemoveStalePanelChildren(Panel panel, HashSet<string> desiredKeys)
        {
            var children = panel.VisualChildren;
            if (children == null)
                return;

            for (int i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i] as ControlTemplate;
                var entry = child?.DataContext as PowerEntry;
                if (entry == null || desiredKeys.Contains(entry.Key))
                    continue;

                panel.RemoveChild(child);
            }
        }

        static void EnsurePanelChildOrder(Panel panel, List<Control> desired)
        {
            if (panel == null || desired == null)
                return;

            var children = panel.VisualChildren;
            bool changed = false;
            for (int i = 0; i < desired.Count; i++)
            {
                var child = desired[i] as ControlTemplate;
                if (child == null)
                    continue;

                if (!ReferenceEquals(child.Parent, panel))
                {
                    panel.AddChild(child);
                    children = panel.VisualChildren;
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

        public override bool HasVisibleItems() => _visibleEntries.Count > 0;

        void OnScrollPanelChanged(ScrollPanel panel) => _interactiveHost.RenderSprites();

        int GetMaxColsFromSurface()
        {
            var max = ViewBox.Width - ViewBox.X;
            var perCol = MINIMUM_COL_WIDTH * Scale;
            return (int)Math.Max(1, Math.Round(max / perCol - .5, MidpointRounding.AwayFromZero));
        }

        void DrawGridPowerCell(List<MySprite> sprites, PowerEntry entry, float xStart, float xEnd,
            float yStart, float rowHeight, string maxLabel, string currentLabel, bool drawAsLines)
        {
            var cellPadding = (LINE * Scale) / 2f;
            var cellView = GetCellViewBox(xStart, xEnd, yStart, rowHeight, cellPadding);
            var slots = GetCellSlots(cellView.X, cellView.Right, cellView.Y, cellView.Bottom, LINE);

            if (!drawAsLines)
            {
                var backgroundColor = entry.Current <= 0 ? ColorComponent.ResolveErrorColor() : GetHeaderColor();
                var hsv = VRageMath.ColorExtensions.ColorToHSV(backgroundColor);
                hsv.Z *= 0.2f;

                var cellRect = new RectangleF(
                    xStart + cellPadding / 2f,
                    yStart + cellPadding / 2f,
                    (xEnd - xStart) - cellPadding,
                    rowHeight - cellPadding);
                var dropShadow = new RectangleF(cellRect.Position + 2, cellRect.Size);
                BorderRenderer.CreateSpritesFromRect(dropShadow, sprites, hsv.HSVtoColor(),
                    radiusScale: Scale);
                BorderRenderer.CreateSpritesFromRect(cellRect, sprites, backgroundColor,
                    radiusScale: Scale);
            }

            var iconRect = slots.Item1;
            var numberRect = slots.Item2;
            var nameRect = slots.Item3;
            var foreground = entry.Current <= 0 && drawAsLines ? ColorComponent.ResolveErrorColor() : Surface.ScriptForegroundColor;

            DrawCellPie(sprites, iconRect, entry.Usage);

            var titleSb = new StringBuilder(entry.Name);
            TrimText(ref titleSb, numberRect.Width);
            var titlePos = numberRect.Center;
            titlePos.X = numberRect.Right;
            titlePos.Y -= numberRect.Height * 0.5f;

            sprites.Add(new MySprite(
                SpriteType.TEXT,
                titleSb.ToString(),
                titlePos,
                null,
                foreground,
                TextFont,
                TextAlignment.RIGHT,
                1.1f * Scale * FontScale
            ));

            var info = new StringBuilder();
            info.AppendLine(maxLabel + FormatingHelper.WattsToString(entry.Max));
            info.AppendLine(currentLabel + FormatingHelper.WattsToString(entry.Current));
            info.AppendLine(entry.UsageLine);
            TrimText(ref info, nameRect.Width, 0.7f);

            var infoPos = nameRect.Center;
            infoPos.X = nameRect.Right;
            infoPos.Y -= nameRect.Height * 0.4f;

            sprites.Add(new MySprite(
                SpriteType.TEXT,
                info.ToString(),
                infoPos,
                null,
                foreground,
                TextFont,
                TextAlignment.RIGHT,
                .9f * Scale * FontScale
            ));
        }

        void RenderPowerEntryControl(ControlTemplate control, List<MySprite> sprites)
        {
            var entry = control?.DataContext as PowerEntry;
            if (entry == null)
                return;

            var bounds = control.Bounds;
            DrawGridPowerCell(
                sprites,
                entry,
                bounds.X,
                bounds.Right,
                bounds.Y,
                bounds.Height,
                entry.MaxLabel,
                entry.CurrentLabel,
                entry.DrawAsLines);
        }

        void DrawCellPie(List<MySprite> sprites, RectangleF iconRect, float usage)
        {
            var pieSize = new Vector2(iconRect.Width, iconRect.Height);
            var pieOrigo = new Vector2(iconRect.X + iconRect.Width / 2f, iconRect.Y + iconRect.Height / 2f);
            var margin = ToScreenMargin(pieOrigo);

            PieChartPanel.CreateSprites(
                sprites,
                string.Empty,
                (IMyTextSurface)Surface,
                margin,
                pieSize,
                usage,
                _ascentColor,
                true,
                false);
        }

        float ContentTop()
        {
            return Host.TitleVisible ? ViewBox.Y + 40f * LayoutScale : ViewBox.Y;
        }

        RectangleF GetCellViewBox(float xStart, float xEnd, float yStart, float cellHeight, float cellPadding)
        {
            var innerLeft = xStart + cellPadding;
            var innerRight = xEnd - cellPadding;
            var innerTop = yStart + cellPadding;
            var innerBottom = yStart + cellHeight - cellPadding;
            return new RectangleF(innerLeft, innerTop, innerRight - innerLeft, innerBottom - innerTop);
        }

        MyTuple<RectangleF, RectangleF, RectangleF> GetCellSlots(float innerLeft, float innerRight,
            float innerTop, float innerBottom, float spacing)
        {
            var topRowHeight = spacing * Scale;
            var bottomRowTop = innerTop + topRowHeight;
            var bottomRowHeight = Math.Max(0f, innerBottom - bottomRowTop);
            var iconSize = innerBottom - innerTop;
            var contentLeft = innerLeft + iconSize;
            var contentWidth = Math.Max(0f, innerRight - contentLeft);

            var iconRect = new RectangleF(innerLeft, innerTop, iconSize, iconSize);
            var numberRect = new RectangleF(contentLeft, innerTop, contentWidth, topRowHeight);
            var nameRect = new RectangleF(contentLeft, bottomRowTop, contentWidth, bottomRowHeight);
            return new MyTuple<RectangleF, RectangleF, RectangleF>(iconRect, numberRect, nameRect);
        }

        void TrimText(ref StringBuilder sb, float availableWidth, float fontSize = 1)
        {
            Vector2 textSize = Surface.MeasureStringInPixels(sb, TextFont, fontSize * Scale * FontScale);

            if (textSize.X <= availableWidth)
                return;

            var source = sb.ToString();
            for (int i = source.Length - 1; i > 0; i--)
            {
                sb.Clear();
                sb.Append(FormatingHelper.TrimName(source, i));
                textSize = Surface.MeasureStringInPixels(sb, TextFont, fontSize * Scale * FontScale);

                if (textSize.X <= availableWidth)
                    break;
            }
        }

        Vector2 ToScreenMargin(Vector2 absoluteCenterInViewBox)
        {
            return new Vector2(absoluteCenterInViewBox.X, 512f - absoluteCenterInViewBox.Y);
        }

        protected static double ToWatts(float powerUnit)
        {
            return powerUnit * 1000000;
        }
    }
}
