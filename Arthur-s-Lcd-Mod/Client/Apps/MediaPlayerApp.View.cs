using System;
using System.Collections.Generic;
using LcdMod.Common.Config.Components;
using LcdMod.Client.Gui.Styling;
using VRageMath;

namespace LcdMod.Client.Apps
{
    public sealed partial class MediaPlayerApp
    {
        internal struct MediaQueueRow
        {
            public string Title;
            public string Detail;
            public string Icon;
        }

        internal float MediaLayoutScale => GeneralComponent.GetScale() > 0f ? GeneralComponent.GetScale() : 1f;
        internal float MediaFontScale => Host.Surface.FontSize > 0f ? Host.Surface.FontSize : 1f;
        internal string MediaTextFont => TextFont;
        internal Color MediaForeground => Host.ForegroundColor;
        internal Color MediaActiveColor => Opaque(GetMediaActiveColor());
        internal Color MediaInactiveSurfaceColor => Opaque(GetMediaInactiveSurfaceColor());
        internal Color MediaInactiveForegroundColor => GetMediaInactiveForegroundColor();
        internal Color MediaErrorColor => Opaque(ColorComponent.ResolveErrorColor());
        internal Color MediaResource(ResourceKey<Color> key, Color fallback) => ResolveResource(key, fallback);

        internal string MediaSongPath
        {
            get
            {
                string path = GetCurrentSongPath();
                return string.IsNullOrEmpty(path) ? ResolveLoc(LOC_NO_SUPPORTED_AUDIO) : path;
            }
        }

        internal bool MediaPlaylistVisible => IsPlaylistVisible();
        internal bool MediaShuffleEnabled => IsShuffleEnabled();
        internal bool MediaRepeatActive => GetRepeatMode() != MediaRepeatMode.Disabled;
        internal string MediaRepeatSuffix => GetRepeatButtonText();
        internal bool MediaPlaying => IsPlayToggleActive();
        internal bool MediaShiftStop => IsShiftPressed();
        internal bool MediaCanReset => CanResetPlayer();
        internal bool MediaPickEnabled => _interactiveHost != null;
        internal bool MediaLibraryEnabled => _pickedAudio != null || HasLibrarySelection() || _queue.Count > 0;

        internal bool MediaCanTogglePlay
        {
            get
            {
                bool canStart = _pickedAudio != null || HasLibrarySelection() || HasQueuedCurrentOrNext();
                return _player != null && (IsShiftPressed()
                    ? CanResetPlayer()
                    : canStart || _player.IsPlaying || _player.IsPaused);
            }
        }

        internal double MediaPositionSeconds => _player == null ? 0d : _player.CurrentPositionSeconds;
        internal double MediaDurationSeconds => _player == null ? 0d : _player.CurrentDurationSeconds;
        internal bool MediaCanSeek => _player != null && _player.CanSeek;
        internal float MediaVolume => _volume;
        internal bool MediaVisualizerEnabled => MediaPlayerComponent.VisualizerEnabled;
        internal float[] MediaVisualizerLevels => _visualizerLevels;
        internal string MediaFileIcon => GetCurrentAudioFileIcon();
        internal int MediaQueueIndex => _queueIndex;
        internal int MediaQueueCount => _queue.Count;
        internal string MediaClearQueueText => ResolveLoc(LOC_CLEAR_QUEUE);
        internal string MediaSaveQueueText => ResolveLoc(LOC_SAVE_QUEUE);
        internal string MediaQueueEmptyText => ResolveLoc(LOC_QUEUE_EMPTY);

        internal void FillQueueRows(List<MediaQueueRow> rows)
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

        internal void MediaPick() { PickAudio(); }
        internal void MediaShuffle() { ToggleShuffle(); }
        internal void MediaPrevious() { Previous(); }
        internal void MediaTogglePlay() { TogglePlay(); }
        internal void MediaNext() { Next(); }
        internal void MediaRepeat() { CycleRepeatMode(); }
        internal void MediaTogglePlaylist() { TogglePlaylist(); }
        internal void MediaStop() { StopClicked(); }
        internal void MediaClearQueue() { ClearQueue(); }
        internal void MediaSaveQueue() { SaveQueue(); }
        internal void MediaSeek(double seconds) { SeekToPosition(seconds); }
        internal void MediaSetVolume(float value) { SetPlaybackVolume(value); }

        internal void MediaPlayQueueEntry(int index)
        {
            if (index >= 0 && index < _queue.Count)
                OnPlaylistEntryClicked(_queue[index]);
        }

        internal void MediaMoveQueueEntry(int source, int target)
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
