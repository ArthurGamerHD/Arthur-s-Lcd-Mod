using System.Collections.Generic;
using VRageMath;

namespace LcdMod.Client.Apps.MediaPlayer
{
    /// <summary>
    /// What the media player's view model reads from and asks of its app: the player state, the surface's look and
    /// the localized texts. <see cref="MediaPlayerApp"/> in game; a design-time host in the IDE preview.
    /// </summary>
    internal interface IMediaPlayerHost
    {
        float MediaFontScale { get; }
        float SurfaceFontSize { get; }
        string MediaTextFont { get; }
        Color MediaForeground { get; }
        Color MediaActiveColor { get; }
        Color MediaInactiveSurfaceColor { get; }
        Color MediaInactiveForegroundColor { get; }
        Color MediaErrorColor { get; }
        bool TitleEnabled { get; }
        string MediaTitleText { get; }

        string MediaSongPath { get; }
        string MediaFileIcon { get; }
        bool MediaPlaylistVisible { get; }
        bool MediaShuffleEnabled { get; }
        bool MediaRepeatActive { get; }
        string MediaRepeatSuffix { get; }
        bool MediaPlaying { get; }
        bool MediaShiftStop { get; }
        bool MediaCanReset { get; }
        bool MediaPickEnabled { get; }
        bool MediaLibraryEnabled { get; }
        bool MediaCanTogglePlay { get; }
        double MediaPositionSeconds { get; }
        double MediaDurationSeconds { get; }
        bool MediaCanSeek { get; }
        float MediaVolume { get; }
        bool MediaVisualizerEnabled { get; }
        float[] MediaVisualizerLevels { get; }
        string MediaVisualizerTexture { get; }
        float MediaVisualizerAspectRatio { get; }
        bool MediaHasVideo { get; }
        object MediaVideoKey { get; }
        bool MediaHasSubtitles { get; }
        string MediaSubtitle { get; }
        int MediaQueueIndex { get; }
        int MediaQueueCount { get; }
        void FillQueueRows(List<MediaPlayerApp.MediaQueueRow> rows);

        string ConfigureVisualizerText { get; }
        string MediaClearQueueText { get; }
        string MediaSaveQueueText { get; }
        string MediaQueueEmptyText { get; }
        string MediaPickText { get; }
        string MediaPreviousText { get; }
        string MediaNextText { get; }
        string MediaShuffleText { get; }
        string MediaRepeatText { get; }
        string MediaPlaylistText { get; }
        string MediaStopText { get; }

        // The surface's own name for a font face, or null when the surface has no such font.
        string MediaResolveFont(string face);

        // Video draws its own sliding title, so the app chrome's title gives way to it.
        void MediaSetVideoChrome(bool video);

        void MediaPick();
        void MediaShuffle();
        void MediaPrevious();
        void MediaTogglePlay();
        void MediaNext();
        void MediaRepeat();
        void MediaTogglePlaylist();
        void MediaStop();
        void MediaClearQueue();
        void MediaSaveQueue();
        void MediaSeek(double seconds);
        void MediaSetVolume(float value);
        void MediaPlayQueueEntry(int index);
        void MediaMoveQueueEntry(int source, int target);
        void OpenVisualizerSettings();
    }
}
