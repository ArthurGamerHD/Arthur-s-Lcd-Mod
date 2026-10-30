using System;
using System.Collections.Generic;
using System.Text;
using Adk.Video;
using Adk.Video.Matroska;
using LcdMod.Client.Images;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Video
{
    /// <summary>
    /// Plays a chunked Matroska video around a playhead: the chunk under the playhead and the next few are
    /// fetched from the <see cref="VideoChunkSource"/> and decoded on a worker into 24 kHz mono audio, an MPEG-1
    /// picture decoder and subtitle cues; older chunks are dropped. The picture is drawn into a generated
    /// texture. Game-thread only.
    /// </summary>
    internal sealed class MediaVideoPlayback : IDisposable
    {
        const int CHUNKS_BEHIND = 1;
        const int CHUNKS_AHEAD = 2;
        const double SAME_TIME_EPSILON = 0.0001;

        struct Cue
        {
            public double Start;
            public double End;
            public string Text;
        }

        sealed class DecodedChunk
        {
            public double Start;
            public double End;
            public byte[] Pcm;
            public MatroskaMpeg1Video Video;
            public List<Cue> Cues;
        }

        readonly VideoChunkSource _source;
        readonly Dictionary<int, DecodedChunk> _decoded = new Dictionary<int, DecodedChunk>();
        readonly int _width;
        readonly int _height;
        FrameTexture _texture;
        byte[] _rgba;
        byte[] _pixels;
        double _requested = -1;
        int _decodingChunk = -1;
        int _pictureChunk = -1;
        bool _decodingPicture;
        bool _disposed;
        bool _uploaded;
        readonly bool _hasSubtitleTrack;

        public MediaVideoPlayback(VideoChunkSource source)
        {
            _source = source;
            MatroskaFile header = MatroskaFile.Load(source.Index.Header);
            MatroskaTrack track = header.FindTrack(MatroskaTrackType.Video, MediaProbe.Mpeg1CodecId);
            if (track == null)
                throw new NotSupportedException("The file has no MPEG-1 video track.");

            _width = track.PixelWidth;
            _height = track.PixelHeight;
            HasAudio = header.FindTrack(MatroskaTrackType.Audio) != null;
            _hasSubtitleTrack = header.FindTrack(MatroskaTrackType.Subtitle, MediaProbe.TextSubtitleCodecId) != null;
        }

        public VideoChunkSource Source => _source;
        public double Duration => _source.Index.GetChunkEnd(_source.Index.ChunkCount - 1);
        public bool HasAudio { get; }

        /// <summary>Whether captions exist, in the file or from a subtitle file next to it.</summary>
        public bool HasSubtitles => _hasSubtitleTrack || _source.Subtitles != null;
        public int Width => _width;
        public int Height => _height;

        /// <summary>Incremented whenever a new picture reaches the texture.</summary>
        public int Version { get; private set; }

        /// <summary>The texture to draw, or null until the first picture is uploaded.</summary>
        public string TextureName => _uploaded && _texture != null ? _texture.Name : null;

        /// <summary>Moves the chunk window to <paramref name="seconds"/> and starts the work it needs.</summary>
        public void Update(double seconds)
        {
            if (_disposed)
                return;

            MatroskaChunkIndex index = _source.Index;
            int current = index.ChunkAt(seconds);
            int first = Math.Max(0, current - CHUNKS_BEHIND);
            int last = Math.Min(index.ChunkCount - 1, current + CHUNKS_AHEAD);
            _source.Keep(first, last);

            var drop = new List<int>();
            foreach (int chunk in _decoded.Keys)
            {
                if ((chunk < first || chunk > last) && chunk != _pictureChunk)
                    drop.Add(chunk);
            }

            for (int i = 0; i < drop.Count; i++)
                _decoded.Remove(drop[i]);

            for (int chunk = current; chunk <= last; chunk++)
            {
                if (_decoded.ContainsKey(chunk))
                    continue;

                _source.Request(chunk);
                if (_decodingChunk < 0)
                    StartDecode(chunk);
            }

            AdvancePicture(seconds, current);
        }

        /// <summary>True when the chunk playing at <paramref name="seconds"/> is decoded.</summary>
        public bool IsReadyAt(double seconds)
        {
            return _decoded.ContainsKey(_source.Index.ChunkAt(seconds));
        }

        /// <summary>
        /// Copies up to <paramref name="maxSeconds"/> of audio from <paramref name="seconds"/>, stopping at the end
        /// of its chunk; false while that chunk is not decoded yet.
        /// </summary>
        public bool TryTakeAudio(double seconds, double maxSeconds, out byte[] pcm, out double taken)
        {
            pcm = null;
            taken = 0;
            DecodedChunk chunk;
            if (!_decoded.TryGetValue(_source.Index.ChunkAt(seconds), out chunk))
                return false;

            int start = Math.Max(0, (int)Math.Round((seconds - chunk.Start) * MediaPcm.SAMPLE_RATE)) * 2;
            int end = Math.Min(chunk.Pcm.Length, start + (int)Math.Round(maxSeconds * MediaPcm.SAMPLE_RATE) * 2);
            if (end <= start)
            {
                taken = Math.Max(chunk.End - seconds, 0.001);
                pcm = new byte[Math.Max(2, (int)(taken * MediaPcm.SAMPLE_RATE) * 2)];
                return true;
            }

            pcm = new byte[end - start];
            Buffer.BlockCopy(chunk.Pcm, start, pcm, 0, pcm.Length);
            taken = pcm.Length / (2.0 * MediaPcm.SAMPLE_RATE);
            return true;
        }

        /// <summary>Text of the subtitle showing at <paramref name="seconds"/>, or null.</summary>
        public string SubtitleAt(double seconds)
        {
            string text = null;
            foreach (DecodedChunk chunk in _decoded.Values)
            {
                for (int i = 0; i < chunk.Cues.Count; i++)
                {
                    Cue cue = chunk.Cues[i];
                    if (cue.Start <= seconds && seconds < cue.End)
                        text = cue.Text;
                }
            }

            if (text == null && _source.Subtitles != null)
                text = _source.Subtitles.TextAt(seconds);
            return text;
        }

        void StartDecode(int chunk)
        {
            byte[] clusters = _source.TryGet(chunk);
            if (clusters == null)
                return;

            _decodingChunk = chunk;
            MatroskaChunkIndex index = _source.Index;
            double start = index.GetChunk(chunk).Start;
            double end = index.GetChunkEnd(chunk);
            DecodedChunk decoded = null;
            string error = null;
            MyAPIGateway.Parallel.Start(
                delegate
                {
                    try
                    {
                        decoded = Decode(index.BuildChunkFile(clusters), start, end);
                    }
                    catch (Exception e)
                    {
                        error = e.Message;
                    }
                },
                delegate
                {
                    _decodingChunk = -1;
                    if (_disposed)
                        return;

                    if (decoded == null)
                    {
                        // Like other players, play through the damage: silence and the last picture.
                        LogHelper.Log(MyLogSeverity.Warning, "Video chunk " + chunk + " could not be decoded: " + error);
                        decoded = new DecodedChunk
                        {
                            Start = start,
                            End = end,
                            Pcm = MediaPcm.FitToRange(null, start, start, end),
                            Cues = new List<Cue>()
                        };
                    }

                    _decoded[chunk] = decoded;
                });
        }

        static DecodedChunk Decode(byte[] data, double start, double end)
        {
            MatroskaFile file = MatroskaFile.Load(data);
            var chunk = new DecodedChunk { Start = start, End = end, Cues = new List<Cue>() };
            try
            {
                chunk.Video = new MatroskaMpeg1Video(file, file.FindTrack(MatroskaTrackType.Video, MediaProbe.Mpeg1CodecId));
            }
            catch (Exception e)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Video track of a chunk at " + start.ToString("0.0") + "s is damaged: " + e.Message);
            }

            try
            {
                chunk.Cues = ReadCues(file);
            }
            catch (Exception)
            {
            }

            MatroskaTrack audioTrack = file.FindTrack(MatroskaTrackType.Audio);
            byte[] mono = null;
            double audioStart = start;
            if (audioTrack != null && audioTrack.FrameCount > 0)
            {
                try
                {
                    MatroskaPcmAudio audio = MatroskaAudioDecoder.Decode(file, audioTrack);
                    mono = MediaPcm.ToMono24k(audio.Samples, audio.SampleRate, audio.Channels, 0);
                    audioStart = audio.StartTime;
                }
                catch (Exception e)
                {
                    LogHelper.Log(MyLogSeverity.Warning, "Audio of a chunk at " + start.ToString("0.0") + "s is damaged: " + e.Message);
                }
            }

            chunk.Pcm = MediaPcm.FitToRange(mono, audioStart, start, end);
            return chunk;
        }

        static List<Cue> ReadCues(MatroskaFile file)
        {
            var cues = new List<Cue>();
            MatroskaTrack subtitles = file.FindTrack(MatroskaTrackType.Subtitle, MediaProbe.TextSubtitleCodecId);
            if (subtitles == null)
                return cues;

            for (int i = 0; i < subtitles.FrameCount; i++)
            {
                MatroskaFrame frame = subtitles.GetFrame(i);
                if (frame.Duration <= 0)
                    continue;

                cues.Add(new Cue
                {
                    Start = frame.Time,
                    End = frame.Time + frame.Duration,
                    Text = Encoding.UTF8.GetString(file.ReadFrame(frame)).Replace("\r", string.Empty).Trim()
                });
            }

            return cues;
        }

        void AdvancePicture(double seconds, int current)
        {
            DecodedChunk chunk;
            if (_decodingPicture || Math.Abs(seconds - _requested) < SAME_TIME_EPSILON ||
                !_decoded.TryGetValue(current, out chunk) || chunk.Video == null)
            {
                return;
            }

            if (_texture == null)
            {
                if (_width <= 0 || _height <= 0)
                    return;

                _texture = new FrameTexture("MediaPlayerVideo", _width, _height);
                if (!_texture.IsAvailable)
                    return;

                _pixels = new byte[_texture.Size.X * _texture.Size.Y * 4];
            }

            if (!_texture.IsAvailable)
                return;

            _requested = seconds;
            _pictureChunk = current;
            _decodingPicture = true;
            bool changed = false;
            string error = null;
            MyAPIGateway.Parallel.Start(
                delegate { changed = DecodePicture(chunk.Video, seconds, ref error); },
                delegate { CompletePicture(changed, error); });
        }

        bool DecodePicture(MatroskaMpeg1Video video, double seconds, ref string error)
        {
            try
            {
                if (!video.SeekTo(seconds) || video.Picture == null)
                    return false;

                var picture = video.Picture;
                int bytes = picture.Width * picture.Height * 4;
                if (_rgba == null || _rgba.Length != bytes)
                    _rgba = new byte[bytes];

                picture.ToRgba(_rgba);
                FrameTexture.Scale(_rgba, picture.Width, picture.Height, _pixels, _texture.Size.X, _texture.Size.Y, false);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        void CompletePicture(bool changed, string error)
        {
            _decodingPicture = false;
            _pictureChunk = -1;
            if (_disposed)
                return;

            if (error != null)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Video frame at " + _requested.ToString("0.00") + "s failed: " + error);
                return;
            }

            if (changed && _texture.Upload(_pixels))
            {
                _uploaded = true;
                Version++;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _decoded.Clear();
            if (_texture != null)
                _texture.Dispose();
            _texture = null;
        }
    }
}
