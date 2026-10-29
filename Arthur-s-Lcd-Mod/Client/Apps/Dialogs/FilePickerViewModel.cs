using System;
using System.Collections.Generic;
using Adk.Gui.Core.Input;
using LcdMod.Common.Mvvm;

namespace LcdMod.Client.Apps.Dialogs
{
    /// <summary>
    /// Browses a folder tree. A click selects an entry and a second click on it opens the folder or picks the file.
    /// In multi-file mode folders open on a single click and files toggle in an ordered selection that Apply returns.
    /// The selection's context actions are offered in the footer; back and forward walk the visited folders.
    /// </summary>
    internal sealed class FilePickerViewModel : DialogViewModel
    {
        const int HISTORY_LIMIT = 8;

        readonly FilePickerMode _mode;
        readonly Action<FilePickerResult> _selected;
        readonly Action<string[]> _applied;
        readonly List<string> _selectedKeys = new List<string>();
        readonly Action<string> _currentPathChanged;
        readonly Func<FilePickerResult, List<FilePickerContextAction>> _contextActions;
        readonly List<FolderModel> _roots = new List<FolderModel>();
        readonly List<FileRow> _rows = new List<FileRow>();
        readonly List<FileActionRow> _actions = new List<FileActionRow>();
        readonly Dictionary<object, FileRow> _rowsByEntry = new Dictionary<object, FileRow>();
        readonly List<string> _back = new List<string>();
        readonly List<string> _forward = new List<string>();
        readonly string _initialPath;
        FolderModel _current;
        FolderModel _selectedFolder;
        FileModel _selectedFile;
        string _search = string.Empty;
        string _loadingText = string.Empty;
        bool _loading;
        bool _busy;
        bool _closed;
        string _anchor;

        public FilePickerViewModel(string title, FilePickerMode mode, IEnumerable<FolderModel> roots,
            Action<FilePickerResult> selected, string initialPath = null, Action<string> currentPathChanged = null,
            Func<FilePickerResult, List<FilePickerContextAction>> contextActions = null)
            : this(title, mode, roots, selected, null, initialPath, currentPathChanged, contextActions)
        {
        }

        FilePickerViewModel(string title, FilePickerMode mode, IEnumerable<FolderModel> roots,
            Action<FilePickerResult> selected, Action<string[]> applied, string initialPath,
            Action<string> currentPathChanged, Func<FilePickerResult, List<FilePickerContextAction>> contextActions)
        {
            _mode = mode;
            _selected = selected;
            _applied = applied;
            _currentPathChanged = currentPathChanged;
            _contextActions = contextActions;
            _initialPath = initialPath;
            Title = string.IsNullOrWhiteSpace(title)
                ? Loc(mode == FilePickerMode.PickFolder ? "LcdMod_FilePicker_PickFolder" : "LcdMod_FilePicker_PickFile")
                : title;
            SelectionHint = Loc("LcdMod_SpritePicker_MultiSelectHint");
            SelectCommand = new DelegateCommand(_ => Accept());
            BackCommand = new DelegateCommand(_ => Navigate(_back, _forward));
            ForwardCommand = new DelegateCommand(_ => Navigate(_forward, _back));
            SetRoots(roots);
        }

        /// <summary>A multi-file picker; files are identified by their string <see cref="FileModel.Tag"/>, or their path.</summary>
        public static FilePickerViewModel Multiple(string title, IEnumerable<FolderModel> roots, IEnumerable<string> selected,
            Action<string[]> applied, string initialPath = null, Action<string> currentPathChanged = null,
            Func<FilePickerResult, List<FilePickerContextAction>> contextActions = null)
        {
            var model = new FilePickerViewModel(title, FilePickerMode.PickFiles, roots, null, applied, initialPath,
                currentPathChanged, contextActions);
            if (selected != null)
                foreach (string key in selected)
                    if (!string.IsNullOrWhiteSpace(key) && !ContainsKey(model._selectedKeys, key.Trim()))
                        model._selectedKeys.Add(key.Trim());
            model.RefreshSelection();
            return model;
        }

