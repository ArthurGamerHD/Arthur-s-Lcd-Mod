using System;
using System.Collections.Generic;
using System.Text;
using Generated;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Audio;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Dialogs;
using LcdMod.Client.Gui.ControlsTemplates.Lists;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Utils;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using InteractiveSurfaceScript = LcdMod.Client.SurfaceScripts.Abstract.InteractiveSurfaceScript;

namespace LcdMod.Client.Apps
{
    [LcdApp(28)]
    [ConfigComponent(APP, typeof(MediaPlayerConfigComponent), PropertyName = "MediaPlayerComponent")]
    public sealed partial class MediaPlayerApp : App, IApp
    {
        const float MIN_BUTTON_HEIGHT = 34f;
        const float MAX_BUTTON_HEIGHT = 56f;
        const float TINY_HEIGHT_TO_WIDTH_RATIO = MIN_SCREEN_HEIGHT_TO_WIDTH_RATIO;
        const float WIDE_WIDTH_TO_HEIGHT_RATIO = 1.25f;
        const float SIDE_PADDING = 12f;
        const float LINE_GAP = 5f;
        const int DEFAULT_VISUALIZER_BARS = 32;
        const string PICK_ICON = "Folder";
        const string SHUFFLE_ICON = "Shuffle";
        const string REPEAT_ICON = "Repeat";
        const string SAVE_ICON = "Diskette";
        const string SOUND_LOW_ICON = "SoundLow";
        const string SOUND_HIGH_ICON = "SoundHigh";
        const string PLAYLIST_SAVE_FILE_PREFIX = "music_cache_playlist_";
        const string PLAYLIST_INDEX_FILE = "music_cache_playlists.txt";
        const string PLAYLIST_FILE_EXTENSION = ".m3u";
        const string FAVORITES_PLAYLIST_FILE = "music_cache_favorites.m3u";

        static readonly object LibraryLock = new object();
        static readonly List<FolderModel> EmptyFolderRoots = new List<FolderModel>(0);
        static MediaItem[] _cachedLibrary;
        static SoundCategoryNameLookup _soundCategoryNameLookup;

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<Control> _children = new List<Control>();
        readonly MediaPlayerRootPanel _rootPanel;
        readonly TextBlock _trackPathText;
        readonly TextBlock _playlistEmptyText;
        readonly MediaIconControl _audioIcon;
        readonly MediaIconControl _volumeLowIcon;
        readonly MediaIconControl _volumeHighIcon;
        readonly Button _pickButton;
        readonly Button _previousButton;
        readonly ToggleButton _shuffleButton;
        readonly ToggleButton _playButton;
        readonly ToggleButton _repeatButton;
        readonly Button _nextButton;
        readonly ToggleButton _playlistButton;
        readonly Button _stopButton;
        readonly Button _clearQueueButton;
        readonly Button _saveQueueButton;
        readonly ListBoxModel<PlaylistEntry> _playlistListModel;
        readonly ListBox<PlaylistEntry> _playlistListBox;
        readonly AudioProgressModel _audioProgressModel;
        readonly AudioProgress _audioProgress;
        readonly AudioVisualizerModel _audioVisualizerModel;
        readonly AudioVisualizer _audioVisualizer;
        readonly HorizontalSliderModel _volumeSliderModel;
        readonly HorizontalSlider _volumeSlider;
        readonly float[] _visualizerLevels = new float[DEFAULT_VISUALIZER_BARS];
        readonly float[] _visualizerTargetLevels = new float[DEFAULT_VISUALIZER_BARS];
        static readonly StyleTree PlaylistListStyles = BuildPlaylistListStyles();
        bool _visualizerFrameScheduled;
        bool _handledPlaybackCompletion;
        bool _handlingPlaybackCompletion;
        bool _restorePickedAudioAttempted;
        readonly Random _shuffleRandom = new Random();
        readonly List<PlaylistEntry> _queue = new List<PlaylistEntry>();
        readonly List<PlaylistEntry> _selectedQueueEntries = new List<PlaylistEntry>(1);
        readonly List<PlaylistEntry> _preShuffleQueue = new List<PlaylistEntry>();
        bool _hasPreShuffleQueue;
        bool _restoreQueueAttempted;
        bool _lastScreenInteractionWasLocal = true;
        bool _suppressConfigSync;
        bool _playlistVisible;
        bool _playlistForceVisibleForLayout;
        bool _playlistCompactMode;
        bool _playlistAutoScrollAllowed;
        int _playlistAutoScrollQueueIndex = -1;
        bool _startingQueueEntry;

        MediaItem[] _library = EmptyLibrary;
        int _selectedIndex = -1;
        int _queueIndex = -1;
        int _shuffleSeed;
        MediaPlayerConfigComponent _lastMediaPlayerComponent;
        GridMediaPlayer _player;
        MediaAudioFileReference _pickedAudio;
        InteractiveSurfaceScript _interactiveHost;

        static readonly MediaItem[] EmptyLibrary = Array.Empty<MediaItem>();

        public string Title
        {
            get
            {
                var title = GetCurrentSongName();
                if (_player != null && _player.IsPaused)
                {
                    var pausedTitle = string.IsNullOrEmpty(title) ? ResolveLoc(LOC_TITLE) : title;
                    return string.Format(FormatingHelper.Culture, ResolveLoc(LOC_PAUSED_TITLE_FORMAT), pausedTitle);
                }

                if (_player != null && _player.IsPlaying && !string.IsNullOrEmpty(title))
                    return title;

                return LOC_TITLE;
            }
        }

        const string LOC_TITLE = MOD_PREFIX + "MediaPlayer_Title";
        const string LOC_PAUSED_TITLE_FORMAT = MOD_PREFIX + "MediaPlayer_PausedTitleFormat";
        const string LOC_NO_SUPPORTED_AUDIO = MOD_PREFIX + "MediaPlayer_NoSupportedAudio";
        const string LOC_QUEUE_EMPTY = MOD_PREFIX + "MediaPlayer_QueueEmpty";
        const string LOC_CLEAR_QUEUE = MOD_PREFIX + "MediaPlayer_ClearQueue";
        const string LOC_SAVE_QUEUE = MOD_PREFIX + "MediaPlayer_SaveQueue";
        const string LOC_PICK = MOD_PREFIX + "MediaPlayer_Pick";
        const string LOC_SHUFFLE = MOD_PREFIX + "MediaPlayer_Shuffle";
        const string LOC_PREVIOUS = MOD_PREFIX + "MediaPlayer_Previous";
        const string LOC_PLAY = MOD_PREFIX + "MediaPlayer_Play";
        const string LOC_PAUSE = MOD_PREFIX + "MediaPlayer_Pause";
        const string LOC_NEXT = MOD_PREFIX + "MediaPlayer_Next";
        const string LOC_PLAYLIST = MOD_PREFIX + "MediaPlayer_Playlist";
        const string LOC_STOP = MOD_PREFIX + "MediaPlayer_Stop";
        const string LOC_REPEAT = MOD_PREFIX + "MediaPlayer_Repeat";
        const string LOC_UNKNOWN_LENGTH = MOD_PREFIX + "MediaPlayer_UnknownLength";
        const string LOC_PICK_AUDIO = MOD_PREFIX + "MediaPlayer_PickAudio";
        const string LOC_LOADING_AUDIO_FILES = MOD_PREFIX + "MediaPlayer_LoadingAudioFiles";
        const string LOC_SAVE_PLAYLIST_TITLE = MOD_PREFIX + "MediaPlayer_SavePlaylistTitle";
        const string LOC_SAVE_PLAYLIST_PROMPT = MOD_PREFIX + "MediaPlayer_SavePlaylistPrompt";
        const string LOC_PLAYLIST_NAME_EMPTY = MOD_PREFIX + "MediaPlayer_PlaylistNameEmpty";
        const string LOC_PLAYLIST_SAVED_FORMAT = MOD_PREFIX + "MediaPlayer_PlaylistSavedFormat";
        const string LOC_CONTEXT_ADD_TO_QUEUE = MOD_PREFIX + "MediaPlayer_Context_AddToQueue";
        const string LOC_CONTEXT_ADD_NEXT = MOD_PREFIX + "MediaPlayer_Context_AddNext";
        const string LOC_CONTEXT_PLAY_NOW = MOD_PREFIX + "MediaPlayer_Context_PlayNow";
        const string LOC_CONTEXT_FAVORITE = MOD_PREFIX + "MediaPlayer_Context_Favorite";
        const string LOC_CONTEXT_ADD_ALL = MOD_PREFIX + "MediaPlayer_Context_AddAll";
        const string LOC_CONTEXT_PLAY_ALL = MOD_PREFIX + "MediaPlayer_Context_PlayAll";
        const string LOC_CONTEXT_DELETE = MOD_PREFIX + "MediaPlayer_Context_Delete";
        const string LOC_DELETE_LOCAL_AUDIO_TITLE = MOD_PREFIX + "MediaPlayer_DeleteLocalAudioTitle";
        const string LOC_DELETE_LOCAL_AUDIO_PROMPT_FORMAT = MOD_PREFIX + "MediaPlayer_DeleteLocalAudioPromptFormat";
        const string LOC_DELETE_PLAYLIST_TITLE = MOD_PREFIX + "MediaPlayer_DeletePlaylistTitle";
        const string LOC_DELETE_PLAYLIST_PROMPT_FORMAT = MOD_PREFIX + "MediaPlayer_DeletePlaylistPromptFormat";
        const string LOC_DELETE_FAILED_FORMAT = MOD_PREFIX + "MediaPlayer_DeleteFailedFormat";
        const string LOC_LOCAL_AUDIO_DELETED_FORMAT = MOD_PREFIX + "MediaPlayer_LocalAudioDeletedFormat";
        const string LOC_PLAYLIST_DELETED_FORMAT = MOD_PREFIX + "MediaPlayer_PlaylistDeletedFormat";
        const string LOC_COMMON_CANCEL = MOD_PREFIX + "Common_Button_Cancel";

        readonly Dictionary<string, string> _locKeysCache = new Dictionary<string, string>();

        delegate bool AudioDeleteOperation(out string failureReason);

        sealed class AudioDeleteWork
        {
            public bool Deleted;
            public string FailureReason;
            public Exception Error;
        }

        sealed class MediaItem
        {
            public string Subtype;
            public string DisplayName;
            public string WavePath;
            public GameAudioContainerKind ContainerKind;
        }

        sealed class SoundCategoryNameLookup
        {
            public readonly Dictionary<string, string> BySoundId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> ByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        sealed class PlaylistEntry
        {
            public MediaAudioFileReference Reference;
            public string SoundSubtype;
            public string Title;
            public string Path;
            public string Detail;
            public string Icon;

            public override string ToString()
            {
                return string.IsNullOrEmpty(Title) ? (Path ?? string.Empty) : Title;
            }
        }

        enum MediaButtonShape
        {
            Rounded,
            Circle,
            LeftRounded,
            RightRounded,
            Transparent
        }

        enum MediaButtonContent
        {
            Text,
            Folder,
            StopSquare,
            Playlist,
            PreviousTrack,
            NextTrack,
            PlayToggle,
            Shuffle,
            Repeat,
            SaveIcon
        }

        enum MediaRepeatMode
        {
            Disabled = 0,
            Single = 1,
            Folder = 2
        }

        enum MediaPlayerLayoutMode
        {
            Default,
            Wide,
            Small
        }

        sealed class MediaButtonModel : ButtonModel
        {
            public MediaButtonContent Content;
            public string DisplayText;
            public MediaButtonShape Shape;
            public RectangleF? DecoratorRect;
        }

        sealed class MediaPlayerRootPanel : Panel
        {
            readonly MediaPlayerApp _owner;

            public MediaPlayerRootPanel(MediaPlayerApp owner, RectangleF bounds)
                : base(bounds)
            {
                _owner = owner;
            }

            protected override void ArrangeChildren()
            {
                if (_owner != null)
                    _owner.UpdateMediaVisualTree(Bounds);

                base.ArrangeChildren();
            }
        }

        sealed class MediaButtonControl : Button
        {
            readonly MediaPlayerApp _owner;

            public MediaButtonControl(MediaPlayerApp owner, RectangleF bounds, MediaButtonModel model)
                : base(bounds, model)
            {
                _owner = owner;
            }

            protected override void RenderDefault(List<MySprite> sprites)
            {
                if (_owner == null || _owner.ShouldUseDefaultMediaButtonRender(DataContext as MediaButtonModel))
                {
                    base.RenderDefault(sprites);
                    return;
                }

                _owner.RenderMediaButtonVisual(this, sprites);
            }
        }

        sealed class MediaToggleButtonControl : ToggleButton
        {
            readonly MediaPlayerApp _owner;

            public MediaToggleButtonControl(MediaPlayerApp owner, RectangleF bounds, MediaButtonModel model)
                : base(bounds, model)
            {
                _owner = owner;
            }

            protected override void RenderDefault(List<MySprite> sprites)
            {
                if (_owner == null || _owner.ShouldUseDefaultMediaButtonRender(DataContext as MediaButtonModel))
                {
                    base.RenderDefault(sprites);
                    return;
                }

                _owner.RenderMediaButtonVisual(this, sprites);
            }
        }

        sealed class MediaIconControl : RectangleControl
        {
            public string Icon;
            public float SizeRatio = 1f;
            public Color IconColor = Color.White;

            public MediaIconControl(RectangleF bounds)
                : base(bounds, CursorType.Default)
            {
            }

            protected override void RenderDefault(List<MySprite> sprites)
            {
                if (string.IsNullOrEmpty(Icon))
                    return;

                var rect = Bounds;
                if (rect.Width <= 0f || rect.Height <= 0f)
                    return;

                var size = Math.Max(1f, Math.Min(rect.Width, rect.Height) * Math.Max(.01f, SizeRatio));
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = Icon,
                    Position = rect.Center,
                    Size = new Vector2(size, size),
                    Color = IconColor,
                    Alignment = TextAlignment.CENTER
                });
            }
        }

        public MediaPlayerApp(IAppHost host) : base(host)
        {
            _interactiveHost = host as InteractiveSurfaceScript;
            _rootPanel = AddLogicalChild(new MediaPlayerRootPanel(this, default(RectangleF)));
            _children.Add(_rootPanel);
            _trackPathText = new TextBlock(default(RectangleF))
            {
                Ellipsize = true,
                HorizontalAlignment = TextAlignment.LEFT,
                VerticalAlignment = TextBlockVerticalAlignment.Top,
                FontScale = .65f
            };
            _playlistEmptyText = new TextBlock(default(RectangleF))
            {
                Ellipsize = true,
                HorizontalAlignment = TextAlignment.CENTER,
                VerticalAlignment = TextBlockVerticalAlignment.Center,
                FontScale = .58f
            };
            _audioIcon = new MediaIconControl(default(RectangleF)) { SizeRatio = .72f };
            _volumeLowIcon = new MediaIconControl(default(RectangleF)) { Icon = SOUND_LOW_ICON, SizeRatio = 1f };
            _volumeHighIcon = new MediaIconControl(default(RectangleF)) { Icon = SOUND_HIGH_ICON, SizeRatio = 1f };
            _pickButton = CreateButton(LOC_PICK, PickAudio);
            _shuffleButton = new MediaToggleButtonControl(this, default(RectangleF), new MediaButtonModel
            {
                Text = ResolveLoc(LOC_SHUFFLE),
                DisplayText = string.Empty,
                Content = MediaButtonContent.Shuffle,
                Clicked = ToggleShuffle,
                Enabled = true
            })
            {
                GetState = IsShuffleEnabled
            };
            _previousButton = CreateButton(LOC_PREVIOUS, Previous);
            _playButton = new MediaToggleButtonControl(this, default(RectangleF), new MediaButtonModel
            {
                Text = ResolveLoc(LOC_PLAY),
                DisplayText = ResolveLoc(LOC_PLAY),
                Content = MediaButtonContent.Text,
                Clicked = TogglePlay,
                Enabled = true
            })
            {
                GetState = IsPlayToggleActive
            };
            _nextButton = CreateButton(LOC_NEXT, Next);
            _playlistButton = new MediaToggleButtonControl(this, default(RectangleF), new MediaButtonModel
            {
                Text = ResolveLoc(LOC_PLAYLIST),
                DisplayText = string.Empty,
                Content = MediaButtonContent.Playlist,
                Clicked = TogglePlaylist,
                Enabled = true
            })
            {
                GetState = IsPlaylistVisible
            };
            _stopButton = CreateButton(LOC_STOP, StopClicked);
            _repeatButton = new MediaToggleButtonControl(this, default(RectangleF), new MediaButtonModel
            {
                Text = ResolveLoc(LOC_REPEAT),
                DisplayText = string.Empty,
                Content = MediaButtonContent.Repeat,
                Clicked = CycleRepeatMode,
                Enabled = true
            })
            {
                GetState = IsRepeatActive
            };
            _clearQueueButton = CreateButton(LOC_CLEAR_QUEUE, ClearQueue);
            _saveQueueButton = CreateButton(LOC_SAVE_QUEUE, SaveQueue);
            _playlistListModel = new ListBoxModel<PlaylistEntry>
            {
                Items = _queue,
                SelectedEntries = _selectedQueueEntries,
                MultiSelect = false,
                SelectionEnabled = true,
                TextSelector = GetPlaylistEntryText,
                EntryClicked = OnPlaylistEntryClicked,
                ItemRenderer = RenderPlaylistEntryItem,
                EntryMoved = MovePlaylistEntry,
                DragTargetIndexFilter = ResolvePlaylistDragTargetIndex
            };
            _playlistListBox = new ListBox<PlaylistEntry>(default(RectangleF), _playlistListModel);
            _playlistListBox.SetStyles(PlaylistListStyles);
            _playlistListBox.ScrollPanel.ScrollChanged = OnPlaylistScrollChanged;
            _audioProgressModel = new AudioProgressModel
            {
                SeekRequested = SeekToPosition
            };
            _audioProgress = new AudioProgress(default(RectangleF), _audioProgressModel);
            _audioVisualizerModel = new AudioVisualizerModel
            {
                BarCount = DEFAULT_VISUALIZER_BARS,
                BarLevels = _visualizerLevels
            };
            _audioVisualizer = new AudioVisualizer(default(RectangleF), _audioVisualizerModel);
            _volumeSliderModel = new HorizontalSliderModel
            {
                Value = 1f,
                ValueChanged = SetPlaybackVolume
            };
            _volumeSlider = new HorizontalSlider(default(RectangleF), _volumeSliderModel);
            AddMediaVisualChildren();
        }

        public override IReadOnlyList<Control> VisualChildren => _children;

        public override void MarkDirty()
        {
            base.MarkDirty();
            if (_rootPanel != null)
                _rootPanel.InvalidateLayout();
        }

