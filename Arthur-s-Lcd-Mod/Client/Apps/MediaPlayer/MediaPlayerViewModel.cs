using Adk.Gui.Core.Input;
using System;
using System.Collections.Generic;
using LcdMod.Common.Helpers;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.MediaPlayer
{
    internal sealed class MediaPlayerViewModel : ObservableObject
    {
        const float MIN_BUTTON_HEIGHT = 34f;
        const float MAX_BUTTON_HEIGHT = 56f;
        const float SIDE_PADDING = 12f;
        const float LINE_GAP = 5f;

        readonly MediaPlayerApp _app;
        readonly List<MediaPlayerApp.MediaQueueRow> _queueData = new List<MediaPlayerApp.MediaQueueRow>();
        readonly List<MediaPlaylistRowModel> _rowPool = new List<MediaPlaylistRowModel>();
        IReadOnlyList<MediaPlaylistRowModel> _rows = new MediaPlaylistRowModel[0];
        Vector2 _viewport;
        int _visualizerVersion;

        public MediaPlayerViewModel(MediaPlayerApp app)
        {
            _app = app;
            PickCommand = new DelegateCommand(_ => Run(_app.MediaPick));
            ShuffleCommand = new DelegateCommand(_ => Run(_app.MediaShuffle));
            PreviousCommand = new DelegateCommand(_ => Run(_app.MediaPrevious));
            TogglePlayCommand = new DelegateCommand(_ => Run(_app.MediaTogglePlay));
            NextCommand = new DelegateCommand(_ => Run(_app.MediaNext));
            RepeatCommand = new DelegateCommand(_ => Run(_app.MediaRepeat));
            PlaylistCommand = new DelegateCommand(_ => Run(_app.MediaTogglePlaylist));
            StopCommand = new DelegateCommand(_ => Run(_app.MediaStop));
            ConfigureVisualizerCommand = new DelegateCommand(_ => Run(_app.OpenVisualizerSettings));
            ClearQueueCommand = new DelegateCommand(_ => Run(_app.MediaClearQueue));
            SaveQueueCommand = new DelegateCommand(_ => Run(_app.MediaSaveQueue));
        }

        public ICommand PickCommand { get; }
        public ICommand ConfigureVisualizerCommand { get; }
        public string ConfigureVisualizerText => _app.ConfigureVisualizerText;
        public ICommand ShuffleCommand { get; }
        public ICommand PreviousCommand { get; }
        public ICommand TogglePlayCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand RepeatCommand { get; }
        public ICommand PlaylistCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand ClearQueueCommand { get; }
        public ICommand SaveQueueCommand { get; }
        public MediaPlayerApp App => _app;

        public bool IsDefault { get; private set; } = true;
        public bool IsWide { get; private set; }
        public bool IsSmall { get; private set; }
        public bool IsRegular => !IsSmall;
        public float PlaylistGap { get; private set; }
        public Vector4 PlaylistGapMargin => new Vector4(0f, PlaylistGap, 0f, 0f);
        public Vector4 OverlayListMargin => new Vector4(PlaylistGap, 0f, PlaylistGap, 0f);
        public Vector4 OverlayRowGapMargin => new Vector4(0f, PlaylistGap, 0f, 0f);
        public Vector4 ProgressMargin { get; private set; }
        public float ActionRadius => ActionHeight * .5f;
        public float OverlayActionRadius => Math.Min(OverlayActionWidth, OverlayRowHeight) * .5f;
        public Vector4 AreaMargin { get; private set; }
        public float Gap { get; private set; }
        public float LayoutScale { get; private set; } = 1f;
        public string TextFont { get; private set; } = "White";

        public string SongPath { get; private set; } = string.Empty;
        public bool PathVisible { get; private set; }
        public float PathFontSize { get; private set; }
        public Vector4 PathMargin { get; private set; }
        public Color Foreground { get; private set; } = Color.White;

        public bool BodyVisible { get; private set; }
        public Vector4 BodyMargin { get; private set; }
        public bool ShowVisualizer { get; private set; }
        public bool ShowFileIcon { get; private set; }
        public bool ShowPlaylistBody { get; private set; }
        public Vector4 MediaIconMargin { get; private set; }
        public float[] VisualizerLevels => _app.MediaVisualizerLevels;
        public string VisualizerTexture => _app.MediaVisualizerTexture;
        public int VisualizerVersion => _visualizerVersion;
        public string FileIcon { get; private set; } = "FileWav";

        public bool ProgressVisible { get; private set; }
        public float ProgressHeight { get; private set; }
        public double PositionSeconds { get; private set; }
        public double DurationSeconds { get; private set; }
        public bool CanSeek { get; private set; }

        public float ButtonHeight { get; private set; }
        public Vector4 ButtonsMargin { get; private set; }
        public bool TransportVisible { get; private set; } = true;
        public bool PickEnabled { get; private set; }
        public bool LibraryEnabled { get; private set; }
        public bool PlayEnabled { get; private set; }
        public bool StopEnabled { get; private set; }
        public bool ShowPlaylistButton { get; private set; }
        public bool ShowStopButton { get; private set; }
        public Color PreviousColor { get; private set; }
        public Color NextColor { get; private set; }
        public Color PlayColor { get; private set; }
        public MediaGlyphKind PlayGlyph { get; private set; } = MediaGlyphKind.Play;
        public Color PlayGlyphColor { get; private set; }
        public float PlayGlyphRatio { get; private set; } = .62f;
        public Color ShuffleColor { get; private set; }
        public Color RepeatColor { get; private set; }
        public string RepeatSuffix { get; private set; } = string.Empty;
        public float RepeatSuffixFontSize { get; private set; } = 1f;
        public Color PlaylistColor { get; private set; }
        public Color ErrorColor { get; private set; }
        public float StopRatio { get; private set; } = .42f;

        public bool VolumeVisible { get; private set; }
        public float VolumeHeight { get; private set; }
        public Vector4 VolumeMargin { get; private set; }
        public float VolumeIconSize { get; private set; }
        public Vector4 VolumeSliderMargin { get; private set; }
        public float Volume { get; private set; } = 1f;
        public Color ActiveColor { get; private set; }
        public Color InactiveSurfaceColor { get; private set; }
        public Color InactiveForegroundColor { get; private set; }

        public bool PlaylistPanelVisible { get; private set; }
        public float PlaylistWidth { get; private set; }
        public Vector4 PlaylistMargin { get; private set; }
        public bool PlaylistCompact { get; private set; }
        public bool PlaylistNormal { get; private set; }
        public bool PlaylistOverlay { get; private set; }
        public float ActionHeight { get; private set; }
        public float OverlayRowHeight { get; private set; }
        public float OverlayActionWidth { get; private set; }
        public Vector4 VolumeOverlayMargin { get; private set; }
        public bool QueueHasItems { get; private set; }
        public Color QueueActionColor { get; private set; }
        public string ClearQueueText { get; private set; } = string.Empty;
        public string SaveQueueText { get; private set; } = string.Empty;
        public string QueueEmptyText { get; private set; } = string.Empty;
        public bool QueueEmpty { get; private set; }
        public float ActionFontSize { get; private set; }
        public float EmptyFontSize { get; private set; }
        public float ListRadius { get; private set; }
        public float RowHeight { get; private set; }
        public IReadOnlyList<MediaPlaylistRowModel> Rows => _rows;

        void Run(Action action)
        {
            action();
            Refresh();
        }

        public void SetViewport(Vector2 size)
        {
            if (_viewport == size)
                return;
            _viewport = size;
            Refresh();
        }

        public void RefreshFrame()
        {
            PositionSeconds = _app.MediaPositionSeconds;
            DurationSeconds = _app.MediaDurationSeconds;
            CanSeek = _app.MediaCanSeek;
            _visualizerVersion++;
            RaisePropertyChanged<double>(nameof(PositionSeconds));
            RaisePropertyChanged<double>(nameof(DurationSeconds));
            RaisePropertyChanged<bool>(nameof(CanSeek));
            RaisePropertyChanged<int>(nameof(VisualizerVersion));
            RaisePropertyChanged<string>(nameof(VisualizerTexture));
        }

        public bool HasFrameBoundContent => ProgressVisible || ShowVisualizer;

        public void Refresh()
        {
            float s = _app.MediaLayoutScale;
            float fs = _app.MediaFontScale;
            LayoutScale = s;
            TextFont = _app.MediaTextFont;
            Foreground = _app.MediaForeground;
            ActiveColor = _app.MediaActiveColor;
            InactiveSurfaceColor = _app.MediaInactiveSurfaceColor;
            InactiveForegroundColor = _app.MediaInactiveForegroundColor;
            ErrorColor = _app.MediaErrorColor;

            float padding = Math.Max(4f, SIDE_PADDING * s);
            AreaMargin = new Vector4(padding);
            float width = Math.Max(0f, _viewport.X - padding * 2f);
            float height = Math.Max(0f, _viewport.Y - padding * 2f);
            bool small = width > 0f && height > 0f && height / Math.Max(1f, width) < Constants.MIN_SCREEN_HEIGHT_TO_WIDTH_RATIO;
            bool wide = !small && width > 0f && height > 0f && width / Math.Max(1f, height) >= 1.25f;
            IsSmall = small;
            IsWide = wide;
            IsDefault = !small && !wide;

            bool playlistVisible = _app.MediaPlaylistVisible;
            bool queueHasItems = _app.MediaQueueCount > 0;
            QueueHasItems = queueHasItems;
            QueueEmpty = !queueHasItems;
            QueueActionColor = queueHasItems ? ActiveColor : InactiveSurfaceColor;
            ClearQueueText = _app.MediaClearQueueText;
            SaveQueueText = _app.MediaSaveQueueText;
            QueueEmptyText = _app.MediaQueueEmptyText;
            ActionFontSize = Math.Max(.42f, .58f * s * fs);
            EmptyFontSize = .58f * s * fs;
            ListRadius = Math.Max(3f, 5f * s);

            SongPath = _app.MediaSongPath;
            PathFontSize = .65f * s * fs;
            FileIcon = _app.MediaFileIcon;
            Volume = _app.MediaVolume;

            UpdateTransport(s, fs);
            if (small)
                LayoutSmall(width, height, s, playlistVisible);
            else
                LayoutRegular(width, height, s, playlistVisible, wide);

            RowHeight = PlaylistCompact ? Math.Max(26f * s, 30f * s * fs) : Math.Max(40f * s, 48f * s * fs);
            RefreshRows(s, fs);
            RefreshFrame();
            RaisePropertyChanged<object>(string.Empty);
        }

        void UpdateTransport(float s, float fs)
        {
            PickEnabled = _app.MediaPickEnabled;
            LibraryEnabled = _app.MediaLibraryEnabled;
            PlayEnabled = _app.MediaCanTogglePlay;
            StopEnabled = _app.MediaCanReset;
            PreviousColor = LibraryEnabled ? ActiveColor : InactiveSurfaceColor;
            NextColor = PreviousColor;
            PlayColor = PlayEnabled ? ActiveColor : InactiveSurfaceColor;
            bool shiftStop = _app.MediaShiftStop;
            PlayGlyph = shiftStop ? MediaGlyphKind.StopSquare : _app.MediaPlaying ? MediaGlyphKind.Pause : MediaGlyphKind.Play;
            PlayGlyphColor = shiftStop ? ErrorColor : Foreground;
            PlayGlyphRatio = PlayEnabled ? .42f : .34f;
            StopRatio = StopEnabled ? .42f : .34f;
            ShuffleColor = _app.MediaShuffleEnabled ? ActiveColor : InactiveForegroundColor;
            RepeatColor = _app.MediaRepeatActive ? ActiveColor : InactiveForegroundColor;
            RepeatSuffix = _app.MediaRepeatSuffix;
            RepeatSuffixFontSize = Math.Max(.8f, 1.05f * s * fs);
            PlaylistColor = _app.MediaPlaylistVisible ? ActiveColor : InactiveForegroundColor;
        }

        void LayoutRegular(float width, float height, float s, bool playlistVisible, bool wide)
        {
            float gap = Math.Max(4f, 8f * s);
            Gap = gap;
            float controlsWidth = width;
            PlaylistPanelVisible = wide;
            if (wide)
            {
                PlaylistWidth = Math.Max(120f * s, Math.Min(width * .42f, 280f * s));
                controlsWidth = Math.Max(1f, width - PlaylistWidth - gap);
                PlaylistMargin = new Vector4(gap, 0f, 0f, 0f);
            }

            bool pathVisible = wide || !playlistVisible;
            PathVisible = pathVisible;
            PathMargin = new Vector4(SIDE_PADDING * s, 6f * s, SIDE_PADDING * s, LINE_GAP * s);
            float pathHeight = pathVisible ? 6f * s + Math.Max(12f, PathFontSize * 30f) + LINE_GAP * s : 6f * s;

            float buttonHeight = MathHelper.Clamp(height * .18f, MIN_BUTTON_HEIGHT * s, MAX_BUTTON_HEIGHT * s);
            float volumeHeight = Math.Max(12f, 16f * s);
            bool volumeFits = height - volumeHeight - gap - buttonHeight >= 0f;
            float buttonTop = volumeFits ? height - volumeHeight - gap - buttonHeight : height - buttonHeight;
            float progressHeight = Math.Max(16f, 20f * s);
            bool progressFits = buttonTop - gap - progressHeight >= 0f;
            float controlsTop = progressFits ? buttonTop - gap - progressHeight : buttonTop;
            float bodyHeight = controlsTop - gap - (pathHeight + gap);

            ButtonHeight = buttonHeight;
            TransportVisible = true;
            ButtonsMargin = new Vector4(0f, progressFits ? gap : 0f, 0f, 0f);
            VolumeVisible = volumeFits;
            VolumeHeight = volumeHeight;
            VolumeMargin = new Vector4(0f, gap, 0f, 0f);
            UpdateVolumeIcons(volumeHeight, s);
            ProgressVisible = progressFits;
            ProgressHeight = progressHeight;
            ProgressMargin = new Vector4(0f, gap, 0f, 0f);
            PlaylistGap = Math.Max(3f, 5f * s);

            BodyVisible = bodyHeight >= Math.Max(18f, 30f * s);
            BodyMargin = new Vector4(0f, pathVisible ? gap : 6f * s + gap, 0f, 0f);
            bool playlistBody = !wide && playlistVisible;
            ShowPlaylistBody = BodyVisible && playlistBody;
            float innerPad = Math.Max(1f, 3f * s);
            MediaIconMargin = new Vector4(innerPad);
            ShowVisualizer = BodyVisible && !playlistBody && _app.MediaVisualizerEnabled;
            ShowFileIcon = BodyVisible && !playlistBody && !_app.MediaVisualizerEnabled;

            ShowStopButton = wide;
            ShowPlaylistButton = !wide;
            PlaylistCompact = false;
            PlaylistNormal = true;
            PlaylistOverlay = false;
            float playlistHeight = wide ? height : Math.Max(1f, bodyHeight);
            ActionHeight = Math.Max(22f * s, Math.Min(32f * s, playlistHeight * .22f));
        }

        void LayoutSmall(float width, float height, float s, bool playlistVisible)
        {
            float gap = Math.Max(3f, 5f * s);
            Gap = gap;
            PathVisible = false;
            PlaylistPanelVisible = false;
            ShowVisualizer = false;
            ShowFileIcon = false;
            ShowPlaylistBody = false;
            BodyVisible = false;
            ShowStopButton = false;
            ShowPlaylistButton = true;

            float buttonHeight = Math.Min(MathHelper.Clamp(height * .55f, MIN_BUTTON_HEIGHT * s, MAX_BUTTON_HEIGHT * s),
                Math.Max(1f, height));
            float volumeHeight = Math.Max(10f, 14f * s);
            bool volumeFits = height - volumeHeight - gap - buttonHeight >= 0f;
            float buttonTop = volumeFits ? height - volumeHeight - gap - buttonHeight : height - buttonHeight;
            float progressHeight = Math.Max(10f, 14f * s);
            ButtonHeight = buttonHeight;
            VolumeHeight = volumeHeight;
            VolumeVisible = volumeFits;
            VolumeMargin = new Vector4(0f, gap, 0f, 0f);
            UpdateVolumeIcons(volumeHeight, s);
            ProgressHeight = progressHeight;

            PlaylistOverlay = playlistVisible;
            PlaylistCompact = playlistVisible;
            PlaylistNormal = false;
            if (playlistVisible)
            {
                TransportVisible = false;
                ProgressVisible = false;
                float overlayHeight = volumeFits ? Math.Max(1f, height - volumeHeight - gap) : height;
                float overlayGap = Math.Max(2f, 4f * s);
                OverlayRowHeight = Math.Max(1f, (overlayHeight - overlayGap) * .5f);
                OverlayActionWidth = MathHelper.Clamp(width * .14f, Math.Max(OverlayRowHeight, 26f * s),
                    Math.Max(OverlayRowHeight, 72f * s));
                PlaylistGap = overlayGap;
                VolumeOverlayMargin = new Vector4(0f, gap, 0f, 0f);
                return;
            }

            TransportVisible = true;
            ProgressMargin = Vector4.Zero;
            ProgressVisible = buttonTop - gap - progressHeight >= 0f;
            ButtonsMargin = new Vector4(0f, ProgressVisible ? gap : 0f, 0f, 0f);
        }

        void UpdateVolumeIcons(float volumeHeight, float s)
        {
            VolumeIconSize = Math.Max(1f, Math.Min(volumeHeight, 18f * s));
            float iconGap = Math.Max(2f, 5f * s);
            VolumeSliderMargin = new Vector4(iconGap, 0f, iconGap, 0f);
        }

        void RefreshRows(float s, float fs)
        {
            _app.FillQueueRows(_queueData);
            while (_rowPool.Count < _queueData.Count)
                _rowPool.Add(new MediaPlaylistRowModel(this, _rowPool.Count));
            bool changed = _rows.Count != _queueData.Count;
            int current = _app.MediaQueueIndex;
            for (int i = 0; i < _queueData.Count; i++)
                _rowPool[i].Update(_queueData[i], i == current, PlaylistCompact, s, fs, RowHeight, Foreground,
                    InactiveForegroundColor);
            if (changed)
            {
                var rows = new MediaPlaylistRowModel[_queueData.Count];
                for (int i = 0; i < rows.Length; i++)
                    rows[i] = _rowPool[i];
                _rows = rows;
            }
        }

        internal void PlayRow(int index)
        {
            Run(() => _app.MediaPlayQueueEntry(index));
        }

        internal void MoveRow(int index, int rows)
        {
            Run(() => _app.MediaMoveQueueEntry(index, index + rows));
        }
    }

    internal sealed class MediaPlaylistRowModel : ObservableObject
    {
        readonly MediaPlayerViewModel _owner;
        readonly int _index;
        string _title = string.Empty;
        string _detail = string.Empty;
        string _compactText = string.Empty;
        string _icon = "FileWav";
        bool _isCurrent;
        bool _compact;
        float _titleFontSize;
        float _detailFontSize;
        float _compactFontSize;
        float _iconSize;
        float _padding;
        float _handleWidth;
        float _rowHeight;
        Color _detailColor;
        Color _iconColor;
        Color _titleColor;

        public MediaPlaylistRowModel(MediaPlayerViewModel owner, int index)
        {
            _owner = owner;
            _index = index;
            Command = new DelegateCommand(_ => _owner.PlayRow(_index));
        }

        public ICommand Command { get; }
        public string Title => _title;
        public string Detail => _detail;
        public string CompactText => _compactText;
        public string Icon => _icon;
        public bool IsCurrent => _isCurrent;
        public bool IsCompact => _compact;
        public bool IsNormal => !_compact;
        public float TitleFontSize => _titleFontSize;
        public float DetailFontSize => _detailFontSize;
        public float CompactFontSize => _compactFontSize;
        public float IconSize => _iconSize;
        public Vector4 RowPadding => new Vector4(_padding, 0f, _padding, 0f);
        public float HandleWidth => _handleWidth;
        public float RowHeight => _rowHeight;
        public Color DetailColor => _detailColor;
        public Color IconColor => _iconColor;
        public Color TitleColor => _titleColor;
        public int Index => _index;

        internal void Move(int rows)
        {
            if (rows != 0)
                _owner.MoveRow(_index, rows);
        }

        internal void Update(MediaPlayerApp.MediaQueueRow row, bool current, bool compact, float s, float fs, float rowHeight,
            Color foreground, Color secondary)
        {
            RaiseAndSetIfChanged(ref _title, row.Title ?? string.Empty, nameof(Title));
            RaiseAndSetIfChanged(ref _detail, row.Detail ?? string.Empty, nameof(Detail));
            RaiseAndSetIfChanged(ref _compactText, string.IsNullOrEmpty(row.Detail) ? _title : _title + "  " + row.Detail,
                nameof(CompactText));
            RaiseAndSetIfChanged(ref _icon, string.IsNullOrEmpty(row.Icon) ? "FileWav" : row.Icon, nameof(Icon));
            RaiseAndSetIfChanged(ref _isCurrent, current, nameof(IsCurrent));
            if (RaiseAndSetIfChanged(ref _compact, compact, nameof(IsCompact)))
                RaisePropertyChanged<bool>(nameof(IsNormal));
            RaiseAndSetIfChanged(ref _titleFontSize, Math.Max(.40f, .52f * s * fs), nameof(TitleFontSize));
            RaiseAndSetIfChanged(ref _detailFontSize, Math.Max(.34f, .42f * s * fs), nameof(DetailFontSize));
            RaiseAndSetIfChanged(ref _compactFontSize, Math.Max(.36f, .46f * s * fs), nameof(CompactFontSize));
            float padding = Math.Max(4f, 6f * s);
            if (RaiseAndSetIfChanged(ref _padding, padding, nameof(RowPadding)))
                RaisePropertyChanged<Vector4>(nameof(RowPadding));
            RaiseAndSetIfChanged(ref _rowHeight, rowHeight, nameof(RowHeight));
            RaiseAndSetIfChanged(ref _iconSize, Math.Max(12f, Math.Min(rowHeight - padding * 2f, 30f * s)), nameof(IconSize));
            RaiseAndSetIfChanged(ref _handleWidth, compact ? Math.Max(18f * s, 22f * s) : Math.Max(24f * s, 30f * s),
                nameof(HandleWidth));
            RaiseAndSetIfChanged(ref _detailColor, current ? foreground : secondary, nameof(DetailColor));
            RaiseAndSetIfChanged(ref _iconColor, foreground, nameof(IconColor));
            RaiseAndSetIfChanged(ref _titleColor, current ? foreground : secondary, nameof(TitleColor));
        }
    }
}
