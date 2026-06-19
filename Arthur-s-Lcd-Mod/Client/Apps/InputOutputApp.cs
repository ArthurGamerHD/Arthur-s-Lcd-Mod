using System;
using System.Collections.Generic;
using LcdMod.Client;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;
using VisualStackPanel = LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel.StackPanel;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps
{
    /// <summary>
    /// Accordion of the grid's refineries/assemblers: one collapsible section per block. The header shows
    /// the block name and a button that cycles its own view between Input, Output and All. When a section is
    /// in "All", its items are shown in two columns (input | output). Collapsed by default.
    /// </summary>
    internal sealed class InputOutputApp : ItemsApp
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

        List<ProductionBlockItems> _blocks = EmptyBlocks;
        List<ProductionBlockItems> _lastPrunedBlocks;
        bool _refreshQueued;
        Color _itemCardColor;
        Color _itemTextColor;
        VisualStackPanel _listPanel;
        float _currentRowHeight;

        public override Dictionary<MyItemType, double> ItemSource => null;

        protected override string DefaultTitle => NAME;

        public bool HasBlocks => _blocks != null && _blocks.Count > 0;

        public InputOutputApp(ScreenConfigWithItems config, IAppHost host) : base(config, host)
        {
            _scroll = AddChild(new ScrollPanel(CursorType.Default, this));
            _scroll.ManualScrollInertiaEnabled = false;
            _scroll.ScrollChanged = OnScrollChanged;
            _listPanel = new VisualStackPanel();
            _listPanel.CustomRender = RenderListPanelContent;
        }

        public override void Update()
        {
            try
            {
                _blocks = GridLogic?.GetProductionBlockItems(AppConfig, Block as IMyTerminalBlock) ?? EmptyBlocks;

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

            _itemCardColor = ResolveThemeColor(Constants.SECONDARY_CONTAINER, AppConfig.HeaderColor.MulValue(0.6f));
            _itemTextColor = ResolveThemeColor(Constants.ON_SECONDARY_CONTAINER, ForegroundColor);

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
                ScrollPanel.DefaultScrollerWidthPixels * Scale,
                rowHeight);
        }

        void RenderListPanelContent(ControlTemplate control, List<MySprite> sprites)
        {
            DrawBlockCards(sprites, _currentRowHeight);

            var children = control != null ? control.Children : null;
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
            var panelColor = AppConfig.HeaderColor;
            var shadowColor = AppConfig.HeaderColor.MulValue(0.2f);

            for (int s = 0; s < _spans.Count; s++)
            {
                var span = _spans[s];
                if (span.RowCount <= 0)
                    continue;

                int end = span.FirstRow + span.RowCount;
                float top = _scroll.ContentBounds.Y + span.FirstRow * rowHeight;
                float height = span.RowCount * rowHeight;
                if (top >= view.Bottom || top + height <= view.Y)
                    continue;

                var cardRect = new RectangleF(view.X + gap, top + gap,
                    Math.Max(0f, view.Width - 2f * gap), Math.Max(0f, height - 2f * gap));
                if (cardRect.Width <= 0f || cardRect.Height <= 0f)
                    continue;

                var dropShadow = new RectangleF(cardRect.Position + 2f, cardRect.Size);
                Border.CreateSpritesFromRect(dropShadow, sprites, shadowColor, radiusScale: Scale);
                Border.CreateSpritesFromRect(cardRect, sprites, panelColor, radiusScale: Scale);
            }
        }

        void SyncRowControls(Panel panel)
        {
            if (panel == null)
                return;

            EnsureRowControlCount(_rows.Count);
            RemoveExtraPanelChildren(panel, _rows.Count);

            var children = panel.Children;
            bool changed = false;
            for (int i = 0; i < _rows.Count; i++)
            {
                var control = _rowControls[i];
                var row = _rows[i];
                control.Row = row;
                control.RowIndex = i;
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
                    children = panel.Children;
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
            var children = panel.Children;
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
                Border.CreateSpritesFromRect(modeRect, sprites, AppConfig.HeaderColor.MulValue(0.45f), radiusScale: Scale);
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
                    Color = AppConfig.HeaderColor.MulValue(0.5f),
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

            Border.CreateSpritesFromRect(card, sprites, _itemCardColor, radiusScale: Scale);

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
                state = new BlockUiState { EntityId = entityId, Expanded = false, Mode = MODE_INPUT };
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
            public long EntityId;
            public bool Expanded;
            public int Mode;
        }

        sealed class RowControl : RectangleControl
        {
            public RowControl()
                : base(default(RectangleF), CursorType.Default)
            {
            }

            public int RowIndex;
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