        internal void RebindHost(IAppHost host)
        {
            RebindAppHost(host);
            _interactiveHost = host as InteractiveSurfaceScript;
            LayoutChanged();
            MarkDirty();
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            _locKeysCache.Clear();
        }

        string ResolveLoc(string key)
        {
            string text;
            if (_locKeysCache.TryGetValue(key, out text))
                return text;

            text = LocHelper.GetLoc(key);
            _locKeysCache[key] = text;
            return text;
        }

        void ObserveMediaConfigOrigin()
        {
            var component = MediaPlayerComponent;
            if (ReferenceEquals(_lastMediaPlayerComponent, component))
                return;

            if (_lastMediaPlayerComponent != null)
            {
                _lastScreenInteractionWasLocal = false;
                _restorePickedAudioAttempted = false;
                _restoreQueueAttempted = false;
                ClearPreShuffleQueue();
            }

            _lastMediaPlayerComponent = component;
        }

        void MarkLocalMediaInteraction()
        {
            _lastScreenInteractionWasLocal = true;
        }

        void EnsureShuffleSeed()
        {
            _shuffleSeed = MediaPlayerComponent.ShuffleSeed;
            if (_shuffleSeed != 0)
                return;

            ResetShuffleSeed();
        }

        void ResetShuffleSeed()
        {
            var next = _shuffleRandom.Next(1, int.MaxValue);
            _shuffleSeed = next;
            MediaPlayerComponent.ShuffleSeed = next;
        }

        public override void Update()
        {
            EnsureLibrary();
            _player = GetHostMediaPlayer();
            if (_player != null && _volumeSliderModel != null)
                _player.Volume = _volumeSliderModel.Value;
            ObserveMediaConfigOrigin();
            EnsureShuffleSeed();
            RestorePickedAudioFromConfig();
            NormalizeSelectionFromConfig();
            RestoreQueueFromConfig();
            HandlePlaybackCompletion();
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();

            if (_rootPanel != null)
            {
                _rootPanel.SetRect(GetContentArea());
                UpdateFrameBoundControls();
                _rootPanel.Render(_sprites);
            }

            ClearDirtyAfterRender();
            return _sprites;
        }

        Button CreateButton(string locKey, Action<ButtonModel, object> clicked)
        {
            var text = ResolveLoc(locKey);
            return new MediaButtonControl(this, default(RectangleF), new MediaButtonModel
            {
                Text = text,
                DisplayText = text,
                Content = MediaButtonContent.Text,
                Clicked = clicked,
                Enabled = true
            });
        }

        void AddMediaVisualChildren()
        {
            if (_rootPanel == null)
                return;

            _rootPanel.AddChild(_trackPathText);
            _rootPanel.AddChild(_audioIcon);
            _rootPanel.AddChild(_audioVisualizer);
            _rootPanel.AddChild(_clearQueueButton);
            _rootPanel.AddChild(_saveQueueButton);
            _rootPanel.AddChild(_playlistListBox);
            _rootPanel.AddChild(_playlistEmptyText);
            _rootPanel.AddChild(_audioProgress);
            _rootPanel.AddChild(_volumeLowIcon);
            _rootPanel.AddChild(_volumeSlider);
            _rootPanel.AddChild(_volumeHighIcon);
            _rootPanel.AddChild(_pickButton);
            _rootPanel.AddChild(_shuffleButton);
            _rootPanel.AddChild(_previousButton);
            _rootPanel.AddChild(_nextButton);
            _rootPanel.AddChild(_playButton);
            _rootPanel.AddChild(_repeatButton);
            _rootPanel.AddChild(_playlistButton);
            _rootPanel.AddChild(_stopButton);
        }

        RectangleF GetContentArea()
        {
            var area = Host.ViewBox;
            if (area.Width <= 0f || area.Height <= 0f)
                return area;

            var scale = GeneralComponent.GetScale();
            var padding = Math.Max(4f, SIDE_PADDING * scale);
            var titleHeight = Host.TitleVisible
                ? 40f * scale * Host.Surface.FontSize
                : 0f;
            return new RectangleF(
                area.X + padding,
                area.Y + titleHeight + padding,
                Math.Max(0f, area.Width - padding * 2f),
                Math.Max(0f, area.Height - titleHeight - padding * 2f));
        }

        string GetCurrentSongName()
        {
            if (_pickedAudio != null)
                return GetPickedAudioSongName(_pickedAudio);

            if (HasLibrarySelection())
            {
                var selected = _library[_selectedIndex];
                if (!string.IsNullOrEmpty(selected.DisplayName))
                    return selected.DisplayName;

                return GetFileNameWithoutExtension(selected.WavePath);
            }

            return string.Empty;
        }

        string GetCurrentSongPath()
        {
            if (_pickedAudio != null)
            {
                if (_pickedAudio.IsLocal && _pickedAudio.LocalAsset != null)
                {
                    if (!string.IsNullOrEmpty(_pickedAudio.LocalAsset.SourceArchivePath))
                        return "Local/" + _pickedAudio.LocalAsset.SourceArchivePath.Replace('\\', '/');

                    if (!string.IsNullOrEmpty(_pickedAudio.LocalAsset.SourcePath))
                        return "Local/" + _pickedAudio.LocalAsset.SourcePath.Replace('\\', '/');
                }

                if (!string.IsNullOrEmpty(_pickedAudio.DefinitionPath))
                    return "Content/" + _pickedAudio.DefinitionPath.Replace('\\', '/');

                return GetPickedAudioTitle(_pickedAudio);
            }

            if (HasLibrarySelection())
            {
                var selected = _library[_selectedIndex];
                return string.IsNullOrEmpty(selected.WavePath)
                    ? selected.Subtype
                    : "Content/" + selected.WavePath.Replace('\\', '/');
            }

            return string.Empty;
        }

        void UpdateMediaVisualTree(RectangleF area)
        {
            _playlistForceVisibleForLayout = false;
            _playlistCompactMode = false;

            var layoutMode = GetMediaLayoutMode(area);
            if (layoutMode == MediaPlayerLayoutMode.Small)
            {
                HideControl(_trackPathText);
                ArrangeSmallMediaControls(area);
                return;
            }

            if (layoutMode == MediaPlayerLayoutMode.Wide)
            {
                _playlistForceVisibleForLayout = true;
                ArrangeWideMediaControls(area);
                return;
            }

            var y = ArrangeTrackPath(area, true);
            ArrangeMediaControls(area, y);
        }

        float ArrangeTrackPath(RectangleF area, bool hideWhenPlaylistVisible)
        {
            var scale = GeneralComponent.GetScale();
            var y = area.Y + Math.Max(0f, 6f * scale);
            var textX = area.X + SIDE_PADDING * scale;
            var textWidth = Math.Max(1f, area.Width - 2f * SIDE_PADDING * scale);
            var detailScale = Math.Max(.45f, .65f * scale);

            if (_trackPathText != null)
            {
                if (hideWhenPlaylistVisible && _playlistVisible)
                {
                    _trackPathText.SetVisible(false);
                }
                else
                {
                    var songPath = GetCurrentSongPath();
                    if (string.IsNullOrEmpty(songPath))
                        songPath = ResolveLoc(LOC_NO_SUPPORTED_AUDIO);

                    _trackPathText.Text = songPath;
                    _trackPathText.FontScale = .65f;
                    _trackPathText.TextColor = Host.ForegroundColor;
                    _trackPathText.SetRect(new RectangleF(textX, y, textWidth, Math.Max(1f, MeasureLineHeight(detailScale))));
                    _trackPathText.SetVisible(true);
                    _trackPathText.SetEnabled(false);
                    y += Math.Max(12f, MeasureLineHeight(detailScale)) + LINE_GAP * scale;
                }
            }

            return y;
        }

        static MediaPlayerLayoutMode GetMediaLayoutMode(RectangleF area)
        {
            if (area.Width <= 0f || area.Height <= 0f)
                return MediaPlayerLayoutMode.Default;

            var heightToWidth = area.Height / Math.Max(1f, area.Width);
            if (heightToWidth < TINY_HEIGHT_TO_WIDTH_RATIO)
                return MediaPlayerLayoutMode.Small;

            var widthToHeight = area.Width / Math.Max(1f, area.Height);
            return widthToHeight >= WIDE_WIDTH_TO_HEIGHT_RATIO
                ? MediaPlayerLayoutMode.Wide
                : MediaPlayerLayoutMode.Default;
        }

        void ArrangeMediaControls(RectangleF area, float contentBottom, bool allowPlaylistBody = true, bool useStopButton = false)
        {
            var scale = GeneralComponent.GetScale();
            var gap = Math.Max(4f, 8f * scale);
            var buttonHeight = MathHelper.Clamp(area.Height * .18f, MIN_BUTTON_HEIGHT * scale, MAX_BUTTON_HEIGHT * scale);
            var volumeHeight = Math.Max(12f, 16f * scale);
            var volumeY = area.Bottom - volumeHeight;
            var volumeFits = volumeY - gap - buttonHeight >= area.Y;
            var buttonY = volumeFits
                ? volumeY - gap - buttonHeight
                : area.Bottom - buttonHeight;
            var progressHeight = Math.Max(16f, 20f * scale);
            var progressY = buttonY - gap - progressHeight;
            var progressFits = progressY >= area.Y;
            var bottomControlsTop = progressFits ? progressY : buttonY;
            var visualizerTop = Math.Max(area.Y, contentBottom + gap);
            var visualizerBottom = bottomControlsTop - gap;
            var canSeek = _player != null && _player.CanSeek;

            if (visualizerBottom - visualizerTop >= Math.Max(18f, 30f * scale))
            {
                var mediaBodyRect = new RectangleF(area.X, visualizerTop, area.Width, visualizerBottom - visualizerTop);
                if (allowPlaylistBody && _playlistVisible)
                    ArrangePlaylistArea(mediaBodyRect, false);
                else
                    ArrangeVisualizerArea(mediaBodyRect);
            }
            else
            {
                HidePlaylistControls();
                HideVisualizerControls();
            }

            if (progressFits)
                ArrangeAudioProgress(new RectangleF(area.X, progressY, area.Width, progressHeight), canSeek);
            else
                HideControl(_audioProgress);

            if (volumeFits)
                ArrangeVolumeSlider(new RectangleF(area.X, volumeY, area.Width, volumeHeight));
            else
                HideVolumeControls();

            ArrangeTransportButtons(area, buttonY, buttonHeight, gap, useStopButton);
        }

        void ArrangeSmallMediaControls(RectangleF area)
        {
            var scale = GeneralComponent.GetScale();
            var gap = Math.Max(3f, 5f * scale);
            var buttonHeight = MathHelper.Clamp(area.Height * .55f, MIN_BUTTON_HEIGHT * scale, MAX_BUTTON_HEIGHT * scale);
            buttonHeight = Math.Min(buttonHeight, Math.Max(1f, area.Height));
            var volumeHeight = Math.Max(10f, 14f * scale);
            var volumeY = area.Bottom - volumeHeight;
            var volumeFits = volumeY - gap - buttonHeight >= area.Y;
            var buttonY = volumeFits
                ? volumeY - gap - buttonHeight
                : area.Bottom - buttonHeight;
            var progressHeight = Math.Max(10f, 14f * scale);
            var progressY = buttonY - gap - progressHeight;
            var canSeek = _player != null && _player.CanSeek;

            HideVisualizerControls();

            if (_playlistVisible)
            {
                HideControl(_audioProgress);
                HideTransportControls();
                if (volumeFits)
                {
                    ArrangeVolumeSlider(new RectangleF(area.X, volumeY, area.Width, volumeHeight));
                    ArrangeTinyPlaylistOverlay(new RectangleF(area.X, area.Y, area.Width, Math.Max(1f, volumeY - gap - area.Y)));
                }
                else
                {
                    HideVolumeControls();
                    ArrangeTinyPlaylistOverlay(area);
                }
                return;
            }

            if (progressY >= area.Y)
                ArrangeAudioProgress(new RectangleF(area.X, progressY, area.Width, progressHeight), canSeek);
            else
                HideControl(_audioProgress);

            var bodyBottom = progressY >= area.Y ? progressY - gap : buttonY - gap;
            var showCompactPlaylist = _playlistVisible && bodyBottom > area.Y + Math.Max(20f, 28f * scale);

            ArrangeTransportButtons(area, buttonY, buttonHeight, gap, hidePlaylistButton: showCompactPlaylist);
            if (volumeFits)
                ArrangeVolumeSlider(new RectangleF(area.X, volumeY, area.Width, volumeHeight));
            else
                HideVolumeControls();

            if (showCompactPlaylist)
                ArrangePlaylistArea(new RectangleF(area.X, area.Y, area.Width, bodyBottom - area.Y), true);
            else
                HidePlaylistControls();
        }

        void ArrangeTinyPlaylistOverlay(RectangleF area)
        {
            if (area.Width <= 0f || area.Height <= 0f)
            {
                HidePlaylistControls();
                return;
            }

            UpdatePlaylistListModel();
            _playlistCompactMode = true;

            var scale = GeneralComponent.GetScale();
            var gap = Math.Max(2f, 4f * scale);
            var rowHeight = Math.Max(1f, (area.Height - gap) * .5f);
            var actionWidth = MathHelper.Clamp(
                area.Width * .14f,
                Math.Max(rowHeight, 26f * scale),
                Math.Max(rowHeight, 72f * scale));
            var playlistSize = rowHeight;

            var clearRect = new RectangleF(area.X, area.Y, actionWidth, rowHeight);
            var saveRect = new RectangleF(area.X, area.Y + rowHeight + gap, actionWidth, rowHeight);
            var playlistRect = new RectangleF(area.Right - playlistSize, saveRect.Y, playlistSize, rowHeight);
            var listX = clearRect.Right + gap;
            var listRight = playlistRect.X - gap;
            var listRect = new RectangleF(listX, area.Y, Math.Max(1f, listRight - listX), area.Height);

            ArrangeMediaButton(_clearQueueButton, clearRect, string.Empty, _queue.Count > 0, MediaButtonShape.Rounded, MediaButtonContent.StopSquare);
            ArrangeMediaButton(_saveQueueButton, saveRect, string.Empty, _queue.Count > 0, MediaButtonShape.Rounded, MediaButtonContent.SaveIcon);
            ArrangeMediaButton(_playlistButton, playlistRect, string.Empty, true, MediaButtonShape.Transparent, MediaButtonContent.Playlist);

            ArrangePlaylistList(listRect, true);
        }

        void ArrangeWideMediaControls(RectangleF area)
        {
            var scale = GeneralComponent.GetScale();
            var gap = Math.Max(4f, 8f * scale);
            var playlistWidth = Math.Max(120f * scale, Math.Min(area.Width * .42f, 280f * scale));
            var playlistRect = new RectangleF(area.Right - playlistWidth, area.Y, playlistWidth, area.Height);
            var controlsRect = new RectangleF(
                area.X,
                area.Y,
                Math.Max(1f, playlistRect.X - area.X - gap),
                area.Height);

            ArrangeMediaControls(controlsRect, ArrangeTrackPath(controlsRect, false), false, true);
            ArrangePlaylistArea(playlistRect, false, false);
        }

        void ArrangeTransportButtons(RectangleF area, float buttonY, float buttonHeight, float gap, bool useStopButton = false, bool hidePlaylistButton = false)
        {
            var buttonWidth = Math.Max(1f, (area.Width - gap * 6f) / 7f);
            var libraryEnabled = _pickedAudio != null || HasLibrarySelection() || _queue.Count > 0;
            var canStart = _pickedAudio != null || HasLibrarySelection() || HasQueuedCurrentOrNext();
            var shiftedStop = IsShiftPressed();
            var canTogglePlay = _player != null && (shiftedStop ? CanResetPlayer() : (canStart || _player.IsPlaying || _player.IsPaused));

            var pickRect = new RectangleF(area.X, buttonY, buttonWidth, buttonHeight);
            var shuffleRect = new RectangleF(area.X + (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var previousRect = new RectangleF(area.X + 2f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var playRect = new RectangleF(area.X + 3f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var nextRect = new RectangleF(area.X + 4f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var repeatRect = new RectangleF(area.X + 5f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var stopRect = new RectangleF(area.X + 6f * (buttonWidth + gap), buttonY, buttonWidth, buttonHeight);
            var trackDecoratorHeight = Math.Max(1f, previousRect.Height * .82f);
            var trackDecoratorY = previousRect.Center.Y - trackDecoratorHeight * .5f;
            var previousDecoratorRect = new RectangleF(
                previousRect.X,
                trackDecoratorY,
                Math.Max(1f, playRect.Center.X - previousRect.X),
                trackDecoratorHeight);
            var nextDecoratorRect = new RectangleF(
                playRect.Center.X,
                trackDecoratorY,
                Math.Max(1f, nextRect.Right - playRect.Center.X),
                trackDecoratorHeight);

            ArrangeMediaButton(_pickButton, pickRect, string.Empty, _interactiveHost != null, MediaButtonShape.Transparent, MediaButtonContent.Folder);
            ArrangeMediaButton(_shuffleButton, shuffleRect, string.Empty, true, MediaButtonShape.Transparent, MediaButtonContent.Shuffle);
            ArrangeMediaButton(_previousButton, previousRect, string.Empty, libraryEnabled, MediaButtonShape.LeftRounded, MediaButtonContent.PreviousTrack, previousDecoratorRect);
            ArrangeMediaButton(_nextButton, nextRect, string.Empty, libraryEnabled, MediaButtonShape.RightRounded, MediaButtonContent.NextTrack, nextDecoratorRect);
            ArrangeMediaButton(_repeatButton, repeatRect, GetRepeatButtonText(), true, MediaButtonShape.Transparent, MediaButtonContent.Repeat);
            if (useStopButton)
            {
                ArrangeMediaButton(_stopButton, stopRect, string.Empty, CanResetPlayer(), MediaButtonShape.Transparent, MediaButtonContent.StopSquare);
            }
            else
            {
                HideControl(_stopButton);
                if (hidePlaylistButton)
                    HideControl(_playlistButton);
                else
                    ArrangeMediaButton(_playlistButton, stopRect, string.Empty, true, MediaButtonShape.Transparent, MediaButtonContent.Playlist);
            }
            ArrangeMediaButton(_playButton, playRect, GetPlayButtonText(), canTogglePlay, MediaButtonShape.Circle, shiftedStop ? MediaButtonContent.StopSquare : MediaButtonContent.PlayToggle);
        }

        bool CanResetPlayer()
        {
            if (_player == null)
                return false;

            return _player.IsActive || _player.HasLoadedAudio;
        }

        static StyleTree BuildPlaylistListStyles()
        {
            var styles = new StyleTree();
            Style<ListBoxItem<PlaylistEntry>> item = styles.For<ListBoxItem<PlaylistEntry>>()
                .Set(ControlTemplate.BackgroundColorProperty, Color.Transparent)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSecondaryContainerColor)
                .Set(ControlTemplate.BorderThicknessPixelsProperty, 0f);

            item.State(StyleState.Hover)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor);

            item.State(StyleState.Selected)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighestColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor);

            item.State(StyleState.Hover | StyleState.Selected)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.AccentColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnAccentColor);

            item.State(StyleState.Pressed)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighestColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor);

            // The regular playlist rows are transparent, but the overlay drag
            // ghost needs an opaque surface so it does not disappear over
            // other controls. Declare this last so it wins over selected/hover
            // combinations while dragging.
            item.State(StyleState.Dragged)
                .Set(ControlTemplate.BackgroundColorProperty, ThemeResources.SurfaceContainerHighestColor)
                .Set(ControlTemplate.TextColorProperty, ThemeResources.OnSurfaceColor);

            return styles;
        }

        Color GetMediaActiveColor()
        {
            return ResolveResource(ThemeResources.AccentContainerColor, GetHeaderColor());
        }

        Color GetMediaInactiveSurfaceColor()
        {
            return ResolveResource(ThemeResources.SecondaryContainerColor, Host.BackgroundColor);
        }

        Color GetMediaInactiveForegroundColor()
        {
            return ResolveResource(ThemeResources.OnSecondaryContainerColor, Host.ForegroundColor);
        }


        void ArrangeVisualizerArea(RectangleF rect)
        {
            HidePlaylistControls();

            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                HideVisualizerControls();
                return;
            }

            var scale = GeneralComponent.GetScale();
            var innerPad = Math.Max(1f, 3f * scale);
            var contentRect = new RectangleF(
                rect.X + innerPad,
                rect.Y + innerPad,
                Math.Max(1f, rect.Width - innerPad * 2f),
                Math.Max(1f, rect.Height - innerPad * 2f));

            if (MediaPlayerComponent.VisualizerEnabled)
                ArrangeVisualizer(contentRect);
            else
                ArrangeAudioFileIcon(contentRect);
        }

