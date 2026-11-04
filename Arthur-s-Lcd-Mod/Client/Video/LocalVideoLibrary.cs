using System;
using System.Collections.Generic;
using System.IO;
using Adk.Video;
using Adk.Video.Matroska;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Library.Utils;
using VRage.Utils;

namespace LcdMod.Client.Video
{
    internal sealed class LocalVideo
    {
        public string FileName;
        public long Length;
        public MediaProbeResult Probe;

        // Chunk layout of a playable file; null for files that cannot play.
        public MatroskaChunkIndex Index;

        // Captions from a subtitle file next to a video that carries none of its own.
        public SubtitleTrack Subtitles;
        public string SubtitleFile;

        public string Name => Path.GetFileNameWithoutExtension(FileName);
        public bool IsPlayable => Probe != null && Probe.IsSupported && Index != null;
        public string Problem => Probe == null ? "The file could not be read." : Probe.Problem;
    }

    /// <summary>
    /// Video files the player dropped into the mod storage folder. Every file with a known video extension is
    /// listed; each is indexed and probed once per session (by name and size) to tell playable MPEG-1 Matroska
    /// files from the rest. Only element headers and the first chunk are read, so file length does not matter:
    /// playback reads one chunk at a time. Files stay where they are.
    /// </summary>
    internal static class LocalVideoLibrary
    {
        const string README_FILE = "readme-for-video-import.txt";
        const int HEAD_BYTES = 64;

        static readonly object Lock = new object();
        static readonly Dictionary<string, LocalVideo> Cache = new Dictionary<string, LocalVideo>(StringComparer.OrdinalIgnoreCase);

