#if EXPERIMENTAL
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Inputs;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
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
        const float OUTER_PADDING_PIXELS = 18f;
        const float INNER_PADDING_X_PIXELS = 18f;
        const float INNER_PADDING_Y_PIXELS = 14f;
        const float SPACING_PIXELS = 9f;
        const float SEARCH_HEIGHT_PIXELS = 36f;
        const float SELECT_BUTTON_WIDTH_PIXELS = 128f;
        const float SELECT_BUTTON_HEIGHT_PIXELS = 36f;

        readonly List<FolderModel> _roots = new List<FolderModel>();
        readonly FilePickerMode _mode;
        readonly string _title;
        readonly Action<FilePickerResult> _onSelected;
        readonly Action<string> _currentPathChanged;
        readonly string _initialPath;
        readonly bool _acceptSelectionOnClose;
        readonly FilePickerGrid _grid;
        bool _accepted;

        TextInput _searchInput;
        TextInputModel _searchInputModel;
        Button _selectButton;
        string _searchText = string.Empty;

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
            Action<string> currentPathChanged = null)
            : base(parentApp)
        {
            _title = string.IsNullOrWhiteSpace(title) ? GetDefaultTitle(mode) : title;
            _mode = mode;
            _onSelected = onSelected;
            _currentPathChanged = currentPathChanged;
            _initialPath = initialPath;
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
            _grid.Accepted = OnGridAccepted;
            _grid.Changed = OnGridChanged;

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

        public void SetRoots(IEnumerable<FolderModel> roots)
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
            _grid.OpenPath(_initialPath);
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
            EnsureContainer(viewBox);
            ContainerControl.ClearChildren();

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
            var titleHeight = MeasureLineHeight(titleScale, surface);
            var closeSize = GetDialogCloseButtonSize(scale);
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

            var pathScale = 0.42f * layoutScale;
            var pathHeight = Math.Max(12f * scale, MeasureLineHeight(pathScale, surface));
            var pathRect = new RectangleF(
                cardRect.X + innerPadding.X,
                cardRect.Y + innerPadding.Y + headerHeight + spacing,
                Math.Max(1f, cardRect.Width - innerPadding.X * 2f),
                pathHeight);
            DrawPath(pathRect, pathScale);

            var searchHeight = Math.Max(
                SEARCH_HEIGHT_PIXELS * scale,
                MeasureLineHeight(0.54f * layoutScale, surface) + 16f * scale);
            var searchRect = new RectangleF(
                cardRect.X + innerPadding.X,
                pathRect.Bottom + spacing,
                Math.Max(1f, cardRect.Width - innerPadding.X * 2f),
                searchHeight);

            var footerButtonHeight = Math.Max(
                SELECT_BUTTON_HEIGHT_PIXELS * scale,
                MeasureLineHeight(0.54f * layoutScale, surface) + 14f * scale);
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

            _grid.SetRect(listRect);
            _grid.SetStyleParent(this);
            ContainerControl.AddChild(_grid);
            _grid.Render(Sprites);

            var selectButtonWidth = Math.Min(
                Math.Max(SELECT_BUTTON_WIDTH_PIXELS * scale, 96f * scale),
                Math.Max(1f, cardRect.Width - innerPadding.X * 2f));
            var selectRect = new RectangleF(
                cardRect.Right - innerPadding.X - selectButtonWidth,
                cardRect.Bottom - innerPadding.Y - footerButtonHeight,
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
            var path = string.IsNullOrEmpty(_grid.CurrentPath) ? "Root" : _grid.CurrentPath.Replace('/', '\\');
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = path,
                Position = new Vector2(rect.X, rect.Y),
                Color = ResolveColor(ThemeResources.OnSurfaceVariantColor),
                FontId = TextFont,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = textScale
            });
        }

        void EnsureSearchInput(RectangleF rect)
        {
            if (_searchInputModel == null)
            {
                _searchInputModel = new TextInputModel
                {
                    Title = "Search File",
                    Subtitle = "Filter files and folders in the current folder",
                    Placeholder = "Search files",
                    ValueChanged = OnSearchChanged
                };
            }

            _searchInputModel.Title = "Search File";
            _searchInputModel.Subtitle = "Filter files and folders in the current folder";
            _searchInputModel.Placeholder = "Search files";
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
            if (_currentPathChanged != null && (_grid == null || !_grid.IsLoading))
                _currentPathChanged(_grid == null ? string.Empty : _grid.CurrentPath);

            MarkDirty();
            if (RequestRedraw != null)
                RequestRedraw();
        }

        string GetSelectButtonText()
        {
            return _mode == FilePickerMode.PickFolder ? "Select Folder" : "Select File";
        }

        static string GetDefaultTitle(FilePickerMode mode)
        {
            return mode == FilePickerMode.PickFolder ? "Pick folder" : "Pick file";
        }
    }
}
#endif