        void ArrangePlaylistArea(RectangleF rect, bool compact, bool hideVisualizer = true)
        {
            if (hideVisualizer)
                HideVisualizerControls();

            if (rect.Width <= 0f || rect.Height <= 0f)
            {
                HidePlaylistControls();
                return;
            }

            UpdatePlaylistListModel();
            _playlistCompactMode = compact;

            var scale = GeneralComponent.GetScale();
            var gap = Math.Max(3f, 5f * scale);
            var actionHeight = Math.Max(22f * scale, Math.Min(compact ? 26f * scale : 32f * scale, rect.Height * .22f));
            RectangleF listRect;

            if (compact)
            {
                var buttonSize = Math.Max(1f, actionHeight);
                var clearRect = new RectangleF(rect.X, rect.Y, buttonSize, actionHeight);
                var saveRect = new RectangleF(clearRect.Right + gap, rect.Y, buttonSize, actionHeight);
                var playlistRect = new RectangleF(rect.Right - buttonSize, rect.Y, buttonSize, actionHeight);

                ArrangeMediaButton(_clearQueueButton, clearRect, string.Empty, _queue.Count > 0, MediaButtonShape.Rounded, MediaButtonContent.StopSquare);
                ArrangeMediaButton(_saveQueueButton, saveRect, string.Empty, _queue.Count > 0, MediaButtonShape.Rounded, MediaButtonContent.SaveIcon);
                ArrangeMediaButton(_playlistButton, playlistRect, string.Empty, true, MediaButtonShape.Transparent, MediaButtonContent.Playlist);

                var listTop = clearRect.Bottom + gap;
                listRect = new RectangleF(rect.X, listTop, rect.Width, Math.Max(1f, rect.Bottom - listTop));
            }
            else
            {
                HideControl(_playlistButton);
                var buttonWidth = Math.Max(1f, (rect.Width - gap) * .5f);
                var clearRect = new RectangleF(rect.X, rect.Y, buttonWidth, actionHeight);
                var saveRect = new RectangleF(clearRect.Right + gap, rect.Y, buttonWidth, actionHeight);

                ArrangeMediaButton(_clearQueueButton, clearRect, ResolveLoc(LOC_CLEAR_QUEUE), _queue.Count > 0, MediaButtonShape.Rounded, MediaButtonContent.Text);
                ArrangeMediaButton(_saveQueueButton, saveRect, ResolveLoc(LOC_SAVE_QUEUE), _queue.Count > 0, MediaButtonShape.Rounded, MediaButtonContent.Text);

                var listTop = clearRect.Bottom + gap;
                listRect = new RectangleF(rect.X, listTop, rect.Width, Math.Max(1f, rect.Bottom - listTop));
            }
            ArrangePlaylistList(listRect, compact);
        }

        void ArrangePlaylistList(RectangleF listRect, bool compact)
        {
            var scale = GeneralComponent.GetScale();
            if (_playlistListBox == null || _playlistListModel == null || listRect.Height <= 0f)
            {
                HideControl(_playlistListBox);
                HideControl(_playlistEmptyText);
                return;
            }

            _playlistListModel.RowHeight = compact
                ? Math.Max(26f * scale, 30f * scale * Host.Surface.FontSize)
                : Math.Max(40f * scale, 48f * scale * Host.Surface.FontSize);
            _playlistListModel.DragHandleWidthPixels = compact ? Math.Max(18f * scale, 22f * scale) : Math.Max(24f * scale, 30f * scale);
            _playlistListModel.ScrollerWidthPixels = Math.Max(6f, 7f * scale);
            _playlistListModel.SelectedPanelColor = ResolveResource(ThemeResources.SurfaceContainerHighestColor, GetMediaActiveColor());
            _playlistListModel.SelectedTextColor = ResolveResource(ThemeResources.OnSurfaceColor, Host.ForegroundColor);

            _playlistListBox.BackgroundColor = GetMediaInactiveSurfaceColor();
            _playlistListBox.TextColor = Host.ForegroundColor;
            _playlistListBox.BorderColor = Color.Transparent;
            _playlistListBox.BorderThicknessPixels = 0f;
            _playlistListBox.BorderRadiusPixels = Math.Max(3f, 5f * scale);
            _playlistListBox.SetRect(listRect);
            _playlistListBox.SetVisible(true);
            _playlistListBox.SetEnabled(true);
            _playlistListBox.SetCursor(CursorType.Hand);
            _playlistListBox.ScrollPanel.SetScrollBarColors(
                ResolveResource(ThemeResources.SurfaceContainerHighestColor, GetMediaInactiveSurfaceColor()),
                ResolveResource(ThemeResources.OnSurfaceColor, Host.ForegroundColor));
            MaybeScrollPlaylistToCurrent();

            ArrangePlaylistEmptyMessage(listRect);
        }

        void ArrangePlaylistEmptyMessage(RectangleF rect)
        {
            if (_playlistEmptyText == null)
                return;

            if (_queue.Count != 0)
            {
                _playlistEmptyText.SetVisible(false);
                return;
            }

            _playlistEmptyText.Text = ResolveLoc(LOC_QUEUE_EMPTY);
            _playlistEmptyText.FontScale = .58f;
            _playlistEmptyText.TextColor = GetMediaInactiveForegroundColor();
            _playlistEmptyText.SetRect(rect);
            _playlistEmptyText.SetVisible(true);
            _playlistEmptyText.SetEnabled(false);
        }

        void MaybeScrollPlaylistToCurrent()
        {
            if (!_playlistAutoScrollAllowed ||
                _playlistAutoScrollQueueIndex == _queueIndex ||
                _playlistListBox == null ||
                _playlistListModel == null ||
                _queueIndex < 0 ||
                _queueIndex >= _queue.Count)
                return;

            var rowHeight = Math.Max(1f, _playlistListModel.RowHeight);
            var visibleRows = Math.Max(1, _playlistListBox.ScrollPanel.VisibleRows);
            var start = Math.Max(0, _queueIndex - visibleRows / 2);
            _playlistListBox.ScrollPanel.SetScrollOffsetPixels(start * rowHeight, notify: false);
            _playlistAutoScrollQueueIndex = _queueIndex;
        }

        void OnPlaylistScrollChanged(ScrollPanel panel)
        {
            _playlistAutoScrollAllowed = false;
            MarkDirty();
        }

        void UpdatePlaylistListModel()
        {
            if (_playlistListModel == null)
                return;

            if (!ReferenceEquals(_playlistListModel.Items, _queue))
                _playlistListModel.Items = _queue;
            if (!ReferenceEquals(_playlistListModel.SelectedEntries, _selectedQueueEntries))
                _playlistListModel.SelectedEntries = _selectedQueueEntries;

            var current = _queueIndex >= 0 && _queueIndex < _queue.Count ? _queue[_queueIndex] : null;
            if (_selectedQueueEntries.Count == 1 && ReferenceEquals(_selectedQueueEntries[0], current))
                return;

            _selectedQueueEntries.Clear();
            if (current != null)
                _selectedQueueEntries.Add(current);
        }

        void ArrangeVolumeSlider(RectangleF rect)
        {
            if (_volumeSlider == null || _volumeSliderModel == null || rect.Width <= 0f || rect.Height <= 0f)
            {
                HideVolumeControls();
                return;
            }

            var scale = GeneralComponent.GetScale();
            var iconSize = Math.Max(1f, Math.Min(rect.Height, 18f * scale));
            var iconGap = Math.Max(2f, 5f * scale);
            var iconColor = GetMediaInactiveForegroundColor();
            var leftIconRect = new RectangleF(rect.X, rect.Center.Y - iconSize * .5f, iconSize, iconSize);
            var rightIconRect = new RectangleF(rect.Right - iconSize, rect.Center.Y - iconSize * .5f, iconSize, iconSize);

            ArrangeIcon(_volumeLowIcon, SOUND_LOW_ICON, leftIconRect, iconColor, 1f);
            ArrangeIcon(_volumeHighIcon, SOUND_HIGH_ICON, rightIconRect, iconColor, 1f);

            var sliderRect = new RectangleF(
                leftIconRect.Right + iconGap,
                rect.Y,
                Math.Max(1f, rightIconRect.X - leftIconRect.Right - iconGap * 2f),
                rect.Height);

            _volumeSliderModel.Value = MathHelper.Clamp(_volumeSliderModel.Value, 0f, 1f);
            _volumeSliderModel.TrackColor = GetMediaInactiveSurfaceColor();
            _volumeSliderModel.FillColor = GetMediaActiveColor();
            _volumeSliderModel.ThumbColor = GetMediaActiveColor();

            _volumeSlider.SetRect(sliderRect);
            _volumeSlider.SetVisible(true);
            _volumeSlider.SetEnabled(true);
            _volumeSlider.SetCursor(CursorType.Hand);
        }

        void ArrangeVisualizer(RectangleF rect)
        {
            HideControl(_audioIcon);

            if (_audioVisualizer == null || _audioVisualizerModel == null)
                return;

            RefreshVisualizerLevels();

            _audioVisualizerModel.BarCount = DEFAULT_VISUALIZER_BARS;
            _audioVisualizerModel.BarLevels = _visualizerLevels;
            _audioVisualizerModel.CenterLineColor = Color.Black;
            _audioVisualizerModel.BarSaturation = 1f;
            _audioVisualizerModel.BarValue = 1f;
            _audioVisualizerModel.BarAlpha = .9f;
            _audioVisualizerModel.BackgroundColor = null;

            _audioVisualizer.SetRect(rect);
            _audioVisualizer.SetVisible(true);
            _audioVisualizer.SetEnabled(true);
            _audioVisualizer.SetCursor(CursorType.Default);

            ScheduleVisualizerFrameIfNeeded();
        }

        void ArrangeAudioFileIcon(RectangleF rect)
        {
            HideControl(_audioVisualizer);
            ArrangeIcon(_audioIcon, GetCurrentAudioFileIcon(), rect, Host.ForegroundColor, .72f);
        }

        string GetCurrentAudioFileIcon()
        {
            string path = null;
            if (_player != null && _player.HasLoadedAudio)
                path = _player.CurrentWavePath;
            else if (_pickedAudio != null)
                path = _pickedAudio.IsLocal ? _pickedAudio.GameContentPath : _pickedAudio.DefinitionPath;
            else if (HasLibrarySelection())
                path = _library[_selectedIndex].WavePath;

            return GetAudioFileIcon(path);
        }

        static string GetAudioFileIcon(string path)
        {
            return GameAudioPcmLoader.GetContainerKind(path) == GameAudioContainerKind.Xwma
                ? "FileXwm"
                : "FileWav";
        }

        void ArrangeAudioProgress(RectangleF rect, bool canSeek)
        {
            if (_audioProgress == null || _audioProgressModel == null)
                return;

            UpdateAudioProgressModel(canSeek);

            _audioProgress.SetRect(rect);
            _audioProgress.SetVisible(true);
            _audioProgress.SetEnabled(true);
            _audioProgress.SetCursor(canSeek ? CursorType.Hand : CursorType.Default);
        }

        void UpdateFrameBoundControls()
        {
            if (_audioProgress != null && _audioProgress.Visible)
                UpdateAudioProgressModel(_player != null && _player.CanSeek);

            if (_audioVisualizer != null && _audioVisualizer.Visible)
            {
                RefreshVisualizerLevels();
                _audioVisualizer.MarkDirty();
            }

            ScheduleVisualizerFrameIfNeeded();
        }

        void UpdateAudioProgressModel(bool canSeek)
        {
            if (_audioProgressModel == null)
                return;

            double duration = _player?.CurrentDurationSeconds ?? 0.0;
            double position = _player?.CurrentPositionSeconds ?? 0.0;

            _audioProgressModel.PositionSeconds = position;
            _audioProgressModel.DurationSeconds = duration;
            _audioProgressModel.SeekEnabled = canSeek;
            _audioProgressModel.TextColor = Host.ForegroundColor;
            _audioProgressModel.BackgroundColor = GetMediaInactiveSurfaceColor();
            _audioProgressModel.FillColor = GetMediaActiveColor();
            _audioProgressModel.ThumbColor = GetMediaActiveColor();
        }

        void ArrangeIcon(MediaIconControl control, string icon, RectangleF rect, Color color, float sizeRatio)
        {
            if (control == null)
                return;

            control.Icon = icon;
            control.IconColor = color;
            control.SizeRatio = sizeRatio;
            control.SetRect(rect);
            control.SetVisible(!string.IsNullOrEmpty(icon) && rect.Width > 0f && rect.Height > 0f);
            control.SetEnabled(false);
            control.SetCursor(CursorType.Default);
        }

        void HidePlaylistControls()
        {
            HideControl(_clearQueueButton);
            HideControl(_saveQueueButton);
            HideControl(_playlistListBox);
            HideControl(_playlistEmptyText);
        }

        void HideVisualizerControls()
        {
            HideControl(_audioVisualizer);
            HideControl(_audioIcon);
        }

        void HideVolumeControls()
        {
            HideControl(_volumeLowIcon);
            HideControl(_volumeSlider);
            HideControl(_volumeHighIcon);
        }

        void HideTransportControls()
        {
            HideControl(_pickButton);
            HideControl(_shuffleButton);
            HideControl(_previousButton);
            HideControl(_playButton);
            HideControl(_nextButton);
            HideControl(_repeatButton);
            HideControl(_stopButton);
        }

        static void HideControl(Control control)
        {
            if (control == null)
                return;

            control.SetVisible(false);
            control.SetEnabled(false);
        }

        void ScheduleVisualizerFrameIfNeeded()
        {
            if (_visualizerFrameScheduled || !ShouldScheduleVisualizerFrames())
                return;

            _visualizerFrameScheduled = true;
            LcdModClientComponent.RunNextFrame.Add(delegate
            {
                _visualizerFrameScheduled = false;
                if (!ShouldScheduleVisualizerFrames())
                    return;

                if (_interactiveHost != null)
                    _interactiveHost.RequestRedraw();
            });
        }

        bool ShouldScheduleVisualizerFrames()
        {
            if (_player == null || _audioProgress == null || _audioVisualizer == null)
                return false;

            bool hasFrameBoundControl = _audioProgress.Visible || _audioVisualizer.Visible;
            return hasFrameBoundControl &&
                   (_player.IsPlaying ||
                    MediaPlayerComponent.VisualizerEnabled && HasVisibleVisualizerLevels());
        }

        bool HasVisibleVisualizerLevels()
        {
            for (int i = 0; i < _visualizerLevels.Length; i++)
            {
                if (_visualizerLevels[i] > .004f)
                    return true;
            }

            return false;
        }

        void RefreshVisualizerLevels()
        {
            bool hasLevels = _player != null && _player.IsPlaying && _player.FillSpectrumLevels(_visualizerTargetLevels);
            for (int i = 0; i < _visualizerLevels.Length; i++)
            {
                float target = hasLevels && i < _visualizerTargetLevels.Length
                    ? MathHelper.Clamp(_visualizerTargetLevels[i], 0f, 1f)
                    : 0f;
                float previous = _visualizerLevels[i];
                float next = target > previous ? target : previous * .86f;
                _visualizerLevels[i] = next < .004f ? 0f : next;
            }
        }

        void ArrangeMediaButton(
            Button button,
            RectangleF rect,
            string text,
            bool enabled,
            MediaButtonShape shape,
            MediaButtonContent content,
            RectangleF? decoratorRect = null)
        {
            if (button == null)
                return;

            var model = button.DataContext as MediaButtonModel;
            if (model != null)
            {
                model.Text = text ?? string.Empty;
                model.DisplayText = text ?? string.Empty;
                model.Content = content;
                model.Enabled = enabled;
                model.Shape = shape;
                model.DecoratorRect = decoratorRect;
            }
            else
            {
                var buttonModel = button.DataContext as ButtonModel;
                if (buttonModel != null)
                {
                    buttonModel.Text = text ?? string.Empty;
                    buttonModel.Enabled = enabled;
                }
            }

            var fillColor = enabled ? GetMediaActiveColor() : GetMediaInactiveSurfaceColor();
            fillColor.A = byte.MaxValue;

            button.CustomRender = null;
            button.BackgroundColor = shape == MediaButtonShape.Transparent ? Color.Transparent : fillColor;
            button.TextColor = Host.ForegroundColor;
            button.BorderColor = Color.Transparent;
            button.BorderThicknessPixels = 0f;
            button.BorderRadiusPixels = GetMediaButtonBorderRadiusPixels(shape, rect);
            button.SetRect(rect);
            button.SetVisible(true);
            button.SetEnabled(enabled);
            button.SetCursor(enabled ? CursorType.Hand : CursorType.Default);
            button.SetClass(GetMediaButtonClass(content));
            button.SetStyleId(null);
        }

