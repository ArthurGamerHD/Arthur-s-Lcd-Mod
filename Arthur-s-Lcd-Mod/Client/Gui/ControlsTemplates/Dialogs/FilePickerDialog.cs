using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using static LcdMod.Common.Helpers.Constants;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
{
    sealed class FilePickerDialog : Dialog
    {
        const float CARD_WIDTH_PERCENT = 0.78f;
        const float CARD_HEIGHT_PERCENT = 0.82f;
        const float MIN_CARD_WIDTH_PIXELS = 340f;
        const float MIN_CARD_HEIGHT_PIXELS = 270f;
        const float COMPACT_HEIGHT_TO_WIDTH_RATIO = 0.2f;
        const float OUTER_PADDING_PIXELS = 18f;
        const float COMPACT_OUTER_PADDING_PIXELS = 2f;
        const float INNER_PADDING_X_PIXELS = 18f;
        const float INNER_PADDING_Y_PIXELS = 14f;
        const float SPACING_PIXELS = 9f;
        const float SEARCH_HEIGHT_PIXELS = 36f;
        const float SELECT_BUTTON_WIDTH_PIXELS = 128f;
        const float SELECT_BUTTON_HEIGHT_PIXELS = 36f;
        const int NAVIGATION_HISTORY_LIMIT = 8;
        const string LOC_PICK_FILE_TITLE = MOD_PREFIX + "FilePicker_PickFile";
        const string LOC_PICK_FOLDER_TITLE = MOD_PREFIX + "FilePicker_PickFolder";
        const string LOC_ROOT = MOD_PREFIX + "FilePicker_Root";
        const string LOC_SEARCH_TITLE = MOD_PREFIX + "FilePicker_SearchTitle";
        const string LOC_SEARCH_SUBTITLE = MOD_PREFIX + "FilePicker_SearchSubtitle";
        const string LOC_SEARCH_PLACEHOLDER = MOD_PREFIX + "FilePicker_SearchPlaceholder";
        const string LOC_SELECT_FILE = MOD_PREFIX + "FilePicker_SelectFile";
        const string LOC_SELECT_FOLDER = MOD_PREFIX + "FilePicker_SelectFolder";

        readonly List<FolderModel> _roots = new List<FolderModel>();
        readonly string _title;
        readonly Action<FilePickerResult> _onSelected;
        readonly Action<string> _currentPathChanged;
        readonly string _initialPath;
        readonly string _selectButtonText;
        readonly bool _acceptSelectionOnClose;
        readonly FilePickerGrid _grid;
        readonly List<string> _backHistory = new List<string>(NAVIGATION_HISTORY_LIMIT);
        readonly List<string> _forwardHistory = new List<string>(NAVIGATION_HISTORY_LIMIT);
        bool _accepted;
        bool _historyNavigationInProgress;

        TextInput _searchInput;
        TextInputModel _searchInputModel;
        Button _selectButton;
        Button _compactCloseButton;
        bool _compactFullscreenThisFrame;
        string _searchText = string.Empty;
        string _lastHistoryPath;
        string _cachedPathSource;
        string _cachedPathText;

        public FilePickerDialog(
            IApp parentApp,
            string title,
            FilePickerMode mode,
            IEnumerable<FolderModel> roots,
            Action<FilePickerResult> onSelected,
            Action requestRedraw = null,
            Action onClosed = null,
            bool acceptSelectionOnClose = false,
            string initialPath = null,
            Action<string> currentPathChanged = null,
            Func<FilePickerResult, List<FilePickerContextAction>> contextActionsProvider = null)
            : base(parentApp)
        {
            _title = string.IsNullOrWhiteSpace(title) ? GetDefaultTitle(mode) : title;
            _onSelected = onSelected;
            _currentPathChanged = currentPathChanged;
            _initialPath = initialPath;
            _selectButtonText = LocHelper.GetLoc(mode == FilePickerMode.PickFolder ? LOC_SELECT_FOLDER : LOC_SELECT_FILE);
            _acceptSelectionOnClose = acceptSelectionOnClose;
            RequestRedraw = requestRedraw;
            OnClosed = onClosed;

            if (roots != null)
            {
                foreach (var root in roots)
                {
                    if (root != null)
                        _roots.Add(root);
                }
            }

            _grid = new FilePickerGrid(default(RectangleF), mode, _roots, initialPath);
            SetContextActionsProvider(contextActionsProvider);
            _grid.Accepted = OnGridAccepted;
            _grid.Changed = OnGridChanged;
            _lastHistoryPath = GetCurrentHistoryPath();

            OnClose = delegate
            {
                AcceptSelectionOnClose();

                if (OnClosed != null)
                    OnClosed();
                else if (RequestRedraw != null)
                    RequestRedraw();
            };
        }

        public Action RequestRedraw { get; set; }
        public Action OnClosed { get; set; }
        public bool FullscreenOnCompactSurfaces { get; set; }

        public void SetContextActionsProvider(Func<FilePickerResult, List<FilePickerContextAction>> contextActionsProvider)
        {
            _grid.ContextActionsProvider = contextActionsProvider;
        }

        protected override bool ShowCloseButton => !_compactFullscreenThisFrame;

        public void SetRoots(IEnumerable<FolderModel> roots)
        {
            ApplyRoots(roots, _initialPath, false);
            ResetNavigationHistoryToCurrentPath();
        }

        public void RefreshRoots(IEnumerable<FolderModel> roots)
        {
            var currentPath = _grid == null ? _initialPath : _grid.CurrentPath;
            ApplyRoots(roots, currentPath, true);
            _lastHistoryPath = GetCurrentHistoryPath();
        }

        void ApplyRoots(IEnumerable<FolderModel> roots, string preferredPath, bool fallbackToInitialPath)
        {
            _roots.Clear();
            if (roots != null)
            {
                foreach (var root in roots)
                {
                    if (root != null)
                        _roots.Add(root);
                }
            }

            _grid.SetRoots(_roots);
            if (!_grid.OpenPath(preferredPath) &&
                fallbackToInitialPath &&
                !string.Equals(preferredPath, _initialPath, StringComparison.OrdinalIgnoreCase))
            {
                _grid.OpenPath(_initialPath);
            }

            MarkDirty();
        }

        public void SetLoading(bool loading, string message = null)
        {
            _grid.SetLoading(loading, message);
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
            var container = EnsureContainer(viewBox);
            ConfigureHistoryShortcuts(container);

            var layoutScale = scale * fontScale;
            var compactFullscreen = FullscreenOnCompactSurfaces &&
                                    viewBox.Height / Math.Max(1f, viewBox.Width) < COMPACT_HEIGHT_TO_WIDTH_RATIO;
            _compactFullscreenThisFrame = compactFullscreen;
            var outerPadding = (compactFullscreen ? COMPACT_OUTER_PADDING_PIXELS : OUTER_PADDING_PIXELS) * scale;
            var innerPadding = compactFullscreen
                ? new Vector2(4f, 2f) * scale
                : new Vector2(INNER_PADDING_X_PIXELS, INNER_PADDING_Y_PIXELS) * scale;
            var spacing = (compactFullscreen ? 4f : SPACING_PIXELS) * scale;

            var maxCardWidth = Math.Max(1f, viewBox.Width - outerPadding * 2f);
            var maxCardHeight = Math.Max(1f, viewBox.Height - outerPadding * 2f);
            var cardWidth = compactFullscreen
                ? maxCardWidth
                : Math.Min(
                    Math.Max(MIN_CARD_WIDTH_PIXELS * scale, viewBox.Width * CARD_WIDTH_PERCENT),
                    maxCardWidth);
            var cardHeight = compactFullscreen
                ? maxCardHeight
                : Math.Min(
                    Math.Max(MIN_CARD_HEIGHT_PIXELS * scale, viewBox.Height * CARD_HEIGHT_PERCENT),
                    maxCardHeight);
            var cardRect = new RectangleF(
                viewBox.Center.X - cardWidth * 0.5f,
                viewBox.Center.Y - cardHeight * 0.5f,
                cardWidth,
                cardHeight);

            if (compactFullscreen)
            {
                BuildCompactFullscreenControls(cardRect, scale, surface);
                return;
            }

            if (_compactCloseButton != null)
                _compactCloseButton.SetVisible(false);

            RegisterDialogCard(cardRect);
            DrawBackground(surface, scale, cardRect);

            var closeSize = GetDialogCloseButtonSize(scale);
            var titleScale = 0.82f * layoutScale;
            var titleHeight = MeasureLineHeight(titleScale, surface);
            var headerHeight = Math.Max(titleHeight, closeSize.Y);

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = _title,
                Position = new Vector2(cardRect.Center.X, cardRect.Y + innerPadding.Y + (headerHeight - titleHeight) * 0.5f),
                Color = ResolveColor(ThemeResources.OnSurfaceColor),
                FontId = TextFont,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = titleScale
            });

            var pathScale = (0.42f) * layoutScale;
            var pathHeight = Math.Max((12f) * scale, MeasureLineHeight(pathScale, surface));
            var pathRect = new RectangleF(
                cardRect.X + innerPadding.X,
                cardRect.Y + innerPadding.Y + headerHeight + (spacing),
                Math.Max(1f, cardRect.Width - innerPadding.X * 2f),
                pathHeight);
            DrawPath(pathRect, pathScale);

            var searchHeight = Math.Max(SEARCH_HEIGHT_PIXELS * scale, MeasureLineHeight(0.54f * layoutScale, surface) + 16f * scale);
            var searchRect = new RectangleF(
                cardRect.X + innerPadding.X,
                pathRect.Bottom + spacing,
                Math.Max(1f, cardRect.Width - innerPadding.X * 2f),
                searchHeight);

            var footerButtonHeight = Math.Max(
                (SELECT_BUTTON_HEIGHT_PIXELS) * scale,
                MeasureLineHeight(0.54f * layoutScale, surface) + (14f) * scale);
            var footerHeight = footerButtonHeight + spacing;
            var listTop = searchRect.Bottom + spacing;
            var listBottom = cardRect.Bottom - innerPadding.Y - footerHeight;
            var listRect = new RectangleF(
                cardRect.X + innerPadding.X,
                listTop,
                Math.Max(1f, cardRect.Width - innerPadding.X * 2f),
                Math.Max(0f, listBottom - listTop));

            EnsureSearchInput(searchRect);
            ContainerControl.AddChild(_searchInput);
            _searchInput.Render(Sprites);

            _grid.CompactRows = false;
            _grid.ChromeVisible = true;
            _grid.SetRect(listRect);
            _grid.SetStyleParent(this);
            ContainerControl.AddChild(_grid);
            _grid.Render(Sprites);

            var footerY = cardRect.Bottom - innerPadding.Y - footerButtonHeight;
            var selectButtonWidth = Math.Min(Math.Max(SELECT_BUTTON_WIDTH_PIXELS * scale, 96f * scale), Math.Max(1f, cardRect.Width - innerPadding.X * 2f));
            _compactCloseButton?.SetVisible(false);

            var selectRect = new RectangleF(
                cardRect.Right - innerPadding.X - selectButtonWidth,
                footerY,
                selectButtonWidth,
                footerButtonHeight);
            EnsureSelectButton(selectRect);
            ContainerControl.AddChild(_selectButton);
            _selectButton.Render(Sprites);
        }

        void DrawBackground(IMyTextSurface surface, float scale, RectangleF cardRect)
        {
            Sprites.Add(new MySprite(
                SpriteType.TEXTURE,
                "SquareSimple",
                surface.TextureSize / 2f,
                surface.TextureSize,
                new Color(0, 0, 0, 128)));

            BorderRenderer.CreateSpritesFromRect(
                new RectangleF(cardRect.Position + 3f * scale, cardRect.Size),
                Sprites,
                ResolveColor(ThemeResources.ShadowColor),
                radiusScale: scale);

            BorderRenderer.CreateSpritesFromRect(
                cardRect,
                Sprites,
                ResolveColor(ThemeResources.SurfaceContainerHighColor),
                radiusScale: scale);
        }

        void DrawPath(RectangleF rect, float textScale)
        {
            var source = _grid == null ? string.Empty : _grid.CurrentPath;
            if (!string.Equals(_cachedPathSource, source, StringComparison.Ordinal))
            {
                _cachedPathSource = source;
                _cachedPathText = string.IsNullOrEmpty(source) ? LocHelper.GetLoc(LOC_ROOT) : source.Replace('/', '\\');
            }

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = _cachedPathText,
                Position = new Vector2(rect.X, rect.Y),
                Color = ResolveColor(ThemeResources.OnSurfaceVariantColor),
                FontId = TextFont,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = textScale
            });
        }

        void BuildCompactFullscreenControls(RectangleF rect, float scale, IMyTextSurface surface)
        {
            RegisterDialogCard(rect);
            DrawBackground(surface, scale, rect);

            if (_searchInput != null)
                _searchInput.SetVisible(false);

            var outerPadding = COMPACT_OUTER_PADDING_PIXELS * scale;
            var gap = Math.Max(2f, 4f * scale);
            var height = Math.Max(1f, rect.Height - outerPadding * 2f);
            var y = rect.Y + outerPadding;
            var closeWidth = Math.Max(24f * scale, Math.Min(height, 38f * scale));
            var selectWidth = Math.Max(54f * scale, Math.Min(rect.Width * .24f, 112f * scale));
            var closeRect = new RectangleF(rect.X + outerPadding, y, closeWidth, height);
            var selectRect = new RectangleF(rect.Right - outerPadding - selectWidth, y, selectWidth, height);
            var listRect = new RectangleF(
                closeRect.Right + gap,
                y,
                Math.Max(1f, selectRect.X - closeRect.Right - gap * 2f),
                height);

            EnsureCompactCloseButton(closeRect);
            ContainerControl.AddChild(_compactCloseButton);
            _compactCloseButton.Render(Sprites);

            _grid.CompactRows = true;
            _grid.ChromeVisible = false;
            _grid.SetRect(listRect);
            _grid.SetStyleParent(this);
            ContainerControl.AddChild(_grid);
            _grid.Render(Sprites);

            EnsureSelectButton(selectRect);
            ContainerControl.AddChild(_selectButton);
            _selectButton.Render(Sprites);
        }

        void EnsureSearchInput(RectangleF rect)
        {
            if (_searchInputModel == null)
            {
                _searchInputModel = new TextInputModel
                {
                    Title = LocHelper.GetLoc(LOC_SEARCH_TITLE),
                    Subtitle = LocHelper.GetLoc(LOC_SEARCH_SUBTITLE),
                    Placeholder = LocHelper.GetLoc(LOC_SEARCH_PLACEHOLDER),
                    ValueChanged = OnSearchChanged
                };
            }

            if (!string.Equals(_searchInputModel.Value, _searchText, StringComparison.Ordinal))
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

        void EnsureCompactCloseButton(RectangleF rect)
        {
            if (_compactCloseButton == null)
            {
                _compactCloseButton = new Button(
                    rect,
                    new ButtonModel
                    {
                        Text = string.Empty,
                        Clicked = OnCompactCloseClicked,
                        Enabled = true
                    });
            }
            else
            {
                _compactCloseButton.SetRect(rect);
            }

            var model = _compactCloseButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = string.Empty;
                model.Enabled = true;
                model.Clicked = OnCompactCloseClicked;
            }

            _compactCloseButton.TextColor = ResolveColor(ThemeResources.OnSurfaceColor);
            _compactCloseButton.BackgroundColor = ResolveColor(ThemeResources.SurfaceContainerColor);
            _compactCloseButton.BorderColor = Color.Transparent;
            _compactCloseButton.BorderThicknessPixels = 0f;
            _compactCloseButton.BorderRadiusPixels = BorderRenderer.DEFAULT_RADIUS_PIXELS;
            _compactCloseButton.CustomRender = RenderCompactCloseButton;
            _compactCloseButton.SetCursor(CursorType.Hand);
            _compactCloseButton.SetVisible(true);
            _compactCloseButton.SetEnabled(true);
            _compactCloseButton.SetStyleParent(this);
        }

        void EnsureSelectButton(RectangleF rect)
        {
            if (_selectButton == null)
                _selectButton = new Button(rect, new ButtonModel { Text = GetSelectButtonText(), Clicked = OnSelectClicked });
            else
                _selectButton.SetRect(rect);

            var model = _selectButton.DataContext as ButtonModel;
            if (model != null)
            {
                model.Text = GetSelectButtonText();
                model.Enabled = _grid.HasAcceptableSelection;
                model.Clicked = OnSelectClicked;
            }

            _selectButton.SetStyleId(_grid.HasAcceptableSelection ? "Primary" : "Disabled");
            _selectButton.SetClass("ControlBase Button");
            _selectButton.SetEnabled(_grid.HasAcceptableSelection);
            _selectButton.SetCursor(_grid.HasAcceptableSelection ? CursorType.Hand : CursorType.Default);
            _selectButton.SetVisible(true);
        }

        void RenderCompactCloseButton(ControlTemplate control, List<MySprite> sprites)
        {
            if (control == null || sprites == null)
                return;

            var rect = control.Bounds;
            var fill = control.IsPointerOver
                ? ResolveColor(ThemeResources.SurfaceContainerLowColor)
                : control.BackgroundColor;
            BorderRenderer.CreateSpritesFromRect(rect, sprites, fill, radiusScale: control.LayoutScale);

            var iconSize = Math.Max(1f, Math.Min(rect.Width, rect.Height) - 8f * control.LayoutScale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Cross",
                Position = rect.Center,
                Size = new Vector2(iconSize, iconSize),
                Color = ColorCorrection,
                Alignment = TextAlignment.CENTER
            });
        }

        void OnCompactCloseClicked(ButtonModel model, object sender)
        {
            Dismiss();
            if (OnClose != null)
                OnClose();
        }

        void OnSearchChanged(string value)
        {
            _searchText = value ?? string.Empty;
            _grid.SetFilter(_searchText);
            OnGridChanged();
        }

        void OnSelectClicked(ButtonModel model, object sender)
        {
            _grid.AcceptSelection();
        }

        void OnGridAccepted(FilePickerResult result)
        {
            _accepted = true;
            var callback = _onSelected;
            Dismiss();

            if (callback != null)
                callback(result);

            if (RequestRedraw != null)
                RequestRedraw();
        }

        void AcceptSelectionOnClose()
        {
            if (_accepted || !_acceptSelectionOnClose || _grid == null || !_grid.HasAcceptableSelection)
                return;

            var result = _grid.GetSelectionResult();
            if (result == null)
                return;

            _accepted = true;
            var callback = _onSelected;
            if (callback != null)
                callback(result);
        }

        void OnGridChanged()
        {
            TrackNavigationHistory();

            if (_currentPathChanged != null && (_grid == null || !_grid.IsLoading))
                _currentPathChanged(_grid == null ? string.Empty : _grid.CurrentPath);

            MarkDirty();
            if (RequestRedraw != null)
                RequestRedraw();
        }

        void ConfigureHistoryShortcuts(ControlTemplate control)
        {
            if (control == null)
                return;

            var historyEnabled = _grid != null && !_grid.IsLoading;
            control.OnBackClick = historyEnabled && _backHistory.Count > 0
                ? (Action<object, object>)OnHistoryBackClicked
                : null;
            control.OnForwardClick = historyEnabled && _forwardHistory.Count > 0
                ? (Action<object, object>)OnHistoryForwardClicked
                : null;
        }

        void OnHistoryBackClicked(object dataContext, object sender)
        {
            NavigateHistory(_backHistory, _forwardHistory);
        }

        void OnHistoryForwardClicked(object dataContext, object sender)
        {
            NavigateHistory(_forwardHistory, _backHistory);
        }

        bool NavigateHistory(List<string> source, List<string> destination)
        {
            if (_grid == null || _grid.IsLoading || source == null || source.Count == 0)
                return false;

            var currentPath = GetCurrentHistoryPath();

            while (source.Count > 0)
            {
                var targetPath = PopHistory(source);
                if (SameHistoryPath(targetPath, currentPath))
                    continue;

                _historyNavigationInProgress = true;
                try
                {
                    if (!_grid.NavigateToPath(targetPath))
                        continue;
                }
                finally
                {
                    _historyNavigationInProgress = false;
                }

                PushHistory(destination, currentPath);
                _lastHistoryPath = GetCurrentHistoryPath();
                ConfigureHistoryShortcuts(ContainerControl);
                MarkDirty();
                if (RequestRedraw != null)
                    RequestRedraw();
                return true;
            }

            MarkDirty();
            ConfigureHistoryShortcuts(ContainerControl);
            if (RequestRedraw != null)
                RequestRedraw();
            return false;
        }

        void TrackNavigationHistory()
        {
            var currentPath = GetCurrentHistoryPath();
            if (_lastHistoryPath == null)
            {
                _lastHistoryPath = currentPath;
                return;
            }

            if (SameHistoryPath(_lastHistoryPath, currentPath))
                return;

            if (!_historyNavigationInProgress)
            {
                PushHistory(_backHistory, _lastHistoryPath);
                _forwardHistory.Clear();
            }

            _lastHistoryPath = currentPath;
            ConfigureHistoryShortcuts(ContainerControl);
        }

        void ResetNavigationHistoryToCurrentPath()
        {
            _backHistory.Clear();
            _forwardHistory.Clear();
            _lastHistoryPath = GetCurrentHistoryPath();
            ConfigureHistoryShortcuts(ContainerControl);
            MarkDirty();
        }

        string GetCurrentHistoryPath()
        {
            return NormalizeHistoryPath(_grid == null ? string.Empty : _grid.CurrentPath);
        }

        static void PushHistory(List<string> history, string path)
        {
            if (history == null)
                return;

            path = NormalizeHistoryPath(path);
            if (history.Count > 0 && SameHistoryPath(history[history.Count - 1], path))
                return;

            history.Add(path);
            while (history.Count > NAVIGATION_HISTORY_LIMIT)
                history.RemoveAt(0);
        }

        static string PopHistory(List<string> history)
        {
            var index = history.Count - 1;
            var path = history[index];
            history.RemoveAt(index);
            return NormalizeHistoryPath(path);
        }

        static bool SameHistoryPath(string left, string right)
        {
            return string.Equals(
                NormalizeHistoryPath(left),
                NormalizeHistoryPath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeHistoryPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Replace('\\', '/').Trim('/');
        }

        string GetSelectButtonText()
        {
            return _selectButtonText;
        }

        static string GetDefaultTitle(FilePickerMode mode)
        {
            return LocHelper.GetLoc(mode == FilePickerMode.PickFolder ? LOC_PICK_FOLDER_TITLE : LOC_PICK_FILE_TITLE);
        }
    }
}