        public Func<bool> IsToggleModifierDown { get; set; } = () => false;
        public Func<bool> IsRangeModifierDown { get; set; } = () => false;

        public IReadOnlyList<FileRow> Rows => _rows;
        public IReadOnlyList<FileActionRow> Actions => _actions;
        public ICommand SelectCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand ForwardCommand { get; }
        public bool IsClosed => _closed;
        public bool IsLoading => _loading;
        public bool IsBusy => _loading || _busy;
        public bool IsBrowsing => !_loading;
        public bool IsMultiple => _mode == FilePickerMode.PickFiles;
        public bool ShowSelectionSummary => IsMultiple && _actions.Count == 0;
        public string SelectionText => Loc("LcdMod_SpritePicker_SelectedFormat", _selectedKeys.Count);
        public string SelectionHint { get; }
        public string LoadingText => _loadingText;
        public bool IsEmpty => !_loading && _rows.Count == 0;
        public bool HasActions => _actions.Count > 0;
        public bool CanGoBack => !_loading && _back.Count > 0;
        public bool CanGoForward => !_loading && _forward.Count > 0;
        public string SelectText => Loc(IsMultiple ? "LcdMod_Common_Button_Apply"
            : _mode == FilePickerMode.PickFolder ? "LcdMod_FilePicker_SelectFolder" : "LcdMod_FilePicker_SelectFile");
        public string CurrentPath => _current == null || string.IsNullOrEmpty(_current.FullPath) ? string.Empty : _current.FullPath;
        public string PathText => CurrentPath.Length == 0 ? "/" : "/" + Normalize(CurrentPath) + "/";

        public string EmptyText => _search.Trim().Length == 0
            ? Loc("LcdMod_FilePicker_NoFilesFound")
            : Loc("LcdMod_FilePicker_NoFilesMatchFormat", _search.Trim());

        public bool CanSelect
        {
            get
            {
                if (_loading)
                    return false;
                if (IsMultiple)
                    return true;
                return _mode == FilePickerMode.PickFile ? _selectedFile != null : _selectedFolder != null || _current != null;
            }
        }

        public string SearchText
        {
            get { return _search; }
            set
            {
                if (!RaiseAndSetIfChanged(ref _search, value ?? string.Empty, nameof(SearchText)))
                    return;
                ClearSelection();
                Rebuild();
            }
        }

        public void SetRoots(IEnumerable<FolderModel> roots)
        {
            ApplyRoots(roots, _initialPath, false);
            _back.Clear();
            _forward.Clear();
            RaiseHistory();
        }

        public void RefreshRoots(IEnumerable<FolderModel> roots)
        {
            ApplyRoots(roots, CurrentPath.Length == 0 ? _initialPath : CurrentPath, true);
        }

        public void SetLoading(bool loading, string message = null)
        {
            string text = string.IsNullOrWhiteSpace(message) ? Loc("LcdMod_FilePicker_Loading") : message;
            bool changed = RaiseAndSetIfChanged(ref _loading, loading, nameof(IsLoading));
            RaiseAndSetIfChanged(ref _loadingText, text, nameof(LoadingText));
            if (!changed)
                return;
            RaisePropertyChanged<bool>(nameof(IsBrowsing));
            RaisePropertyChanged<bool>(nameof(IsBusy));
            RaisePropertyChanged<bool>(nameof(IsEmpty));
            RaisePropertyChanged<bool>(nameof(CanSelect));
            RaiseHistory();
            RefreshActions();
            if (!loading && _currentPathChanged != null)
                _currentPathChanged(CurrentPath);
        }

        /// <summary>Shows the progress bar while the list stays usable, for work that only adds entries.</summary>
        public void SetBusy(bool busy)
        {
            if (_busy == busy)
                return;
            _busy = busy;
            RaisePropertyChanged<bool>(nameof(IsBusy));
        }

        protected internal override void OnClosed()
        {
            _closed = true;
        }