        bool ShouldUseDefaultMediaButtonRender(MediaButtonModel model)
        {
            return model == null ||
                   model.Content == MediaButtonContent.Text &&
                   model.Shape == MediaButtonShape.Rounded &&
                   !model.DecoratorRect.HasValue;
        }

        float GetMediaButtonBorderRadiusPixels(MediaButtonShape shape, RectangleF rect)
        {
            if (shape == MediaButtonShape.Circle ||
                shape == MediaButtonShape.LeftRounded ||
                shape == MediaButtonShape.RightRounded ||
                shape == MediaButtonShape.Rounded)
                return Math.Max(0f, Math.Min(rect.Width, rect.Height) * .5f);

            return 0f;
        }

        void RenderMediaButtonVisual(ControlTemplate control, List<MySprite> sprites)
        {
            var model = control.DataContext as MediaButtonModel;
            if (model == null)
            {
                RenderSearchStyleTextButton(control, sprites, control.DataContext == null ? string.Empty : control.DataContext.ToString());
                return;
            }

            DrawMediaButtonDecorator(
                sprites,
                model.DecoratorRect ?? control.Bounds,
                control.BackgroundColor,
                model.Shape);

            var foreground = control.TextColor;
            if (model.Content == MediaButtonContent.Shuffle)
                foreground = IsShuffleEnabled()
                    ? GetMediaActiveColor()
                    : GetMediaInactiveForegroundColor();
            else if (model.Content == MediaButtonContent.Playlist)
                foreground = IsPlaylistVisible()
                    ? GetMediaActiveColor()
                    : GetMediaInactiveForegroundColor();
            else if (model.Content == MediaButtonContent.Repeat)
                foreground = GetRepeatMode() == MediaRepeatMode.Disabled
                    ? GetMediaInactiveForegroundColor()
                    : GetMediaActiveColor();
            else if (!control.Enabled)
                foreground = new Color(foreground.R, foreground.G, foreground.B, byte.MaxValue);

            if (model.Content == MediaButtonContent.Folder)
                DrawCenteredIcon(sprites, control.Bounds, PICK_ICON, foreground, .62f);
            else if (model.Content == MediaButtonContent.StopSquare)
            {
                var errorColor = ColorComponent.ResolveErrorColor();
                errorColor.A = byte.MaxValue;
                DrawCenteredSquare(sprites, control.Bounds, errorColor, control.Enabled ? .42f : .34f);
            }
            else if (model.Content == MediaButtonContent.Playlist)
                DrawPlaylistIcon(sprites, control.Bounds, foreground);
            else if (model.Content == MediaButtonContent.PreviousTrack)
                DrawTrackIcon(sprites, control.Bounds, foreground, false);
            else if (model.Content == MediaButtonContent.NextTrack)
                DrawTrackIcon(sprites, control.Bounds, foreground, true);
            else if (model.Content == MediaButtonContent.PlayToggle)
                DrawPlayPauseIcon(sprites, control.Bounds, foreground, IsPlayToggleActive());
            else if (model.Content == MediaButtonContent.Shuffle)
                DrawCenteredIcon(sprites, control.Bounds, SHUFFLE_ICON, foreground, .62f);
            else if (model.Content == MediaButtonContent.SaveIcon)
                DrawCenteredIcon(sprites, control.Bounds, SAVE_ICON, foreground, .62f);
            else if (model.Content == MediaButtonContent.Repeat)
                DrawRepeatIcon(sprites, control.Bounds, foreground, model.DisplayText);
            else
                RenderSearchStyleTextButton(control, sprites, model.DisplayText);
        }

        string GetMediaButtonClass(MediaButtonContent content)
        {
            if (content == MediaButtonContent.Repeat)
                return "ControlBase Button MediaButton Repeat " + GetRepeatModeClass(GetRepeatMode());

            if (content == MediaButtonContent.Shuffle)
                return IsShuffleEnabled()
                    ? "ControlBase Button MediaButton Shuffle active"
                    : "ControlBase Button MediaButton Shuffle disabled";

            if (content == MediaButtonContent.Playlist)
                return IsPlaylistVisible()
                    ? "ControlBase Button MediaButton Playlist active"
                    : "ControlBase Button MediaButton Playlist disabled";

            return "ControlBase Button MediaButton";
        }

        static string GetRepeatModeClass(MediaRepeatMode mode)
        {
            switch (mode)
            {
                case MediaRepeatMode.Single:
                    return "single";
                case MediaRepeatMode.Folder:
                    return "folder";
                default:
                    return "disabled";
            }
        }

        string GetRepeatButtonText()
        {
            return GetRepeatMode() == MediaRepeatMode.Single ? "1" : string.Empty;
        }

        void RenderSearchStyleTextButton(ControlTemplate control, List<MySprite> sprites, string text)
        {
            DrawCenteredText(sprites, text, control.Bounds, control.TextColor);
        }

