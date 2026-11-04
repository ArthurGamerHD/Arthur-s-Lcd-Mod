using Acrux.Dialogs;
using System;
using System.Collections.Generic;
using System.Text;
using Generated;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Audio;
using LcdMod.Client.Video;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Apps.Dialogs;
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
        const int DEFAULT_VISUALIZER_BARS = 32;
        const string PLAYLIST_SAVE_FILE_PREFIX = "music_cache_playlist_";
        const string PLAYLIST_INDEX_FILE = "music_cache_playlists.txt";
        const string PLAYLIST_FILE_EXTENSION = ".m3u";
        const string FAVORITES_PLAYLIST_FILE = "music_cache_favorites.m3u";

        static readonly object LibraryLock = new object();
        static readonly List<FolderModel> EmptyFolderRoots = new List<FolderModel>(0);
        static MediaItem[] _cachedLibrary;
        static SoundCategoryNameLookup _soundCategoryNameLookup;

        float _volume = 1f;
        readonly float[] _visualizerLevels = new float[DEFAULT_VISUALIZER_BARS];
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
        bool _startingQueueEntry;

        MediaItem[] _library = EmptyLibrary;
        int _selectedIndex = -1;
        int _queueIndex = -1;
        int _shuffleSeed;
        MediaPlayerConfigComponent _lastMediaPlayerComponent;
        GridMediaPlayer _player;
        MediaAudioFileReference _pickedAudio;
        InteractiveSurfaceScript _interactiveHost;
        MediaPlayer.MediaPlayerViewModel _nativeViewModel;
        bool _videoChrome;

        static readonly MediaItem[] EmptyLibrary = Array.Empty<MediaItem>();

        public override string Title
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
        const string LOC_PICK = MOD_PREFIX + "MediaPlayer_Pick";
        const string LOC_PREVIOUS = MOD_PREFIX + "MediaPlayer_Previous";
        const string LOC_NEXT = MOD_PREFIX + "MediaPlayer_Next";
        const string LOC_SHUFFLE = MOD_PREFIX + "MediaPlayer_Shuffle";
        const string LOC_REPEAT = MOD_PREFIX + "MediaPlayer_Repeat";
        const string LOC_PLAYLIST = MOD_PREFIX + "MediaPlayer_Playlist";
        const string LOC_STOP = MOD_PREFIX + "MediaPlayer_Stop";

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

        enum MediaRepeatMode
        {
            Disabled = 0,
            Single = 1,
            Folder = 2
        }

        public MediaPlayerApp(IAppHost host) : base(host)
        {
            BindView();
            _interactiveHost = host as InteractiveSurfaceScript;
        }

        internal void RebindHost(IAppHost host)
        {
            _visualizerDetached = false;
            RebindAppHost(host);
            _interactiveHost = host as InteractiveSurfaceScript;
            BindView();
            LayoutChanged();
            MarkDirty();
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
        }

        void BindView()
        {
            _nativeViewModel = new MediaPlayer.MediaPlayerViewModel(this);
            SetBackgroundContent(new MediaPlayer.MediaVisualizerView { DataContext = _nativeViewModel });
            SetContent(new MediaPlayer.MediaPlayerView
            {
                DataContext = _nativeViewModel
            });
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            _locKeysCache.Clear();
            MediaSetVideoChrome(_videoChrome);
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
            UpdatePlayback();
            _nativeViewModel.Refresh();
            if (!CanRenderVisualizer)
                ReleaseVisualizerTexture();
            ScheduleVisualizerFrameIfNeeded();
        }

        // Playback only (volume, playlist, track advance): the view and visualizer need a screen a detached player lacks.
        internal void UpdateDetached()
        {
            UpdatePlayback();
        }

        void UpdatePlayback()
        {
            EnsureLibrary();
            _player = GetHostMediaPlayer();
            if (_player != null)
                _player.Volume = _volume;
            ObserveMediaConfigOrigin();
            EnsureShuffleSeed();
            RestorePickedAudioFromConfig();
            NormalizeSelectionFromConfig();
            RestoreQueueFromConfig();
            HandlePlaybackCompletion();
        }


        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
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

                if (_pickedAudio.IsVideo)
                    return _pickedAudio.PickerFullPath;

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








        bool CanResetPlayer()
        {
            if (_player == null)
                return false;

            return _player.IsActive || _player.HasLoadedAudio;
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
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase))
                return "FileMkv";
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase))
                return "FileWebm";
            if (Adk.Video.MediaProbe.IsVideoExtension(path))
                return "FileBlank";

            return GameAudioPcmLoader.GetContainerKind(path) == GameAudioContainerKind.Xwma
                ? "FileXwm"
                : "FileWav";
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

                TickVisualizerTexture();
                _nativeViewModel.RefreshFrame();
                if (_interactiveHost != null)
                    _interactiveHost.RenderSprites();
                ScheduleVisualizerFrameIfNeeded();
            });
        }

        bool ShouldScheduleVisualizerFrames()
        {
            if (_visualizerClosed || _visualizerDetached || _player == null || _nativeViewModel == null || _interactiveHost == null)
                return false;

            bool hasFrameBoundControl = _nativeViewModel.HasFrameBoundContent;
            return hasFrameBoundControl &&
                   (_player.IsPlaying ||
                    NeedsVisualizerFrames);
        }




        string GetRepeatButtonText()
        {
            return GetRepeatMode() == MediaRepeatMode.Single ? "1" : string.Empty;
        }




        // The file picker was opened at least once (the local videos FTUE tip waits for it).
        public bool PickerOpened { get; private set; }

        void PickAudio()
        {
            if (_interactiveHost == null)
                return;

            FilePickerViewModel dialog = null;
            dialog = new FilePickerViewModel(
                ResolveLoc(LOC_PICK_AUDIO),
                FilePickerMode.PickFile,
                EmptyFolderRoots,
                OnAudioPicked,
                MediaAudioFilePickerTreeProvider.CurrentPath,
                MediaAudioFilePickerTreeProvider.SetCurrentPath,
                result => BuildAudioPickerContextActions(result, dialog));
            dialog.SetLoading(true, ResolveLoc(LOC_LOADING_AUDIO_FILES));
            if (!ShowDialog(new FilePickerView(dialog)))
                return;
            PickerOpened = true;

            MediaAudioFilePickerTreeProvider.BuildRootsAsync(delegate(List<FolderModel> roots, Exception error)
            {
                if (dialog.IsClosed)
                    return;

                dialog.SetRoots(roots ?? EmptyFolderRoots);
                dialog.SetLoading(false);
                if (error != null)
                    LogHelper.Log(MyLogSeverity.Warning, "Could not build media audio picker tree: " + error.Message);

                if (_interactiveHost != null)
                    _interactiveHost.RequestRedraw();
            });
        }

        void Previous()
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

        void Next()
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

        void TogglePlay()
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

            _player.Volume = _volume;

            CancelPendingLocalAudioStream();
            if (_pickedAudio != null && _pickedAudio.IsVideo)
            {
                var source = new LocalVideoChunkSource(_pickedAudio.LocalVideo);
                _player.PlayVideo(block, GetPickedAudioTitle(_pickedAudio), source, startPaused);
                TryStartLocalVideoStream(block, source);
                MarkDirty();
                return;
            }

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


        bool IsShiftPressed()
        {
            return MyAPIGateway.Input.IsAnyShiftKeyPressed();
        }

        bool IsPlaylistVisible()
        {
            return _playlistVisible;
        }

        void TogglePlaylist()
        {
            MarkLocalMediaInteraction();
            _playlistVisible = !_playlistVisible;
            if (_playlistVisible)
            {
            }
            else
            {
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

        void ClearQueue()
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

        void SaveQueue()
        {
            MarkLocalMediaInteraction();
            if (_queue.Count == 0)
                return;

            AskText(
                ResolveLoc(LOC_SAVE_PLAYLIST_TITLE),
                ResolveLoc(LOC_SAVE_PLAYLIST_PROMPT),
                "playlist_" + DateTime.UtcNow.ToFileTime(),
                OnSavePlaylistNameEntered);
        }

        void OnSavePlaylistNameEntered(string name)
        {
            var displayName = NormalizePlaylistDisplayName(name);
            if (string.IsNullOrEmpty(displayName))
            {
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

        List<FilePickerContextAction> BuildAudioPickerContextActions(FilePickerResult result, FilePickerViewModel dialog)
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

        void DeleteLocalAudioFromPicker(MediaAudioFileReference reference, FilePickerViewModel dialog)
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

        void DeletePlaylistFromPicker(MediaAudioPlaylistReference playlist, FilePickerViewModel dialog)
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

        void DeleteLocalAudio(MediaAudioFileReference reference, FilePickerViewModel dialog, string displayName)
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

        void DeletePlaylist(MediaAudioPlaylistReference playlist, FilePickerViewModel dialog, string displayName)
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

        void QueueAudioDelete(FilePickerViewModel dialog, AudioDeleteOperation deleteOperation, Action completed)
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

        void CompleteQueuedAudioDelete(FilePickerViewModel dialog, AudioDeleteWork work, Action completed)
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

        void SetAudioPickerBusy(FilePickerViewModel dialog, bool busy)
        {
            if (dialog != null && !dialog.IsClosed)
                dialog.SetLoading(busy, ResolveLoc(LOC_LOADING_AUDIO_FILES));

            if (_interactiveHost != null)
                _interactiveHost.RequestRedraw();
        }


        bool AudioReferencesMatch(MediaAudioFileReference left, MediaAudioFileReference right)
        {
            if (ReferenceEquals(left, right))
                return true;

            return PlaylistEntriesMatch(CreatePlaylistEntry(left), CreatePlaylistEntry(right));
        }

        void RefreshAudioPickerDialog(FilePickerViewModel dialog)
        {
            MediaAudioFilePickerTreeProvider.InvalidateCache();
            if (dialog == null || dialog.IsClosed)
            {
                if (_interactiveHost != null)
                    _interactiveHost.RequestRedraw();
                return;
            }

            dialog.SetLoading(true, ResolveLoc(LOC_LOADING_AUDIO_FILES));
            MediaAudioFilePickerTreeProvider.BuildRootsAsync(delegate(List<FolderModel> roots, Exception error)
            {
                if (!dialog.IsClosed)
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

            MyAPIGateway.Utilities.ShowNotification(
                string.Format(FormatingHelper.Culture, ResolveLoc(LOC_DELETE_FAILED_FORMAT), failureReason),
                4000,
                "Red");
        }

        static void ShowAudioDeleteNotification(string message)
        {
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
            if (playlist == null || string.IsNullOrEmpty(playlist.FileName))
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
            if (string.IsNullOrEmpty(fileName))
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
            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(PLAYLIST_INDEX_FILE, typeof(LcdModClientComponent)))
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
            if (string.IsNullOrEmpty(fileName) || entries == null || entries.Count == 0)
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

            if (reference.IsVideo)
                return reference.PickerFullPath;

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

        void ToggleShuffle()
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

        void CycleRepeatMode()
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
            _volume = value;

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
                ClientFrame = MyAPIGateway.Session.GameplayFrameCounter
            };

            if (includeSource && !TryFillMediaPlayerCommandSource(packet))
                return false;

            if (MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
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
                if ((_pickedAudio.IsLocal && _pickedAudio.LocalAsset != null) || _pickedAudio.IsVideo)
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

        void TryStartLocalVideoStream(IMyTerminalBlock block, LocalVideoChunkSource source)
        {
            if (!IsMultiplayerMediaCommandRequired() || block == null || _pickedAudio == null ||
                !_pickedAudio.IsVideo || LcdModSessionComponent.Client == null)
            {
                return;
            }

            LcdModSessionComponent.Client.StartMediaPlayerLocalVideoStream(
                block,
                Host.SurfaceIndex,
                source,
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
            return MyAPIGateway.Multiplayer.MultiplayerActive;
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

        void StopClicked()
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

                // The picker tree lives in Acrux UI: read each list across once, then work locally.
                var files = current.Files == null ? null : new List<FileModel>(current.Files);
                if (files != null && files.Count > 0)
                {
                    files.Sort(ComparePickerFiles);
                    for (int i = 0; i < files.Count; i++)
                    {
                        if (files[i] != null && !files[i].IsBlocked &&
                            (files[i].Tag as MediaAudioFileReference) != null)
                            result.Add(files[i]);
                    }
                }

                var folders = current.Folders == null ? null : new List<FolderModel>(current.Folders);
                if (folders == null || folders.Count == 0)
                    continue;

                folders.Sort(ComparePickerFolders);
                for (int i = folders.Count - 1; i >= 0; i--)
                    stack.Push(folders[i]);
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

            if (reference.IsVideo && reference.LocalVideo.Probe != null && reference.LocalVideo.Probe.Duration > 0)
                return FormatDuration(reference.LocalVideo.Probe.Duration);

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

            if (reference.IsVideo)
                return "Video · " + reference.LocalVideo.FileName;

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