        // Lists and probes the local videos. Reads whole files, so call it off the game thread.
        public static List<LocalVideo> Scan()
        {
            var videos = new List<LocalVideo>();
            string root = LocalStorageRoot();
            var files = new List<string>();
            var subtitleFiles = new List<string>();
            try
            {
                for (int i = 0; i < MediaProbe.VideoExtensions.Length; i++)
                    files.AddRange(PathUtils.GetFilesRecursively(root, "*" + MediaProbe.VideoExtensions[i]));
                for (int i = 0; i < SubtitleTrack.Extensions.Length; i++)
                    foreach (string file in PathUtils.GetFilesRecursively(root, "*" + SubtitleTrack.Extensions[i]))
                        if (string.Equals(Normalize(Path.GetDirectoryName(file)), root, StringComparison.OrdinalIgnoreCase))
                            subtitleFiles.Add(Path.GetFileName(file));
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not list local videos: " + e.Message);
                return videos;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (!MediaProbe.IsVideoExtension(fileName) || !seen.Add(fileName) ||
                    !string.Equals(Normalize(Path.GetDirectoryName(files[i])), root, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                LocalVideo video = Probe(fileName);
                if (video == null)
                    continue;
                if (video.IsPlayable && !video.Probe.HasSubtitles)
                    LoadSidecarSubtitles(video, subtitleFiles);
                videos.Add(video);
            }

            videos.Sort((left, right) => string.Compare(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase));
            return videos;
        }

        public static bool TryReadChunk(LocalVideo video, MatroskaChunk chunk, out byte[] data, out string error)
        {
            data = null;
            error = null;
            try
            {
                using (BinaryReader reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(video.FileName, typeof(LcdModSessionComponent)))
                {
                    if (chunk.Offset < 0 || chunk.Offset + chunk.Length > reader.BaseStream.Length)
                    {
                        error = "the file changed since it was indexed";
                        return false;
                    }

                    reader.BaseStream.Position = chunk.Offset;
                    data = reader.ReadBytes(chunk.Length);
                    if (data.Length == chunk.Length)
                        return true;

                    data = null;
                    error = "the file ended early";
                    return false;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        public static void Unload()
        {
            lock (Lock)
                Cache.Clear();
        }

        public static void WriteReadme()
        {
            try
            {
                using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(README_FILE, typeof(LcdModSessionComponent)))
                {
                    writer.Write(
                        "Arthur's Lcd Mod - Videos\n" +
                        "=========================\n\n" +
                        "Copy video files into this folder:\n" +
                        "   " + LocalStorageRoot() + "\n\n" +
                        "They show up in the Media Player's file picker under Videos. Videos play in chunks of about\n" +
                        "five seconds, so any length works; players near the screen are sent the chunks around the\n" +
                        "current position as it plays.\n\n" +
                        "Playable format: Matroska (.mkv/.webm) with MPEG-1 video, MS-ADPCM or 16-bit PCM audio and\n" +
                        "optional SRT subtitles. Each cluster must stay under " + Constants.MAX_VIDEO_CHUNK_BYTES / (1024 * 1024) + " MiB, which ffmpeg does by default.\n" +
                        "Other files are listed with the reason they cannot play.\n\n" +
                        "Convert any video with ffmpeg:\n\n" +
                        "   ffmpeg -i input.mp4 -vf \"scale=1024:-2,fps=10,format=yuv420p\" -c:v mpeg1video -strict unofficial\n" +
                        "          -q:v 4 -bf 0 -g 30 -c:a adpcm_ms -ar 24000 -ac 1 output.mkv\n\n" +
                        "Add subtitles with: -i input.srt -map 0:v -map 0:a -map 1:s -c:s srt\n\n" +
                        "A video without subtitles of its own also picks up an .srt or .vtt file next to it with the same\n" +
                        "name, such as \"Movie.srt\" or \"Movie.en.srt\" for \"Movie.mkv\".\n");
                }
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not write the video readme: " + e.Message);
            }
        }

        static LocalVideo Probe(string fileName)
        {
            long length;
            try
            {
                using (BinaryReader reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(fileName, typeof(LcdModSessionComponent)))
                {
                    length = reader.BaseStream.Length;
                    lock (Lock)
                    {
                        LocalVideo cached;
                        if (Cache.TryGetValue(fileName, out cached) && cached.Length == length)
                            return cached;
                    }

                    var video = new LocalVideo { FileName = fileName, Length = length };
                    byte[] head = reader.ReadBytes((int)Math.Min(length, HEAD_BYTES));
                    MediaContainer container = MediaProbe.Identify(head);
                    if (container == MediaContainer.Matroska || container == MediaContainer.WebM)
                        ProbeMatroska(video, reader, container);
                    else
                        video.Probe = MediaProbe.Probe(head);

                    lock (Lock)
                        Cache[fileName] = video;
                    return video;
                }
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not read local video " + fileName + ": " + e.Message);
                return null;
            }
        }

        // Picks "Name.srt" for "Name.mkv", else a tagged one such as "Name.en.srt", and reads it. Clears the captions
        // when no such file is left.
        static void LoadSidecarSubtitles(LocalVideo video, List<string> subtitleFiles)
        {
            string name = video.Name;
            string best = null;
            for (int i = 0; i < subtitleFiles.Count; i++)
            {
                string candidate = subtitleFiles[i];
                string stem = Path.GetFileNameWithoutExtension(candidate);
                bool exact = string.Equals(stem, name, StringComparison.OrdinalIgnoreCase);
                if (!exact && !stem.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (exact)
                {
                    best = candidate;
                    break;
                }
                if (best == null || string.Compare(candidate, best, StringComparison.OrdinalIgnoreCase) < 0)
                    best = candidate;
            }

            video.SubtitleFile = best;
            video.Subtitles = null;
            if (best == null)
                return;

            try
            {
                using (TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(best, typeof(LcdModSessionComponent)))
                {
                    SubtitleTrack track = SubtitleTrack.Parse(reader.ReadToEnd());
                    if (track.Count > 0)
                        video.Subtitles = track;
                }
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not read subtitles " + best + ": " + e.Message);
            }
        }

        static void ProbeMatroska(LocalVideo video, BinaryReader reader, MediaContainer container)
        {
            MatroskaChunkIndex index;
            try
            {
                index = MatroskaChunkIndex.Build(reader.BaseStream, Constants.MAX_VIDEO_CHUNK_BYTES);
            }
            catch (Exception e)
            {
                video.Probe = MediaProbe.Rejected(container, "The file could not be indexed: " + e.Message);
                return;
            }

            MatroskaChunk first = index.GetChunk(0);
            reader.BaseStream.Position = first.Offset;
            video.Probe = MediaProbe.Probe(index.BuildChunkFile(reader.ReadBytes(first.Length)));
            if (video.Probe.IsSupported)
                video.Index = index;
        }

        static string LocalStorageRoot()
        {
            return Normalize(Path.Combine(
                MyAPIGateway.Utilities.GamePaths.UserDataPath,
                "Storage",
                MyAPIGateway.Utilities.GamePaths.ModScopeName));
        }

        static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/').TrimEnd('/');
        }
    }
}
