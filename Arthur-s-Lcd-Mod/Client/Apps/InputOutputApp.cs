using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.Entity;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;
using GridLinkTypeEnum = VRage.Game.ModAPI.GridLinkTypeEnum;
using VisualStackPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel.StackPanel;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    /// <summary>
    /// Accordion of the grid's refineries/assemblers: one collapsible section per block. The header shows
    /// the block name and a button that cycles its own view between Input, Output and All. When a section is
    /// in "All", its items are shown in two columns (input | output). Collapsed by default.
    /// </summary>
    [LcdApp(5)]
    // ReSharper disable once PartialTypeWithSinglePart
    internal sealed partial class InputOutputApp : ItemsApp
    {
        public const string NAME = MOD_PREFIX + "InputOutput";

        const int MODE_INPUT = 0;
        const int MODE_OUTPUT = 1;
        const int MODE_ALL = 2;

        const int ROW_HEADER = 0;
        const int ROW_ITEM = 1;
        const int ROW_COLUMNS = 2;

        const string CHEVRON_SPRITE = "AH_BoreSight";

        const float CARD_GAP = 3f;    
        const float CARD_PAD = 12f;   
        const float ITEM_INSET = 9f;       
        const float ITEM_INSET_TOP = 3f;   
        const float ITEM_INSET_BOTTOM = 7f;
        const float ITEM_PAD = 10f;        

        const string LOC_INPUT = MOD_PREFIX + "InputOutput_Input";
        const string LOC_OUTPUT = MOD_PREFIX + "InputOutput_Output";
        const string LOC_ALL = MOD_PREFIX + "InputOutput_All";
        const string LOC_EMPTY = MOD_PREFIX + "InputOutput_Empty";

        static readonly List<ProductionBlockItems> EmptyBlocks = new List<ProductionBlockItems>();

        readonly ScrollPanel _scroll;
        readonly Dictionary<long, BlockUiState> _state = new Dictionary<long, BlockUiState>();
        readonly Dictionary<long, RectangleControl> _headerControls = new Dictionary<long, RectangleControl>();
        readonly Dictionary<long, RectangleControl> _modeControls = new Dictionary<long, RectangleControl>();
        readonly List<RowControl> _rowControls = new List<RowControl>();
        readonly List<Row> _rows = new List<Row>();
        readonly List<BlockSpan> _spans = new List<BlockSpan>();
        readonly HashSet<long> _liveIds = new HashSet<long>();
        readonly List<long> _removeIds = new List<long>();
        readonly LinkedTypedBlockSourceSet<IMyAssembler> _assemblerSources =
            new LinkedTypedBlockSourceSet<IMyAssembler>(blocks => blocks.Assemblers);
        readonly LinkedTypedBlockSourceSet<IMyRefinery> _refinerySources =
            new LinkedTypedBlockSourceSet<IMyRefinery>(blocks => blocks.Refineries);
        readonly List<IMyTerminalBlock> _terminalGroupBlocks = new List<IMyTerminalBlock>();
        readonly HashSet<long> _selectedBlockIds = new HashSet<long>();
        readonly HashSet<long> _seenProductionBlockIds = new HashSet<long>();
        readonly HashSet<TypedItemCollection> _productionItemSources = new HashSet<TypedItemCollection>();
        readonly HashSet<GridLogic> _productionItemLogics = new HashSet<GridLogic>();
        readonly GridLogic _subscribedGridLogic;
        readonly Dictionary<MyItemType, MyFixedPoint> _inventoryAmountsScratch =
            new Dictionary<MyItemType, MyFixedPoint>();

        List<ProductionBlockItems> _blocks = EmptyBlocks;
        List<ProductionBlockItems> _lastPrunedBlocks;
        bool _refreshQueued;
        bool _productionDataDirty = true;
        bool _hasQueryToken;
        SearchQueryToken _queryToken;
        Color _itemCardColor;
        Color _itemTextColor;
        VisualStackPanel _listPanel;
        float _currentRowHeight;

        public Dictionary<MyItemType, double> ItemSource => null;

        protected override string DefaultTitle => NAME;

        public bool HasBlocks => _blocks != null && _blocks.Count > 0;

        public InputOutputApp(IAppHost host) : base(host)
        {
            // Input/Output owns the item capabilities for the production grids it reads.
            // The inherited item view model is otherwise unused by this experimental app.
            if (ViewModel != null)
                ViewModel.Dispose();

            _assemblerSources.Changed += OnProductionSourceChanged;
            _refinerySources.Changed += OnProductionSourceChanged;
            _subscribedGridLogic = GridLogic;
            if (_subscribedGridLogic != null)
                _subscribedGridLogic.TerminalGroupChanged += OnTerminalGroupChanged;
            _scroll = AddLogicalChild(new ScrollPanel(CursorType.Default, this));
            _scroll.ManualScrollInertiaEnabled = false;
            _scroll.ScrollChanged = OnScrollChanged;
            _listPanel = new VisualStackPanel();
            _listPanel.CustomRender = RenderListPanelContent;
        }

        public override void Update()
        {
            try
            {
                var queryToken = SearchQueryToken.GetToken(BlockSelectionComponent, ItemSelectionComponent);
                if (!_hasQueryToken || !_queryToken.Equals(queryToken))
                {
                    _queryToken = queryToken;
                    _hasQueryToken = true;
                    _productionDataDirty = true;
                }

                var gridLogic = GridLogic;
                var linkType = (GridLinkTypeEnum)BlockSelectionComponent.GridLinkTypeInternal;
                _assemblerSources.Bind(gridLogic, linkType);
                _refinerySources.Bind(gridLogic, linkType);

                if (_productionDataDirty)
                {
                    _blocks = BuildProductionBlockItems();
                    _productionDataDirty = false;
                }

                if (!ReferenceEquals(_blocks, _lastPrunedBlocks))
                {
                    PruneStaleState();
                    _lastPrunedBlocks = _blocks;
                }

                BuildRows();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                _blocks = EmptyBlocks;
                _rows.Clear();
            }
        }

        List<ProductionBlockItems> BuildProductionBlockItems()
        {
            var gridLogic = GridLogic;
            if (gridLogic == null)
                return EmptyBlocks;

            UnbindProductionItemSources();

            _selectedBlockIds.Clear();
            var selectedBlocks = BlockSelectionComponent.SelectedBlocks ?? new long[0];
            for (var i = 0; i < selectedBlocks.Length; i++)
                _selectedBlockIds.Add(selectedBlocks[i]);

            var selectedGroups = BlockSelectionComponent.SelectedGroups ?? new string[0];
            if (selectedGroups.Length > 0)
            {
                gridLogic.GetTerminalGroupBlocks(selectedGroups, _terminalGroupBlocks);
                for (var i = 0; i < _terminalGroupBlocks.Count; i++)
                {
                    var groupBlock = _terminalGroupBlocks[i];
                    if (groupBlock != null)
                        _selectedBlockIds.Add(groupBlock.EntityId);
                }
            }

            var hasWhitelist = selectedBlocks.Length > 0 || selectedGroups.Length > 0;
            var result = new List<ProductionBlockItems>();
            _seenProductionBlockIds.Clear();
            AddProductionBlocks(_assemblerSources.Sources, gridLogic, hasWhitelist, result);
            AddProductionBlocks(_refinerySources.Sources, gridLogic, hasWhitelist, result);
            return result;
        }

        void AddProductionBlocks<T>(
            IReadOnlyList<LcdMod.Common.Mvvm.IObservableList<T>> sources,
            GridLogic root,
            bool hasWhitelist,
            List<ProductionBlockItems> result)
            where T : class, IMyTerminalBlock
        {
            for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                var source = sources[sourceIndex];
                for (var blockIndex = 0; blockIndex < source.Count; blockIndex++)
                {
                    var block = source[blockIndex];
                    if (block == null ||
                        !_seenProductionBlockIds.Add(block.EntityId) ||
                        (hasWhitelist && !_selectedBlockIds.Contains(block.EntityId)))
                    {
                        continue;
                    }

                    var owner = block.CubeGrid != null
                        ? LcdModSessionComponent.GetOrCreateGridLogic(block.CubeGrid)
                        : root;
                    BindProductionItemSource(owner);
                    var entry = new ProductionBlockItems(block.EntityId, GetBlockDisplayName(block));
                    AddInventoryItems(owner, block, 0, entry.Input);
                    AddInventoryItems(owner, block, 1, entry.Output);
                    result.Add(entry);
                }
            }
        }

        void AddInventoryItems(
            GridLogic owner,
            IMyTerminalBlock block,
            int inventoryIndex,
            List<KeyValuePair<MyItemType, double>> destination)
        {
            if (owner == null || block == null || inventoryIndex >= block.InventoryCount)
                return;

            var inventory = block.GetInventory(inventoryIndex) as MyInventoryBase;
            if (inventory == null)
                return;

            _inventoryAmountsScratch.Clear();
            var items = inventory.GetItems();
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var item = items[itemIndex];
                if (item.Content == null)
                    continue;

                var itemType = (MyItemType)item.Content;
                MyFixedPoint currentAmount;
                _inventoryAmountsScratch.TryGetValue(itemType, out currentAmount);
                _inventoryAmountsScratch[itemType] = MyFixedPoint.AddSafe(currentAmount, item.Amount);
            }

            foreach (var amount in _inventoryAmountsScratch)
            {
                if (amount.Value > MyFixedPoint.Zero)
                    destination.Add(new KeyValuePair<MyItemType, double>(amount.Key, (double)amount.Value));
            }
            destination.Sort((left, right) => right.Value.CompareTo(left.Value));
        }

        static string GetBlockDisplayName(IMyTerminalBlock block)
        {
            if (!string.IsNullOrEmpty(block.CustomName))
                return block.CustomName;
            if (!string.IsNullOrEmpty(block.DisplayNameText))
                return block.DisplayNameText;
            return block.BlockDefinition.SubtypeName ?? string.Empty;
        }

        public override void Close()
        {
            _assemblerSources.Changed -= OnProductionSourceChanged;
            _refinerySources.Changed -= OnProductionSourceChanged;
            if (_subscribedGridLogic != null)
                _subscribedGridLogic.TerminalGroupChanged -= OnTerminalGroupChanged;
            UnbindProductionItemSources();
            _assemblerSources.Dispose();
            _refinerySources.Dispose();
            base.Close();
        }

        void BindProductionItemSource(GridLogic logic)
        {
            if (logic == null || !_productionItemLogics.Add(logic))
                return;

            logic.RequestCapability(GridCapability.Items);
            try
            {
                var source = logic.Items;
                if (_productionItemSources.Add(source))
                    source.InventoryChanged += OnProductionInventoryChanged;
            }
            catch
            {
                _productionItemLogics.Remove(logic);
                logic.Release(GridCapability.Items);
                throw;
            }
        }

        void UnbindProductionItemSources()
        {
            foreach (var source in _productionItemSources)
                source.InventoryChanged -= OnProductionInventoryChanged;
            _productionItemSources.Clear();
            foreach (var logic in _productionItemLogics)
                logic.Release(GridCapability.Items);
            _productionItemLogics.Clear();
        }

        void OnProductionSourceChanged()
        {
            _productionDataDirty = true;
            MarkDirty();
        }

        void OnProductionInventoryChanged(MyInventoryBase inventory)
        {
            _productionDataDirty = true;
            MarkDirty();
        }

        void OnTerminalGroupChanged(object sender, TerminalGroupChangedArgs args)
        {
            _productionDataDirty = true;
            MarkDirty();
        }

        void PruneStaleState()
        {
            _liveIds.Clear();
            for (int i = 0; i < _blocks.Count; i++)
                _liveIds.Add(_blocks[i].EntityId);

            PruneStale(_state);
            PruneStale(_headerControls);
            PruneStale(_modeControls);
        }

        void PruneStale<T>(Dictionary<long, T> dict)
        {
            _removeIds.Clear();
            foreach (var kv in dict)
                if (!_liveIds.Contains(kv.Key))
                    _removeIds.Add(kv.Key);

            for (int i = 0; i < _removeIds.Count; i++)
                dict.Remove(_removeIds[i]);
        }

        void BuildRows()
        {
            _rows.Clear();
            _spans.Clear();

            for (int i = 0; i < _blocks.Count; i++)
            {
                var block = _blocks[i];
                var state = GetState(block.EntityId);
                int firstRow = _rows.Count;

                _rows.Add(new Row { Kind = ROW_HEADER, BlockIndex = i });

                if (state.Expanded && state.Mode == MODE_ALL)
                {
                    _rows.Add(new Row { Kind = ROW_COLUMNS, BlockIndex = i, TwoColumn = true });

                    int count = Math.Max(block.Input.Count, block.Output.Count);
                    if (count == 0)
                        _rows.Add(new Row { Kind = ROW_ITEM, BlockIndex = i, TwoColumn = true });

                    for (int r = 0; r < count; r++)
                    {
                        var row = new Row { Kind = ROW_ITEM, BlockIndex = i, TwoColumn = true };
                        if (r < block.Input.Count)
                        {
                            row.HasLeft = true;
                            row.LeftType = block.Input[r].Key;
                            row.LeftAmount = block.Input[r].Value;
                        }

                        if (r < block.Output.Count)
                        {
                            row.HasRight = true;
                            row.RightType = block.Output[r].Key;
                            row.RightAmount = block.Output[r].Value;
                        }

                        _rows.Add(row);
                    }
                }
                else if (state.Expanded)
                {
                    var list = state.Mode == MODE_OUTPUT ? block.Output : block.Input;
                    if (list.Count == 0)
                        _rows.Add(new Row { Kind = ROW_ITEM, BlockIndex = i });

                    for (int r = 0; r < list.Count; r++)
                        _rows.Add(new Row
                        {
                            Kind = ROW_ITEM,
                            BlockIndex = i,
                            HasLeft = true,
                            LeftType = list[r].Key,
                            LeftAmount = list[r].Value
                        });
                }

                _spans.Add(new BlockSpan { FirstRow = firstRow, RowCount = _rows.Count - firstRow });
            }
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            _children.Clear();
            HideRowControls();

            if (_blocks.Count == 0)
                return sprites;

            _itemCardColor = ResolveThemeColor(SECONDARY_CONTAINER, GetHeaderColor().MulValue(0.6f));
            _itemTextColor = ResolveThemeColor(ON_SECONDARY_CONTAINER, ForegroundColor);

            float rowHeight = LINE_HEIGHT * Scale;
            _currentRowHeight = rowHeight;
            _scroll.SetContent(_listPanel);
            _listPanel.RowHeight = rowHeight;
            _listPanel.Gap = 0f;
            SyncRowControls(_listPanel);
            ConfigureAutomaticScroll(rowHeight);
            _scroll.SetVisible(true);
            _children.Add(_scroll);
            _scroll.Render(sprites);
            return sprites;
        }

        void ConfigureAutomaticScroll(float rowHeight)
        {
            var contentTop = ContentTop();
            var viewportHeight = Math.Max(0f, ViewBox.Bottom - contentTop);
            _scroll.AutoScrollSecondsPerStep = 0f;
            _scroll.ConfigureAutomatic(
                new RectangleF(ViewBox.X, contentTop, ViewBox.Width, viewportHeight),
                ScrollPanel.DEFAULT_SCROLLER_WIDTH_PIXELS * Scale,
                rowHeight);
        }

        void RenderListPanelContent(ControlTemplate control, List<MySprite> sprites)
        {
            DrawBlockCards(sprites, _currentRowHeight);

            var children = control?.VisualChildren;
            if (children == null)
                return;

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i] as ControlTemplate;
                if (child != null)
                    child.Render(sprites);
            }
        }

        void DrawBlockCards(List<MySprite> sprites, float rowHeight)
        {
            var view = _scroll.ContentViewportBounds;
            float gap = CARD_GAP * Scale;
            var panelColor = GetHeaderColor();
            var shadowColor = GetHeaderColor().MulValue(0.2f);

            for (int s = 0; s < _spans.Count; s++)
            {
                var span = _spans[s];
                if (span.RowCount <= 0)
                    continue;

                float top = _scroll.ContentBounds.Y + span.FirstRow * rowHeight;
                float height = span.RowCount * rowHeight;
                if (top >= view.Bottom || top + height <= view.Y)
                    continue;

                var cardRect = new RectangleF(view.X + gap, top + gap,
                    Math.Max(0f, view.Width - 2f * gap), Math.Max(0f, height - 2f * gap));
                if (cardRect.Width <= 0f || cardRect.Height <= 0f)
                    continue;

                var dropShadow = new RectangleF(cardRect.Position + 2f, cardRect.Size);
                BorderRenderer.CreateSpritesFromRect(dropShadow, sprites, shadowColor, radiusScale: Scale);
                BorderRenderer.CreateSpritesFromRect(cardRect, sprites, panelColor, radiusScale: Scale);
            }
        }

        void SyncRowControls(Panel panel)
        {
            if (panel == null)
                return;

            EnsureRowControlCount(_rows.Count);
            RemoveExtraPanelChildren(panel, _rows.Count);

            var children = panel.VisualChildren;
            bool changed = false;
            for (int i = 0; i < _rows.Count; i++)
            {
                var control = _rowControls[i];
                var row = _rows[i];
                control.Row = row;
                control.CustomRender = RenderRowControl;
                control.SetVisible(true);

                if (row.Kind == ROW_HEADER)
                {
                    var block = _blocks[row.BlockIndex];
                    control.ClearChildren();
                    control.SetDataContext(GetState(block.EntityId));
                    control.SetCursor(CursorType.Hand);
                    control.SetOnClick(OnHeaderClicked);
                }
                else
                {
                    control.SetDataContext(null);
                    control.SetCursor(CursorType.Default);
                    control.SetOnClick(null);
                    control.ClearChildren();
                }

                if (!ReferenceEquals(control.Parent, panel))
                {
                    panel.AddChild(control);
                    children = panel.VisualChildren;
                    changed = true;
                }

                if (children == null || i >= children.Count || ReferenceEquals(children[i], control))
                    continue;

                int currentIndex = IndexOfChild(children, control);
                if (currentIndex < 0)
                    continue;

                if (panel.MoveChild(control, i))
                    changed = true;
            }

            if (changed)
                panel.InvalidateLayout();
        }

        void EnsureRowControlCount(int count)
        {
            while (_rowControls.Count < count)
                _rowControls.Add(new RowControl());
        }

        void HideRowControls()
        {
            for (int i = 0; i < _rowControls.Count; i++)
            {
                if (_rowControls[i] != null)
                {
                    _rowControls[i].SetVisible(false);
                    _rowControls[i].ClearChildren();
                }
            }

            foreach (var kv in _modeControls)
            {
                if (kv.Value != null)
                    kv.Value.SetVisible(false);
            }
        }

        void RemoveExtraPanelChildren(Panel panel, int desiredCount)
        {
            var children = panel.VisualChildren;
            if (children == null)
                return;

            for (int i = children.Count - 1; i >= desiredCount; i--)
                panel.RemoveChild(children[i]);
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

        void RenderRowControl(ControlTemplate control, List<MySprite> sprites)
        {
            var rowControl = control as RowControl;
            if (rowControl == null)
                return;

            var row = rowControl.Row;
            switch (row.Kind)
            {
                case ROW_HEADER:
                    RenderHeader(rowControl, sprites, row, rowControl.Bounds);
                    break;
                case ROW_COLUMNS:
                    RenderColumnsHeader(sprites, rowControl.Bounds);
                    break;
                default:
                    RenderItem(sprites, row, rowControl.Bounds);
                    break;
            }
        }

        void RenderHeader(RowControl rowControl, List<MySprite> sprites, Row row, RectangleF bounds)
        {
            var block = _blocks[row.BlockIndex];
            var state = GetState(block.EntityId);

            float pad = (CARD_GAP + CARD_PAD) * Scale;

            float textScale = Scale * FontScale;
            float lineH = MeasureLineHeight(textScale);
            float centeredY = bounds.Y + (bounds.Height - lineH) * 0.5f;

            float nameRight = bounds.Right - pad;
            if (state.Expanded)
            {
                string modeLabel = ModeLabel(state.Mode);
                float modeTextW = FormatingHelper.GetSizeInPixel(modeLabel, TextFont, textScale * 0.85f, Surface).X;
                float modeW = Math.Max(82f * Scale, modeTextW + 28f * Scale);
                float modeH = Math.Min(bounds.Height - (2f * CARD_GAP + 6f) * Scale, lineH + 10f * Scale);
                var modeRect = new RectangleF(
                    bounds.Right - pad - modeW,
                    bounds.Y + (bounds.Height - modeH) * 0.5f,
                    modeW,
                    modeH);
                BorderRenderer.CreateSpritesFromRect(modeRect, sprites, GetHeaderColor().MulValue(0.45f), radiusScale: Scale);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = modeLabel,
                    Position = new Vector2(modeRect.Center.X, modeRect.Center.Y - lineH * 0.5f),
                    RotationOrScale = textScale * 0.85f,
                    Color = ForegroundColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = TextFont
                });

                var mode = GetOrCreateControl(_modeControls, block.EntityId, state, OnModeClicked);
                mode.SetRect(modeRect);
                mode.SetVisible(true);
                rowControl.AddChild(mode);

                nameRight = modeRect.X - 12f * Scale;
            }

            float arrowSize = 16f * Scale;
            var arrowCenter = new Vector2(bounds.X + pad + arrowSize * 0.5f, bounds.Y + bounds.Height * 0.5f);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = CHEVRON_SPRITE,
                Position = arrowCenter,
                Size = new Vector2(arrowSize),
                RotationOrScale = state.Expanded ? MathHelper.PiOver2 : 0,
                Color = ForegroundColor,
                Alignment = TextAlignment.CENTER
            });

            float nameLeft = bounds.X + pad + arrowSize + 8f * Scale;
            string name = TrimText(block.Name, Math.Max(10f, nameRight - nameLeft));
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = name,
                Position = new Vector2(nameLeft, centeredY),
                RotationOrScale = textScale,
                Color = ForegroundColor,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
        }

        void RenderColumnsHeader(List<MySprite> sprites, RectangleF bounds)
        {
            float textScale = Scale * FontScale * 0.75f;
            float lineH = MeasureLineHeight(textScale);
            float centeredY = bounds.Y + (bounds.Height - lineH) * 0.5f;
            float indent = (ITEM_INSET + ITEM_PAD) * Scale;
            float mid = bounds.X + bounds.Width * 0.5f;
            var labelColor = ForegroundColor;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = MyTexts.GetString(LOC_INPUT),
                Position = new Vector2(bounds.X + indent, centeredY),
                RotationOrScale = textScale,
                Color = labelColor,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = MyTexts.GetString(LOC_OUTPUT),
                Position = new Vector2(mid + indent, centeredY),
                RotationOrScale = textScale,
                Color = labelColor,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
        }

        void RenderItem(List<MySprite> sprites, Row row, RectangleF bounds)
        {
            if (row.TwoColumn)
            {
                float mid = bounds.X + bounds.Width * 0.5f;
                float dividerTop = bounds.Y + ITEM_INSET_TOP * Scale;
                float dividerHeight = Math.Max(0f, bounds.Height - (ITEM_INSET_TOP + ITEM_INSET_BOTTOM) * Scale);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(mid, dividerTop + dividerHeight * 0.5f),
                    Size = new Vector2(1f, dividerHeight),
                    Color = GetHeaderColor().MulValue(0.5f),
                    Alignment = TextAlignment.CENTER
                });

                var leftRect = new RectangleF(bounds.X, bounds.Y, bounds.Width * 0.5f, bounds.Height);
                var rightRect = new RectangleF(mid, bounds.Y, bounds.Width * 0.5f, bounds.Height);

                if (!row.HasLeft && !row.HasRight)
                {
                    DrawEmpty(sprites, bounds);
                    return;
                }

                if (row.HasLeft)
                    DrawItemCell(sprites, row.LeftType, row.LeftAmount, leftRect);
                if (row.HasRight)
                    DrawItemCell(sprites, row.RightType, row.RightAmount, rightRect);

                return;
            }

            if (row.HasLeft)
                DrawItemCell(sprites, row.LeftType, row.LeftAmount, bounds);
            else
                DrawEmpty(sprites, bounds);
        }

        void DrawItemCell(List<MySprite> sprites, MyItemType type, double amount, RectangleF rect)
        {
            float hInset = ITEM_INSET * Scale;
            float topInset = ITEM_INSET_TOP * Scale;
            float bottomInset = ITEM_INSET_BOTTOM * Scale;
            var card = new RectangleF(rect.X + hInset, rect.Y + topInset,
                Math.Max(0f, rect.Width - 2f * hInset), Math.Max(0f, rect.Height - topInset - bottomInset));
            if (card.Width <= 0f || card.Height <= 0f)
                return;

            BorderRenderer.CreateSpritesFromRect(card, sprites, _itemCardColor, radiusScale: Scale);

            float pad = ITEM_PAD * Scale;
            float iconSize = card.Height * 0.66f;
            var iconPos = new Vector2(card.X + pad + iconSize * 0.5f, card.Y + card.Height * 0.5f);
            DrawItemIcon(sprites, ResolveSprite(type), iconPos, new Vector2(iconSize), TextAlignment.CENTER, Color.White);

            float textScale = Scale * FontScale * 0.85f;
            float lineH = MeasureLineHeight(textScale);
            float centeredY = card.Y + (card.Height - lineH) * 0.5f;

            string qty = FormatingHelper.FormatItemQty(amount);
            float qtyW = FormatingHelper.GetSizeInPixel(qty, TextFont, textScale, Surface).X;
            float nameLeft = card.X + pad + iconSize + 8f * Scale;
            float nameRight = card.Right - qtyW - (pad + 6f * Scale);
            string name = TrimText(ResolveDisplayName(type), Math.Max(10f, nameRight - nameLeft), 0.85f);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = name,
                Position = new Vector2(nameLeft, centeredY),
                RotationOrScale = textScale,
                Color = _itemTextColor,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = qty,
                Position = new Vector2(card.Right - pad, centeredY),
                RotationOrScale = textScale,
                Color = _itemTextColor,
                Alignment = TextAlignment.RIGHT,
                FontId = TextFont
            });
        }

        Color ResolveThemeColor(string role, Color fallback)
        {
            return ResolveRole(role, fallback);
        }

        void DrawEmpty(List<MySprite> sprites, RectangleF bounds)
        {
            float textScale = Scale * FontScale * 0.8f;
            float lineH = MeasureLineHeight(textScale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = MyTexts.GetString(LOC_EMPTY),
                Position = new Vector2(bounds.Center.X, bounds.Center.Y - lineH * 0.5f),
                RotationOrScale = textScale,
                Color = ForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = TextFont
            });
        }

        RectangleControl GetOrCreateControl(Dictionary<long, RectangleControl> cache, long entityId, BlockUiState state,
            Action<object, object> onClick)
        {
            RectangleControl control;
            if (!cache.TryGetValue(entityId, out control) || control == null)
            {
                control = new RectangleControl(default(RectangleF), CursorType.Hand, state, onClick);
                cache[entityId] = control;
            }

            return control;
        }

        BlockUiState GetState(long entityId)
        {
            BlockUiState state;
            if (!_state.TryGetValue(entityId, out state))
            {
                state = new BlockUiState { Expanded = false, Mode = MODE_INPUT };
                _state[entityId] = state;
            }

            return state;
        }

        string ModeLabel(int mode)
        {
            switch (mode)
            {
                case MODE_OUTPUT:
                    return MyTexts.GetString(LOC_OUTPUT);
                case MODE_ALL:
                    return MyTexts.GetString(LOC_ALL);
                default:
                    return MyTexts.GetString(LOC_INPUT);
            }
        }

        float ContentTop()
        {
            return TitleVisible ? ViewBox.Y + 40f * Scale * FontScale : ViewBox.Y;
        }

        void OnHeaderClicked(object dataContext, object sender)
        {
            var state = dataContext as BlockUiState;
            if (state == null)
                return;

            state.Expanded = !state.Expanded;
            ScheduleRefresh();
        }

        void OnModeClicked(object dataContext, object sender)
        {
            var state = dataContext as BlockUiState;
            if (state == null)
                return;

            state.Mode = (state.Mode + 1) % 3;
            ScheduleRefresh();
        }

        void OnScrollChanged(ScrollPanel panel)
        {
            ScheduleRefresh();
        }

        void ScheduleRefresh()
        {
            if (_refreshQueued)
                return;

            _refreshQueued = true;
            LcdModClientComponent.RunNextFrame.Add(RunQueuedRefresh);
        }

        void RunQueuedRefresh()
        {
            _refreshQueued = false;

            try
            {
                Update();
                Host.RenderSprites();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }
        }

        sealed class BlockUiState
        {
            public bool Expanded;
            public int Mode;
        }

        sealed class RowControl : RectangleControl
        {
            public RowControl()
                : base(default(RectangleF), CursorType.Default)
            {
            }

            public Row Row;
        }

        struct BlockSpan
        {
            public int FirstRow;
            public int RowCount;
        }

        struct Row
        {
            public int Kind;
            public int BlockIndex;
            public bool TwoColumn;
            public bool HasLeft;
            public MyItemType LeftType;
            public double LeftAmount;
            public bool HasRight;
            public MyItemType RightType;
            public double RightAmount;
        }
    }
}