        protected override void OnFontSizeChanged()
        {
            foreach (var row in _rowsByEntry.Values)
                row.RaiseFontSize();
            foreach (var action in _actions)
                action.RaisePropertyChanged<float>(nameof(FileActionRow.FontSize));
        }

        internal bool IsSelected(object entry)
        {
            if (entry == null)
                return false;
            var file = entry as FileModel;
            if (IsMultiple && file != null)
                return ContainsKey(_selectedKeys, KeyFor(file));
            return ReferenceEquals(entry, _selectedFolder) || ReferenceEquals(entry, _selectedFile);
        }

        internal void Click(FileRow row)
        {
            if (_loading)
                return;
            if (row.IsUp)
            {
                Open(row.Folder);
                return;
            }
            if (IsMultiple)
            {
                if (row.Folder != null)
                    Open(row.Folder);
                else
                    Toggle(row.File);
                return;
            }
            if (row.Folder != null)
            {
                if (ReferenceEquals(_selectedFolder, row.Folder))
                    Open(row.Folder);
                else
                    Select(row.Folder, null);
                return;
            }
            if (ReferenceEquals(_selectedFile, row.File) && _mode == FilePickerMode.PickFile)
                Finish(FileResult(row.File));
            else
                Select(null, row.File);
        }

        void Toggle(FileModel file)
        {
            string key = KeyFor(file);
            if (IsRangeModifierDown() && _anchor != null)
                SelectRange(_anchor, key);
            else if (IsToggleModifierDown())
            {
                if (!RemoveKey(_selectedKeys, key))
                    _selectedKeys.Add(key);
                _anchor = key;
            }
            else
            {
                _selectedKeys.Clear();
                _selectedKeys.Add(key);
                _anchor = key;
            }
            _selectedFile = file;
            RefreshSelection();
        }

        void SelectRange(string from, string to)
        {
            var files = new List<string>();
            foreach (var row in _rows)
                if (row.File != null)
                    files.Add(KeyFor(row.File));
            int start = files.FindIndex(key => string.Equals(key, from, StringComparison.OrdinalIgnoreCase));
            int end = files.FindIndex(key => string.Equals(key, to, StringComparison.OrdinalIgnoreCase));
            _selectedKeys.Clear();
            if (start < 0 || end < 0)
            {
                _selectedKeys.Add(to);
                _anchor = to;
                return;
            }
            for (int i = Math.Min(start, end); i <= Math.Max(start, end); i++)
                _selectedKeys.Add(files[i]);
        }

        void RefreshSelection()
        {
            foreach (var row in _rows)
                row.Refresh();
            RaisePropertyChanged<string>(nameof(SelectionText));
            RaisePropertyChanged<bool>(nameof(CanSelect));
            RefreshActions();
        }

        void Accept()
        {
            if (!CanSelect)
                return;
            if (IsMultiple)
            {
                var keys = _selectedKeys.ToArray();
                Close();
                if (_applied != null)
                    _applied(keys);
                return;
            }
            Finish(_mode == FilePickerMode.PickFile ? FileResult(_selectedFile) : FolderResult(_selectedFolder ?? _current));
        }

        void Finish(FilePickerResult result)
        {
            Close();
            if (_selected != null && result != null)
                _selected(result);
        }

        void Select(FolderModel folder, FileModel file)
        {
            _selectedFolder = folder;
            _selectedFile = file;
            foreach (var row in _rows)
                row.Refresh();
            RaisePropertyChanged<bool>(nameof(CanSelect));
            RefreshActions();
        }

        void ClearSelection()
        {
            _selectedFolder = null;
            _selectedFile = null;
        }

        void Open(FolderModel folder)
        {
            if (ReferenceEquals(_current, folder) && _selectedFolder == null && _selectedFile == null)
                return;
            Push(_back, CurrentPath);
            _forward.Clear();
            SetCurrent(folder);
        }

