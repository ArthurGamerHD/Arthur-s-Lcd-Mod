using System;
using System.Collections.Generic;
using LcdMod.Client;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Grid;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.StackPanel;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Models.Apps;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.Apps
{
    /// <summary>
    /// Accordion of the grid's refineries/assemblers: one collapsible section per block. The header shows
    /// the block name and a button that cycles its own view between Input, Output and All. When a section is
    /// in "All", its items are shown in two columns (input | output). Collapsed by default.
    /// </summary>
    internal sealed class InputOutputApp : ItemsAppBase
    {
        public const string NAME = "LcdMod_InputOutput";

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

        const string LOC_INPUT = "LcdMod_InputOutput_Input";
        const string LOC_OUTPUT = "LcdMod_InputOutput_Output";
        const string LOC_ALL = "LcdMod_InputOutput_All";
        const string LOC_EMPTY = "LcdMod_InputOutput_Empty";

        static readonly List<ProductionBlockItems> EmptyBlocks = new List<ProductionBlockItems>();

        readonly ScrollPanel _scroll;
        readonly Dictionary<long, BlockUiState> _state = new Dictionary<long, BlockUiState>();
        readonly Dictionary<long, RectangleControl> _headerControls = new Dictionary<long, RectangleControl>();
        readonly Dictionary<long, RectangleControl> _modeControls = new Dictionary<long, RectangleControl>();
        readonly List<Row> _rows = new List<Row>();
        readonly List<BlockSpan> _spans = new List<BlockSpan>();
        readonly HashSet<long> _liveIds = new HashSet<long>();
        readonly List<long> _removeIds = new List<long>();

        List<ProductionBlockItems> _blocks = EmptyBlocks;
        List<ProductionBlockItems> _lastPrunedBlocks;
        bool _refreshQueued;
        Color _itemCardColor;
        Color _itemTextColor;

        public override Dictionary<MyItemType, double> ItemSource => null;

        protected override string DefaultTitle => NAME;

        public bool HasBlocks => _blocks != null && _blocks.Count > 0;

        public InputOutputApp(ScreenConfigWithItems config, IAppHost host) : base(config, host)
        {
            _scroll = new ScrollPanel(CursorType.Default, this);
            _scroll.ManualScrollInertiaEnabled = false;
            _scroll.ScrollChanged = OnScrollChanged;
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
            InteractiveList.Clear();
            _scroll.ClearChildren();

            if (_blocks.Count == 0)
                return sprites;

            _itemCardColor = ResolveThemeColor(Constants.SECONDARY_CONTAINER, AppConfig.HeaderColor.MulValue(0.6f));
            _itemTextColor = ResolveThemeColor(Constants.ON_SECONDARY_CONTAINER, ForegroundColor);

            float rowHeight = LINE_HEIGHT * Scale;
            _scroll.Configure(ViewBox, ContentTop(), 0f, rowHeight, _rows.Count, SCROLLER_WIDTH * Scale, 0f);
            ConfigureScrollColors();
            _scroll.SetVisible(true);
            InteractiveList.Add(_scroll);

            var context = CreateItemRenderContext();
            _scroll.Render(context, sprites);

            var stack = StackPanel.Create(_scroll.ContentBounds, rowHeight, _rows.Count, _scroll.GetStartIndex(1));
            if (stack.VisibleCellCount <= 0)
                return sprites;

            BeginClip(sprites, _scroll.ContentViewportBounds);

            DrawBlockCards(sprites, rowHeight, _scroll.StartRow, _scroll.StartRow + stack.VisibleCellCount);

            for (int i = 0; i < stack.VisibleCellCount; i++)
            {
                var cell = stack.GetCell(i);
                if (cell.ItemIndex < 0 || cell.ItemIndex >= _rows.Count)
                    continue;

                var row = _rows[cell.ItemIndex];
                switch (row.Kind)
                {
                    case ROW_HEADER:
                        RenderHeader(sprites, row, cell.Bounds);
                        break;
                    case ROW_COLUMNS:
                        RenderColumnsHeader(sprites, cell.Bounds);
                        break;
                    default:
                        RenderItem(sprites, row, cell.Bounds);
                        break;
                }
            }

            EndClip(sprites);
            return sprites;
        }

        void DrawBlockCards(List<MySprite> sprites, float rowHeight, int visibleStart, int visibleEnd)
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
                if (span.FirstRow >= visibleEnd || end <= visibleStart)
                    continue;

                float top = _scroll.ContentBounds.Y + (span.FirstRow - _scroll.StartRow) * rowHeight;
                float height = span.RowCount * rowHeight;
                var cardRect = new RectangleF(view.X + gap, top + gap,
                    Math.Max(0f, view.Width - 2f * gap), Math.Max(0f, height - 2f * gap));
                if (cardRect.Width <= 0f || cardRect.Height <= 0f)
                    continue;

                var dropShadow = new RectangleF(cardRect.Position + 2f, cardRect.Size);
                Border.CreateSpritesFromRect(dropShadow, sprites, shadowColor, radiusScale: Scale);
                Border.CreateSpritesFromRect(cardRect, sprites, panelColor, radiusScale: Scale);
            }
        }

        void RenderHeader(List<MySprite> sprites, Row row, RectangleF bounds)
        {
            var block = _blocks[row.BlockIndex];
            var state = GetState(block.EntityId);

            var header = GetOrCreateControl(_headerControls, block.EntityId, state, OnHeaderClicked);
            header.SetRect(bounds);
            header.SetVisible(true);
            _scroll.AddChild(header);

            float pad = (CARD_GAP + CARD_PAD) * Scale;

            float textScale = Scale * FontScale;
            float lineH = FormatingHelper.LineHeight(textScale, Surface);
            float centeredY = bounds.Y + (bounds.Height - lineH) * 0.5f;

            float nameRight = bounds.Right - pad;
            if (state.Expanded)
            {
                string modeLabel = ModeLabel(state.Mode);
                float modeTextW = FormatingHelper.GetSizeInPixel(modeLabel, "White", textScale * 0.85f, Surface).X;
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
                    FontId = "White"
                });

                var mode = GetOrCreateControl(_modeControls, block.EntityId, state, OnModeClicked);
                mode.SetRect(modeRect);
                mode.SetVisible(true);
                _scroll.AddChild(mode);

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
                FontId = "White"
            });
        }

        void RenderColumnsHeader(List<MySprite> sprites, RectangleF bounds)
        {
            float textScale = Scale * FontScale * 0.75f;
            float lineH = FormatingHelper.LineHeight(textScale, Surface);
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
                FontId = "White"
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = MyTexts.GetString(LOC_OUTPUT),
                Position = new Vector2(mid + indent, centeredY),
                RotationOrScale = textScale,
                Color = labelColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White"
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
            float lineH = FormatingHelper.LineHeight(textScale, Surface);
            float centeredY = card.Y + (card.Height - lineH) * 0.5f;

            string qty = FormatingHelper.FormatItemQty(amount);
            float qtyW = FormatingHelper.GetSizeInPixel(qty, "White", textScale, Surface).X;
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
                FontId = "White"
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = qty,
                Position = new Vector2(card.Right - pad, centeredY),
                RotationOrScale = textScale,
                Color = _itemTextColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White"
            });
        }

        Color ResolveThemeColor(string role, Color fallback)
        {
            try
            {
                return GetThemeColor(role);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        void DrawEmpty(List<MySprite> sprites, RectangleF bounds)
        {
            float textScale = Scale * FontScale * 0.8f;
            float lineH = FormatingHelper.LineHeight(textScale, Surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = MyTexts.GetString(LOC_EMPTY),
                Position = new Vector2(bounds.Center.X, bounds.Center.Y - lineH * 0.5f),
                RotationOrScale = textScale,
                Color = ForegroundColor,
                Alignment = TextAlignment.CENTER,
                FontId = "White"
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

        void ConfigureScrollColors()
        {
            var track = new Color(ForegroundColor.R, ForegroundColor.G, ForegroundColor.B, (byte)127);
            var thumb = new Color(AppConfig.HeaderColor.R, AppConfig.HeaderColor.G, AppConfig.HeaderColor.B, (byte)250);
            _scroll.SetScrollBarColors(track, thumb);
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

        void BeginClip(List<MySprite> sprites, RectangleF bounds)
        {
            if (bounds.Width <= 0f || bounds.Height <= 0f)
                return;

            int x = (int)Math.Floor(bounds.X);
            int y = (int)Math.Floor(bounds.Y);
            int right = (int)Math.Ceiling(bounds.Right);
            int bottom = (int)Math.Ceiling(bounds.Bottom);
            sprites.Add(MySprite.CreateClipRect(new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y))));
        }

        void EndClip(List<MySprite> sprites)
        {
            sprites.Add(MySprite.CreateClearClipRect());
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
