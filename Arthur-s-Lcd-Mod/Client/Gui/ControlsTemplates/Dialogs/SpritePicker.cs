using System;
using System.Collections.Generic;
using LcdMod.Client;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Input;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
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
        const float APPLY_BUTTON_WIDTH_PIXELS = 116f;
        const float APPLY_BUTTON_HEIGHT_PIXELS = 36f;
        const float ROW_HEIGHT_PIXELS = 40f;
        const float ROW_GAP_PIXELS = 3f;
        const float ICON_SIZE_PIXELS = 32f;

        readonly List<string> _selectedSpriteNames = new List<string>();
        readonly HashSet<string> _selectedSpriteNameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, SpriteRowModel> _spriteModelsByName =
            new Dictionary<string, SpriteRowModel>(StringComparer.OrdinalIgnoreCase);
        readonly List<SpriteRowModel> _allSprites = new List<SpriteRowModel>();
        readonly List<SpriteRowModel> _filteredSprites = new List<SpriteRowModel>();
        readonly ScrollPanel _scrollPanel = new ScrollPanel();
        readonly VirtualizedStackPanel<SpriteRowModel> _listPanel = new VirtualizedStackPanel<SpriteRowModel>();

        TextInput _searchInput;
        TextInputModel _searchInputModel;
        Button _applyButton;


        bool _spritesLoaded;
        bool _childRenderQueued;
        string _searchText = string.Empty;
        string _selectionAnchorSpriteName = string.Empty;
        InteractiveSurfaceScript _owner;

        public SpritePicker(IApp parentApp)
            : this(parentApp, (Action<string>)null, null, null)
        {
        }

        public SpritePicker(IApp parentApp, Action<string> onSelected, Action requestRedraw = null, Action onClosed = null)
            : base(parentApp)
        {
            OnSelected = onSelected;
            RequestRedraw = requestRedraw;
            OnClosed = onClosed;

            OnClose = delegate
            {
                if (OnClosed != null)
                    OnClosed();
                else if (RequestRedraw != null)
                    RequestRedraw();
            };

            _scrollPanel.ManualScrollInertiaEnabled = false;
            _scrollPanel.ScrollChanged = OnScrollChanged;
            _listPanel.CreateControl = CreateRowControl;
            _listPanel.BindControl = BindRowControl;
        }

        public SpritePicker(
            IApp parentApp,
            Action<string[]> onApplied,
            IEnumerable<string> selectedSprites,
            Action requestRedraw = null,
            Action onClosed = null)
            : this(parentApp, null, requestRedraw, onClosed)
        {
            AllowMultiSelection = true;
            OnApplied = onApplied;
            SetSelectedSprites(selectedSprites);
        }

        public Action<string> OnSelected { get; set; }

        public Action<string[]> OnApplied { get; set; }

        public bool AllowMultiSelection { get; set; }

        public Action RequestRedraw { get; set; }

        public Action OnClosed { get; set; }

        public void Show(Action<string> onSelected, Action requestRedraw = null)
        {
            OnSelected = onSelected;

            if (requestRedraw != null)
                RequestRedraw = requestRedraw;
        }

        public void ReloadSprites()
        {
            _spritesLoaded = false;
            _spriteModelsByName.Clear();
            _allSprites.Clear();
            _filteredSprites.Clear();
        }

        protected override void BuildDialogControls(
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
            _owner = owner;
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
                Color = ResolveColor(ThemeResources.OnSurfaceColor),
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
            var footerButtonHeight = Math.Max(APPLY_BUTTON_HEIGHT_PIXELS * scale, FormatingHelper.LineHeight(0.54f * layoutScale, surface) + 14f * scale);
            var footerHeight = AllowMultiSelection ? footerButtonHeight + spacing : 0f;
            var listBottom = cardRect.Bottom - innerPadding.Y - footerHeight;
            var listRect = new RectangleF(
                cardRect.X + innerPadding.X,
                listTop,
                Math.Max(1f, cardRect.Width - innerPadding.X * 2f),
                Math.Max(0f, listBottom - listTop));

            EnsureSearchInput(searchRect);
            ContainerControl.AddChild(_searchInput);
            _searchInput.Render(Sprites);

            RenderSpriteList(listRect, scale, surface);

            if (AllowMultiSelection)
            {
                var applyButtonWidth = Math.Min(
                    Math.Max(APPLY_BUTTON_WIDTH_PIXELS * scale, 92f * scale),
                    Math.Max(1f, cardRect.Width - innerPadding.X * 2f));
                var applyRect = new RectangleF(
                    cardRect.Right - innerPadding.X - applyButtonWidth,
                    cardRect.Bottom - innerPadding.Y - footerButtonHeight,
                    applyButtonWidth,
                    footerButtonHeight);

                EnsureApplyButton(applyRect);
                ContainerControl.AddChild(_applyButton);
                _applyButton.Render(Sprites);
            }
            else if (_applyButton != null)
            {
                _applyButton.SetVisible(false);
            }
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
                ResolveColor(ThemeResources.ShadowColor),
                radiusScale: scale);

            Border.CreateSpritesFromRect(
                cardRect,
                Sprites,
                ResolveColor(ThemeResources.SurfaceContainerHighColor),
                radiusScale: scale);
        }

        void EnsureSpritesLoaded(IMyTextSurface surface)
        {
            if (_spritesLoaded)
                return;

            _allSprites.Clear();

            var seenSprites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var registeredSprites = new List<string>();
            TextureHelper.GetRegisteredSpriteNames(registeredSprites);
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

                _allSprites.Add(GetOrCreateSpriteModel(sprite));
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
            _searchInput.SetStyleId("Primary");
            _searchInput.SetCursor(CursorType.Hand);
            _searchInput.SetVisible(true);
        }

        void RenderSpriteList(
            RectangleF listRect,
            float scale,
            IMyTextSurface surface)
        {
            if (listRect.Height <= 1f)
                return;

            Border.CreateSpritesFromRect(
                listRect,
                Sprites,
                ResolveColor(ThemeResources.SurfaceContainerColor),
                radiusScale: scale);

            var rowHeight = GetRowHeight(scale);
            var scrollerWidth = _scrollPanel.AutomaticScrollerWidthPixels * scale;

            _scrollPanel.SetContent(_listPanel);
            _listPanel.ItemsSource = _filteredSprites;
            _listPanel.RowHeight = rowHeight;
            _listPanel.Gap = ROW_GAP_PIXELS * scale;
            _scrollPanel.ConfigureAutomatic(
                listRect,
                scrollerWidth,
                rowHeight,
                0f);

            _scrollPanel.SetScrollBarColors(
                ResolveColor(ThemeResources.SurfaceContainerHighestColor),
                ResolveColor(ThemeResources.OnSurfaceColor));

            _scrollPanel.SetVisible(true);
            ContainerControl.AddChild(_scrollPanel);

            if (_filteredSprites.Count == 0)
            {
                DrawEmptyListMessage(listRect, scale, surface);
                _scrollPanel.Render(Sprites);
                return;
            }

            _scrollPanel.Render(Sprites);
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
                Color = ResolveColor(ThemeResources.OnSurfaceVariantColor),
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

        ControlTemplate CreateRowControl(SpriteRowModel model)
        {
            var control = new RectangleControl(
                default(RectangleF),
                CursorType.Hand,
                model,
                OnSpriteClicked);

            control.CustomRender = RenderSpriteRow;
            control.SetClass("ControlBase Button Row");
            return control;
        }

        void BindRowControl(ControlTemplate control, SpriteRowModel model, int index)
        {
            if (control == null || model == null)
                return;

            model.IsSelected = AllowMultiSelection && _selectedSpriteNameSet.Contains(model.SpriteName);
            control.SetDataContext(model);
            control.SetCursor(CursorType.Hand);
            control.SetClass(model.IsSelected ? "ControlBase Button Row Selected" : "ControlBase Button Row");
            control.SetStyleId(model.IsSelected ? "Primary" : null);
            control.CustomRender = RenderSpriteRow;
            control.SetVisible(true);
        }

        void RenderSpriteRow(ControlTemplate entry, List<MySprite> sprites)
        {
            var model = entry.DataContext as SpriteRowModel;
            if (model == null || string.IsNullOrEmpty(model.SpriteName))
                return;

            var rect = entry.Bounds;
            if (rect.Height > ROW_GAP_PIXELS * entry.LayoutScale)
            {
                rect = new RectangleF(
                    rect.X,
                    rect.Y,
                    rect.Width,
                    Math.Max(1f, rect.Height - ROW_GAP_PIXELS * entry.LayoutScale));
            }
            var hovered = entry.IsMouseOver;
            var selected = AllowMultiSelection && model.IsSelected;
            var backgroundColor = selected
                ? ResolveColor(ThemeResources.AccentContainerColor)
                : entry.BackgroundColor;
            var foregroundColor = selected
                ? ResolveColor(ThemeResources.OnAccentContainerColor)
                : entry.TextColor;

            Border.CreateSpritesFromRect(
                rect,
                sprites,
                backgroundColor,
                radiusScale: entry.LayoutScale);

            var iconTargetSize = GetIconTargetSize(entry.LayoutScale);
            var iconSize = Math.Min(
                iconTargetSize,
                Math.Max(1f, Math.Min(rect.Height, rect.Width) - 4f * Math.Max(1f, entry.LayoutScale)));
            var textScale = 0.48f * entry.LayoutScale * entry.FontScale;
            var minimumTextWidth = 72f * entry.LayoutScale;
            var iconOnly = rect.Width < iconSize + 14f * entry.LayoutScale + minimumTextWidth;

            var iconRect = iconOnly
                ? new RectangleF(
                    rect.Center.X - iconSize * 0.5f,
                    rect.Center.Y - iconSize * 0.5f,
                    iconSize,
                    iconSize)
                : new RectangleF(
                    rect.X + 4f * entry.LayoutScale,
                    rect.Center.Y - iconSize * 0.5f,
                    iconSize,
                    iconSize);

            Border.CreateSpritesFromRect(
                iconRect,
                sprites,
                ResolveColor(ThemeResources.SurfaceContainerLowestColor),
                radiusScale: entry.LayoutScale);

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

            var textHeight = FormatingHelper.LineHeight(textScale, entry.TextSurface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = model.SpriteName,
                Position = new Vector2(
                    iconRect.Right + 10f * entry.LayoutScale,
                    rect.Center.Y - textHeight * 0.5f),
                Color = foregroundColor,
                FontId = "White",
                Alignment = TextAlignment.LEFT,
                RotationOrScale = textScale
            });
        }

        void OnSearchChanged(string value)
        {
            _searchText = value ?? string.Empty;
            ApplyFilter();
            QueueChildRenderIfNeeded();
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
                var model = _allSprites[i];
                if (model != null &&
                    model.SpriteName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _filteredSprites.Add(model);
                }
            }
        }

        void OnSpriteClicked(object dataContext, object sender)
        {
            var model = dataContext as SpriteRowModel;
            if (model == null || string.IsNullOrEmpty(model.SpriteName))
                return;

            if (AllowMultiSelection)
            {
                if (IsShiftPressed())
                {
                    SelectSpriteRange(model.SpriteName);
                }
                else if (IsCtrlPressed())
                {
                    ToggleSelectedSprite(model.SpriteName);
                    _selectionAnchorSpriteName = model.SpriteName;
                }
                else
                {
                    SetSelectedSprites(new[] { model.SpriteName });
                    _selectionAnchorSpriteName = model.SpriteName;
                }

                QueueChildRenderIfNeeded();

                return;
            }

            var callback = OnSelected;

            Dismiss();

            if (callback != null)
                callback(model.SpriteName);

            if (RequestRedraw != null)
                RequestRedraw();
        }

        void OnApplyClicked(ButtonModel model, object sender)
        {
            var callback = OnApplied;
            var selectedSprites = _selectedSpriteNames.ToArray();

            Dismiss();

            if (callback != null)
                callback(selectedSprites);

            if (RequestRedraw != null)
                RequestRedraw();
        }

        void OnScrollChanged(ScrollPanel panel)
        {
            QueueChildRenderIfNeeded();
        }

        void QueueChildRenderIfNeeded()
        {
            if (_childRenderQueued || !CanSelfRender())
                return;

            _childRenderQueued = true;
            LcdModClientComponent.RunNextFrame.Add(RunQueuedChildRender);
        }

        void RunQueuedChildRender()
        {
            _childRenderQueued = false;

            try
            {
                if (!CanSelfRender())
                    return;

                _owner.RenderSprites();
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, GetType());
            }
        }

        bool CanSelfRender()
        {
            try
            {
                return _owner != null &&
                       _owner.Surface != null &&
                       _owner.Block != null &&
                       !_owner.Block.MarkedForClose &&
                       !_owner.Block.Closed &&
                       !Dismissed;
            }
            catch
            {
                return false;
            }
        }

        void EnsureApplyButton(RectangleF rect)
        {
            if (_applyButton == null)
                _applyButton = new Button(rect, new ButtonModel { Text = "Apply", Clicked = OnApplyClicked });
            else
                _applyButton.SetRect(rect);

            var model = _applyButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = "Apply";
                model.Enabled = true;
                model.Clicked = OnApplyClicked;
            }

            _applyButton.SetStyleId("Primary");
            _applyButton.SetClass("ControlBase Button");
            _applyButton.CustomRender = RenderTextButton;
            _applyButton.SetCursor(CursorType.Hand);
            _applyButton.SetVisible(true);
        }

        void RenderTextButton(ControlTemplate control, List<MySprite> sprites)
        {
            var rect = control.Bounds;
            var buttonModel = control.DataContext as ButtonModel;
            var enabled = buttonModel == null || buttonModel.Enabled;
            var hovered = enabled && rect.Contains(new Vector2(float.NaN, float.NaN));
            Border.CreateSpritesFromRect(rect, sprites, control.BackgroundColor, radiusScale: control.LayoutScale);

            var text = buttonModel == null ? string.Empty : buttonModel.Text;
            var textScale = 0.54f * control.LayoutScale * control.FontScale;
            var availableWidth = Math.Max(0f, rect.Width - 12f * control.LayoutScale);
            var trimmed = TrimText(text, availableWidth, textScale, control.TextSurface);
            var textHeight = FormatingHelper.LineHeight(textScale, control.TextSurface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = trimmed,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textHeight * 0.5f),
                Color = control.TextColor,
                FontId = "White",
                RotationOrScale = textScale,
                Alignment = TextAlignment.CENTER
            });
        }

        static string TrimText(string text, float width, float scale, IMyTextSurface surface)
        {
            if (string.IsNullOrEmpty(text) || width <= 0f || surface == null)
                return string.Empty;

            var size = FormatingHelper.GetSizeInPixel(text, "White", scale, surface);
            if (size.X <= width)
                return text;

            return FormatingHelper.TrimName(text, Math.Max(1, (int)(text.Length * width / Math.Max(1f, size.X))));
        }

        void SetSelectedSprites(IEnumerable<string> spriteNames)
        {
            _selectedSpriteNames.Clear();
            _selectedSpriteNameSet.Clear();

            if (spriteNames == null)
                return;

            foreach (var spriteName in spriteNames)
                AddSelectedSprite(spriteName);
        }

        void ToggleSelectedSprite(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName))
                return;

            var normalized = spriteName.Trim();
            if (_selectedSpriteNameSet.Contains(normalized))
            {
                _selectedSpriteNameSet.Remove(normalized);
                for (var i = _selectedSpriteNames.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_selectedSpriteNames[i], normalized, StringComparison.OrdinalIgnoreCase))
                        _selectedSpriteNames.RemoveAt(i);
                }

                return;
            }

            AddSelectedSprite(normalized);
        }

        void AddSelectedSprite(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName))
                return;

            var normalized = spriteName.Trim();
            if (!_selectedSpriteNameSet.Add(normalized))
                return;

            _selectedSpriteNames.Add(normalized);
        }

        void SelectSpriteRange(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName))
                return;

            if (string.IsNullOrWhiteSpace(_selectionAnchorSpriteName))
            {
                SetSelectedSprites(new[] { spriteName });
                _selectionAnchorSpriteName = spriteName;
                return;
            }

            var anchorIndex = FindFilteredSpriteIndex(_selectionAnchorSpriteName);
            var targetIndex = FindFilteredSpriteIndex(spriteName);
            if (anchorIndex < 0 || targetIndex < 0)
            {
                SetSelectedSprites(new[] { spriteName });
                _selectionAnchorSpriteName = spriteName;
                return;
            }

            var start = Math.Min(anchorIndex, targetIndex);
            var end = Math.Max(anchorIndex, targetIndex);
            var range = new List<string>(end - start + 1);
            for (var i = start; i <= end; i++)
            {
                var model = _filteredSprites[i];
                if (model != null && !string.IsNullOrWhiteSpace(model.SpriteName))
                    range.Add(model.SpriteName);
            }

            SetSelectedSprites(range);
        }

        int FindFilteredSpriteIndex(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName))
                return -1;

            for (var i = 0; i < _filteredSprites.Count; i++)
            {
                var model = _filteredSprites[i];
                if (model != null &&
                    string.Equals(model.SpriteName, spriteName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        static bool IsCtrlPressed()
        {
            var input = MyAPIGateway.Input;
            return input != null &&
                   (input.IsKeyPress(MyKeys.Control) ||
                    input.IsKeyPress(MyKeys.LeftControl) ||
                    input.IsKeyPress(MyKeys.RightControl));
        }

        static bool IsShiftPressed()
        {
            var input = MyAPIGateway.Input;
            return input != null &&
                   (input.IsKeyPress(MyKeys.Shift) ||
                    input.IsKeyPress(MyKeys.LeftShift) ||
                    input.IsKeyPress(MyKeys.RightShift));
        }

        SpriteRowModel GetOrCreateSpriteModel(string spriteName)
        {
            SpriteRowModel model;
            if (_spriteModelsByName.TryGetValue(spriteName, out model))
                return model;

            model = new SpriteRowModel(spriteName);
            _spriteModelsByName.Add(spriteName, model);
            return model;
        }

        sealed class SpriteRowModel
        {
            public SpriteRowModel(string spriteName)
            {
                SpriteName = spriteName;
            }

            public string SpriteName { get; private set; }

            public bool IsSelected { get; set; }

            public override string ToString()
            {
                return SpriteName ?? string.Empty;
            }
        }
    }
}