        void SetCurrent(FolderModel folder)
        {
            _current = folder;
            ClearSelection();
            Rebuild();
            RaisePropertyChanged<string>(nameof(PathText));
            RaiseHistory();
            if (!_loading && _currentPathChanged != null)
                _currentPathChanged(CurrentPath);
        }

        void Navigate(List<string> source, List<string> destination)
        {
            if (_loading)
                return;
            while (source.Count > 0)
            {
                string path = source[source.Count - 1];
                source.RemoveAt(source.Count - 1);
                if (SamePath(path, CurrentPath))
                    continue;
                FolderModel target = path.Length == 0 ? null : FindFolder(path);
                if (path.Length > 0 && target == null)
                    continue;
                Push(destination, CurrentPath);
                SetCurrent(target);
                return;
            }
            RaiseHistory();
        }

        void ApplyRoots(IEnumerable<FolderModel> roots, string preferredPath, bool fallBackToInitial)
        {
            _roots.Clear();
            _rowsByEntry.Clear();
            if (roots != null)
                foreach (var root in roots)
                    if (root != null)
                    {
                        Prepare(root, null, root.Name);
                        _roots.Add(root);
                    }

            FolderModel folder = FindFolder(preferredPath);
            if (folder == null && fallBackToInitial)
                folder = FindFolder(_initialPath);
            _current = folder;
            ClearSelection();
            Rebuild();
            RaisePropertyChanged<string>(nameof(PathText));
        }

        void Rebuild()
        {
            _rows.Clear();
            string query = _search.Trim();
            if (_current == null)
            {
                if (query.Length > 0)
                    foreach (var root in _roots)
                        AddMatches(root, query, true);
                else
                    foreach (var root in _roots)
                        _rows.Add(RowFor(root));
            }
            else
            {
                _rows.Add(UpRow(_current));
                if (query.Length > 0)
                    AddMatches(_current, query, false);
                else
                {
                    _current.Folders.Sort(CompareFolders);
                    foreach (var child in _current.Folders)
                        if (child != null)
                            _rows.Add(RowFor(child));
                    _current.Files.Sort(CompareFiles);
                    foreach (var file in _current.Files)
                        if (file != null)
                            _rows.Add(RowFor(file));
                }
            }
            foreach (var row in _rows)
                row.Refresh();
            RaisePropertyChanged<IReadOnlyList<FileRow>>(nameof(Rows));
            RaisePropertyChanged<bool>(nameof(IsEmpty));
            RaisePropertyChanged<string>(nameof(EmptyText));
            RaisePropertyChanged<bool>(nameof(CanSelect));
            RefreshActions();
        }

        void AddMatches(FolderModel folder, string query, bool includeFolder)
        {
            if (includeFolder && Matches(folder.Name, folder.FullPath, folder.Subtitle, query))
                _rows.Add(RowFor(folder));
            folder.Files.Sort(CompareFiles);
            foreach (var file in folder.Files)
                if (file != null && Matches(file.Name, file.FullPath, file.Subtitle, query))
                    _rows.Add(RowFor(file));
            folder.Folders.Sort(CompareFolders);
            foreach (var child in folder.Folders)
                if (child != null)
                    AddMatches(child, query, true);
        }

        FileRow RowFor(object entry)
        {
            FileRow row;
            if (!_rowsByEntry.TryGetValue(entry, out row))
                _rowsByEntry[entry] = row = new FileRow(this, entry);
            return row;
        }

        FileRow UpRow(FolderModel current)
        {
            foreach (var pair in _rowsByEntry)
            {
                var up = pair.Key as UpKey;
                if (up != null && ReferenceEquals(up.Folder, current))
                    return pair.Value;
            }
            var key = new UpKey(current);
            var row = new FileRow(this, key);
            _rowsByEntry[key] = row;
            return row;
        }

