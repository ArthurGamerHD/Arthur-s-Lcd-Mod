using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Gui.ControlsTemplates.Interactive
{
    sealed class SpritePicker : Dialog
    {
        const string TITLE = "Sprite Picker";
        const string SEARCH_TITLE = "Search Sprite";
        const string SEARCH_PLACEHOLDER = "Search sprites";

        const float CARD_WIDTH_PERCENT = 0.76f;
        const float CARD_HEIGHT_PERCENT = 0.82f;

        const float MIN_CARD_WIDTH_PIXELS = 320f;
        const float MIN_CARD_HEIGHT_PIXELS = 260f;

        const float OUTER_PADDING_PIXELS = 18f;
        const float INNER_PADDING_X_PIXELS = 18f;
        const float INNER_PADDING_Y_PIXELS = 14f;
        const float SPACING_PIXELS = 10f;

        const float SEARCH_HEIGHT_PIXELS = 38f;
        const float ROW_HEIGHT_PIXELS = 40f;
        const float ROW_GAP_PIXELS = 3f;
        const float ICON_SIZE_PIXELS = 32f;
        const float SCROLLER_WIDTH_PIXELS = 12f;

        readonly List<string> _allSprites = new List<string>();
        readonly List<string> _filteredSprites = new List<string>();
        readonly List<RectangleControl> _rowControls = new List<RectangleControl>();
        readonly ScrollPanel _scrollPanel = new ScrollPanel();

        TextInput _searchInput;
        TextInputModel _searchInputModel;

        ControlStyle _searchStyle;
        ControlStyle _rowStyle;

        bool _spritesLoaded;
        string _searchText = string.Empty;

        public SpritePicker(IApp parentApp)
            : this(parentApp, null, null)
        {
        }

        public SpritePicker(IApp parentApp, Action<string> onSelected, Action requestRedraw = null)
            : base(parentApp)
        {
            OnSelected = onSelected;
            RequestRedraw = requestRedraw;

            OnClose = delegate
            {
                if (RequestRedraw != null)
                    RequestRedraw();
            };

            _scrollPanel.ManualScrollInertiaEnabled = false;
            _scrollPanel.ScrollChanged = OnScrollChanged;
        }

        public Action<string> OnSelected { get; set; }

        public Action RequestRedraw { get; set; }

        public void Show(Action<string> onSelected, Action requestRedraw = null)
        {
            OnSelected = onSelected;

            if (requestRedraw != null)
                RequestRedraw = requestRedraw;
        }

        public void ReloadSprites()
        {
            _spritesLoaded = false;
            _allSprites.Clear();
            _filteredSprites.Clear();
        }

        protected override void RenderCore(
            InteractiveSurfaceScript owner,
            RectangleF viewBox,
            float scale,
            float fontScale,
            IMyTextSurface surface,
            Color textColor,
            Color backgroundColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            EnsureContainer(viewBox);
            ContainerControl.ClearChildren();

            EnsureSpritesLoaded(surface);

            var layoutScale = scale * fontScale;
            var outerPadding = OUTER_PADDING_PIXELS * scale;
            var innerPadding = new Vector2(INNER_PADDING_X_PIXELS, INNER_PADDING_Y_PIXELS) * scale;
            var spacing = SPACING_PIXELS * scale;

            var maxCardWidth = Math.Max(1f, viewBox.Width - outerPadding * 2f);
            var maxCardHeight = Math.Max(1f, viewBox.Height - outerPadding * 2f);

            var cardWidth = Math.Min(
                Math.Max(MIN_CARD_WIDTH_PIXELS * scale, viewBox.Width * CARD_WIDTH_PERCENT),
                maxCardWidth);

            var cardHeight = Math.Min(
                Math.Max(MIN_CARD_HEIGHT_PIXELS * scale, viewBox.Height * CARD_HEIGHT_PERCENT),
                maxCardHeight);

            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth,
                cardHeight);

            RegisterDialogCard(cardRect);

            DrawBackground(surface, scale, cardRect);

            var titleScale = 0.82f * layoutScale;
            var titleHeight = FormatingHelper.LineHeight(titleScale, surface);
            var closeSize = GetDialogCloseButtonSize(scale);
            var headerHeight = Math.Max(titleHeight, closeSize.Y);

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = TITLE,
                Position = new Vector2(cardRect.Center.X, cardRect.Y + innerPadding.Y + (headerHeight - titleHeight) * 0.5f),
                Color = GetThemeColor(Constants.ON_SURFACE),
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = titleScale
            });

            var searchHeight = Math.Max(
                SEARCH_HEIGHT_PIXELS * scale,
                FormatingHelper.LineHeight(0.58f * layoutScale, surface) + 18f * scale);

            var searchRect = new RectangleF(
                cardRect.X + innerPadding.X,
                cardRect.Y + innerPadding.Y + headerHeight + spacing,
                Math.Max(1f, cardRect.Width - innerPadding.X * 2f),
                searchHeight);

            var listTop = searchRect.Bottom + spacing;
            var listBottom = cardRect.Bottom - innerPadding.Y;
            var listRect = new RectangleF(
                cardRect.X + innerPadding.X,
                listTop,
                Math.Max(1f, cardRect.Width - innerPadding.X * 2f),
                Math.Max(0f, listBottom - listTop));

            var renderContext = CreateRenderContext(surface, scale, fontScale, textColor, panelColor, cursorPosition);

            EnsureSearchInput(searchRect);
            ContainerControl.AddChild(_searchInput);
            _searchInput.Render(renderContext, Sprites);

            RenderSpriteList(renderContext, listRect, scale, surface);
        }

        void DrawBackground(IMyTextSurface surface, float scale, RectangleF cardRect)
        {
            Sprites.Add(new MySprite(
                SpriteType.TEXTURE,
                "SquareSimple",
                surface.TextureSize / 2f,
                surface.TextureSize,
                new Color(0, 0, 0, 128)));

            Border.CreateSpritesFromRect(
                new RectangleF(cardRect.Position + 3f * scale, cardRect.Size),
                Sprites,
                GetThemeColor(Constants.SHADOW),
                radiusScale: scale);

            Border.CreateSpritesFromRect(
                cardRect,
                Sprites,
                GetThemeColor(Constants.SURFACE_CONTAINER_HIGH),
                radiusScale: scale);
        }

        void EnsureSpritesLoaded(IMyTextSurface surface)
        {
            if (_spritesLoaded)
                return;

            _allSprites.Clear();

            var seenSprites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var registeredSprites = new List<string>();
            BlockIconHelper.GetRegisteredSpriteNames(registeredSprites);
            registeredSprites.Sort(StringComparer.OrdinalIgnoreCase);
            AddUniqueSprites(registeredSprites, seenSprites);

            if (surface != null)
            {
                var lcdSprites = new List<string>();
                surface.GetSprites(lcdSprites);
                lcdSprites.Sort(StringComparer.OrdinalIgnoreCase);
                AddUniqueSprites(lcdSprites, seenSprites);
            }

            _spritesLoaded = true;

            ApplyFilter();
        }

        void AddUniqueSprites(List<string> sprites, HashSet<string> seenSprites)
        {
            for (var i = 0; i < sprites.Count; i++)
            {
                var sprite = sprites[i];
                if (string.IsNullOrEmpty(sprite))
                    continue;

                if (!seenSprites.Add(sprite))
                    continue;

                _allSprites.Add(sprite);
            }
        }

        void EnsureSearchInput(RectangleF rect)
        {
            if (_searchInputModel == null)
            {
                _searchInputModel = new TextInputModel
                {
                    Title = SEARCH_TITLE,
                    Subtitle = "Filter sprites containing this text",
                    Placeholder = SEARCH_PLACEHOLDER,
                    ValueChanged = OnSearchChanged
                };
            }

            _searchInputModel.Title = SEARCH_TITLE;
            _searchInputModel.Subtitle = "Filter sprites containing this text";
            _searchInputModel.Placeholder = SEARCH_PLACEHOLDER;
            _searchInputModel.Value = _searchText;
            _searchInputModel.Enabled = true;
            _searchInputModel.ValueChanged = OnSearchChanged;

            if (_searchInput == null)
                _searchInput = new TextInput(rect, _searchInputModel);
            else
                _searchInput.SetRect(rect);

            _searchInput.SetDataContext(_searchInputModel);
            _searchInput.SetStyle(GetSearchStyle());
            _searchInput.SetCursor(CursorType.Hand);
            _searchInput.SetVisible(true);
        }

        void RenderSpriteList(
            ControlRenderContext context,
            RectangleF listRect,
            float scale,
            IMyTextSurface surface)
        {
            HideUnusedRows(0);

            if (listRect.Height <= 1f)
                return;

            Border.CreateSpritesFromRect(
                listRect,
                Sprites,
                GetThemeColor(Constants.SURFACE_CONTAINER),
                radiusScale: scale);

            var rowHeight = GetRowHeight(scale);
            var scrollerWidth = SCROLLER_WIDTH_PIXELS * scale;

            _scrollPanel.ClearChildren();
            _scrollPanel.Configure(
                listRect,
                listRect.Y,
                0f,
                rowHeight,
                _filteredSprites.Count,
                scrollerWidth,
                0f);

            _scrollPanel.SetScrollBarColors(
                GetThemeColor(Constants.SURFACE_CONTAINER_HIGHEST),
                GetThemeColor(Constants.ON_SURFACE));

            _scrollPanel.SetVisible(true);
            ContainerControl.AddChild(_scrollPanel);

            if (_filteredSprites.Count == 0)
            {
                DrawEmptyListMessage(listRect, scale, surface);
                _scrollPanel.Render(context, Sprites);
                return;
            }

            BeginClip(Sprites, _scrollPanel.ContentViewportBounds);

            var usedControls = 0;
            var startRow = _scrollPanel.StartRow;
            var endRow = Math.Min(_filteredSprites.Count, startRow + _scrollPanel.RenderRows);

            for (var spriteIndex = startRow; spriteIndex < endRow; spriteIndex++)
            {
                var visibleIndex = spriteIndex - startRow;
                var rowRect = new RectangleF(
                    _scrollPanel.ContentViewportBounds.X,
                    _scrollPanel.ContentBounds.Y + visibleIndex * rowHeight,
                    _scrollPanel.ContentViewportBounds.Width,
                    Math.Max(1f, rowHeight - ROW_GAP_PIXELS * scale));

                var control = GetRowControl(usedControls++);
                ConfigureRowControl(control, rowRect, _filteredSprites[spriteIndex]);

                _scrollPanel.AddChild(control);
                control.Render(context, Sprites);
            }

            EndClip(Sprites);

            HideUnusedRows(usedControls);

            _scrollPanel.Render(context, Sprites);
        }

        void DrawEmptyListMessage(RectangleF listRect, float scale, IMyTextSurface surface)
        {
            var text = string.IsNullOrWhiteSpace(_searchText)
                ? "No sprites found"
                : "No sprites match \"" + _searchText + "\"";

            var textScale = 0.58f * scale * surface.FontSize;
            var textHeight = FormatingHelper.LineHeight(textScale, surface);

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(listRect.Center.X, listRect.Center.Y - textHeight * 0.5f),
                Color = GetThemeColor(Constants.ON_SURFACE_VARIANT),
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });
        }


        static float GetIconTargetSize(float scale)
        {
            return Math.Max(ICON_SIZE_PIXELS, ICON_SIZE_PIXELS * scale);
        }

        static float GetRowHeight(float scale)
        {
            return Math.Max(ROW_HEIGHT_PIXELS * scale, GetIconTargetSize(scale) + 8f * Math.Max(1f, scale));
        }

        RectangleControl GetRowControl(int index)
        {
            while (_rowControls.Count <= index)
            {
                var control = new RectangleControl(
                    default(RectangleF),
                    CursorType.Hand,
                    null,
                    OnSpriteClicked);

                control.CustomRender = RenderSpriteRow;
                _rowControls.Add(control);
            }

            return _rowControls[index];
        }

        void ConfigureRowControl(RectangleControl control, RectangleF rect, string spriteName)
        {
            control.SetRect(rect);
            control.SetDataContext(new SpriteRowModel(spriteName));
            control.SetCursor(CursorType.Hand);
            control.SetStyle(GetRowStyle());
            control.CustomRender = RenderSpriteRow;
            control.SetVisible(true);
        }

        void RenderSpriteRow(ControlBase entry, ControlRenderContext context, List<MySprite> sprites)
        {
            var model = entry.DataContext as SpriteRowModel;
            if (model == null || string.IsNullOrEmpty(model.SpriteName))
                return;

            var rect = entry.Bounds;
            var hovered = rect.Contains(context.CursorPosition);
            var backgroundColor = context.Style.GetPanelColor(hovered);
            var foregroundColor = context.Style.GetTextColor(hovered);

            Border.CreateSpritesFromRect(
                rect,
                sprites,
                backgroundColor,
                radiusScale: context.Scale);

            var iconTargetSize = GetIconTargetSize(context.Scale);
            var iconSize = Math.Min(
                iconTargetSize,
                Math.Max(1f, Math.Min(rect.Height, rect.Width) - 4f * Math.Max(1f, context.Scale)));
            var textScale = 0.48f * context.Scale * context.FontScale;
            var minimumTextWidth = 72f * context.Scale;
            var iconOnly = rect.Width < iconSize + 14f * context.Scale + minimumTextWidth;

            var iconRect = iconOnly
                ? new RectangleF(
                    rect.Center.X - iconSize * 0.5f,
                    rect.Center.Y - iconSize * 0.5f,
                    iconSize,
                    iconSize)
                : new RectangleF(
                    rect.X + 4f * context.Scale,
                    rect.Center.Y - iconSize * 0.5f,
                    iconSize,
                    iconSize);

            Border.CreateSpritesFromRect(
                iconRect,
                sprites,
                GetThemeColor(Constants.SURFACE_CONTAINER_LOWEST),
                radiusScale: context.Scale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = model.SpriteName,
                Position = iconRect.Center,
                Size = new Vector2(iconSize),
                Color = foregroundColor,
                Alignment = TextAlignment.CENTER
            });

            if (iconOnly)
                return;

            var textHeight = FormatingHelper.LineHeight(textScale, context.Surface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = model.SpriteName,
                Position = new Vector2(
                    iconRect.Right + 10f * context.Scale,
                    rect.Center.Y - textHeight * 0.5f),
                Color = foregroundColor,
                FontId = "White",
                Alignment = TextAlignment.LEFT,
                RotationOrScale = textScale
            });
        }

        void HideUnusedRows(int usedControls)
        {
            for (var i = usedControls; i < _rowControls.Count; i++)
                _rowControls[i].SetVisible(false);
        }

        void OnSearchChanged(string value)
        {
            _searchText = value ?? string.Empty;
            ApplyFilter();

            if (RequestRedraw != null)
                RequestRedraw();
        }

        void ApplyFilter()
        {
            _filteredSprites.Clear();

            var query = (_searchText ?? string.Empty).Trim();

            if (query.Length == 0)
            {
                _filteredSprites.AddRange(_allSprites);
                return;
            }

            for (var i = 0; i < _allSprites.Count; i++)
            {
                var sprite = _allSprites[i];
                if (sprite != null &&
                    sprite.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _filteredSprites.Add(sprite);
                }
            }
        }

        void OnSpriteClicked(object dataContext, object sender)
        {
            var model = dataContext as SpriteRowModel;
            if (model == null || string.IsNullOrEmpty(model.SpriteName))
                return;

            var callback = OnSelected;

            Dismiss();

            if (callback != null)
                callback(model.SpriteName);

            if (RequestRedraw != null)
                RequestRedraw();
        }

        void OnScrollChanged(ScrollPanel panel)
        {
            if (RequestRedraw != null)
                RequestRedraw();
        }

        ControlStyle GetSearchStyle()
        {
            if (_searchStyle == null)
                _searchStyle = Button.CreatePrimaryButtonStyle(ParentTheme);
            else
                _searchStyle.ThemeColors = ParentTheme;

            return _searchStyle;
        }

        ControlStyle GetRowStyle()
        {
            if (_rowStyle == null)
            {
                _rowStyle = ControlStyle.FromThemeRoles(
                    Constants.ON_SURFACE,
                    Constants.SURFACE_CONTAINER_LOW,
                    Constants.SURFACE_CONTAINER_HIGHEST,
                    Constants.ON_SURFACE,
                    ParentTheme);
            }
            else
            {
                _rowStyle.ThemeColors = ParentTheme;
            }

            return _rowStyle;
        }

        static void BeginClip(List<MySprite> sprites, RectangleF bounds)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.CLIP_RECT,
                Position = bounds.Position,
                Size = bounds.Size,
                Alignment = TextAlignment.LEFT
            });
        }

        static void EndClip(List<MySprite> sprites)
        {
            sprites.Add(MySprite.CreateClearClipRect());
        }

        sealed class SpriteRowModel
        {
            public SpriteRowModel(string spriteName)
            {
                SpriteName = spriteName;
            }

            public string SpriteName { get; private set; }

            public override string ToString()
            {
                return SpriteName ?? string.Empty;
            }
        }
    }
}