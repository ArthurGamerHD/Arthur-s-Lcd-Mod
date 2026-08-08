using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Utility;
using static LcdMod.Common.Helpers.Constants;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Gui.ControlsTemplates.Dialogs
{
    sealed class FilePickerGrid : RectangleControl
    {
        const float ROW_HEIGHT_PIXELS = 42f;
        const float ROW_GAP_PIXELS = 3f;
        const float ICON_SIZE_PIXELS = 30f;
        const float SCROLLER_WIDTH_PIXELS = 10f;
        const string LOC_LOADING = MOD_PREFIX + "FilePicker_Loading";
        const string LOC_NO_FILES_FOUND = MOD_PREFIX + "FilePicker_NoFilesFound";
        const string LOC_NO_FILES_MATCH_FORMAT = MOD_PREFIX + "FilePicker_NoFilesMatchFormat";
        const string LOC_BACK_TO_ROOTS = MOD_PREFIX + "FilePicker_BackToRoots";

        readonly List<FolderModel> _roots = new List<FolderModel>();
        readonly List<FilePickerEntryModel> _items = new List<FilePickerEntryModel>();
        readonly Dictionary<FolderModel, FolderControlModel> _folderEntryModels = new Dictionary<FolderModel, FolderControlModel>(ReferenceEqualityComparer<FolderModel>.Instance);
        readonly Dictionary<FileModel, FileControlModel> _fileEntryModels = new Dictionary<FileModel, FileControlModel>(ReferenceEqualityComparer<FileModel>.Instance);
        readonly Dictionary<FolderModel, FolderControlModel> _upEntryModels = new Dictionary<FolderModel, FolderControlModel>(ReferenceEqualityComparer<FolderModel>.Instance);
        readonly FolderControlModel _rootsUpEntryModel = new FolderControlModel { IsUpEntry = true };
        readonly ScrollPanel _scrollPanel = new ScrollPanel();
        readonly VirtualizedStackPanel<FilePickerEntryModel> _listPanel = new VirtualizedStackPanel<FilePickerEntryModel>();
        readonly List<FilePickerContextAction> _contextActions = new List<FilePickerContextAction>();
        readonly List<Button> _contextButtons = new List<Button>();
        Vector2 _contextMenuPosition;
        bool _contextMenuOpen;

        string _filter = string.Empty;
        bool _loadingFrameQueued;

        sealed class ContextMenuButtonModel : ButtonModel
        {
            public int ActionIndex;
        }

        public FilePickerGrid(RectangleF rect, FilePickerMode mode, IEnumerable<FolderModel> roots, string initialPath = null)
            : base(rect, CursorType.Default)
        {
            Mode = mode;
            _scrollPanel.ManualScrollInertiaEnabled = false;
            _scrollPanel.ScrollChanged = OnScrollChanged;
            _scrollPanel.SetContent(_listPanel);
            _listPanel.CreateControl = CreateRowControl;
            _listPanel.BindControl = BindRowControl;
            AddChild(_scrollPanel);
            SetRoots(roots);
            OpenPath(initialPath);
        }

        public FilePickerMode Mode { get; private set; }
        public FolderModel CurrentFolder { get; private set; }
        public FolderModel SelectedFolder { get; private set; }
        public FileModel SelectedFile { get; private set; }
        public Action<FilePickerResult> Accepted { get; set; }
        public Action Changed { get; set; }
        public Func<FilePickerResult, List<FilePickerContextAction>> ContextActionsProvider { get; set; }
        public bool IsLoading { get; private set; }
        public string LoadingMessage { get; private set; }
        public bool CompactRows { get; set; }
        public bool ChromeVisible { get; set; } = true;

        public string CurrentPath =>
            CurrentFolder == null || string.IsNullOrEmpty(CurrentFolder.FullPath)
                ? string.Empty
                : CurrentFolder.FullPath;

        public void SetRoots(IEnumerable<FolderModel> roots)
        {
            _roots.Clear();
            _folderEntryModels.Clear();
            _fileEntryModels.Clear();
            _upEntryModels.Clear();
            if (roots != null)
            {
                foreach (var root in roots)
                {
                    if (root == null)
                        continue;

                    PrepareFolder(root, null, root.Name);
                    _roots.Add(root);
                }
            }

            CurrentFolder = null;
            SelectedFolder = null;
            SelectedFile = null;
            RebuildItems(resetScroll: true);
        }

        public void SetLoading(bool loading, string message = null)
        {
            var nextMessage = string.IsNullOrWhiteSpace(message) ? LocHelper.GetLoc(LOC_LOADING) : message;
            if (IsLoading == loading && string.Equals(LoadingMessage, nextMessage, StringComparison.Ordinal))
                return;

            IsLoading = loading;
            LoadingMessage = nextMessage;
            _scrollPanel.SetEnabled(!loading);
            _scrollPanel.SetCursor(CursorType.Default);
            if (loading)
                CloseContextMenu();
            MarkDirty();
            NotifyChanged();
        }

        public bool OpenPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var folder = FindFolderByPath(path);
            if (folder == null)
                return false;

            if (ReferenceEquals(CurrentFolder, folder) && SelectedFolder == null && SelectedFile == null)
                return true;

            CurrentFolder = folder;
            SelectedFolder = null;
            SelectedFile = null;
            RebuildItems(resetScroll: true);
            NotifyChanged();
            return true;
        }

        public bool NavigateToPath(string path)
        {
            if (IsLoading)
                return false;

            CloseContextMenu();
            return string.IsNullOrWhiteSpace(path)
                ? OpenFolder(null)
                : OpenPath(path);
        }

        public void SetFilter(string filter)
        {
            var next = filter ?? string.Empty;
            if (string.Equals(_filter, next, StringComparison.Ordinal))
                return;

            _filter = next;
            SelectedFolder = null;
            SelectedFile = null;
            RebuildItems(resetScroll: true);
            NotifyChanged();
        }

        public bool HasAcceptableSelection
        {
            get
            {
                if (IsLoading)
                    return false;

                if (Mode == FilePickerMode.PickFile)
                    return SelectedFile != null;

                return SelectedFolder != null || CurrentFolder != null;
            }
        }

        public void AcceptSelection()
        {
            if (IsLoading)
                return;

            var callback = Accepted;
            if (callback == null)
                return;

            var result = GetSelectionResult();
            if (result != null)
                callback(result);
        }

        public FilePickerResult GetSelectionResult()
        {
            if (Mode == FilePickerMode.PickFile)
                return SelectedFile == null ? null : CreateFileResult(SelectedFile);

            var folder = SelectedFolder ?? CurrentFolder;
            return folder == null ? null : CreateFolderResult(folder);
        }

        internal void OnFolderControlClicked(FolderControlModel model, object sender)
        {
            CloseContextMenu();
            if (IsLoading)
                return;

            if (model == null)
                return;

            if (model.IsUpEntry && model.Folder == null)
            {
                OpenFolder(null);
                return;
            }

            if (model.Folder == null)
                return;

            if (ReferenceEquals(SelectedFolder, model.Folder))
            {
                OpenFolder(model.Folder);
                return;
            }

            SelectedFolder = model.Folder;
            SelectedFile = null;
            RefreshSelectionFlags();
            NotifyChanged();
        }

        internal void OnFileControlClicked(FileControlModel model, object sender)
        {
            CloseContextMenu();
            if (IsLoading)
                return;

            if (model == null || model.File == null)
                return;

            if (ReferenceEquals(SelectedFile, model.File) && Mode == FilePickerMode.PickFile)
            {
                AcceptFile(model.File);
                return;
            }

            SelectedFile = model.File;
            SelectedFolder = null;
            RefreshSelectionFlags();
            NotifyChanged();
        }

        internal bool OnFolderControlSecondaryClicked(FolderControlModel model, RectangleF bounds, Vector2 clickPosition, object sender)
        {
            if (IsLoading)
                return false;

            if (model == null || model.IsUpEntry || model.Folder == null)
                return false;

            SelectedFolder = model.Folder;
            SelectedFile = null;
            RefreshSelectionFlags();
            return OpenContextMenu(CreateFolderResult(model.Folder), bounds, clickPosition);
        }

        internal bool OnFileControlSecondaryClicked(FileControlModel model, RectangleF bounds, Vector2 clickPosition, object sender)
        {
            if (IsLoading)
                return false;

            if (model == null || model.File == null)
                return false;

            SelectedFile = model.File;
            SelectedFolder = null;
            RefreshSelectionFlags();
            return OpenContextMenu(CreateFileResult(model.File), bounds, clickPosition);
        }

        internal void RenderEntry(ControlTemplate control, FilePickerEntryModel model, List<MySprite> sprites)
        {
            if (control == null || model == null || sprites == null)
                return;

            var rect = control.Bounds;
            if (rect.Height > ROW_GAP_PIXELS * control.LayoutScale)
            {
                rect = new RectangleF(
                    rect.X,
                    rect.Y,
                    rect.Width,
                    Math.Max(1f, rect.Height - ROW_GAP_PIXELS * control.LayoutScale));
            }

            var backgroundColor = control.BackgroundColor;
            var foregroundColor = control.TextColor;
            var secondaryColor = control.TextColor;

            BorderRenderer.CreateSpritesFromRect(
                rect,
                sprites,
                backgroundColor,
                radiusScale: control.LayoutScale);

            if (CompactRows)
            {
                var compactIconSize = Math.Min(
                    Math.Max(10f * control.LayoutScale, 14f * control.LayoutScale * control.FontScale),
                    Math.Max(1f, Math.Min(rect.Height, rect.Width) - 6f * Math.Max(1f, control.LayoutScale)));
                var compactIconRect = new RectangleF(
                    rect.X + 5f * control.LayoutScale,
                    rect.Center.Y - compactIconSize * 0.5f,
                    compactIconSize,
                    compactIconSize);
                var compactTextX = compactIconRect.Right + 6f * control.LayoutScale;
                var compactTextWidth = Math.Max(1f, rect.Right - compactTextX - 8f * control.LayoutScale);
                var compactScale = 0.42f * control.LayoutScale * control.FontScale;
                var compactHeight = FormatingHelper.LineHeight(compactScale, control, control.TextSurface);
                var compactText = string.IsNullOrEmpty(model.Subtitle)
                    ? model.Name
                    : model.Name + "  " + model.Subtitle;
                var compactIcon = string.IsNullOrEmpty(model.Icon) ? "MissingIcon" : model.Icon;
                var compactBackground = backgroundColor;
                compactBackground.A = byte.MaxValue;

                BorderRenderer.CreateSpritesFromRect(
                    rect,
                    sprites,
                    compactBackground,
                    radiusScale: control.LayoutScale);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = compactIcon,
                    Position = compactIconRect.Center,
                    Size = new Vector2(compactIconSize, compactIconSize),
                    Color = ColorCorrection,
                    Alignment = TextAlignment.CENTER
                });

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = TrimToWidth(compactText, compactTextWidth, compactScale, control),
                    Position = new Vector2(compactTextX, rect.Center.Y - compactHeight * 0.5f),
                    Color = foregroundColor,
                    FontId = control.TextFont,
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = compactScale
                });
                return;
            }

            var iconSize = Math.Min(
                Math.Max(1f, ICON_SIZE_PIXELS * control.LayoutScale),
                Math.Max(1f, Math.Min(rect.Height, rect.Width) - 8f * Math.Max(1f, control.LayoutScale)));

            var iconRect = new RectangleF(
                rect.X + 7f * control.LayoutScale,
                rect.Center.Y - iconSize * 0.5f,
                iconSize,
                iconSize);

            var icon = string.IsNullOrEmpty(model.Icon) ? "MissingIcon" : model.Icon;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = icon,
                Position = iconRect.Center,
                Size = new Vector2(iconSize, iconSize),
                Color = ColorCorrection,
                Alignment = TextAlignment.CENTER
            });

            var textX = iconRect.Right + 9f * control.LayoutScale;
            var availableTextWidth = Math.Max(1f, rect.Right - textX - 8f * control.LayoutScale);
            var titleScale = 0.48f * control.LayoutScale * control.FontScale;
            var subtitleScale = 0.36f * control.LayoutScale * control.FontScale;
            var titleHeight = FormatingHelper.LineHeight(titleScale, control, control.TextSurface);

            var hasSubtitle = !string.IsNullOrEmpty(model.Subtitle) && rect.Height >= 35f * control.LayoutScale;
            var titleY = hasSubtitle
                ? rect.Center.Y - titleHeight - 1f * control.LayoutScale
                : rect.Center.Y - titleHeight * 0.5f;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = TrimToWidth(model.Name, availableTextWidth, titleScale, control),
                Position = new Vector2(textX, titleY),
                Color = foregroundColor,
                FontId = control.TextFont,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = titleScale
            });

            if (!hasSubtitle)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = TrimToWidth(model.Subtitle, availableTextWidth, subtitleScale, control),
                Position = new Vector2(textX, rect.Center.Y + 1f * control.LayoutScale),
                Color = secondaryColor,
                FontId = control.TextFont,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = subtitleScale
            });
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var rect = Bounds;
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            if (ChromeVisible)
            {
                BorderRenderer.CreateSpritesFromRect(
                    rect,
                    sprites,
                    ResolveColor(ThemeResources.SurfaceContainerColor),
                    radiusScale: LayoutScale);
            }

            var rowHeight = GetRowHeight(LayoutScale, CompactRows);
            _listPanel.ItemsSource = _items;
            _listPanel.RowHeight = rowHeight;
            _listPanel.Gap = ROW_GAP_PIXELS * LayoutScale;
            _scrollPanel.AutoScrollSecondsPerStep = 0f;
            _scrollPanel.ConfigureAutomatic(
                rect,
                Math.Max(SCROLLER_WIDTH_PIXELS * LayoutScale, _scrollPanel.AutomaticScrollerWidthPixels * LayoutScale),
                rowHeight);
            _scrollPanel.SetScrollBarColors(
                ResolveColor(ThemeResources.SurfaceContainerHighestColor),
                ResolveColor(ThemeResources.OnSurfaceColor));
            _scrollPanel.SetVisible(true);
            _scrollPanel.SetEnabled(!IsLoading);
            _scrollPanel.SetCursor(CursorType.Default);
            _scrollPanel.Render(sprites);

            if (IsLoading)
            {
                DrawLoadingListMessage(rect, sprites);
                HideContextButtonsFrom(0);
            }
            else if (_items.Count == 0)
            {
                DrawEmptyListMessage(rect, sprites);
                RenderContextMenu(sprites);
            }
            else
            {
                RenderContextMenu(sprites);
            }
        }

        void RenderContextMenu(List<MySprite> sprites)
        {
            if (!_contextMenuOpen || _contextActions.Count == 0 || sprites == null)
            {
                HideContextButtonsFrom(0);
                return;
            }

            var scale = Math.Max(1f, LayoutScale);
            var itemHeight = Math.Max(22f * scale, 26f * scale * FontScale);
            var width = Math.Max(118f * scale, Bounds.Width * .32f);
            var height = itemHeight * _contextActions.Count;
            var x = MathHelper.Clamp(_contextMenuPosition.X, Bounds.X, Math.Max(Bounds.X, Bounds.Right - width));
            var spaceBelow = Bounds.Bottom - _contextMenuPosition.Y;
            var spaceAbove = _contextMenuPosition.Y - Bounds.Y;
            var preferredY = spaceBelow >= height || spaceBelow >= spaceAbove
                ? _contextMenuPosition.Y
                : _contextMenuPosition.Y - height;
            var y = MathHelper.Clamp(preferredY, Bounds.Y, Math.Max(Bounds.Y, Bounds.Bottom - height));
            var menuRect = new RectangleF(x, y, width, height);

            BorderRenderer.CreateSpritesFromRect(
                menuRect,
                sprites,
                ResolveColor(ThemeResources.SurfaceContainerHighestColor),
                radiusScale: scale);

            for (int i = 0; i < _contextActions.Count; i++)
            {
                var action = _contextActions[i];
                var button = EnsureContextButton(i);
                var model = button.DataContext as ContextMenuButtonModel;
                if (model != null)
                {
                    model.ActionIndex = i;
                    model.Text = action == null ? string.Empty : action.Text;
                    model.Enabled = action != null && action.Enabled && action.Clicked != null;
                    model.Clicked = OnContextActionClicked;

                    button.BackgroundColor = Color.Transparent;
                    button.TextColor = model.Enabled
                        ? ResolveContextActionTextColor(action)
                        : ResolveColor(ThemeResources.DisabledColor);
                    button.BorderColor = Color.Transparent;
                    button.BorderThicknessPixels = 0f;
                    button.BorderRadiusPixels = 0f;
                    button.SetRect(new RectangleF(menuRect.X, menuRect.Y + itemHeight * i, menuRect.Width, itemHeight));
                    button.SetVisible(true);
                    button.SetEnabled(model.Enabled);
                    button.SetCursor(model.Enabled ? CursorType.Hand : CursorType.Default);
                }

                button.SetClass("ControlBase Button ContextMenuItem");
                button.CustomRender = RenderContextActionButton;
                button.Render(sprites);
            }

            HideContextButtonsFrom(_contextActions.Count);
        }

        Color ResolveContextActionTextColor(FilePickerContextAction action)
        {
            return action != null && action.UseErrorTextStyle
                ? ResolveColor(ThemeResources.ErrorColor)
                : ResolveColor(ThemeResources.OnSurfaceColor);
        }

        void HideContextButtonsFrom(int startIndex)
        {
            if (startIndex < 0)
                startIndex = 0;

            for (int i = startIndex; i < _contextButtons.Count; i++)
            {
                if (_contextButtons[i] != null)
                    _contextButtons[i].SetVisible(false);
            }
        }

        Button EnsureContextButton(int index)
        {
            while (_contextButtons.Count <= index)
            {
                var button = new Button(default(RectangleF), new ContextMenuButtonModel());
                _contextButtons.Add(button);
                AddChild(button);
            }

            return _contextButtons[index];
        }

        void RenderContextActionButton(ControlTemplate control, List<MySprite> sprites)
        {
            if (control == null || sprites == null)
                return;

            var rect = control.Bounds;
            var fill = control.IsMouseOver
                ? ResolveColor(ThemeResources.AccentContainerColor)
                : Color.Transparent;
            if (fill.A > 0)
                BorderRenderer.CreateSpritesFromRect(rect, sprites, fill, radiusScale: LayoutScale);

            var model = control.DataContext as ButtonModel;
            var text = model == null ? string.Empty : model.Text;
            var textScale = 0.46f * LayoutScale * FontScale;
            var y = rect.Center.Y - FormatingHelper.LineHeight(textScale, control, TextSurface) * .5f;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = TrimToWidth(text, Math.Max(1f, rect.Width - 14f * LayoutScale), textScale, control),
                Position = new Vector2(rect.X + 7f * LayoutScale, y),
                Color = control.TextColor,
                FontId = TextFont,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = textScale
            });
        }

        void OnContextActionClicked(ButtonModel model, object sender)
        {
            if (IsLoading)
                return;

            var contextModel = model as ContextMenuButtonModel;
            if (contextModel == null || contextModel.ActionIndex < 0 || contextModel.ActionIndex >= _contextActions.Count)
                return;

            var action = _contextActions[contextModel.ActionIndex];
            CloseContextMenu();
            if (action != null && action.Enabled && action.Clicked != null)
                action.Clicked();

            NotifyChanged();
        }

        bool OpenContextMenu(FilePickerResult result, RectangleF sourceBounds, Vector2 clickPosition)
        {
            var provider = ContextActionsProvider;
            var actions = provider?.Invoke(result);
            _contextActions.Clear();
            if (actions != null)
            {
                foreach (var t in actions.Where(t => t != null)) _contextActions.Add(t);
            }

            if (_contextActions.Count == 0)
            {
                CloseContextMenu();
                NotifyChanged();
                return false;
            }

            if (float.IsNaN(clickPosition.X) || float.IsNaN(clickPosition.Y))
                _contextMenuPosition = new Vector2(sourceBounds.Right - Math.Max(1f, 8f * LayoutScale), sourceBounds.Y);
            else
                _contextMenuPosition = clickPosition;
            _contextMenuOpen = true;
            NotifyChanged();
            return true;
        }

        void CloseContextMenu()
        {
            if (!_contextMenuOpen && _contextActions.Count == 0)
                return;

            _contextMenuOpen = false;
            _contextActions.Clear();
            HideContextButtonsFrom(0);
        }

        protected override bool CanResolveChildren(Vector2 point, bool selfHit)
        {
            return selfHit;
        }

        ControlTemplate CreateRowControl(FilePickerEntryModel model)
        {
            return new FilePickerRowControl(default(RectangleF), this);
        }

        void BindRowControl(ControlTemplate control, FilePickerEntryModel model, int index)
        {
            var row = control as FilePickerRowControl;
            if (row == null)
                return;

            row.SetOwner(this);
            row.SetEntryModel(model);
            row.SetCursor(CursorType.Hand);
            row.SetClass(model != null && model.IsSelected
                ? "ControlBase Button Row Selected"
                : "ControlBase Button Row");
            row.SetStyleId(null);
            row.SetVisible(true);
        }

        bool OpenFolder(FolderModel folder)
        {
            if (ReferenceEquals(CurrentFolder, folder) && SelectedFolder == null && SelectedFile == null)
                return true;

            CurrentFolder = folder;
            SelectedFolder = null;
            SelectedFile = null;
            RebuildItems(resetScroll: true);
            NotifyChanged();
            return true;
        }

        void AcceptFile(FileModel file)
        {
            var callback = Accepted;
            if (callback == null || file == null)
                return;

            callback(CreateFileResult(file));
        }

        FilePickerResult CreateFileResult(FileModel file)
        {
            if (file == null)
                return null;

            return new FilePickerResult
            {
                Mode = Mode,
                RootName = GetRootName(file.FullPath),
                FullPath = file.FullPath,
                File = file,
                Tag = file.Tag
            };
        }

        FilePickerResult CreateFolderResult(FolderModel folder)
        {
            if (folder == null)
                return null;

            return new FilePickerResult
            {
                Mode = Mode,
                RootName = GetRootName(folder.FullPath),
                FullPath = folder.FullPath,
                Folder = folder,
                Tag = folder.Tag
            };
        }

        void RebuildItems(bool resetScroll = false)
        {
            _items.Clear();

            var query = (_filter ?? string.Empty).Trim();
            var hasQuery = !string.IsNullOrEmpty(query);
            if (CurrentFolder == null)
            {
                if (hasQuery)
                {
                    foreach (var t in _roots)
                        AddMatchingEntriesRecursive(t, query, includeFolder: true);
                }
                else
                {
                    foreach (var root in _roots.Where(root => root != null))
                    {
                        _items.Add(GetFolderEntryModel(root));
                    }
                }

                InvalidateItemsLayout(resetScroll);
                return;
            }

            if (CurrentFolder.Parent != null || IsRootFolder(CurrentFolder))
            {
                var parent = CurrentFolder.Parent;
                _items.Add(GetUpEntryModel(CurrentFolder, parent));
            }

            if (hasQuery)
                AddMatchingEntriesRecursive(CurrentFolder, query, includeFolder: false);
            else
            {
                AddFolders(CurrentFolder, query);
                AddFiles(CurrentFolder, query);
            }

            InvalidateItemsLayout(resetScroll);
        }

        void InvalidateItemsLayout(bool resetScroll)
        {
            if (resetScroll)
                _scrollPanel.ResetScroll(notify: false);

            _listPanel.InvalidateLayout();
            _scrollPanel.InvalidateLayout();
            MarkDirty();
        }

        void RefreshSelectionFlags()
        {
            foreach (var item in _items)
            {
                var folder = item as FolderControlModel;
                if (folder != null)
                {
                    folder.IsSelected = ReferenceEquals(SelectedFolder, folder.Folder);
                    continue;
                }

                var file = item as FileControlModel;
                if (file != null)
                    file.IsSelected = ReferenceEquals(SelectedFile, file.File);
            }
        }

        void AddFolders(FolderModel folder, string query)
        {
            if (folder == null || folder.Folders == null)
                return;

            folder.Folders.Sort(CompareFolders);
            foreach (var child in folder.Folders.Where(child => child != null && MatchesFolder(child, query))) 
                _items.Add(GetFolderEntryModel(child));
        }

        void AddFiles(FolderModel folder, string query)
        {
            if (folder == null || folder.Files == null)
                return;

            folder.Files.Sort(CompareFiles);
            for (int i = 0; i < folder.Files.Count; i++)
            {
                var file = folder.Files[i];
                if (file == null || !MatchesFile(file, query))
                    continue;

                _items.Add(GetFileEntryModel(file));
            }
        }

        void AddMatchingEntriesRecursive(FolderModel folder, string query, bool includeFolder)
        {
            if (folder == null)
                return;

            if (includeFolder && MatchesFolder(folder, query))
                _items.Add(GetFolderEntryModel(folder));

            AddFiles(folder, query);

            if (folder.Folders == null)
                return;

            folder.Folders.Sort(CompareFolders);
            for (int i = 0; i < folder.Folders.Count; i++)
                AddMatchingEntriesRecursive(folder.Folders[i], query, includeFolder: true);
        }

        FolderControlModel GetFolderEntryModel(FolderModel folder)
        {
            FolderControlModel model;
            if (!_folderEntryModels.TryGetValue(folder, out model))
            {
                model = new FolderControlModel();
                _folderEntryModels.Add(folder, model);
            }

            model.Folder = folder;
            model.Name = folder.Name;
            model.FullPath = folder.FullPath;
            model.Subtitle = folder.Subtitle;
            model.Icon = "Folder";
            model.IsUpEntry = false;
            model.IsSelected = ReferenceEquals(SelectedFolder, folder);
            return model;
        }

        FolderControlModel GetUpEntryModel(FolderModel currentFolder, FolderModel parent)
        {
            FolderControlModel model;
            if (currentFolder == null)
            {
                model = _rootsUpEntryModel;
            }
            else if (!_upEntryModels.TryGetValue(currentFolder, out model))
            {
                model = new FolderControlModel();
                _upEntryModels.Add(currentFolder, model);
            }

            model.Folder = parent;
            model.Name = "..";
            model.FullPath = parent == null ? string.Empty : parent.FullPath;
            model.Subtitle = parent == null ? LocHelper.GetLoc(LOC_BACK_TO_ROOTS) : parent.FullPath;
            model.Icon = "Folder";
            model.IsUpEntry = true;
            model.IsSelected = ReferenceEquals(SelectedFolder, parent);
            return model;
        }

        FileControlModel GetFileEntryModel(FileModel file)
        {
            FileControlModel model;
            if (!_fileEntryModels.TryGetValue(file, out model))
            {
                model = new FileControlModel();
                _fileEntryModels.Add(file, model);
            }

            model.File = file;
            model.Name = file.Name;
            model.FullPath = file.FullPath;
            model.Subtitle = file.Subtitle;
            model.Icon = ResolveFileIcon(GetFileIconPath(file));
            model.IsUpEntry = false;
            model.IsSelected = ReferenceEquals(SelectedFile, file);
            return model;
        }

        bool IsRootFolder(FolderModel folder)
        {
            if (folder == null)
                return false;

            for (int i = 0; i < _roots.Count; i++)
            {
                if (ReferenceEquals(_roots[i], folder))
                    return true;
            }

            return false;
        }

        FolderModel FindFolderByPath(string path)
        {
            path = NormalizePath(path);
            if (string.IsNullOrEmpty(path))
                return null;

            for (int i = 0; i < _roots.Count; i++)
            {
                var found = FindFolderByPath(_roots[i], path);
                if (found != null)
                    return found;
            }

            return null;
        }

        static FolderModel FindFolderByPath(FolderModel folder, string path)
        {
            if (folder == null)
                return null;

            if (string.Equals(NormalizePath(folder.FullPath), path, StringComparison.OrdinalIgnoreCase))
                return folder;

            if (folder.Folders == null)
                return null;

            for (int i = 0; i < folder.Folders.Count; i++)
            {
                var found = FindFolderByPath(folder.Folders[i], path);
                if (found != null)
                    return found;
            }

            return null;
        }

        static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Replace('\\', '/').Trim('/');
        }

        void DrawEmptyListMessage(RectangleF rect, List<MySprite> sprites)
        {
            IMyTextSurface surface = TextSurface;
            var text = string.IsNullOrWhiteSpace(_filter)
                ? LocHelper.GetLoc(LOC_NO_FILES_FOUND)
                : string.Format(FormatingHelper.Culture, LocHelper.GetLoc(LOC_NO_FILES_MATCH_FORMAT), _filter);
            var textScale = 0.52f * LayoutScale * FontScale;
            var textHeight = surface == null ? 0f : FormatingHelper.LineHeight(textScale, this, surface);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, rect.Center.Y - textHeight * 0.5f),
                Color = ResolveColor(ThemeResources.OnSurfaceVariantColor),
                FontId = TextFont,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });
        }

        void DrawLoadingListMessage(RectangleF rect, List<MySprite> sprites)
        {
            IMyTextSurface surface = TextSurface;
            var center = rect.Center;
            var outerSize = Math.Max(26f, Math.Min(rect.Width, rect.Height) * 0.18f);
            var innerSize = outerSize * 0.6f;
            var color = ResolveColor(ThemeResources.OnSurfaceVariantColor);
            var session = Sandbox.ModAPI.MyAPIGateway.Session;
            var seconds = session == null ? 0.0 : session.GameplayFrameCounter / 60.0;
            var outerRotation = (float)(seconds * 2.4);
            var innerRotation = -outerRotation;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Screen_LoadingBar",
                Position = center,
                Size = new Vector2(outerSize),
                Color = color,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = outerRotation
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Screen_LoadingBar",
                Position = center,
                Size = new Vector2(innerSize),
                Color = color,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = innerRotation
            });

            var text = string.IsNullOrEmpty(LoadingMessage) ? LocHelper.GetLoc(LOC_LOADING) : LoadingMessage;
            var textScale = 0.52f * LayoutScale * FontScale;
            var textHeight = surface == null ? 0f : FormatingHelper.LineHeight(textScale, this, surface);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = new Vector2(rect.Center.X, center.Y + outerSize * 0.75f + textHeight * .25f),
                Color = color,
                FontId = TextFont,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });

            ScheduleLoadingFrame();
        }

        void ScheduleLoadingFrame()
        {
            if (_loadingFrameQueued || !IsLoading)
                return;

            _loadingFrameQueued = true;
            LcdModClientComponent.RunNextFrame.Add(delegate
            {
                _loadingFrameQueued = false;
                if (IsLoading)
                    NotifyChanged();
            });
        }

        void OnScrollChanged(ScrollPanel panel)
        {
            NotifyChanged();
        }

        void NotifyChanged()
        {
            MarkDirty();
            var callback = Changed;
            if (callback != null)
                callback();
        }

        static void PrepareFolder(FolderModel folder, FolderModel parent, string path)
        {
            if (folder == null)
                return;

            folder.Parent = parent;
            if (string.IsNullOrEmpty(folder.Name))
                folder.Name = string.Empty;

            if (string.IsNullOrEmpty(folder.FullPath))
                folder.FullPath = string.IsNullOrEmpty(path) ? folder.Name : path;

            if (folder.Folders != null)
            {
                for (int i = 0; i < folder.Folders.Count; i++)
                {
                    var child = folder.Folders[i];
                    if (child == null)
                        continue;

                    var childPath = string.IsNullOrEmpty(child.FullPath)
                        ? (string.IsNullOrEmpty(folder.FullPath) ? child.Name : folder.FullPath + "/" + child.Name)
                        : child.FullPath;
                    PrepareFolder(child, folder, childPath);
                }
            }

            if (folder.Files != null)
            {
                for (int i = 0; i < folder.Files.Count; i++)
                {
                    var file = folder.Files[i];
                    if (file == null)
                        continue;

                    if (string.IsNullOrEmpty(file.Name))
                        file.Name = string.Empty;
                    if (string.IsNullOrEmpty(file.FullPath))
                        file.FullPath = string.IsNullOrEmpty(folder.FullPath)
                            ? file.Name
                            : folder.FullPath + "/" + file.Name;
                }
            }
        }

        static bool MatchesFolder(FolderModel folder, string query)
        {
            if (folder == null)
                return false;
            if (string.IsNullOrEmpty(query))
                return true;
            return Contains(folder.Name, query) || Contains(folder.FullPath, query) || Contains(folder.Subtitle, query);
        }

        static bool MatchesFile(FileModel file, string query)
        {
            if (file == null)
                return false;
            if (string.IsNullOrEmpty(query))
                return true;
            return Contains(file.Name, query) || Contains(file.FullPath, query) || Contains(file.Subtitle, query);
        }

        static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static int CompareFolders(FolderModel left, FolderModel right)
        {
            return string.Compare(left == null ? null : left.Name, right == null ? null : right.Name, StringComparison.OrdinalIgnoreCase);
        }

        static int CompareFiles(FileModel left, FileModel right)
        {
            return string.Compare(left == null ? null : left.Name, right == null ? null : right.Name, StringComparison.OrdinalIgnoreCase);
        }

        static string ResolveFileIcon(string fileName)
        {
            var extension = System.IO.Path.GetExtension(fileName);
            if (string.Equals(extension, ".xwm", StringComparison.OrdinalIgnoreCase))
                return "FileXwm";
            if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
                return "FileWav";
            if (string.Equals(extension, ".m3u", StringComparison.OrdinalIgnoreCase))
                return "FileWav";
            return "MissingIcon";
        }

        static string GetFileIconPath(FileModel file)
        {
            if (file == null)
                return null;

            if (!string.IsNullOrEmpty(file.IconPath))
                return file.IconPath;

            if (!string.IsNullOrEmpty(file.FullPath))
                return file.FullPath;

            return file.Name;
        }

        static string GetRootName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var normalized = path.Replace('\\', '/');
            var slash = normalized.IndexOf('/');
            return slash < 0 ? normalized : normalized.Substring(0, slash);
        }

        static float GetRowHeight(float scale, bool compact)
        {
            if (compact)
                return Math.Max(24f * scale, 28f * Math.Max(1f, scale));

            return Math.Max(ROW_HEIGHT_PIXELS * scale, ICON_SIZE_PIXELS * scale + 8f * Math.Max(1f, scale));
        }

        static string TrimToWidth(string text, float width, float scale, ControlTemplate control)
        {
            if (string.IsNullOrEmpty(text) || control == null)
                return string.Empty;

            if (width <= 0f || control.TextSurface == null)
                return text;

            if (FormatingHelper.GetSizeInPixel(text, control, scale, control.TextSurface).X <= width)
                return text;

            const string ellipsis = "...";
            var max = text.Length;
            while (max > 0)
            {
                var candidate = text.Substring(0, max) + ellipsis;
                if (FormatingHelper.GetSizeInPixel(candidate, control, scale, control.TextSurface).X <= width)
                    return candidate;
                max--;
            }

            return ellipsis;
        }
    }

    sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

        ReferenceEqualityComparer()
        {
        }

        public bool Equals(T x, T y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }

    sealed class FilePickerRowControl : RectangleControl
    {
        readonly FolderControl _folderControl;
        readonly FileControl _fileControl;

        public FilePickerRowControl(RectangleF rect, FilePickerGrid owner)
            : base(rect, CursorType.Hand)
        {
            _folderControl = new FolderControl(rect, owner);
            _fileControl = new FileControl(rect, owner);
            AddChild(_folderControl);
            AddChild(_fileControl);
        }

        public void SetOwner(FilePickerGrid owner)
        {
            _folderControl.SetOwner(owner);
            _fileControl.SetOwner(owner);
        }

        public void SetEntryModel(FilePickerEntryModel model)
        {
            var folder = model as FolderControlModel;
            var file = model as FileControlModel;

            _folderControl.SetDataContext(folder);
            _folderControl.SetVisible(folder != null);
            _folderControl.SetCursor(CursorType.Hand);
            _folderControl.SetClass(model != null && model.IsSelected ? "ControlBase Button Row Selected" : "ControlBase Button Row");
            _folderControl.SetStyleId(null);

            _fileControl.SetDataContext(file);
            _fileControl.SetVisible(file != null);
            _fileControl.SetCursor(CursorType.Hand);
            _fileControl.SetClass(model != null && model.IsSelected ? "ControlBase Button Row Selected" : "ControlBase Button Row");
            _fileControl.SetStyleId(null);
        }

        public override void Arrange(RectangleF bounds)
        {
            base.Arrange(bounds);
            _folderControl.SetRect(bounds);
            _fileControl.SetRect(bounds);
        }

        protected override bool CanResolveChildren(Vector2 point, bool selfHit)
        {
            return selfHit;
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            if (_folderControl.Visible)
                _folderControl.Render(sprites);
            if (_fileControl.Visible)
                _fileControl.Render(sprites);
        }
    }

    static class FilePickerClickPosition
    {
        public static Vector2 FromSenderOrBounds(object sender, RectangleF bounds)
        {
            var eyeTracking = sender as IEyeTracking;
            if (eyeTracking != null)
            {
                var position = eyeTracking.CursorPosition + eyeTracking.HitTestOffset;
                if (!float.IsNaN(position.X) && !float.IsNaN(position.Y))
                    return position;
            }

            return bounds.Position;
        }
    }

    sealed class FolderControl : RectangleControl
    {
        FilePickerGrid _owner;

        public FolderControl(RectangleF rect, FilePickerGrid owner)
            : base(rect, CursorType.Hand)
        {
            _owner = owner;
            SetOnClick(OnClicked);
            OnSecondaryClick = OnSecondaryClicked;
        }

        public void SetOwner(FilePickerGrid owner)
        {
            _owner = owner;
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            if (_owner != null)
                _owner.RenderEntry(this, DataContext as FolderControlModel, sprites);
        }

        protected override StyleState GetStyleState()
        {
            var state = base.GetStyleState();
            var model = DataContext as FolderControlModel;
            if (model != null && model.IsSelected)
                state |= StyleState.Selected;
            return state;
        }

        void OnClicked(object dataContext, object sender)
        {
            if (_owner != null)
                _owner.OnFolderControlClicked(dataContext as FolderControlModel, sender);
        }

        public override bool SecondaryClickAt(Vector2 point, object sender)
        {
            return _owner != null && _owner.OnFolderControlSecondaryClicked(DataContext as FolderControlModel, Bounds, point, sender);
        }

        void OnSecondaryClicked(object dataContext, object sender)
        {
            if (_owner != null)
                _owner.OnFolderControlSecondaryClicked(dataContext as FolderControlModel, Bounds, FilePickerClickPosition.FromSenderOrBounds(sender, Bounds), sender);
        }
    }

    sealed class FileControl : RectangleControl
    {
        FilePickerGrid _owner;

        public FileControl(RectangleF rect, FilePickerGrid owner)
            : base(rect, CursorType.Hand)
        {
            _owner = owner;
            SetOnClick(OnClicked);
            OnSecondaryClick = OnSecondaryClicked;
        }

        public void SetOwner(FilePickerGrid owner)
        {
            _owner = owner;
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            if (_owner != null)
                _owner.RenderEntry(this, DataContext as FileControlModel, sprites);
        }

        protected override StyleState GetStyleState()
        {
            var state = base.GetStyleState();
            var model = DataContext as FileControlModel;
            if (model != null && model.IsSelected)
                state |= StyleState.Selected;
            return state;
        }

        void OnClicked(object dataContext, object sender)
        {
            if (_owner != null)
                _owner.OnFileControlClicked(dataContext as FileControlModel, sender);
        }

        public override bool SecondaryClickAt(Vector2 point, object sender)
        {
            return _owner != null && _owner.OnFileControlSecondaryClicked(DataContext as FileControlModel, Bounds, point, sender);
        }

        void OnSecondaryClicked(object dataContext, object sender)
        {
            if (_owner != null)
                _owner.OnFileControlSecondaryClicked(dataContext as FileControlModel, Bounds, FilePickerClickPosition.FromSenderOrBounds(sender, Bounds), sender);
        }
    }
}