        void DrawMediaButtonDecorator(List<MySprite> sprites, RectangleF rect, Color color, MediaButtonShape shape)
        {
            if (rect.Width <= 0f || rect.Height <= 0f || color.A == 0)
                return;

            if (shape == MediaButtonShape.Transparent)
                return;

            if (shape == MediaButtonShape.Circle)
            {
                float diameter = Math.Max(1f, Math.Min(rect.Width, rect.Height) * 1.1f);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = rect.Center,
                    Size = new Vector2(diameter, diameter),
                    Color = color,
                    Alignment = TextAlignment.CENTER
                });
                return;
            }

            if (shape == MediaButtonShape.LeftRounded)
            {
                DrawSafeCapsule(sprites, rect, color, true, false);
                return;
            }

            if (shape == MediaButtonShape.RightRounded)
            {
                DrawSafeCapsule(sprites, rect, color, false, true);
                return;
            }

            DrawSafeCapsule(sprites, rect, color, true, true);
        }

        static void DrawSafeCapsule(List<MySprite> sprites, RectangleF rect, Color color, bool roundLeft, bool roundRight)
        {
            var radius = Math.Max(0f, Math.Min(rect.Width, rect.Height) * .5f);
            if (radius <= 0f)
            {
                sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", rect.Center, rect.Size, color));
                return;
            }

            var left = rect.X;
            var right = rect.Right;
            var centerY = rect.Center.Y;

            if (roundLeft)
            {
                sprites.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(left + radius, centerY), new Vector2(radius * 2f, rect.Height), color));
                left += radius;
            }

            if (roundRight)
            {
                sprites.Add(new MySprite(SpriteType.TEXTURE, "Circle", new Vector2(right - radius, centerY), new Vector2(radius * 2f, rect.Height), color));
                right -= radius;
            }

            if (right > left)
            {
                sprites.Add(new MySprite(
                    SpriteType.TEXTURE,
                    "SquareSimple",
                    new Vector2((left + right) * .5f, centerY),
                    new Vector2(right - left, rect.Height),
                    color));
            }
        }


        void DrawRepeatIcon(List<MySprite> sprites, RectangleF rect, Color color, string suffixText)
        {
            DrawCenteredIcon(sprites, rect, REPEAT_ICON, color, .62f);
            if (string.IsNullOrEmpty(suffixText) || rect.Width <= 0f || rect.Height <= 0f)
                return;

            var scale = Math.Max(.8f, 1.05f * GeneralComponent.GetScale() * Host.Surface.FontSize);
            var size = MeasureText(suffixText, scale);
            var x = rect.Center.X + Math.Min(rect.Width, rect.Height) * .20f;
            var y = rect.Center.Y - size.Y * .5f + Math.Min(rect.Width, rect.Height) * .12f;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = suffixText,
                Position = new Vector2(x, y),
                Color = color,
                FontId = TextFont,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = scale
            });
        }

        void DrawCenteredIcon(List<MySprite> sprites, RectangleF rect, string icon, Color color, float sizeRatio)
        {
            if (string.IsNullOrEmpty(icon) || rect.Width <= 0f || rect.Height <= 0f)
                return;

            var size = Math.Max(1f, Math.Min(rect.Width, rect.Height) * sizeRatio);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = icon,
                Position = rect.Center,
                Size = new Vector2(size, size),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        static void DrawCenteredSquare(List<MySprite> sprites, RectangleF rect, Color color, float sizeRatio)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var size = Math.Max(1f, Math.Min(rect.Width, rect.Height) * sizeRatio);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = rect.Center,
                Size = new Vector2(size, size),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }
        static void DrawTrackIcon(List<MySprite> sprites, RectangleF rect, Color color, bool forward)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var size = Math.Max(1f, Math.Min(rect.Width, rect.Height));
            var triangleSize = size * .27f;
            var triangleOverlap = triangleSize * .16f;
            var barOverlap = triangleSize * .08f;
            var triangleStep = Math.Max(1f, triangleSize - triangleOverlap);
            var groupWidth = triangleSize * 2f - triangleOverlap - barOverlap;
            var startX = rect.Center.X - groupWidth * .5f;
            var centerY = rect.Center.Y;
            var triangleSpriteSize = new Vector2(triangleSize, triangleSize);

            if (forward)
            {
                var firstTriangleX = startX + triangleSize * .5f;
                var secondTriangleX = firstTriangleX + triangleStep;
                DrawSideTriangle(sprites, new Vector2(firstTriangleX, centerY), triangleSpriteSize, color, true);
                DrawSideTriangle(sprites, new Vector2(secondTriangleX, centerY), triangleSpriteSize, color, true);
            }
            else
            {
                var firstTriangleX = startX + triangleSize * .5f;
                var secondTriangleX = firstTriangleX + triangleStep;
                DrawSideTriangle(sprites, new Vector2(firstTriangleX, centerY), triangleSpriteSize, color, false);
                DrawSideTriangle(sprites, new Vector2(secondTriangleX, centerY), triangleSpriteSize, color, false);
            }
        }

        static void DrawPlayPauseIcon(List<MySprite> sprites, RectangleF rect, Color color, bool playing)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var size = Math.Max(1f, Math.Min(rect.Width, rect.Height));
            if (!playing)
            {
                var triangleSize = size * .38f;
                DrawSideTriangle(sprites, rect.Center + new Vector2(size * .035f, 0f), new Vector2(triangleSize, triangleSize), color, true);
                return;
            }

            var barWidth = Math.Max(1f, size * .105f);
            var barHeight = size * .43f;
            var gap = size * .12f;
            DrawVerticalRect(sprites, rect.Center + new Vector2(-(barWidth + gap) * .5f, 0f), barWidth, barHeight, color);
            DrawVerticalRect(sprites, rect.Center + new Vector2((barWidth + gap) * .5f, 0f), barWidth, barHeight, color);
        }

        static void DrawPlaylistIcon(List<MySprite> sprites, RectangleF rect, Color color)
        {
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var size = Math.Max(1f, Math.Min(rect.Width, rect.Height));
            var width = size * .44f;
            var height = Math.Max(1f, size * .055f);
            var gap = size * .15f;
            var center = rect.Center;
            DrawVerticalRect(sprites, center + new Vector2(0f, -gap), width, height, color);
            DrawVerticalRect(sprites, center, width, height, color);
            DrawVerticalRect(sprites, center + new Vector2(0f, gap), width, height, color);
        }

        static void DrawVerticalRect(List<MySprite> sprites, Vector2 center, float width, float height, Color color)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = center,
                Size = new Vector2(Math.Max(1f, width), Math.Max(1f, height)),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        static void DrawSideTriangle(List<MySprite> sprites, Vector2 center, Vector2 size, Color color, bool right)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Triangle",
                Position = center,
                Size = size,
                Color = color,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = right ? MathHelper.PiOver2 : -MathHelper.PiOver2
            });
        }


        void DrawCenteredText(List<MySprite> sprites, string text, RectangleF rect, Color color)
        {
            if (string.IsNullOrEmpty(text) || rect.Width <= 0f || rect.Height <= 0f)
                return;

            var scale = Math.Max(.42f, .58f * GeneralComponent.GetScale() * Host.Surface.FontSize);
            var trimmed = TrimToWidth(text, Math.Max(1f, rect.Width - 6f * GeneralComponent.GetScale()), scale);
            var size = MeasureText(trimmed, scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = trimmed,
                Position = new Vector2(rect.Center.X, rect.Center.Y - size.Y * .5f),
                Color = color,
                FontId = TextFont,
                Alignment = TextAlignment.CENTER,
                RotationOrScale = scale
            });
        }


        string TrimToWidth(string text, float width, float scale)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (width <= 0f)
                return text;

            if (MeasureText(text, scale).X <= width)
                return text;

            const string ellipsis = "...";
            var max = text.Length;
            while (max > 0)
            {
                var candidate = text.Substring(0, max) + ellipsis;
                if (MeasureText(candidate, scale).X <= width)
                    return candidate;
                max--;
            }

            return ellipsis;
        }

        void PickAudio(ButtonModel model, object sender)
        {
            if (_interactiveHost == null)
                return;

            var dialog = new FilePickerDialog(
                this,
                ResolveLoc(LOC_PICK_AUDIO),
                FilePickerMode.PickFile,
                EmptyFolderRoots,
                OnAudioPicked,
                _interactiveHost.RequestRedraw,
                null,
                false,
                MediaAudioFilePickerTreeProvider.CurrentPath,
                MediaAudioFilePickerTreeProvider.SetCurrentPath)
            {
                FullscreenOnCompactSurfaces = true
            };
            dialog.SetContextActionsProvider(delegate(FilePickerResult result)
            {
                return BuildAudioPickerContextActions(result, dialog);
            });
            dialog.SetLoading(true, ResolveLoc(LOC_LOADING_AUDIO_FILES));
            _interactiveHost.ShowDialog(dialog);

            MediaAudioFilePickerTreeProvider.BuildRootsAsync(delegate(List<FolderModel> roots, Exception error)
            {
                if (dialog.Dismissed)
                    return;

                dialog.SetRoots(roots ?? EmptyFolderRoots);
                dialog.SetLoading(false);
                if (error != null)
                    LogHelper.Log(MyLogSeverity.Warning, "Could not build media audio picker tree: " + error.Message);

                if (_interactiveHost != null)
                    _interactiveHost.RequestRedraw();
            });
        }

        void Previous(ButtonModel model, object sender)
        {
            MarkLocalMediaInteraction();
            if (_queue.Count > 0)
            {
                MoveQueue(-1, applyToActivePlayer: true, loop: GetRepeatMode() == MediaRepeatMode.Folder);
                return;
            }

            MoveSelection(
                -1,
                allowScopeLoop: GetRepeatMode() == MediaRepeatMode.Folder,
                useShuffle: false,
                allowSameShuffleSelection: false,
                applyToActivePlayer: true);
        }

        void Next(ButtonModel model, object sender)
        {
            MarkLocalMediaInteraction();
            if (_queue.Count > 0)
            {
                MoveQueue(1, applyToActivePlayer: true, loop: GetRepeatMode() == MediaRepeatMode.Folder);
                return;
            }

            if (!MoveSelection(
                    1,
                    allowScopeLoop: GetRepeatMode() == MediaRepeatMode.Folder,
                    useShuffle: false,
                    allowSameShuffleSelection: GetRepeatMode() == MediaRepeatMode.Folder,
                    applyToActivePlayer: true) &&
                _pickedAudio != null &&
                _pickedAudio.IsContent &&
                _player != null)
            {
                _player.ResetPlaybackEngine();
                MarkDirty();
            }
        }

        bool MoveSelection(
            int direction,
            bool allowScopeLoop,
            bool useShuffle,
            bool allowSameShuffleSelection,
            bool applyToActivePlayer)
        {
            if (direction == 0)
                return false;

            if (_pickedAudio != null)
                return useShuffle && direction > 0
                    ? ShufflePickedAudio(allowSameShuffleSelection, applyToActivePlayer)
                    : MovePickedAudio(direction, allowScopeLoop, applyToActivePlayer);

            if (!HasLibrarySelection())
                return false;

            return useShuffle && direction > 0
                ? ShuffleLibrarySelection(allowSameShuffleSelection, applyToActivePlayer)
                : MoveLibrarySelection(direction, allowScopeLoop, applyToActivePlayer);
        }

        bool MovePickedAudio(int direction, bool loopScope, bool applyToActivePlayer)
        {
            if (_pickedAudio == null || direction == 0)
                return false;

            var root = GetPickedAudioRoot();
            if (root == null)
                return false;

            var candidates = new List<FileModel>();
            AddAudioFiles(root, candidates);
            if (candidates.Count == 0)
                return false;

            var index = IndexOfMatchingFile(candidates, _pickedAudio);
            if (index < 0)
                return false;

            var nextIndex = index + (direction < 0 ? -1 : 1);
            if (nextIndex < 0 || nextIndex >= candidates.Count)
            {
                if (!loopScope)
                    return false;

                nextIndex = nextIndex < 0 ? candidates.Count - 1 : 0;
            }

            var reference = candidates[nextIndex].Tag as MediaAudioFileReference;
            if (reference == null)
                return false;

            SelectPickedAudio(reference, syncContentSubtype: true, applyToActivePlayer: applyToActivePlayer);
            return true;
        }

        bool MoveLibrarySelection(int direction, bool loop, bool applyToActivePlayer)
        {
            if (!HasLibrarySelection() || direction == 0)
                return false;

            var nextIndex = _selectedIndex + (direction < 0 ? -1 : 1);
            if (nextIndex < 0 || nextIndex >= _library.Length)
            {
                if (!loop)
                    return false;

                nextIndex = nextIndex < 0 ? _library.Length - 1 : 0;
            }

            SetSelectedIndex(nextIndex, applyToActivePlayer);
            return true;
        }

        bool ShuffleLibrarySelection(bool allowSameSelection, bool applyToActivePlayer)
        {
            if (!HasLibrarySelection())
                return false;

            if (_library.Length == 1 && !allowSameSelection)
                return false;

            var nextIndex = _shuffleRandom.Next(_library.Length);
            if (_library.Length > 1 && nextIndex == _selectedIndex)
                nextIndex = (nextIndex + 1) % _library.Length;

            SetSelectedIndex(nextIndex, applyToActivePlayer);
            return true;
        }

        bool ShufflePickedAudio(bool allowSameSelection, bool applyToActivePlayer)
        {
            if (_pickedAudio == null)
                return false;

            var root = GetPickedAudioRoot();
            if (root == null)
                return false;

            var candidates = new List<FileModel>();
            AddAudioFiles(root, candidates);
            if (candidates.Count == 0)
                return false;

            if (candidates.Count == 1 && FileMatchesReference(candidates[0], _pickedAudio) && !allowSameSelection)
                return false;

            var nextIndex = _shuffleRandom.Next(candidates.Count);
            if (candidates.Count > 1 && FileMatchesReference(candidates[nextIndex], _pickedAudio))
                nextIndex = (nextIndex + 1) % candidates.Count;

            var reference = candidates[nextIndex].Tag as MediaAudioFileReference;
            if (reference == null)
                return false;

            SelectPickedAudio(reference, syncContentSubtype: true, applyToActivePlayer: applyToActivePlayer);
            return true;
        }

        FolderModel GetPickedAudioRoot()
        {
            if (_pickedAudio == null)
                return null;

            var roots = MediaAudioFilePickerTreeProvider.GetCachedRootsOrBuild();
            return FindAudioRoot(roots, _pickedAudio.Source);
        }

        void TogglePlay(ButtonModel model, object sender)
        {
            MarkLocalMediaInteraction();
            if (IsShiftPressed())
            {
                CancelPendingLocalAudioStream();
                if (!TrySendMediaPlayerCommand(MediaPlayerCommandKind.Stop, GetPlayerPositionSeconds(), false))
                    Stop();
                return;
            }

            if (_player != null && _player.IsPlaying)
            {
                if (!TrySendMediaPlayerCommand(MediaPlayerCommandKind.Pause, _player.CurrentPositionSeconds, false))
                {
                    _player.Pause();
                    MarkDirty();
                }
                return;
            }

            if (_player != null && _player.IsPaused)
            {
                if (!TrySendMediaPlayerCommand(MediaPlayerCommandKind.Resume, _player.CurrentPositionSeconds, false))
                {
                    _player.Resume();
                    MarkDirty();
                }
                return;
            }

            if (_pickedAudio == null && !HasLibrarySelection() && _queue.Count > 0)
            {
                PlayQueueEntry(_queueIndex >= 0 && _queueIndex < _queue.Count ? _queueIndex : 0, false);
                return;
            }

            StartSelectedAudio();
        }

        void StartSelectedAudio()
        {
            StartSelectedAudio(false);
        }

        void StartSelectedAudio(bool startPaused)
        {
            ClearPlaybackCompletionHandled();

            if (_pickedAudio == null && !HasLibrarySelection())
                return;

            EnsureCurrentSelectionQueued();
            SyncConfig();

            if (Host.GridLogic == null)
                return;

            var block = Host.Block as IMyTerminalBlock;
            if (block == null)
                return;

            _player = GetHostMediaPlayer();
            if (_player == null)
                return;

            if (_volumeSliderModel != null)
                _player.Volume = _volumeSliderModel.Value;

            CancelPendingLocalAudioStream();
            if (_pickedAudio != null && _pickedAudio.IsLocal)
            {
                TryStartLocalAudioStream(block);
                _player.PlayLocalAudio(block, _pickedAudio.LocalAsset, startPaused);
                MarkDirty();
                return;
            }

            if (TrySendMediaPlayerCommand(MediaPlayerCommandKind.Play, 0.0, true))
                return;

            if (_pickedAudio != null)
            {
                if (_pickedAudio.IsSoundBlock)
                    _player.PlayGameSound(block, _pickedAudio.FirstSoundSubtype, startPaused);
                else
                    _player.PlayGameAudioFile(block, GetPickedAudioTitle(_pickedAudio), _pickedAudio.DefinitionPath, startPaused);
                MarkDirty();
                return;
            }

            if (HasLibrarySelection())
                _player.PlayGameSound(block, _library[_selectedIndex].Subtype, startPaused);
            MarkDirty();
        }

        void ApplySelectedAudioToActivePlayer()
        {
            var player = _player;
            if (player == null && Host.GridLogic != null)
                player = GetHostMediaPlayer();

            if (player == null || !player.IsActive)
                return;

            StartSelectedAudio(player.IsPaused);
        }

        bool IsPlayToggleActive()
        {
            return _player != null && _player.IsPlaying;
        }

        string GetPlayButtonText()
        {
            return IsPlayToggleActive() ? ResolveLoc(LOC_PAUSE) : ResolveLoc(LOC_PLAY);
        }


        bool IsShiftPressed()
        {
            return MyAPIGateway.Input != null && MyAPIGateway.Input.IsAnyShiftKeyPressed();
        }

        bool IsPlaylistVisible()
        {
            return _playlistVisible || _playlistForceVisibleForLayout;
        }

        void TogglePlaylist(ButtonModel model, object sender)
        {
            MarkLocalMediaInteraction();
            _playlistVisible = !_playlistVisible;
            if (_playlistVisible)
            {
                _playlistAutoScrollAllowed = true;
                _playlistAutoScrollQueueIndex = -1;
            }
            else
            {
                _playlistAutoScrollAllowed = false;
            }

            MarkDirty();
        }

        bool HasQueuedCurrentOrNext()
        {
            return _queue.Count > 0 && (_queueIndex < 0 || _queueIndex < _queue.Count);
        }

        string GetPlaylistEntryText(PlaylistEntry entry)
        {
            if (entry == null)
                return string.Empty;

            return string.IsNullOrEmpty(entry.Title) ? entry.Path : entry.Title;
        }

        void RenderPlaylistEntryItem(ListBoxItem<PlaylistEntry> control, PlaylistEntry entry, List<MySprite> sprites)
        {
            if (control == null || entry == null || sprites == null)
                return;

            var rect = control.GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            var scale = GeneralComponent.GetScale();
            var padding = Math.Max(4f, 6f * scale);
            var dragWidth = Math.Max(18f * scale, _playlistListModel?.DragHandleWidthPixels ?? 24f * scale);
            var dragRect = new RectangleF(rect.Right - padding - dragWidth, rect.Y, dragWidth, rect.Height);
            var index = GetPlaylistEntryIndex(control, entry);
            var title = string.IsNullOrEmpty(entry.Title) ? entry.Path : entry.Title;
            var detail = GetPlaylistEntryDetailText(entry, index);
            var foreground = control.TextColor;
            var secondary = GetMediaInactiveForegroundColor();
            if (index == _queueIndex)
                secondary = foreground;

            if (_playlistCompactMode)
            {
                var textX = rect.X + padding;
                var textRight = Math.Max(textX + 1f, dragRect.X - Math.Max(5f, 7f * scale));
                var titleScale = Math.Max(.36f, .46f * scale * Host.Surface.FontSize);
                var text = string.IsNullOrEmpty(detail) ? title : title + "  " + detail;
                var textHeight = MeasureLineHeight(titleScale);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = TrimToWidth(text, Math.Max(1f, textRight - textX), titleScale),
                    Position = new Vector2(textX, rect.Center.Y - textHeight * .5f),
                    Color = foreground,
                    FontId = TextFont,
                    Alignment = TextAlignment.LEFT,
                    RotationOrScale = titleScale
                });

                DrawPlaylistIcon(sprites, dragRect, secondary);
                return;
            }

            var iconSize = Math.Max(12f, Math.Min(rect.Height - padding * 2f, 30f * scale));
            var iconRect = new RectangleF(rect.X + padding, rect.Center.Y - iconSize * .5f, iconSize, iconSize);
            var textXNormal = iconRect.Right + Math.Max(6f, 8f * scale);
            var textRightNormal = Math.Max(textXNormal + 1f, dragRect.X - Math.Max(5f, 7f * scale));
            var titleScaleNormal = Math.Max(.40f, .52f * scale * Host.Surface.FontSize);
            var detailScale = Math.Max(.34f, .42f * scale * Host.Surface.FontSize);
            var titleHeight = MeasureLineHeight(titleScaleNormal);
            var detailHeight = MeasureLineHeight(detailScale);
            var totalTextHeight = titleHeight + detailHeight + Math.Max(1f, 2f * scale);
            var titleY = rect.Center.Y - totalTextHeight * .5f;
            var detailY = titleY + titleHeight + Math.Max(1f, 2f * scale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = string.IsNullOrEmpty(entry.Icon) ? GetPlaylistEntryIcon(entry) : entry.Icon,
                Position = iconRect.Center,
                Size = new Vector2(iconSize, iconSize),
                Color = Host.ForegroundColor,
                Alignment = TextAlignment.CENTER
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = TrimToWidth(title, Math.Max(1f, textRightNormal - textXNormal), titleScaleNormal),
                Position = new Vector2(textXNormal, titleY),
                Color = foreground,
                FontId = TextFont,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = titleScaleNormal
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = TrimToWidth(detail, Math.Max(1f, textRightNormal - textXNormal), detailScale),
                Position = new Vector2(textXNormal, detailY),
                Color = secondary,
                FontId = TextFont,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = detailScale
            });

            DrawPlaylistIcon(sprites, dragRect, secondary);
        }

        int GetPlaylistEntryIndex(ListBoxItem<PlaylistEntry> control, PlaylistEntry entry)
        {
            var model = control?.ItemModel;
            if (model != null && model.Index >= 0 && model.Index < _queue.Count && ReferenceEquals(_queue[model.Index], entry))
                return model.Index;

            return _queue.IndexOf(entry);
        }

        string GetPlaylistEntryDetailText(PlaylistEntry entry, int index)
        {
            if (entry == null)
                return string.Empty;

            if (index == _queueIndex && _player != null && _player.CurrentDurationSeconds > 0.0)
                return FormatDuration(_player.CurrentDurationSeconds);

            return string.IsNullOrEmpty(entry.Detail) ? ResolveLoc(LOC_UNKNOWN_LENGTH) : entry.Detail;
        }

        string GetPlaylistEntryIcon(PlaylistEntry entry)
        {
            if (entry == null)
                return "FileWav";

            if (entry.Reference != null)
            {
                var path = entry.Reference.IsLocal ? entry.Reference.GameContentPath : entry.Reference.DefinitionPath;
                return GetAudioFileIcon(path);
            }

            return GetAudioFileIcon(entry.Path);
        }

        void OnPlaylistEntryClicked(PlaylistEntry entry)
        {
            MarkLocalMediaInteraction();
            var index = _queue.IndexOf(entry);
            if (index >= 0)
                PlayQueueEntry(index, false);
        }

        void ClearQueue(ButtonModel model, object sender)
        {
            MarkLocalMediaInteraction();
            _queue.Clear();
            _queueIndex = -1;
            _selectedQueueEntries.Clear();
            ClearPreShuffleQueue();
            Stop();
            SyncConfig();
            MarkDirty();
        }

        void SaveQueue(ButtonModel model, object sender)
        {
            MarkLocalMediaInteraction();
            if (_queue.Count == 0)
                return;

            TextInputHelper.SpawnForLocalPlayer(
                ResolveLoc(LOC_SAVE_PLAYLIST_TITLE),
                OnSavePlaylistNameEntered,
                "playlist_" + DateTime.UtcNow.ToFileTime(),
                ResolveLoc(LOC_SAVE_PLAYLIST_PROMPT));
        }

        void OnSavePlaylistNameEntered(string name)
        {
            var displayName = NormalizePlaylistDisplayName(name);
            if (string.IsNullOrEmpty(displayName))
            {
                if (MyAPIGateway.Utilities != null)
                    MyAPIGateway.Utilities.ShowNotification(ResolveLoc(LOC_PLAYLIST_NAME_EMPTY));
                return;
            }

            var entries = BuildQueueSnapshotForSave();
            if (entries.Count == 0)
                return;

            var fileName = BuildPlaylistFileName(displayName);
            AppendM3U(fileName, entries, false);
            RegisterSavedPlaylist(fileName, displayName);
            MediaAudioFilePickerTreeProvider.InvalidateCache();
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.ShowNotification(string.Format(FormatingHelper.Culture, ResolveLoc(LOC_PLAYLIST_SAVED_FORMAT), displayName));
            MarkDirty();
        }

        void EnsureCurrentSelectionQueued()
        {
            if (_startingQueueEntry)
                return;

            var entry = CreatePlaylistEntryFromCurrentSelection();
            if (entry == null)
                return;

            var matching = IndexOfMatchingQueueEntry(entry);
            if (matching >= 0)
            {
                _queueIndex = matching;
                return;
            }

            var insertAt = _queueIndex < 0 || _queueIndex >= _queue.Count
                ? _queue.Count
                : _queueIndex + 1;
            _queue.Insert(insertAt, entry);
            _queueIndex = insertAt;
        }

        PlaylistEntry CreatePlaylistEntryFromCurrentSelection()
        {
            if (_pickedAudio != null)
                return CreatePlaylistEntry(_pickedAudio);

            if (HasLibrarySelection())
                return CreatePlaylistEntry(_library[_selectedIndex]);

            return null;
        }

        PlaylistEntry CreatePlaylistEntry(MediaAudioFileReference reference)
        {
            if (reference == null)
                return null;

            return new PlaylistEntry
            {
                Reference = reference,
                SoundSubtype = reference.FirstSoundSubtype ?? string.Empty,
                Title = GetPickedAudioSongName(reference),
                Path = GetPickedAudioM3UPath(reference),
                Detail = GetPickedAudioLengthOrDetail(reference),
                Icon = GetAudioFileIcon(reference.IsLocal ? reference.GameContentPath : reference.DefinitionPath)
            };
        }

        PlaylistEntry CreatePlaylistEntry(MediaItem item)
        {
            if (item == null)
                return null;

            return new PlaylistEntry
            {
                SoundSubtype = item.Subtype ?? string.Empty,
                Title = string.IsNullOrEmpty(item.DisplayName) ? GetFileNameWithoutExtension(item.WavePath) : item.DisplayName,
                Path = string.IsNullOrEmpty(item.WavePath) ? item.Subtype : "Content/" + item.WavePath.Replace('\\', '/'),
                Detail = GetMediaItemDetail(item),
                Icon = GetAudioFileIcon(item.WavePath)
            };
        }

        int IndexOfMatchingQueueEntry(PlaylistEntry entry)
        {
            if (entry == null)
                return -1;

            for (int i = 0; i < _queue.Count; i++)
            {
                if (PlaylistEntriesMatch(_queue[i], entry))
                    return i;
            }

            return -1;
        }

        static bool PlaylistEntriesMatch(PlaylistEntry left, PlaylistEntry right)
        {
            if (left == null || right == null)
                return false;

            if (left.Reference != null && right.Reference != null)
            {
                if (!string.IsNullOrEmpty(left.Reference.PickerFullPath) &&
                    string.Equals(left.Reference.PickerFullPath, right.Reference.PickerFullPath, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (!string.IsNullOrEmpty(left.Path) &&
                    string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (!string.IsNullOrEmpty(left.SoundSubtype) &&
                string.Equals(left.SoundSubtype, right.SoundSubtype, StringComparison.OrdinalIgnoreCase))
                return true;

            return !string.IsNullOrEmpty(left.Path) &&
                   string.Equals(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
        }

        void AddPickedReferenceToQueue(MediaAudioFileReference reference)
        {
            MarkLocalMediaInteraction();
            AddPlaylistEntryToQueue(CreatePlaylistEntry(reference), false);
        }

        void AddPickedReferenceNext(MediaAudioFileReference reference)
        {
            MarkLocalMediaInteraction();
            AddPlaylistEntryToQueue(CreatePlaylistEntry(reference), true);
        }

        void PlayPickedReferenceNow(MediaAudioFileReference reference)
        {
            MarkLocalMediaInteraction();
            PlayEntryNow(CreatePlaylistEntry(reference));
        }

        void AddFolderToQueue(FolderModel folder, bool playNow)
        {
            MarkLocalMediaInteraction();
            var entries = CreatePlaylistEntries(folder);
            if (entries.Count == 0)
                return;

            if (playNow)
                ReplaceQueueAndPlay(entries);
            else
            {
                for (int i = 0; i < entries.Count; i++)
                    AddPlaylistEntryToQueue(entries[i], false);
            }
        }

        List<PlaylistEntry> CreatePlaylistEntries(FolderModel folder)
        {
            var entries = new List<PlaylistEntry>();
            var files = new List<FileModel>();
            AddAudioFiles(folder, files);
            for (int i = 0; i < files.Count; i++)
            {
                if (files[i] == null)
                    continue;

                var playlist = files[i].Tag as MediaAudioPlaylistReference;
                if (playlist != null)
                {
                    entries.AddRange(CreatePlaylistEntries(playlist));
                    continue;
                }

                var reference = files[i].Tag as MediaAudioFileReference;
                var entry = CreatePlaylistEntry(reference);
                if (entry != null)
                    entries.Add(entry);
            }

            return entries;
        }

        void AddPlaylistEntryToQueue(PlaylistEntry entry, bool next)
        {
            if (entry == null)
                return;

            if (next && _queueIndex < 0)
                EnsureCurrentSelectionQueued();

            var insertAt = _queue.Count;
            if (next && _queueIndex >= 0 && _queueIndex < _queue.Count)
                insertAt = _queueIndex + 1;

            _queue.Insert(insertAt, entry);
            SyncConfig();
            MarkDirty();
        }

        void PlayEntryNow(PlaylistEntry entry)
        {
            if (entry == null)
                return;

            var insertAt = _queueIndex < 0 || _queueIndex >= _queue.Count ? _queue.Count : _queueIndex + 1;
            _queue.Insert(insertAt, entry);
            PlayQueueEntry(insertAt, false);
        }

        void ReplaceQueueAndPlay(List<PlaylistEntry> entries)
        {
            _queue.Clear();
            _queue.AddRange(entries);
            ClearPreShuffleQueue();
            _queueIndex = _queue.Count == 0 ? -1 : 0;
            if (_queueIndex >= 0)
                PlayQueueEntry(_queueIndex, false);
            else
            {
                SyncConfig();
                MarkDirty();
            }
        }

        int ResolvePlaylistDragTargetIndex(PlaylistEntry entry, int sourceIndex, int targetIndex)
        {
            if (entry == null || _queue.Count <= 1)
                return sourceIndex;

            sourceIndex = _queue.IndexOf(entry);
            if (sourceIndex < 0)
                return sourceIndex;

            if (targetIndex < 0)
                targetIndex = 0;
            if (targetIndex >= _queue.Count)
                targetIndex = _queue.Count - 1;

            if (_queueIndex < 0 || _queueIndex >= _queue.Count)
                return targetIndex;

            // The current/played part of the queue is immutable while a track is
            // playing. Dragging can reorder only the upcoming region. Already
            // played entries may be dragged into the upcoming region, but they
            // keep their current slot until the pointer reaches that region.
            var firstUpcomingIndex = _queueIndex + 1;
            if (sourceIndex == _queueIndex || firstUpcomingIndex >= _queue.Count)
                return sourceIndex;

            if (targetIndex < firstUpcomingIndex)
                return sourceIndex < firstUpcomingIndex ? sourceIndex : firstUpcomingIndex;

            return targetIndex;
        }

        void MovePlaylistEntry(PlaylistEntry entry, int sourceIndex, int targetIndex)
        {
            if (sourceIndex < 0) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            if (entry == null || _queue.Count <= 1)
                return;

            sourceIndex = _queue.IndexOf(entry);
            if (sourceIndex < 0)
                return;

            if (targetIndex < 0)
                targetIndex = 0;
            if (targetIndex >= _queue.Count)
                targetIndex = _queue.Count - 1;

            targetIndex = ResolvePlaylistDragTargetIndex(entry, sourceIndex, targetIndex);
            if (targetIndex < 0)
                targetIndex = 0;
            if (targetIndex >= _queue.Count)
                targetIndex = _queue.Count - 1;
            if (sourceIndex == targetIndex)
                return;

            _queue.RemoveAt(sourceIndex);
            _queue.Insert(targetIndex, entry);

            if (_queueIndex == sourceIndex)
                _queueIndex = targetIndex;
            else if (sourceIndex < _queueIndex && targetIndex >= _queueIndex)
                _queueIndex--;
            else if (sourceIndex > _queueIndex && targetIndex <= _queueIndex)
                _queueIndex++;

            MarkLocalMediaInteraction();
            SyncConfig();
            MarkDirty();
        }

        bool MoveQueue(int direction, bool applyToActivePlayer, bool loop)
        {
            if (_queue.Count == 0 || direction == 0)
                return false;

            var nextIndex = _queueIndex < 0 ? 0 : _queueIndex + (direction < 0 ? -1 : 1);
            if (nextIndex < 0 || nextIndex >= _queue.Count)
            {
                if (!loop)
                    return false;

                nextIndex = nextIndex < 0 ? _queue.Count - 1 : 0;
            }

            if (applyToActivePlayer)
                PlayQueueEntry(nextIndex, false);
            else
            {
                _queueIndex = nextIndex;
                SyncConfig();
                MarkDirty();
            }
            return true;
        }

        void PlayQueueEntry(int index, bool startPaused)
        {
            if (index < 0 || index >= _queue.Count)
                return;

            var entry = _queue[index];
            ApplyPlaylistEntrySelection(entry);
            _queueIndex = index;

            var previous = _startingQueueEntry;
            _startingQueueEntry = true;
            try
            {
                StartSelectedAudio(startPaused);
            }
            finally
            {
                _startingQueueEntry = previous;
            }

            MarkDirty();
            SyncConfig();
        }

        void ApplyPlaylistEntrySelection(PlaylistEntry entry)
        {
            if (entry == null)
                return;

            if (entry.Reference != null)
            {
                SelectPickedAudio(entry.Reference, syncContentSubtype: true, applyToActivePlayer: false);
                return;
            }

            if (!string.IsNullOrEmpty(entry.SoundSubtype))
            {
                for (int i = 0; i < _library.Length; i++)
                {
                    if (string.Equals(_library[i].Subtype, entry.SoundSubtype, StringComparison.OrdinalIgnoreCase))
                    {
                        SetSelectedIndex(i, applyToActivePlayer: false);
                        return;
                    }
                }
            }
        }

        void ShuffleQueueFuture()
        {
            if (_queue.Count <= 2)
                return;

            var start = _queueIndex < 0 ? 0 : _queueIndex + 1;
            if (start >= _queue.Count - 1)
                return;

            var random = _shuffleSeed == 0 ? _shuffleRandom : new Random(_shuffleSeed);
            for (int i = _queue.Count - 1; i > start; i--)
            {
                var j = random.Next(start, i + 1);
                var tmp = _queue[i];
                _queue[i] = _queue[j];
                _queue[j] = tmp;
            }
        }

        void CapturePreShuffleQueue()
        {
            _preShuffleQueue.Clear();
            _preShuffleQueue.AddRange(_queue);
            _hasPreShuffleQueue = _preShuffleQueue.Count > 0;
        }

        void ClearPreShuffleQueue()
        {
            _preShuffleQueue.Clear();
            _hasPreShuffleQueue = false;
        }

        void RestorePreShuffleQueueFuture()
        {
            if (!_hasPreShuffleQueue || _queue.Count <= 1)
            {
                ClearPreShuffleQueue();
                return;
            }

            var restoreStart = _queueIndex < 0 ? 0 : Math.Min(_queue.Count, _queueIndex + 1);
            if (restoreStart >= _queue.Count)
            {
                ClearPreShuffleQueue();
                return;
            }

            var future = new List<PlaylistEntry>();
            for (int i = restoreStart; i < _queue.Count; i++)
                future.Add(_queue[i]);

            var restored = new List<PlaylistEntry>();
            for (int i = 0; i < _preShuffleQueue.Count; i++)
            {
                var index = IndexOfRestorableEntry(future, _preShuffleQueue[i]);
                if (index < 0)
                    continue;

                restored.Add(future[index]);
                future.RemoveAt(index);
            }

            restored.AddRange(future);
            _queue.RemoveRange(restoreStart, _queue.Count - restoreStart);
            _queue.AddRange(restored);
            ClearPreShuffleQueue();
        }

        static int IndexOfRestorableEntry(List<PlaylistEntry> entries, PlaylistEntry target)
        {
            if (entries == null || target == null)
                return -1;

            for (int i = 0; i < entries.Count; i++)
            {
                if (ReferenceEquals(entries[i], target))
                    return i;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (PlaylistEntriesMatch(entries[i], target))
                    return i;
            }

            return -1;
        }

        List<FilePickerContextAction> BuildAudioPickerContextActions(FilePickerResult result, FilePickerDialog dialog)
        {
            if (result == null)
                return null;

            var actions = new List<FilePickerContextAction>();
            if (result.File != null)
            {
                var playlist = result.Tag as MediaAudioPlaylistReference;
                if (playlist != null)
                {
                    var playlistReference = playlist;
                    actions.Add(new FilePickerContextAction(ResolveLoc(LOC_CONTEXT_ADD_TO_QUEUE), delegate { AddPlaylistReferenceToQueue(playlistReference, false); }));
                    actions.Add(new FilePickerContextAction(ResolveLoc(LOC_CONTEXT_ADD_NEXT), delegate { AddPlaylistReferenceToQueue(playlistReference, true); }));
                    actions.Add(new FilePickerContextAction(ResolveLoc(LOC_CONTEXT_PLAY_NOW), delegate { PlayPlaylistReferenceNow(playlistReference); }));
                    actions.Add(CreateDeleteContextAction(delegate { DeletePlaylistFromPicker(playlistReference, dialog); }));
                }
                else
                {
                    var reference = result.Tag as MediaAudioFileReference;
                    if (reference != null)
                    {
                        var fileReference = reference;
                        actions.Add(new FilePickerContextAction(ResolveLoc(LOC_CONTEXT_ADD_TO_QUEUE), delegate { AddPickedReferenceToQueue(fileReference); }));
                        actions.Add(new FilePickerContextAction(ResolveLoc(LOC_CONTEXT_ADD_NEXT), delegate { AddPickedReferenceNext(fileReference); }));
                        actions.Add(new FilePickerContextAction(ResolveLoc(LOC_CONTEXT_PLAY_NOW), delegate { PlayPickedReferenceNow(fileReference); }));
                        actions.Add(new FilePickerContextAction(ResolveLoc(LOC_CONTEXT_FAVORITE), delegate { FavoritePickedReference(fileReference); }));
                        if (fileReference.IsLocal)
                            actions.Add(CreateDeleteContextAction(delegate { DeleteLocalAudioFromPicker(fileReference, dialog); }));
                    }
                }
            }
            else if (result.Folder != null)
            {
                var folder = result.Folder;
                actions.Add(new FilePickerContextAction(ResolveLoc(LOC_CONTEXT_ADD_ALL), delegate { AddFolderToQueue(folder, false); }));
                actions.Add(new FilePickerContextAction(ResolveLoc(LOC_CONTEXT_PLAY_ALL), delegate { AddFolderToQueue(folder, true); }));
            }

            return actions.Count == 0 ? null : actions;
        }

        FilePickerContextAction CreateDeleteContextAction(Action clicked)
        {
            return new FilePickerContextAction(ResolveLoc(LOC_CONTEXT_DELETE), clicked)
            {
                UseErrorTextStyle = true
            };
        }

        void DeleteLocalAudioFromPicker(MediaAudioFileReference reference, FilePickerDialog dialog)
        {
            if (reference == null || !reference.IsLocal)
                return;

            var displayName = GetLocalAudioDeleteDisplayName(reference);
            if (IsShiftPressed())
            {
                DeleteLocalAudio(reference, dialog, displayName);
                return;
            }

            ShowAudioDeleteConfirmation(
                ResolveLoc(LOC_DELETE_LOCAL_AUDIO_TITLE),
                string.Format(FormatingHelper.Culture, ResolveLoc(LOC_DELETE_LOCAL_AUDIO_PROMPT_FORMAT), displayName),
                delegate { DeleteLocalAudio(reference, dialog, displayName); });
        }

        void DeletePlaylistFromPicker(MediaAudioPlaylistReference playlist, FilePickerDialog dialog)
        {
            if (playlist == null)
                return;

            var displayName = GetPlaylistDeleteDisplayName(playlist);
            if (IsShiftPressed())
            {
                DeletePlaylist(playlist, dialog, displayName);
                return;
            }

            ShowAudioDeleteConfirmation(
                ResolveLoc(LOC_DELETE_PLAYLIST_TITLE),
                string.Format(FormatingHelper.Culture, ResolveLoc(LOC_DELETE_PLAYLIST_PROMPT_FORMAT), displayName),
                delegate { DeletePlaylist(playlist, dialog, displayName); });
        }

        void ShowAudioDeleteConfirmation(string title, string content, Action confirmed)
        {
            if (_interactiveHost == null)
                return;

            _interactiveHost.ShowMessageBox(
                title,
                content,
                ResolveLoc(LOC_CONTEXT_DELETE),
                ResolveLoc(LOC_COMMON_CANCEL),
                delegate
                {
                    if (confirmed != null)
                        confirmed();
                },
                null,
                "Danger");
        }

        void DeleteLocalAudio(MediaAudioFileReference reference, FilePickerDialog dialog, string displayName)
        {
            MarkLocalMediaInteraction();

            QueueAudioDelete(
                dialog,
                delegate(out string failureReason)
                {
                    return MediaAudioFilePickerTreeProvider.TryDeleteLocalAudio(reference, out failureReason);
                },
                delegate
                {
                    RemoveDeletedLocalAudioReferences(reference);
                    ShowAudioDeleteNotification(string.Format(FormatingHelper.Culture, ResolveLoc(LOC_LOCAL_AUDIO_DELETED_FORMAT), displayName));
                    SyncConfig();
                    MarkDirty();
                });
        }

        void DeletePlaylist(MediaAudioPlaylistReference playlist, FilePickerDialog dialog, string displayName)
        {
            MarkLocalMediaInteraction();

            QueueAudioDelete(
                dialog,
                delegate(out string failureReason)
                {
                    return MediaAudioFilePickerTreeProvider.TryDeletePlaylist(playlist, out failureReason);
                },
                delegate
                {
                    ShowAudioDeleteNotification(string.Format(FormatingHelper.Culture, ResolveLoc(LOC_PLAYLIST_DELETED_FORMAT), displayName));
                    MarkDirty();
                });
        }

        void QueueAudioDelete(FilePickerDialog dialog, AudioDeleteOperation deleteOperation, Action completed)
        {
            if (deleteOperation == null)
                return;

            SetAudioPickerBusy(dialog, true);

            var work = new AudioDeleteWork();
            MyAPIGateway.Parallel.Start(
                delegate
                {
                    try
                    {
                        string failureReason;
                        work.Deleted = deleteOperation(out failureReason);
                        work.FailureReason = failureReason;
                    }
                    catch (Exception error)
                    {
                        work.Error = error;
                        work.FailureReason = error.Message;
                    }
                },
                delegate { CompleteQueuedAudioDelete(dialog, work, completed); });
        }

        void CompleteQueuedAudioDelete(FilePickerDialog dialog, AudioDeleteWork work, Action completed)
        {
            if (work == null)
            {
                SetAudioPickerBusy(dialog, false);
                ShowAudioDeleteFailure("unknown error");
                return;
            }

            if (work.Error != null)
            {
                SetAudioPickerBusy(dialog, false);
                ShowAudioDeleteFailure("Could not delete audio: " + work.Error.Message);
                return;
            }

            if (!work.Deleted)
            {
                SetAudioPickerBusy(dialog, false);
                ShowAudioDeleteFailure(work.FailureReason);
                return;
            }

            if (completed != null)
                completed();

            RefreshAudioPickerDialog(dialog);
        }

        void SetAudioPickerBusy(FilePickerDialog dialog, bool busy)
        {
            if (dialog != null && !dialog.Dismissed)
                dialog.SetLoading(busy, ResolveLoc(LOC_LOADING_AUDIO_FILES));

            if (_interactiveHost != null)
                _interactiveHost.RequestRedraw();
        }

        void RemoveDeletedLocalAudioReferences(MediaAudioFileReference reference)
        {
            var target = CreatePlaylistEntry(reference);
            var removedCurrentQueueEntry = false;
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (!PlaylistEntriesMatch(_queue[i], target))
                    continue;

                if (i == _queueIndex)
                    removedCurrentQueueEntry = true;

                _queue.RemoveAt(i);
                if (i < _queueIndex)
                    _queueIndex--;
            }

            if (removedCurrentQueueEntry)
            {
                _queueIndex = -1;
                Stop();
            }
            else if (_queueIndex >= _queue.Count)
            {
                _queueIndex = _queue.Count - 1;
            }

            if (_queue.Count == 0)
                _queueIndex = -1;

            _selectedQueueEntries.Clear();
            ClearPreShuffleQueue();

            if (_pickedAudio != null && AudioReferencesMatch(_pickedAudio, reference))
            {
                _pickedAudio = null;
                _restorePickedAudioAttempted = true;
                MediaPlayerComponent.SelectedAudioSource = string.Empty;
                MediaPlayerComponent.SelectedPickerFullPath = string.Empty;
                Stop();
            }

            UpdatePlaylistListModel();
        }

        bool AudioReferencesMatch(MediaAudioFileReference left, MediaAudioFileReference right)
        {
            if (ReferenceEquals(left, right))
                return true;

            return PlaylistEntriesMatch(CreatePlaylistEntry(left), CreatePlaylistEntry(right));
        }

        void RefreshAudioPickerDialog(FilePickerDialog dialog)
        {
            MediaAudioFilePickerTreeProvider.InvalidateCache();
            if (dialog == null || dialog.Dismissed)
            {
                if (_interactiveHost != null)
                    _interactiveHost.RequestRedraw();
                return;
            }

            dialog.SetLoading(true, ResolveLoc(LOC_LOADING_AUDIO_FILES));
            MediaAudioFilePickerTreeProvider.BuildRootsAsync(delegate(List<FolderModel> roots, Exception error)
            {
                if (!dialog.Dismissed)
                {
                    dialog.RefreshRoots(roots ?? EmptyFolderRoots);
                    dialog.SetLoading(false);
                }

                if (error != null)
                    LogHelper.Log(MyLogSeverity.Warning, "Could not refresh media audio picker tree: " + error.Message);

                if (_interactiveHost != null)
                    _interactiveHost.RequestRedraw();
            });
        }

        void ShowAudioDeleteFailure(string failureReason)
        {
            if (string.IsNullOrWhiteSpace(failureReason))
                failureReason = "unknown error";

            if (MyAPIGateway.Utilities != null)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    string.Format(FormatingHelper.Culture, ResolveLoc(LOC_DELETE_FAILED_FORMAT), failureReason),
                    4000,
                    "Red");
            }
        }

        static void ShowAudioDeleteNotification(string message)
        {
            if (MyAPIGateway.Utilities != null)
                MyAPIGateway.Utilities.ShowNotification(message);
        }

        string GetLocalAudioDeleteDisplayName(MediaAudioFileReference reference)
        {
            var displayName = GetPickedAudioSongName(reference);
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;

            displayName = GetPickedAudioTitle(reference);
            return string.IsNullOrWhiteSpace(displayName) ? "local audio" : displayName;
        }

        static string GetPlaylistDeleteDisplayName(MediaAudioPlaylistReference playlist)
        {
            if (playlist == null)
                return "playlist";

            if (!string.IsNullOrWhiteSpace(playlist.DisplayName))
                return playlist.DisplayName;

            return string.IsNullOrWhiteSpace(playlist.FileName) ? "playlist" : playlist.FileName;
        }

        void AddPlaylistReferenceToQueue(MediaAudioPlaylistReference playlist, bool next)
        {
            MarkLocalMediaInteraction();
            var entries = CreatePlaylistEntries(playlist);
            AddPlaylistEntriesToQueue(entries, next);
        }

        void PlayPlaylistReferenceNow(MediaAudioPlaylistReference playlist)
        {
            MarkLocalMediaInteraction();
            var entries = CreatePlaylistEntries(playlist);
            if (entries.Count == 0)
                return;

            ReplaceQueueAndPlay(entries);
        }

        void AddPlaylistEntriesToQueue(List<PlaylistEntry> entries, bool next)
        {
            if (entries == null || entries.Count == 0)
                return;

            if (next && _queueIndex < 0)
                EnsureCurrentSelectionQueued();

            var insertAt = _queue.Count;
            if (next && _queueIndex >= 0 && _queueIndex < _queue.Count)
                insertAt = _queueIndex + 1;

            for (int i = 0; i < entries.Count; i++)
                _queue.Insert(insertAt + i, entries[i]);

            SyncConfig();
            MarkDirty();
        }

        List<PlaylistEntry> CreatePlaylistEntries(MediaAudioPlaylistReference playlist)
        {
            var entries = new List<PlaylistEntry>();
            if (playlist == null || string.IsNullOrEmpty(playlist.FileName) || MyAPIGateway.Utilities == null)
                return entries;

            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(playlist.FileName, typeof(LcdModClientComponent)))
                    return entries;

                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(playlist.FileName, typeof(LcdModClientComponent)))
                {
                    if (reader == null)
                        return entries;

                    string pendingTitle = null;
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0)
                            continue;

                        if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                        {
                            var comma = line.IndexOf(',');
                            pendingTitle = comma >= 0 && comma + 1 < line.Length ? line.Substring(comma + 1).Trim() : null;
                            continue;
                        }

                        if (line[0] == '#')
                            continue;

                        var entry = CreatePlaylistEntryFromM3UPath(line, pendingTitle);
                        pendingTitle = null;
                        if (entry != null)
                            entries.Add(entry);
                    }
                }
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not load media playlist: " + error.Message);
            }

            return entries;
        }

        PlaylistEntry CreatePlaylistEntryFromM3UPath(string path, string title)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            var roots = MediaAudioFilePickerTreeProvider.GetCachedRootsOrBuild();
            if (roots != null)
            {
                for (int i = 0; i < roots.Count; i++)
                {
                    var files = new List<FileModel>();
                    AddAudioFiles(roots[i], files);
                    for (int j = 0; j < files.Count; j++)
                    {
                        var reference = files[j] == null ? null : files[j].Tag as MediaAudioFileReference;
                        if (reference == null || !ReferenceMatchesM3UPath(reference, path, files[j].FullPath))
                            continue;

                        var entry = CreatePlaylistEntry(reference);
                        ApplyM3UTitle(entry, title);
                        return entry;
                    }
                }
            }

            var contentPath = StripM3UPathPrefix(path, "Content/");
            if (!string.IsNullOrEmpty(contentPath))
            {
                for (int i = 0; i < _library.Length; i++)
                {
                    var item = _library[i];
                    if (item == null)
                        continue;

                    if (M3UPathsEqual(contentPath, item.WavePath) ||
                        M3UPathsEqual(path, item.WavePath) ||
                        string.Equals(path, item.Subtype, StringComparison.OrdinalIgnoreCase))
                    {
                        var entry = CreatePlaylistEntry(item);
                        ApplyM3UTitle(entry, title);
                        return entry;
                    }
                }
            }

            return null;
        }

        static void ApplyM3UTitle(PlaylistEntry entry, string title)
        {
            if (entry != null && !string.IsNullOrWhiteSpace(title))
                entry.Title = title.Trim();
        }

        static bool ReferenceMatchesM3UPath(MediaAudioFileReference reference, string path, string pickerFullPath)
        {
            if (reference == null || string.IsNullOrWhiteSpace(path))
                return false;

            if (M3UPathsEqual(path, pickerFullPath) ||
                M3UPathsEqual(path, reference.PickerFullPath) ||
                M3UPathsEqual(path, GetPickedAudioM3UPath(reference)) ||
                M3UPathsEqual(path, reference.DefinitionPath) ||
                M3UPathsEqual(StripM3UPathPrefix(path, "Content/"), reference.DefinitionPath))
                return true;

            if (reference.IsLocal && reference.LocalAsset != null)
            {
                if (M3UPathsEqual(StripM3UPathPrefix(path, "Local/"), reference.LocalAsset.RuntimePath) ||
                    M3UPathsEqual(StripM3UPathPrefix(path, "Local/"), reference.LocalAsset.SourceArchivePath) ||
                    M3UPathsEqual(StripM3UPathPrefix(path, "Local/"), reference.LocalAsset.SourcePath) ||
                    M3UPathsEqual(path, reference.LocalAsset.RuntimePath) ||
                    M3UPathsEqual(path, reference.LocalAsset.SourceArchivePath) ||
                    M3UPathsEqual(path, reference.LocalAsset.SourcePath))
                    return true;
            }

            return !string.IsNullOrEmpty(reference.FirstSoundSubtype) &&
                   string.Equals(path, reference.FirstSoundSubtype, StringComparison.OrdinalIgnoreCase);
        }

        static string StripM3UPathPrefix(string path, string prefix)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(prefix))
                return string.Empty;

            var normalized = NormalizeM3UPath(path);
            return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(prefix.Length)
                : string.Empty;
        }

        static bool M3UPathsEqual(string left, string right)
        {
            left = NormalizeM3UPath(left);
            right = NormalizeM3UPath(right);
            return left.Length > 0 && right.Length > 0 && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeM3UPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Replace('\\', '/').Trim('/');
        }

        void FavoritePickedReference(MediaAudioFileReference reference)
        {
            MarkLocalMediaInteraction();
            var entry = CreatePlaylistEntry(reference);
            if (entry == null)
                return;

            AppendM3U(FAVORITES_PLAYLIST_FILE, new List<PlaylistEntry> { entry }, true);
            MediaAudioFilePickerTreeProvider.InvalidateCache();
            MarkDirty();
        }

        List<PlaylistEntry> BuildQueueSnapshotForSave()
        {
            if (_queue.Count == 0)
                return new List<PlaylistEntry>();

            if (!_hasPreShuffleQueue || !MediaPlayerComponent.ShuffleEnabled)
                return new List<PlaylistEntry>(_queue);

            var remaining = new List<PlaylistEntry>(_queue);
            var ordered = new List<PlaylistEntry>();
            for (int i = 0; i < _preShuffleQueue.Count; i++)
            {
                var index = IndexOfRestorableEntry(remaining, _preShuffleQueue[i]);
                if (index < 0)
                    continue;

                ordered.Add(remaining[index]);
                remaining.RemoveAt(index);
            }

            ordered.AddRange(remaining);
            return ordered;
        }

        static string BuildPlaylistFileName(string displayName)
        {
            return PLAYLIST_SAVE_FILE_PREFIX + NormalizePlaylistDisplayName(displayName) + PLAYLIST_FILE_EXTENSION;
        }

        static string NormalizePlaylistDisplayName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var name = value.Trim();
            if (name.EndsWith(PLAYLIST_FILE_EXTENSION, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - PLAYLIST_FILE_EXTENSION.Length);

            var builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (c == '\\' || c == '/' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|' || char.IsControl(c))
                    builder.Append('_');
                else
                    builder.Append(c);
            }

            return builder.ToString().Trim();
        }

        static void RegisterSavedPlaylist(string fileName, string displayName)
        {
            if (string.IsNullOrEmpty(fileName) || MyAPIGateway.Utilities == null)
                return;

            var records = ReadPlaylistIndex();
            records[fileName] = string.IsNullOrWhiteSpace(displayName) ? NormalizePlaylistDisplayName(fileName) : displayName.Trim();

            try
            {
                var builder = new StringBuilder();
                foreach (var pair in records)
                {
                    builder.Append(pair.Key).Append('|').AppendLine((pair.Value ?? string.Empty).Replace('|', ' '));
                }

                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(PLAYLIST_INDEX_FILE, typeof(LcdModClientComponent)))
                {
                    writer.Write(builder.ToString());
                }
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not update media playlist index: " + error.Message);
            }
        }

        static Dictionary<string, string> ReadPlaylistIndex()
        {
            var records = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (MyAPIGateway.Utilities == null ||
                !MyAPIGateway.Utilities.FileExistsInLocalStorage(PLAYLIST_INDEX_FILE, typeof(LcdModClientComponent)))
                return records;

            try
            {
                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(PLAYLIST_INDEX_FILE, typeof(LcdModClientComponent)))
                {
                    if (reader == null)
                        return records;

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0)
                            continue;

                        var separator = line.IndexOf('|');
                        var fileName = separator < 0 ? line : line.Substring(0, separator).Trim();
                        var name = separator < 0 ? NormalizePlaylistDisplayName(fileName) : line.Substring(separator + 1).Trim();
                        if (!string.IsNullOrEmpty(fileName) && fileName.EndsWith(PLAYLIST_FILE_EXTENSION, StringComparison.OrdinalIgnoreCase))
                            records[fileName] = string.IsNullOrWhiteSpace(name) ? NormalizePlaylistDisplayName(fileName) : name;
                    }
                }
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not read media playlist index: " + error.Message);
            }

            return records;
        }

        void AppendM3U(string fileName, IList<PlaylistEntry> entries, bool append)
        {
            if (string.IsNullOrEmpty(fileName) || entries == null || entries.Count == 0 || MyAPIGateway.Utilities == null)
                return;

            try
            {
                var builder = new StringBuilder();
                if (append && MyAPIGateway.Utilities.FileExistsInLocalStorage(fileName, typeof(LcdModClientComponent)))
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(fileName, typeof(LcdModClientComponent)))
                    {
                        if (reader != null)
                            builder.Append(reader.ReadToEnd());
                    }
                }

                if (builder.Length == 0)
                    builder.AppendLine("#EXTM3U");

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null)
                        continue;

                    var title = string.IsNullOrEmpty(entry.Title) ? entry.Path : entry.Title;
                    builder.Append("#EXTINF:-1,").AppendLine(title ?? string.Empty);
                    builder.AppendLine(entry.Path ?? string.Empty);
                }

                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(fileName, typeof(LcdModClientComponent)))
                {
                    writer.Write(builder.ToString());
                }
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not save media playlist: " + error.Message);
            }
        }

        static string GetPickedAudioM3UPath(MediaAudioFileReference reference)
        {
            if (reference == null)
                return string.Empty;

            if (reference.IsLocal && reference.LocalAsset != null)
            {
                if (!string.IsNullOrEmpty(reference.LocalAsset.RuntimePath))
                    return "Local/" + reference.LocalAsset.RuntimePath.Replace('\\', '/');
                if (!string.IsNullOrEmpty(reference.LocalAsset.SourceArchivePath))
                    return "Local/" + reference.LocalAsset.SourceArchivePath.Replace('\\', '/');
                if (!string.IsNullOrEmpty(reference.LocalAsset.SourcePath))
                    return "Local/" + reference.LocalAsset.SourcePath.Replace('\\', '/');
            }

            if (!string.IsNullOrEmpty(reference.DefinitionPath))
                return "Content/" + reference.DefinitionPath.Replace('\\', '/');

            if (!string.IsNullOrEmpty(reference.PickerFullPath))
                return reference.PickerFullPath.Replace('\\', '/');

            return reference.FirstSoundSubtype ?? string.Empty;
        }


        bool IsShuffleEnabled()
        {
            return MediaPlayerComponent.ShuffleEnabled;
        }

        void ToggleShuffle(ButtonModel model, object sender)
        {
            MarkLocalMediaInteraction();
            if (MediaPlayerComponent.ShuffleEnabled)
            {
                MediaPlayerComponent.ShuffleEnabled = false;
                RestorePreShuffleQueueFuture();
            }
            else
            {
                MediaPlayerComponent.ShuffleEnabled = true;
                ResetShuffleSeed();
                CapturePreShuffleQueue();
                ShuffleQueueFuture();
            }

            SyncConfig();
            MarkDirty();
        }

        MediaRepeatMode GetRepeatMode()
        {
            var value = MediaPlayerComponent.RepeatModeInternal;
            if (value == (int)MediaRepeatMode.Single)
                return MediaRepeatMode.Single;
            if (value == (int)MediaRepeatMode.Folder)
                return MediaRepeatMode.Folder;
            return MediaRepeatMode.Disabled;
        }

        bool IsRepeatActive()
        {
            return GetRepeatMode() != MediaRepeatMode.Disabled;
        }

        void CycleRepeatMode(ButtonModel model, object sender)
        {
            MarkLocalMediaInteraction();
            var mode = GetRepeatMode();
            MediaRepeatMode next;
            if (mode == MediaRepeatMode.Disabled)
                next = MediaRepeatMode.Single;
            else if (mode == MediaRepeatMode.Single)
                next = MediaRepeatMode.Folder;
            else
                next = MediaRepeatMode.Disabled;

            MediaPlayerComponent.RepeatModeInternal = (int)next;
            SyncConfig();
            MarkDirty();
        }

        void HandlePlaybackCompletion()
        {
            var player = _player;
            if (player == null)
                return;

            if (player.IsActive)
            {
                _handledPlaybackCompletion = false;
                return;
            }

            if (_handledPlaybackCompletion ||
                player.IsPaused ||
                player.IsDecoding ||
                !player.HasLoadedAudio ||
                player.CurrentDurationSeconds <= 0.0 ||
                player.CurrentPositionSeconds + 0.05 < player.CurrentDurationSeconds)
            {
                return;
            }

            _handledPlaybackCompletion = true;

            var previousHandlingState = _handlingPlaybackCompletion;
            _handlingPlaybackCompletion = true;
            try
            {
                var repeatMode = GetRepeatMode();
                if (repeatMode == MediaRepeatMode.Single)
                {
                    StartSelectedAudio();
                    return;
                }

                if (MoveQueue(1, applyToActivePlayer: true, loop: repeatMode == MediaRepeatMode.Folder))
                    return;

                if (repeatMode == MediaRepeatMode.Folder)
                {
                    var moved = MoveSelection(
                        1,
                        allowScopeLoop: true,
                        useShuffle: false,
                        allowSameShuffleSelection: true,
                        applyToActivePlayer: false);

                    if (moved)
                        StartSelectedAudio();
                }
            }
            finally
            {
                _handlingPlaybackCompletion = previousHandlingState;
            }
        }

        void ClearPlaybackCompletionHandled()
        {
            if (!_handlingPlaybackCompletion)
                _handledPlaybackCompletion = false;
        }

        void SetPlaybackVolume(float value)
        {
            value = MathHelper.Clamp(value, 0f, 1f);
            if (_volumeSliderModel != null)
                _volumeSliderModel.Value = value;

            var player = _player;
            if (player == null && Host.GridLogic != null)
                player = GetHostMediaPlayer();

            if (player != null)
                player.Volume = value;

            MarkDirty();
        }

        GridMediaPlayer GetHostMediaPlayer()
        {
            if (Host.GridLogic == null)
                return null;

            return Host.GridLogic.MediaPlayers.Get(Host.Block?.EntityId ?? 0L, Host.SurfaceIndex);
        }

        bool TrySendMediaPlayerCommand(MediaPlayerCommandKind command, double positionSeconds, bool includeSource)
        {
            if (!IsMultiplayerMediaCommandRequired())
                return false;

            var block = Host.Block as IMyTerminalBlock;
            if (block == null)
                return false;

            var packet = new PacketRequestMediaPlayerCommand
            {
                BlockEntityId = block.EntityId,
                SurfaceIndex = Host.SurfaceIndex,
                AppTypeId = (int)AppType.MediaPlayer,
                Command = command,
                SourceKind = MediaPlayerSourceKind.None,
                SourceId = string.Empty,
                DisplayName = string.Empty,
                PositionSeconds = SanitizeCommandPosition(positionSeconds),
                ClientFrame = MyAPIGateway.Session == null ? 0L : MyAPIGateway.Session.GameplayFrameCounter
            };

            if (includeSource && !TryFillMediaPlayerCommandSource(packet))
                return false;

            if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
                LcdModSessionComponent.Server.HandleLocalRequestMediaPlayerCommand(packet);
            else
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, false);

            return true;
        }

        bool TryFillMediaPlayerCommandSource(PacketRequestMediaPlayerCommand packet)
        {
            if (packet == null)
                return false;

            if (_pickedAudio != null)
            {
                if (_pickedAudio.IsLocal && _pickedAudio.LocalAsset != null)
                    return false;

                if (_pickedAudio.IsSoundBlock)
                {
                    packet.SourceKind = MediaPlayerSourceKind.SoundSubtype;
                    packet.SourceId = _pickedAudio.FirstSoundSubtype;
                    packet.DisplayName = GetPickedAudioTitle(_pickedAudio);
                    return !string.IsNullOrWhiteSpace(packet.SourceId);
                }

                packet.SourceKind = MediaPlayerSourceKind.ContentPath;
                packet.SourceId = _pickedAudio.DefinitionPath;
                packet.DisplayName = GetPickedAudioTitle(_pickedAudio);
                return !string.IsNullOrWhiteSpace(packet.SourceId);
            }

            if (HasLibrarySelection())
            {
                packet.SourceKind = MediaPlayerSourceKind.SoundSubtype;
                packet.SourceId = _library[_selectedIndex].Subtype;
                packet.DisplayName = _library[_selectedIndex].DisplayName;
                return !string.IsNullOrWhiteSpace(packet.SourceId);
            }

            return false;
        }

        void TryStartLocalAudioStream(IMyTerminalBlock block)
        {
            if (!IsMultiplayerMediaCommandRequired() ||
                block == null ||
                _pickedAudio == null ||
                !_pickedAudio.IsLocal ||
                _pickedAudio.LocalAsset == null ||
                LcdModSessionComponent.Client == null)
            {
                return;
            }

            LcdModSessionComponent.Client.StartMediaPlayerLocalAudioStream(
                block,
                Host.SurfaceIndex,
                _pickedAudio.LocalAsset,
                GetPickedAudioTitle(_pickedAudio));
        }

        void CancelPendingLocalAudioStream()
        {
            var block = Host.Block as IMyTerminalBlock;
            if (block == null || LcdModSessionComponent.Client == null) return;

            LcdModSessionComponent.Client.CancelMediaPlayerLocalAudioStream(
                block.EntityId,
                Host.SurfaceIndex,
                stopPlayback: true);
        }

        static bool IsMultiplayerMediaCommandRequired()
        {
            return MyAPIGateway.Multiplayer != null && MyAPIGateway.Multiplayer.MultiplayerActive;
        }

        double GetPlayerPositionSeconds()
        {
            return _player?.CurrentPositionSeconds ?? 0.0;
        }

        static double SanitizeCommandPosition(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
                return 0.0;

            return seconds;
        }

        void SeekToPosition(double seconds)
        {
            if (_player != null)
            {
                if (!TrySendMediaPlayerCommand(MediaPlayerCommandKind.Seek, seconds, false))
                    _player.SeekTo(seconds);
                MarkDirty();
            }
        }

        void Stop()
        {
            CancelPendingLocalAudioStream();
            if (_player == null) 
                return;

            _player.ResetPlaybackEngine();
            MarkDirty();
        }

        void StopClicked(ButtonModel model, object sender)
        {
            MarkLocalMediaInteraction();
            CancelPendingLocalAudioStream();
            if (!TrySendMediaPlayerCommand(MediaPlayerCommandKind.Stop, GetPlayerPositionSeconds(), false))
                Stop();
        }

        void OnAudioPicked(FilePickerResult result)
        {
            MarkLocalMediaInteraction();
            var playlist = result?.Tag as MediaAudioPlaylistReference;
            if (playlist != null)
            {
                AddPlaylistReferenceToQueue(playlist, false);
                return;
            }

            var reference = result?.Tag as MediaAudioFileReference;
            if (reference == null)
                return;

            SelectPickedAudio(reference, syncContentSubtype: true, applyToActivePlayer: true);
        }

        void SelectPickedAudio(MediaAudioFileReference reference, bool syncContentSubtype, bool applyToActivePlayer)
        {
            if (reference == null)
                return;

            _pickedAudio = reference;
            _restorePickedAudioAttempted = true;
            ClearPlaybackCompletionHandled();
            MediaPlayerComponent.SelectedAudioSource = reference.Source ?? string.Empty;
            MediaPlayerComponent.SelectedPickerFullPath = reference.PickerFullPath ?? string.Empty;

            if (syncContentSubtype && !string.IsNullOrEmpty(reference.FirstSoundSubtype))
                SelectSubtypeWithoutClearingPicked(reference.FirstSoundSubtype);

            SyncConfig();
            if (applyToActivePlayer)
                ApplySelectedAudioToActivePlayer();
            MarkDirty();
        }

        static FolderModel FindAudioRoot(List<FolderModel> roots, string source)
        {
            if (roots == null || string.IsNullOrEmpty(source))
                return null;

            for (int i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (AudioRootMatchesSource(root, source))
                    return root;
            }

            return null;
        }

        static bool AudioRootMatchesSource(FolderModel root, string source)
        {
            if (root == null || string.IsNullOrEmpty(source))
                return false;

            if (string.Equals(root.FullPath, source, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(root.Name, source, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var files = new List<FileModel>();
            AddAudioFiles(root, files);
            for (int i = 0; i < files.Count; i++)
            {
                var reference = files[i] == null ? null : files[i].Tag as MediaAudioFileReference;
                if (reference != null && string.Equals(reference.Source, source, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
        
        static void AddAudioFiles(FolderModel folder, List<FileModel> result)
        {
            if (folder == null || result == null)
                return;

            var stack = new Stack<FolderModel>();
            var visited = new HashSet<FolderModel>();
            stack.Push(folder);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current == null || !visited.Add(current))
                    continue;

                if (current.Files != null && current.Files.Count > 0)
                {
                    current.Files.Sort(ComparePickerFiles);
                    for (int i = 0; i < current.Files.Count; i++)
                    {
                        if (current.Files[i] != null && (current.Files[i].Tag as MediaAudioFileReference) != null)
                            result.Add(current.Files[i]);
                    }
                }

                if (current.Folders == null || current.Folders.Count == 0)
                    continue;

                current.Folders.Sort(ComparePickerFolders);
                for (int i = current.Folders.Count - 1; i >= 0; i--)
                    stack.Push(current.Folders[i]);
            }
        }

        static FileModel FindFileByPickerFullPath(FolderModel folder, string pickerFullPath)
        {
            if (folder == null || string.IsNullOrEmpty(pickerFullPath))
                return null;

            var files = new List<FileModel>();
            AddAudioFiles(folder, files);
            for (int i = 0; i < files.Count; i++)
            {
                var file = files[i];
                if (file == null)
                    continue;

                if (string.Equals(file.FullPath, pickerFullPath, StringComparison.OrdinalIgnoreCase))
                    return file;

                var reference = file.Tag as MediaAudioFileReference;
                if (reference != null &&
                    string.Equals(reference.PickerFullPath, pickerFullPath, StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            return null;
        }

        static int IndexOfMatchingFile(List<FileModel> files, MediaAudioFileReference reference)
        {
            if (files == null || reference == null)
                return -1;

            for (int i = 0; i < files.Count; i++)
            {
                if (FileMatchesReference(files[i], reference))
                    return i;
            }

            return -1;
        }

        static bool FileMatchesReference(FileModel file, MediaAudioFileReference reference)
        {
            if (file == null || reference == null)
                return false;

            var fileReference = file.Tag as MediaAudioFileReference;
            if (ReferenceEquals(fileReference, reference))
                return true;

            if (fileReference == null)
                return false;

            if (!string.Equals(fileReference.Source, reference.Source, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(reference.PickerFullPath) &&
                string.Equals(fileReference.PickerFullPath, reference.PickerFullPath, StringComparison.OrdinalIgnoreCase))
                return true;

            if (reference.IsLocal)
                return reference.LocalAsset != null && ReferenceEquals(fileReference.LocalAsset, reference.LocalAsset);

            if (reference.IsSoundBlock)
                return !string.IsNullOrEmpty(reference.FirstSoundSubtype) &&
                       string.Equals(fileReference.FirstSoundSubtype, reference.FirstSoundSubtype, StringComparison.OrdinalIgnoreCase);

            if (reference.IsContent && !string.IsNullOrEmpty(reference.DefinitionPath) &&
                string.Equals(fileReference.DefinitionPath, reference.DefinitionPath, StringComparison.OrdinalIgnoreCase))
                return true;

            return !string.IsNullOrEmpty(reference.FirstSoundSubtype) &&
                   string.Equals(fileReference.FirstSoundSubtype, reference.FirstSoundSubtype, StringComparison.OrdinalIgnoreCase);
        }

        static int ComparePickerFolders(FolderModel left, FolderModel right)
        {
            return string.Compare(left?.Name, right?.Name, StringComparison.OrdinalIgnoreCase);
        }

        static int ComparePickerFiles(FileModel left, FileModel right)
        {
            return string.Compare(left?.Name, right?.Name, StringComparison.OrdinalIgnoreCase);
        }

        void SetSelectedIndex(int index, bool applyToActivePlayer)
        {
            if (_library.Length == 0)
                return;

            _pickedAudio = null;
            _restorePickedAudioAttempted = false;
            ClearPlaybackCompletionHandled();
            MediaPlayerComponent.SelectedAudioSource = string.Empty;
            MediaPlayerComponent.SelectedPickerFullPath = string.Empty;

            if (index < 0)
                index = 0;
            if (index >= _library.Length)
                index = _library.Length - 1;

            _selectedIndex = index;
            MediaPlayerComponent.SelectedIndex = _selectedIndex;
            MediaPlayerComponent.SelectedSoundSubtype = _library[_selectedIndex].Subtype;
            SyncConfig();
            if (applyToActivePlayer)
                ApplySelectedAudioToActivePlayer();
            MarkDirty();
        }

        void SelectSubtypeWithoutClearingPicked(string subtype)
        {
            if (_library.Length == 0 || string.IsNullOrEmpty(subtype))
                return;

            for (int i = 0; i < _library.Length; i++)
            {
                if (string.Equals(_library[i].Subtype, subtype, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedIndex = i;
                    MediaPlayerComponent.SelectedIndex = i;
                    MediaPlayerComponent.SelectedSoundSubtype = _library[i].Subtype;
                    return;
                }
            }
        }

        string GetPickedAudioLengthOrDetail(MediaAudioFileReference reference)
        {
            if (reference == null)
                return string.Empty;

            if (reference.IsLocal && reference.LocalAsset != null && reference.LocalAsset.DurationTicks > 0)
                return FormatDuration(TimeSpan.FromTicks(reference.LocalAsset.DurationTicks).TotalSeconds);

            return GetPickedAudioDetail(reference);
        }

        static string GetMediaItemDetail(MediaItem item)
        {
            if (item == null)
                return string.Empty;

            return "Content · " + GameAudioPcmLoader.GetContainerDisplayName(item.ContainerKind);
        }

        string FormatDuration(double seconds)
        {
            if (seconds <= 0.0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                return ResolveLoc(LOC_UNKNOWN_LENGTH);

            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalHours >= 1.0)
                return ((int)span.TotalHours).ToString(FormatingHelper.Culture) + ":" +
                       span.Minutes.ToString("00", FormatingHelper.Culture) + ":" +
                       span.Seconds.ToString("00", FormatingHelper.Culture);

            return span.Minutes.ToString(FormatingHelper.Culture) + ":" +
                   span.Seconds.ToString("00", FormatingHelper.Culture);
        }

        static string GetPickedAudioSongName(MediaAudioFileReference reference)
        {
            if (reference == null)
                return string.Empty;

            var subtype = reference.FirstSoundSubtype;
            if (!string.IsNullOrEmpty(subtype))
            {
                var definitionName = ResolveAudioDefinitionSongName(subtype, reference.DefinitionPath);
                if (!string.IsNullOrEmpty(definitionName))
                    return definitionName;
            }

            if (reference.IsLocal && reference.LocalAsset != null)
            {
                if (!string.IsNullOrEmpty(reference.LocalAsset.SourcePath))
                    return GetFileNameWithoutExtension(reference.LocalAsset.SourcePath);
                if (!string.IsNullOrEmpty(reference.LocalAsset.SourceArchivePath))
                    return GetFileNameWithoutExtension(reference.LocalAsset.SourceArchivePath);
            }

            if (!string.IsNullOrEmpty(reference.DefinitionPath))
                return GetFileNameWithoutExtension(reference.DefinitionPath);

            return GetFileNameWithoutExtension(GetPickedAudioTitle(reference));
        }

        static string ResolveAudioDefinitionSongName(string subtype, string fallbackPath)
        {
            if (MyDefinitionManager.Static == null || string.IsNullOrEmpty(subtype))
                return GetFileNameWithoutExtension(fallbackPath);

            var lookupName = ResolveSoundCategorySongName(subtype, fallbackPath);
            if (!string.IsNullOrEmpty(lookupName))
                return lookupName;

            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition == null || !string.Equals(definition.Id.SubtypeName, subtype, StringComparison.OrdinalIgnoreCase))
                    continue;

                return ResolveAudioDefinitionSongName(definition, fallbackPath);
            }

            return GetFileNameWithoutExtension(fallbackPath);
        }

        static string ResolveAudioDefinitionSongName(MyAudioDefinition definition, string fallbackPath)
        {
            if (definition == null)
                return GetFileNameWithoutExtension(fallbackPath);

            var lookupName = ResolveSoundCategorySongName(definition.Id.SubtypeName, fallbackPath);
            if (!string.IsNullOrEmpty(lookupName))
                return lookupName;

            var displayName = definition.DisplayNameText;
            if (IsMusicDefinition(definition, fallbackPath) &&
                !string.IsNullOrEmpty(displayName) &&
                !string.Equals(displayName, definition.Id.SubtypeName, StringComparison.OrdinalIgnoreCase))
                return displayName;

            return GetFileNameWithoutExtension(fallbackPath);
        }

        static string ResolveSoundCategorySongName(string subtype, string path)
        {
            var lookup = GetSoundCategoryNameLookup();
            if (lookup == null)
                return string.Empty;

            string name;
            if (!string.IsNullOrEmpty(subtype) && lookup.BySoundId.TryGetValue(subtype, out name))
                return name;

            var fileName = GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(fileName) && lookup.ByFileName.TryGetValue(fileName, out name))
                return name;

            return string.Empty;
        }

        static SoundCategoryNameLookup GetSoundCategoryNameLookup()
        {
            if (_soundCategoryNameLookup != null)
                return _soundCategoryNameLookup;

            if (MyDefinitionManager.Static == null)
                return null;

            var lookup = new SoundCategoryNameLookup();
            foreach (MySoundCategoryDefinition category in MyDefinitionManager.Static.GetSoundCategoryDefinitions())
            {
                if (category == null || category.Sounds == null)
                    continue;

                for (int i = 0; i < category.Sounds.Count; i++)
                {
                    var sound = category.Sounds[i];
                    if (sound == null || string.IsNullOrEmpty(sound.SoundId))
                        continue;

                    var text = sound.SoundText;
                    if (string.IsNullOrEmpty(text))
                        text = sound.SoundName;
                    if (!string.IsNullOrEmpty(text) && !lookup.BySoundId.ContainsKey(sound.SoundId))
                        lookup.BySoundId.Add(sound.SoundId, text);
                }
            }

            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition == null || string.IsNullOrEmpty(definition.Id.SubtypeName))
                    continue;

                string text;
                if (!lookup.BySoundId.TryGetValue(definition.Id.SubtypeName, out text) || string.IsNullOrEmpty(text))
                    continue;

                AddWaveFileNameLookup(lookup, GridMediaPlayer.FindStartWave(definition), text);
            }

            _soundCategoryNameLookup = lookup;
            return _soundCategoryNameLookup;
        }

        static void AddWaveFileNameLookup(SoundCategoryNameLookup lookup, string path, string text)
        {
            if (lookup == null || string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text))
                return;

            var fileName = GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(fileName) || lookup.ByFileName.ContainsKey(fileName))
                return;

            lookup.ByFileName.Add(fileName, text);
        }

        static bool IsMusicDefinition(MyAudioDefinition definition, string path)
        {
            if (definition != null &&
                !string.IsNullOrEmpty(definition.Id.SubtypeName) &&
                definition.Id.SubtypeName.StartsWith("Mus_", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.IsNullOrEmpty(path))
                return false;

            var normalized = path.Replace('\\', '/');
            return normalized.IndexOf("/MUS/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.StartsWith("MUS/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.IndexOf("/Music/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   normalized.StartsWith("Music/", StringComparison.OrdinalIgnoreCase);
        }

        static string GetFileNameWithoutExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var normalized = path.Replace('\\', '/');
            var slash = normalized.LastIndexOf('/');
            var name = slash >= 0 && slash + 1 < normalized.Length
                ? normalized.Substring(slash + 1)
                : normalized;
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(0, dot) : name;
        }

        static string GetPickedAudioTitle(MediaAudioFileReference reference)
        {
            if (reference == null)
                return string.Empty;

            if (reference.IsLocal && reference.LocalAsset != null)
                return string.IsNullOrEmpty(reference.LocalAsset.SourcePath)
                    ? reference.LocalAsset.Id
                    : reference.LocalAsset.SourcePath;

            return string.IsNullOrEmpty(reference.DefinitionPath)
                ? reference.FirstSoundSubtype
                : reference.DefinitionPath.Replace('/', '\\');
        }

        static string GetPickedAudioDetail(MediaAudioFileReference reference)
        {
            if (reference == null)
                return string.Empty;

            if (reference.IsLocal && reference.LocalAsset != null)
                return "Local · " + reference.LocalAsset.RuntimePath;

            var format = GameAudioPcmLoader.GetContainerDisplayName(GameAudioPcmLoader.GetContainerKind(reference.DefinitionPath));
            var owner = string.IsNullOrEmpty(reference.FirstSoundSubtype)
                ? string.Empty
                : " · " + reference.FirstSoundSubtype + "." + reference.FirstWaveSlot;
            return "Content · " + format + owner;
        }

        void RestorePickedAudioFromConfig()
        {
            if (_pickedAudio != null || _restorePickedAudioAttempted)
                return;

            var source = MediaPlayerComponent.SelectedAudioSource;
            if (string.IsNullOrEmpty(source))
                return;

            _restorePickedAudioAttempted = true;

            var roots = MediaAudioFilePickerTreeProvider.GetCachedRootsOrBuild();
            var root = FindAudioRoot(roots, source);
            if (root == null)
                return;

            var file = FindFileByPickerFullPath(root, MediaPlayerComponent.SelectedPickerFullPath);
            var reference = file?.Tag as MediaAudioFileReference;
            if (reference == null)
                return;

            _pickedAudio = reference;
            if (!string.IsNullOrEmpty(reference.FirstSoundSubtype))
                SelectSubtypeWithoutClearingPicked(reference.FirstSoundSubtype);
        }

        void RestoreQueueFromConfig()
        {
            if (_restoreQueueAttempted)
                return;

            _restoreQueueAttempted = true;
            var paths = MediaPlayerComponent.PlaylistPaths;
            if (paths == null || paths.Length == 0)
                return;

            var titles = MediaPlayerComponent.PlaylistTitles;
            var entries = new List<PlaylistEntry>();
            for (int i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                var title = titles != null && i < titles.Length ? titles[i] : null;
                var entry = CreatePlaylistEntryFromM3UPath(path, title);
                if (entry != null)
                    entries.Add(entry);
            }

            _queue.Clear();
            _queue.AddRange(entries);
            _selectedQueueEntries.Clear();
            ClearPreShuffleQueue();

            if (_queue.Count == 0)
            {
                _queueIndex = -1;
                return;
            }

            _queueIndex = MediaPlayerComponent.PlaylistIndex;
            if (_queueIndex < 0)
                _queueIndex = 0;
            if (_queueIndex >= _queue.Count)
                _queueIndex = _queue.Count - 1;
            var current = _queue[_queueIndex];
            var previousSuppress = _suppressConfigSync;
            _suppressConfigSync = true;
            try
            {
                ApplyPlaylistEntrySelection(current);
            }
            finally
            {
                _suppressConfigSync = previousSuppress;
            }
            UpdatePlaylistListModel();
        }

        void PersistPlaylistStateToConfig()
        {
            if (_shuffleSeed == 0)
                ResetShuffleSeed();

            var paths = new string[_queue.Count];
            var titles = new string[_queue.Count];
            for (int i = 0; i < _queue.Count; i++)
            {
                var entry = _queue[i];
                paths[i] = entry == null ? string.Empty : entry.Path ?? string.Empty;
                titles[i] = entry == null ? string.Empty : entry.Title ?? string.Empty;
            }

            MediaPlayerComponent.PlaylistPaths = paths;
            MediaPlayerComponent.PlaylistTitles = titles;
            MediaPlayerComponent.PlaylistIndex = _queueIndex >= 0 && _queueIndex < _queue.Count ? _queueIndex : -1;
            MediaPlayerComponent.ShuffleSeed = _shuffleSeed;
        }

        void NormalizeSelectionFromConfig()
        {
            if (_library.Length == 0)
            {
                _selectedIndex = -1;
                MediaPlayerComponent.SelectedIndex = -1;
                MediaPlayerComponent.SelectedSoundSubtype = string.Empty;
                return;
            }

            var selectedSubtype = MediaPlayerComponent.SelectedSoundSubtype;
            if (!string.IsNullOrEmpty(selectedSubtype))
            {
                for (int i = 0; i < _library.Length; i++)
                {
                    if (string.Equals(_library[i].Subtype, selectedSubtype, StringComparison.OrdinalIgnoreCase))
                    {
                        _selectedIndex = i;
                        MediaPlayerComponent.SelectedIndex = i;
                        return;
                    }
                }

                MediaPlayerComponent.SelectedSoundSubtype = string.Empty;
            }

            _selectedIndex = -1;
            MediaPlayerComponent.SelectedIndex = -1;
        }

        bool HasLibrarySelection()
        {
            return _library != null && _selectedIndex >= 0 && _selectedIndex < _library.Length;
        }

        void EnsureLibrary()
        {
            if (_cachedLibrary == null)
            {
                lock (LibraryLock)
                {
                    if (_cachedLibrary == null)
                        _cachedLibrary = BuildLibrary();
                }
            }

            _library = _cachedLibrary ?? EmptyLibrary;
        }

        static MediaItem[] BuildLibrary()
        {
            if (MyDefinitionManager.Static == null)
                return EmptyLibrary;

            var list = new List<MediaItem>();
            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition == null)
                    continue;

                var wavePath = GridMediaPlayer.FindStartWave(definition);
                if (string.IsNullOrEmpty(wavePath))
                    continue;

                list.Add(new MediaItem
                {
                    Subtype = definition.Id.SubtypeName,
                    DisplayName = ResolveAudioDefinitionSongName(definition, wavePath),
                    WavePath = wavePath,
                    ContainerKind = GameAudioPcmLoader.GetContainerKind(wavePath)
                });
            }

            list.Sort(delegate(MediaItem left, MediaItem right)
            {
                return string.Compare(left.Subtype, right.Subtype, StringComparison.OrdinalIgnoreCase);
            });

            return list.ToArray();
        }

        void SyncConfig()
        {
            if (_suppressConfigSync || !_lastScreenInteractionWasLocal)
                return;

            var terminalBlock = Host.Block as IMyTerminalBlock;
            if (terminalBlock == null)
                return;

            PersistPlaylistStateToConfig();
            ConfigManager.Sync(terminalBlock, Host.ProviderConfig);
        }
    }
}