        void RefreshActions()
        {
            _actions.Clear();
            if (!_loading && _contextActions != null)
            {
                FilePickerResult result = _selectedFile != null ? FileResult(_selectedFile)
                    : _selectedFolder != null ? FolderResult(_selectedFolder)
                    : null;
                var actions = result == null ? null : _contextActions(result);
                if (actions != null)
                    foreach (var action in actions)
                        if (action != null)
                            _actions.Add(new FileActionRow(this, action));
            }
            RaisePropertyChanged<IReadOnlyList<FileActionRow>>(nameof(Actions));
            RaisePropertyChanged<bool>(nameof(HasActions));
            RaisePropertyChanged<bool>(nameof(ShowSelectionSummary));
        }

        internal void Run(FilePickerContextAction action)
        {
            if (_loading || !action.Enabled || action.Clicked == null)
                return;
            action.Clicked();
            RefreshActions();
        }

        void RaiseHistory()
        {
            RaisePropertyChanged<bool>(nameof(CanGoBack));
            RaisePropertyChanged<bool>(nameof(CanGoForward));
        }

        FilePickerResult FileResult(FileModel file)
        {
            return new FilePickerResult { Mode = _mode, RootName = RootName(file.FullPath), FullPath = file.FullPath, File = file, Tag = file.Tag };
        }

        FilePickerResult FolderResult(FolderModel folder)
        {
            return new FilePickerResult { Mode = _mode, RootName = RootName(folder.FullPath), FullPath = folder.FullPath, Folder = folder, Tag = folder.Tag };
        }

        FolderModel FindFolder(string path)
        {
            path = Normalize(path);
            if (path.Length == 0)
                return null;
            foreach (var root in _roots)
            {
                var found = FindFolder(root, path);
                if (found != null)
                    return found;
            }
            return null;
        }

        static FolderModel FindFolder(FolderModel folder, string path)
        {
            if (string.Equals(Normalize(folder.FullPath), path, StringComparison.OrdinalIgnoreCase))
                return folder;
            foreach (var child in folder.Folders)
            {
                var found = child == null ? null : FindFolder(child, path);
                if (found != null)
                    return found;
            }
            return null;
        }

        static void Prepare(FolderModel folder, FolderModel parent, string path)
        {
            folder.Parent = parent;
            if (folder.Name == null)
                folder.Name = string.Empty;
            if (string.IsNullOrEmpty(folder.FullPath))
                folder.FullPath = string.IsNullOrEmpty(path) ? folder.Name : path;
            foreach (var child in folder.Folders)
                if (child != null)
                    Prepare(child, folder, string.IsNullOrEmpty(child.FullPath)
                        ? (string.IsNullOrEmpty(folder.FullPath) ? child.Name : folder.FullPath + "/" + child.Name)
                        : child.FullPath);
            foreach (var file in folder.Files)
            {
                if (file == null)
                    continue;
                if (file.Name == null)
                    file.Name = string.Empty;
                if (string.IsNullOrEmpty(file.FullPath))
                    file.FullPath = string.IsNullOrEmpty(folder.FullPath) ? file.Name : folder.FullPath + "/" + file.Name;
            }
        }

        static string KeyFor(FileModel file)
        {
            return file.Tag as string ?? file.FullPath;
        }

