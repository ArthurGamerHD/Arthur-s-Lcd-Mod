#if EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
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

        readonly List<FolderModel> _roots = new List<FolderModel>();
        readonly List<FilePickerEntryModel> _items = new List<FilePickerEntryModel>();
        readonly Dictionary<FolderModel, FolderControlModel> _folderEntryModels = new Dictionary<FolderModel, FolderControlModel>(ReferenceEqualityComparer<FolderModel>.Instance);
        readonly Dictionary<FileModel, FileControlModel> _fileEntryModels = new Dictionary<FileModel, FileControlModel>(ReferenceEqualityComparer<FileModel>.Instance);
        readonly Dictionary<FolderModel, FolderControlModel> _upEntryModels = new Dictionary<FolderModel, FolderControlModel>(ReferenceEqualityComparer<FolderModel>.Instance);
        readonly FolderControlModel _rootsUpEntryModel = new FolderControlModel { IsUpEntry = true };
        readonly ScrollPanel _scrollPanel = new ScrollPanel();
        readonly VirtualizedStackPanel<FilePickerEntryModel> _listPanel = new VirtualizedStackPanel<FilePickerEntryModel>();

        string _filter = string.Empty;

        public FilePickerGrid(RectangleF rect, FilePickerMode mode, IEnumerable<FolderModel> roots)
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
        }

        public FilePickerMode Mode { get; private set; }
        public FolderModel CurrentFolder { get; private set; }
        public FolderModel SelectedFolder { get; private set; }
        public FileModel SelectedFile { get; private set; }
        public Action<FilePickerResult> Accepted { get; set; }
        public Action Changed { get; set; }

        public string CurrentPath
        {
            get
            {
                return CurrentFolder == null || string.IsNullOrEmpty(CurrentFolder.FullPath)
                    ? string.Empty
                    : CurrentFolder.FullPath;
            }
        }

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
                if (Mode == FilePickerMode.PickFile)
                    return SelectedFile != null;

                return SelectedFolder != null || CurrentFolder != null;
            }
        }

        public void AcceptSelection()
        {
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

            var selected = model.IsSelected;
            var backgroundColor = selected
                ? ResolveColor(ThemeResources.AccentContainerColor)
                : control.BackgroundColor;
            var foregroundColor = selected
                ? ResolveColor(ThemeResources.OnAccentContainerColor)
                : control.TextColor;
            var secondaryColor = selected
                ? ResolveColor(ThemeResources.OnAccentContainerColor)
                : ResolveColor(ThemeResources.OnSurfaceVariantColor);

            BorderRenderer.CreateSpritesFromRect(
                rect,
                sprites,
                backgroundColor,
                radiusScale: control.LayoutScale);

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
                Color = Constants.ColorCorrection,
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

            var subtitleHeight = FormatingHelper.LineHeight(subtitleScale, control, control.TextSurface);
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

            BorderRenderer.CreateSpritesFromRect(
                rect,
                sprites,
                ResolveColor(ThemeResources.SurfaceContainerColor),
                radiusScale: LayoutScale);

            var rowHeight = GetRowHeight(LayoutScale);
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
            _scrollPanel.Render(sprites);

            if (_items.Count == 0)
                DrawEmptyListMessage(rect, sprites);
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
            row.SetStyleId(model != null && model.IsSelected ? "Primary" : null);
            row.SetVisible(true);
        }

        void OpenFolder(FolderModel folder)
        {
            CurrentFolder = folder;
            SelectedFolder = null;
            SelectedFile = null;
            RebuildItems(resetScroll: true);
            NotifyChanged();
        }

        void AcceptFile(FileModel file)
        {
            var callback = Accepted;
            if (callback == null || file == null)
                return;

            callback(CreateFileResult(file));
        }

        void AcceptFolder(FolderModel folder)
        {
            var callback = Accepted;
            if (callback == null || folder == null)
                return;

            callback(CreateFolderResult(folder));
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
            if (CurrentFolder == null)
            {
                for (int i = 0; i < _roots.Count; i++)
                {
                    var root = _roots[i];
                    if (root == null || !MatchesFolder(root, query))
                        continue;

                    _items.Add(GetFolderEntryModel(root));
                }

                InvalidateItemsLayout(resetScroll);
                return;
            }

            if (CurrentFolder.Parent != null || IsRootFolder(CurrentFolder))
            {
                var parent = CurrentFolder.Parent;
                _items.Add(GetUpEntryModel(CurrentFolder, parent));
            }

            AddFolders(CurrentFolder, query);
            AddFiles(CurrentFolder, query);
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
            for (int i = 0; i < _items.Count; i++)
            {
                var folder = _items[i] as FolderControlModel;
                if (folder != null)
                {
                    folder.IsSelected = ReferenceEquals(SelectedFolder, folder.Folder);
                    continue;
                }

                var file = _items[i] as FileControlModel;
                if (file != null)
                    file.IsSelected = ReferenceEquals(SelectedFile, file.File);
            }
        }

        void AddFolders(FolderModel folder, string query)
        {
            if (folder == null || folder.Folders == null)
                return;

            folder.Folders.Sort(CompareFolders);
            for (int i = 0; i < folder.Folders.Count; i++)
            {
                var child = folder.Folders[i];
                if (child == null || !MatchesFolder(child, query))
                    continue;

                _items.Add(GetFolderEntryModel(child));
            }
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

        FolderControlModel GetFolderEntryModel(FolderModel folder)
        {
            FolderControlModel model;
            if (!_folderEntryModels.TryGetValue(folder, out model))
            {
                model = new FolderControlModel();
                _folderEntryModels.Add(folder, model);
            }

            model.Folder = folder;
            model.Name = folder == null ? string.Empty : folder.Name;
            model.FullPath = folder == null ? string.Empty : folder.FullPath;
            model.Subtitle = folder == null ? string.Empty : folder.Subtitle;
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
            model.Subtitle = parent == null ? "Back to roots" : parent.FullPath;
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
            model.Name = file == null ? string.Empty : file.Name;
            model.FullPath = file == null ? string.Empty : file.FullPath;
            model.Subtitle = file == null ? string.Empty : file.Subtitle;
            model.Icon = ResolveFileIcon(file == null ? null : file.Name);
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

        void DrawEmptyListMessage(RectangleF rect, List<MySprite> sprites)
        {
            IMyTextSurface surface = TextSurface;
            var text = string.IsNullOrWhiteSpace(_filter)
                ? "No files found"
                : "No files match \"" + _filter + "\"";
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
            folder.FullPath = string.IsNullOrEmpty(path) ? folder.Name : path;

            if (folder.Folders != null)
            {
                for (int i = 0; i < folder.Folders.Count; i++)
                {
                    var child = folder.Folders[i];
                    if (child == null)
                        continue;

                    var childPath = string.IsNullOrEmpty(folder.FullPath)
                        ? child.Name
                        : folder.FullPath + "/" + child.Name;
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
            return "MissingIcon";
        }

        static string GetRootName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var normalized = path.Replace('\\', '/');
            var slash = normalized.IndexOf('/');
            return slash < 0 ? normalized : normalized.Substring(0, slash);
        }

        static float GetRowHeight(float scale)
        {
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
            return obj == null ? 0 : RuntimeHelpers.GetHashCode(obj);
        }
    }

    sealed class FilePickerRowControl : RectangleControl
    {
        readonly FolderControl _folderControl;
        readonly FileControl _fileControl;
        FilePickerGrid _owner;

        public FilePickerRowControl(RectangleF rect, FilePickerGrid owner)
            : base(rect, CursorType.Hand)
        {
            _owner = owner;
            _folderControl = new FolderControl(rect, owner);
            _fileControl = new FileControl(rect, owner);
            AddChild(_folderControl);
            AddChild(_fileControl);
        }

        public void SetOwner(FilePickerGrid owner)
        {
            _owner = owner;
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
            _folderControl.SetStyleId(model != null && model.IsSelected ? "Primary" : null);

            _fileControl.SetDataContext(file);
            _fileControl.SetVisible(file != null);
            _fileControl.SetCursor(CursorType.Hand);
            _fileControl.SetClass(model != null && model.IsSelected ? "ControlBase Button Row Selected" : "ControlBase Button Row");
            _fileControl.SetStyleId(model != null && model.IsSelected ? "Primary" : null);
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

    sealed class FolderControl : RectangleControl
    {
        FilePickerGrid _owner;

        public FolderControl(RectangleF rect, FilePickerGrid owner)
            : base(rect, CursorType.Hand)
        {
            _owner = owner;
            SetOnClick(OnClicked);
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

        void OnClicked(object dataContext, object sender)
        {
            if (_owner != null)
                _owner.OnFolderControlClicked(dataContext as FolderControlModel, sender);
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

        void OnClicked(object dataContext, object sender)
        {
            if (_owner != null)
                _owner.OnFileControlClicked(dataContext as FileControlModel, sender);
        }
    }
}
#endif
