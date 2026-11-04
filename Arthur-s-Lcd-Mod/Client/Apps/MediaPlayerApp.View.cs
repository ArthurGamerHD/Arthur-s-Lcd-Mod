using System;
using System.Collections.Generic;
using LcdMod.Common.Config.Components;
using LcdMod.Client.Apps.MediaPlayer;
using LcdMod.Client.Gui.Styling;
using VRageMath;

namespace LcdMod.Client.Apps
{
    public sealed partial class MediaPlayerApp : IMediaPlayerHost
    {
        internal struct MediaQueueRow
        {
            public string Title;
            public string Detail;
            public string Icon;
        }

        Dictionary<string, string> _surfaceFonts;

        internal override float WindowRenderScaling => CardScale;
        float IMediaPlayerHost.MediaFontScale => Host.Surface.FontSize > 0f ? Host.Surface.FontSize : 1f;
        string IMediaPlayerHost.MediaTextFont => TextFont;
        Color IMediaPlayerHost.MediaForeground => Host.ForegroundColor;
        Color IMediaPlayerHost.MediaActiveColor => Opaque(GetMediaActiveColor());
        Color IMediaPlayerHost.MediaInactiveSurfaceColor => Opaque(GetMediaInactiveSurfaceColor());
        Color IMediaPlayerHost.MediaInactiveForegroundColor => GetMediaInactiveForegroundColor();
        Color IMediaPlayerHost.MediaErrorColor => Opaque(ColorComponent.ResolveErrorColor());
        internal Color MediaResource(ResourceKey<Color> key, Color fallback) => ResolveResource(key, fallback);

        string IMediaPlayerHost.MediaSongPath
        {
            get
            {
                string path = GetCurrentSongPath();
                return string.IsNullOrEmpty(path) ? ResolveLoc(LOC_NO_SUPPORTED_AUDIO) : path;
            }
        }

        bool IMediaPlayerHost.MediaPlaylistVisible => IsPlaylistVisible();
        bool IMediaPlayerHost.MediaShuffleEnabled => IsShuffleEnabled();
        bool IMediaPlayerHost.MediaRepeatActive => GetRepeatMode() != MediaRepeatMode.Disabled;
        string IMediaPlayerHost.MediaRepeatSuffix => GetRepeatButtonText();
        bool IMediaPlayerHost.MediaPlaying => IsPlayToggleActive();
        bool IMediaPlayerHost.MediaShiftStop => IsShiftPressed();
        bool IMediaPlayerHost.MediaCanReset => CanResetPlayer();
        bool IMediaPlayerHost.MediaPickEnabled => _interactiveHost != null;
        bool IMediaPlayerHost.MediaLibraryEnabled => _pickedAudio != null || HasLibrarySelection() || _queue.Count > 0;

        bool IMediaPlayerHost.MediaCanTogglePlay
        {
            get
            {
                bool canStart = _pickedAudio != null || HasLibrarySelection() || HasQueuedCurrentOrNext();
                return _player != null && (IsShiftPressed()
                    ? CanResetPlayer()
                    : canStart || _player.IsPlaying || _player.IsPaused);
            }
        }

        double IMediaPlayerHost.MediaPositionSeconds => _player == null ? 0d : _player.CurrentPositionSeconds;
        double IMediaPlayerHost.MediaDurationSeconds => _player == null ? 0d : _player.CurrentDurationSeconds;
        bool IMediaPlayerHost.MediaCanSeek => _player != null && _player.CanSeek;
        float IMediaPlayerHost.MediaVolume => _volume;
        bool IMediaPlayerHost.MediaVisualizerEnabled => MediaPlayerComponent.VisualizerEnabled;
        bool IMediaPlayerHost.MediaHasVideo => _player != null && _player.Video != null;

        float IMediaPlayerHost.MediaVisualizerAspectRatio
        {
            get
            {
                var video = _player?.Video;
                return video == null || video.Height <= 0 ? 1f : video.Width / (float)video.Height;
            }
        }

        object IMediaPlayerHost.MediaVideoKey => _player?.Video;
        bool IMediaPlayerHost.MediaHasSubtitles => _player?.Video != null && _player.Video.HasSubtitles;

        // The surface's own name for face, or null when the surface has no such font.
        string IMediaPlayerHost.MediaResolveFont(string face)
        {
            if (string.IsNullOrEmpty(face))
                return null;
            if (_surfaceFonts == null)
            {
                var fonts = new List<string>();
                Host.Surface.GetFonts(fonts);
                _surfaceFonts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string font in fonts)
                    _surfaceFonts[font] = font;
            }
            string resolved;
            return _surfaceFonts.TryGetValue(face, out resolved) ? resolved : null;
        }