        static bool ContainsKey(List<string> keys, string key)
        {
            foreach (string item in keys)
                if (string.Equals(item, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static bool RemoveKey(List<string> keys, string key)
        {
            for (int i = 0; i < keys.Count; i++)
                if (string.Equals(keys[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    keys.RemoveAt(i);
                    return true;
                }
            return false;
        }

        static void Push(List<string> history, string path)
        {
            path = Normalize(path);
            if (history.Count > 0 && SamePath(history[history.Count - 1], path))
                return;
            history.Add(path);
            while (history.Count > HISTORY_LIMIT)
                history.RemoveAt(0);
        }

        static bool SamePath(string left, string right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }

        static string Normalize(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/').Trim('/');
        }

        static bool Matches(string name, string path, string subtitle, string query)
        {
            return Contains(name, query) || Contains(path, query) || Contains(subtitle, query);
        }

        static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static int CompareFolders(FolderModel left, FolderModel right)
        {
            return string.Compare(left == null ? null : left.Name, right == null ? null : right.Name, StringComparison.OrdinalIgnoreCase);
        }

        static int CompareFiles(FileModel left, FileModel right)
        {
            return string.Compare(left == null ? null : left.Name, right == null ? null : right.Name, StringComparison.OrdinalIgnoreCase);
        }

        static string RootName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;
            var normalized = path.Replace('\\', '/');
            int slash = normalized.IndexOf('/');
            return slash < 0 ? normalized : normalized.Substring(0, slash);
        }

        internal string BackToRootsText => Loc("LcdMod_FilePicker_BackToRoots");

        sealed class UpKey
        {
            public readonly FolderModel Folder;

            public UpKey(FolderModel folder)
            {
                Folder = folder;
            }
        }

        internal static object UpTarget(object entry)
        {
            var up = entry as UpKey;
            return up == null ? null : (object)up.Folder;
        }
    }

    internal sealed class FileRow : ObservableObject
    {
        readonly FilePickerViewModel _owner;
        readonly object _entry;
        string _classes = "dialog-row";

        public FileRow(FilePickerViewModel owner, object entry)
        {
            _owner = owner;
            _entry = entry;
            Command = new DelegateCommand(_ => _owner.Click(this));
            var current = FilePickerViewModel.UpTarget(entry) as FolderModel;
            IsUp = current != null;
            if (IsUp)
            {
                Folder = current == null ? null : current.Parent;
                Name = "..";
                Subtitle = Folder == null ? owner.BackToRootsText : Folder.FullPath;
                Icon = "Folder";
                return;
            }
            Folder = entry as FolderModel;
            File = entry as FileModel;
            Name = Folder != null ? Folder.Name : File.Name;
            Subtitle = (Folder != null ? Folder.Subtitle : File.Subtitle) ?? string.Empty;
            Icon = Folder != null ? "Folder" : !string.IsNullOrEmpty(File.Icon) ? File.Icon : IconFor(File);
        }

        public bool IsUp { get; }
        public FolderModel Folder { get; }
        public FileModel File { get; }
        public string Name { get; }
        public string Subtitle { get; }
        public bool HasSubtitle => Subtitle.Length > 0;
        public string Icon { get; }
        public ICommand Command { get; }
        public string Classes => _classes;
        public float FontSize => _owner.FontSize;
        public float SmallFontSize => _owner.SmallFontSize;

        internal void Refresh()
        {
            RaiseAndSetIfChanged(ref _classes, !IsUp && _owner.IsSelected(_entry) ? "dialog-row selected" : "dialog-row", nameof(Classes));
        }

        internal void RaiseFontSize()
        {
            RaisePropertyChanged<float>(nameof(FontSize));
            RaisePropertyChanged<float>(nameof(SmallFontSize));
        }

        static string IconFor(FileModel file)
        {
            string path = !string.IsNullOrEmpty(file.IconPath) ? file.IconPath : !string.IsNullOrEmpty(file.FullPath) ? file.FullPath : file.Name;
            string extension = System.IO.Path.GetExtension(path ?? string.Empty);
            if (string.Equals(extension, ".xwm", StringComparison.OrdinalIgnoreCase))
                return "FileXwm";
            if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".m3u", StringComparison.OrdinalIgnoreCase))
                return "FileWav";
            return "MissingIcon";
        }
    }

    internal sealed class FileActionRow : ObservableObject
    {
        readonly FilePickerViewModel _owner;

        public FileActionRow(FilePickerViewModel owner, FilePickerContextAction action)
        {
            _owner = owner;
            Text = action.Text ?? string.Empty;
            IsEnabled = action.Enabled;
            Classes = action.UseErrorTextStyle ? "dialog-button danger" : "dialog-button";
            Command = new DelegateCommand(_ => _owner.Run(action));
        }

        public string Text { get; }
        public bool IsEnabled { get; }
        public string Classes { get; }
        public ICommand Command { get; }
        public float FontSize => _owner.SmallFontSize;
    }
}