        string IMediaPlayerHost.MediaSubtitle
        {
            get
            {
                var video = _player?.Video;
                return video == null ? null : video.SubtitleAt(_player.CurrentPositionSeconds);
            }
        }
        float[] IMediaPlayerHost.MediaVisualizerLevels => _visualizerLevels;
        string IMediaPlayerHost.MediaFileIcon => GetCurrentAudioFileIcon();
        int IMediaPlayerHost.MediaQueueIndex => _queueIndex;
        int IMediaPlayerHost.MediaQueueCount => _queue.Count;
        string IMediaPlayerHost.MediaClearQueueText => ResolveLoc(LOC_CLEAR_QUEUE);
        string IMediaPlayerHost.MediaSaveQueueText => ResolveLoc(LOC_SAVE_QUEUE);
        string IMediaPlayerHost.MediaQueueEmptyText => ResolveLoc(LOC_QUEUE_EMPTY);
        string IMediaPlayerHost.MediaPickText => ResolveLoc(LOC_PICK);
        string IMediaPlayerHost.MediaPreviousText => ResolveLoc(LOC_PREVIOUS);
        string IMediaPlayerHost.MediaNextText => ResolveLoc(LOC_NEXT);
        string IMediaPlayerHost.MediaShuffleText => ResolveLoc(LOC_SHUFFLE);
        string IMediaPlayerHost.MediaRepeatText => ResolveLoc(LOC_REPEAT);
        string IMediaPlayerHost.MediaPlaylistText => ResolveLoc(LOC_PLAYLIST);
        string IMediaPlayerHost.MediaStopText => ResolveLoc(LOC_STOP);

        void IMediaPlayerHost.FillQueueRows(List<MediaQueueRow> rows)
        {
            rows.Clear();
            for (int i = 0; i < _queue.Count; i++)
            {
                var entry = _queue[i];
                rows.Add(new MediaQueueRow
                {
                    Title = GetPlaylistEntryText(entry),
                    Detail = GetPlaylistEntryDetailText(entry, i),
                    Icon = string.IsNullOrEmpty(entry.Icon) ? GetPlaylistEntryIcon(entry) : entry.Icon
                });
            }
        }

        string IMediaPlayerHost.MediaTitleText
        {
            get
            {
                string title = Title;
                return title == LOC_TITLE ? ResolveLoc(LOC_TITLE) : title;
            }
        }

        string IMediaPlayerHost.MediaVisualizerTexture => MediaVisualizerTexture;
        string IMediaPlayerHost.ConfigureVisualizerText => ConfigureVisualizerText;
        void IMediaPlayerHost.OpenVisualizerSettings() { OpenVisualizerSettings(); }
        void IMediaPlayerHost.MediaSetVideoChrome(bool video) { MediaSetVideoChrome(video); }

        // Video draws its own sliding title, so the app chrome's title gives way to it.
        internal void MediaSetVideoChrome(bool video)
        {
            _videoChrome = video;
            SetContentLayout(video || !Host.TitleVisible);
        }

        void IMediaPlayerHost.MediaPick() { PickAudio(); }
        void IMediaPlayerHost.MediaShuffle() { ToggleShuffle(); }
        void IMediaPlayerHost.MediaPrevious() { Previous(); }
        void IMediaPlayerHost.MediaTogglePlay() { TogglePlay(); }
        void IMediaPlayerHost.MediaNext() { Next(); }
        void IMediaPlayerHost.MediaRepeat() { CycleRepeatMode(); }
        void IMediaPlayerHost.MediaTogglePlaylist() { TogglePlaylist(); }
        void IMediaPlayerHost.MediaStop() { StopClicked(); }
        void IMediaPlayerHost.MediaClearQueue() { ClearQueue(); }
        void IMediaPlayerHost.MediaSaveQueue() { SaveQueue(); }
        void IMediaPlayerHost.MediaSeek(double seconds) { SeekToPosition(seconds); }
        void IMediaPlayerHost.MediaSetVolume(float value) { SetPlaybackVolume(value); }

        void IMediaPlayerHost.MediaPlayQueueEntry(int index)
        {
            if (index >= 0 && index < _queue.Count)
                OnPlaylistEntryClicked(_queue[index]);
        }

        void IMediaPlayerHost.MediaMoveQueueEntry(int source, int target)
        {
            if (source < 0 || source >= _queue.Count)
                return;
            target = Math.Max(0, Math.Min(_queue.Count - 1, target));
            if (target != source)
                MovePlaylistEntry(_queue[source], source, target);
        }

        static Color Opaque(Color color)
        {
            color.A = byte.MaxValue;
            return color;
        }
    }
}
