using System.IO;
using Adk.Video;
using Adk.Video.Matroska;
using Adk.Video.Mpeg1;
using LcdMod.Client.Video;
using Xunit;

namespace Arthur_s_Lcd_Mod.Tests
{
    public sealed class VideoMediaTests
    {
        static byte[] Load(string name)
        {
            return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "Video", name));
        }

        [Fact]
        public void ProbeAcceptsMpeg1WithAdpcmAndSubtitles()
        {
            MediaProbeResult result = MediaProbe.Probe(Load("mpeg1-adpcm-srt.mkv"));

            Assert.True(result.IsSupported, result.Problem);
            Assert.Equal(MediaContainer.Matroska, result.Container);
            Assert.Equal("MPEG-1", result.VideoCodec);
            Assert.Equal("MS-ADPCM", result.AudioCodec);
            Assert.True(result.HasSubtitles);
            Assert.Equal(64, result.Width);
            Assert.Equal(48, result.Height);
            Assert.InRange(result.Duration, 1.9, 2.1);
        }

        [Theory]
        [InlineData("h264-aac.mp4", MediaContainer.Mp4)]
        [InlineData("mpeg1-opus.mkv", MediaContainer.Matroska)]
        [InlineData("vp9.webm", MediaContainer.WebM)]
        public void ProbeExplainsIncompatibleFiles(string name, MediaContainer container)
        {
            MediaProbeResult result = MediaProbe.Probe(Load(name));

            Assert.False(result.IsSupported);
            Assert.Equal(container, result.Container);
            Assert.False(string.IsNullOrEmpty(result.Problem));
        }

        [Fact]
        public void ProbeNamesTheRejectedCodec()
        {
            Assert.Contains("Opus", MediaProbe.Probe(Load("mpeg1-opus.mkv")).Problem);
            Assert.Contains("VP9", MediaProbe.Probe(Load("vp9.webm")).Problem);
        }

        [Fact]
        public void IdentifiesAnimatedPng()
        {
            Assert.True(MediaProbe.IsAnimatedPng(Load("animated.png")));
            Assert.Equal(MediaContainer.Apng, MediaProbe.Identify(Load("animated.png")));
        }

        [Fact]
        public void DecodesAdpcmAudioTrackToItsDuration()
        {
            MatroskaFile file = MatroskaFile.Load(Load("mpeg1-adpcm-srt.mkv"));
            MatroskaPcmAudio audio = MatroskaAudioDecoder.Decode(file, file.FindTrack(MatroskaTrackType.Audio));

            Assert.Equal(24000, audio.SampleRate);
            Assert.Equal(1, audio.Channels);
            Assert.InRange(audio.Duration, 1.95, 2.1);
        }

        [Theory]
        [InlineData("mpeg1-adpcm-srt.mkv")]
        [InlineData("mpeg1-bframes.mkv")]
        public void SeekingBackShowsTheSamePictureAsPlayingThrough(string name)
        {
            MatroskaFile file = MatroskaFile.Load(Load(name));
            MatroskaTrack track = file.FindTrack(MatroskaTrackType.Video);

            var linear = new MatroskaMpeg1Video(file, track);
            byte[] expected = null;
            for (double t = 0; t <= 1.5; t += 0.05)
            {
                linear.SeekTo(t);
                if (t >= 1.25 && expected == null)
                    expected = Rgba(linear.Picture);
            }

            var seeking = new MatroskaMpeg1Video(file, track);
            seeking.SeekTo(1.9);
            seeking.SeekTo(0.1);
            seeking.SeekTo(1.25);

            Assert.NotNull(expected);
            Assert.Equal(expected, Rgba(seeking.Picture));
        }

        [Fact]
        public void PlaybackAdvancesThroughDistinctPictures()
        {
            MatroskaFile file = MatroskaFile.Load(Load("mpeg1-bframes.mkv"));
            var video = new MatroskaMpeg1Video(file, file.FindTrack(MatroskaTrackType.Video));

            Assert.True(video.SeekTo(0));
            byte[] first = Rgba(video.Picture);
            Assert.True(video.SeekTo(1.0));
            Assert.NotEqual(first, Rgba(video.Picture));
            Assert.InRange(video.Picture.Time, 0.85, 1.0);
        }

        [Fact]
        public void ChunkIndexSplitsIntoPlayableChunksStartingOnKeyFrames()
        {
            byte[] data = Load("mpeg1-21s.mkv");
            MatroskaFile whole = MatroskaFile.Load(data);
            MatroskaChunkIndex index;
            using (var stream = new MemoryStream(data))
                index = MatroskaChunkIndex.Build(stream, 1024 * 1024);

            Assert.True(index.ChunkCount >= 3);
            Assert.InRange(index.Duration, 20.9, 21.1);

            int videoFrames = 0;
            int audioBytes = 0;
            for (int i = 0; i < index.ChunkCount; i++)
            {
                MatroskaChunk chunk = index.GetChunk(i);
                var clusters = new byte[chunk.Length];
                Array.Copy(data, chunk.Offset, clusters, 0, chunk.Length);
                MatroskaFile file = MatroskaFile.Load(index.BuildChunkFile(clusters));

                MatroskaTrack video = file.FindTrack(MatroskaTrackType.Video);
                Assert.True(video.GetFrame(0).IsKeyFrame);
                Assert.InRange(video.GetFrame(0).Time, chunk.Start - 1e-6, chunk.Start + 0.5);
                if (i + 1 < index.ChunkCount)
                    Assert.True(index.GetChunkEnd(i) - chunk.Start >= MatroskaChunkIndex.DefaultChunkSeconds - 1e-6);

                videoFrames += video.FrameCount;
                audioBytes += MatroskaAudioDecoder.Decode(file, file.FindTrack(MatroskaTrackType.Audio)).Samples.Length;
            }

            Assert.Equal(whole.FindTrack(MatroskaTrackType.Video).FrameCount, videoFrames);
            Assert.Equal(MatroskaAudioDecoder.Decode(whole, whole.FindTrack(MatroskaTrackType.Audio)).Samples.Length, audioBytes);
            Assert.Equal(index.ChunkCount - 1, index.ChunkAt(1000));
            Assert.Equal(1, index.ChunkAt(index.GetChunk(1).Start));
        }

        [Fact]
        public void ChunkIndexSurvivesSerialization()
        {
            MatroskaChunkIndex index;
            using (var stream = new MemoryStream(Load("mpeg1-21s.mkv")))
                index = MatroskaChunkIndex.Build(stream, 1024 * 1024);

            MatroskaChunkIndex copy = MatroskaChunkIndex.Deserialize(index.Serialize(), 1024 * 1024);

            Assert.Equal(index.Header, copy.Header);
            Assert.Equal(index.Duration, copy.Duration);
            Assert.Equal(index.ChunkCount, copy.ChunkCount);
            for (int i = 0; i < index.ChunkCount; i++)
                Assert.Equal(index.GetChunk(i), copy.GetChunk(i));
        }

        [Fact]
        public void ChunkIndexPlaysTheReadablePartOfACutOffFile()
        {
            byte[] data = Load("mpeg1-21s.mkv");
            MatroskaChunkIndex intact;
            using (var stream = new MemoryStream(data))
                intact = MatroskaChunkIndex.Build(stream, 1024 * 1024);
            Assert.False(intact.IsDamaged);

            MatroskaChunk cutChunk = intact.GetChunk(2);
            var cut = new byte[cutChunk.Offset + cutChunk.Length / 2];
            Array.Copy(data, cut, cut.Length);

            MatroskaChunkIndex index;
            using (var stream = new MemoryStream(cut))
                index = MatroskaChunkIndex.Build(stream, 1024 * 1024);

            Assert.True(index.IsDamaged);
            Assert.True(index.ChunkCount >= 2);
            Assert.InRange(index.Duration, intact.GetChunk(1).Start, cutChunk.Start + MatroskaChunkIndex.DefaultChunkSeconds);
            AssertChunksLoad(index, cut);
        }

        [Fact]
        public void ChunkIndexSkipsADamagedClusterAndKeepsPlaying()
        {
            byte[] data = Load("mpeg1-21s.mkv");
            MatroskaChunkIndex intact;
            using (var stream = new MemoryStream(data))
                intact = MatroskaChunkIndex.Build(stream, 1024 * 1024);

            var damaged = (byte[])data.Clone();
            long hit = intact.GetChunk(1).Offset;
            for (int i = 0; i < 8; i++)
                damaged[hit + i] = 0;

            MatroskaChunkIndex index;
            using (var stream = new MemoryStream(damaged))
                index = MatroskaChunkIndex.Build(stream, 1024 * 1024);

            Assert.True(index.IsDamaged);
            Assert.True(index.GetChunk(index.ChunkCount - 1).Start >= intact.GetChunk(2).Start - 1e-6);
            Assert.InRange(index.Duration, 19.0, 21.1);
            for (int i = 0; i < index.ChunkCount; i++)
                Assert.True(index.GetChunk(i).Offset >= hit + 8 || index.GetChunk(i).Offset + index.GetChunk(i).Length <= hit);
            AssertChunksLoad(index, damaged);
        }

        static void AssertChunksLoad(MatroskaChunkIndex index, byte[] data)
        {
            for (int i = 0; i < index.ChunkCount; i++)
            {
                MatroskaChunk chunk = index.GetChunk(i);
                var clusters = new byte[chunk.Length];
                Array.Copy(data, chunk.Offset, clusters, 0, chunk.Length);
                MatroskaFile file = MatroskaFile.Load(index.BuildChunkFile(clusters));
                Assert.True(file.FindTrack(MatroskaTrackType.Video).FrameCount > 0);
            }
        }

        [Fact]
        public void ChunkIndexRejectsClustersAboveTheChunkLimit()
        {
            using (var stream = new MemoryStream(Load("mpeg1-21s.mkv")))
                Assert.ThrowsAny<Exception>(() => MatroskaChunkIndex.Build(stream, 1024));
        }

        [Fact]
        public void ChunkAudioPiecesTogetherIntoTheWholeTrack()
        {
            byte[] data = Load("mpeg1-21s.mkv");
            MatroskaFile whole = MatroskaFile.Load(data);
            MatroskaPcmAudio wholeAudio = MatroskaAudioDecoder.Decode(whole, whole.FindTrack(MatroskaTrackType.Audio));
            byte[] expected = MediaPcm.FitToRange(
                MediaPcm.ToMono24k(wholeAudio.Samples, wholeAudio.SampleRate, wholeAudio.Channels, 0),
                wholeAudio.StartTime, 0, 21);

            MatroskaChunkIndex index;
            using (var stream = new MemoryStream(data))
                index = MatroskaChunkIndex.Build(stream, 1024 * 1024);

            const int window = 960;
            const int tolerance = 24;
            for (int i = 0; i < index.ChunkCount; i++)
            {
                MatroskaChunk chunk = index.GetChunk(i);
                var clusters = new byte[chunk.Length];
                Array.Copy(data, chunk.Offset, clusters, 0, chunk.Length);
                MatroskaFile file = MatroskaFile.Load(index.BuildChunkFile(clusters));
                MatroskaPcmAudio audio = MatroskaAudioDecoder.Decode(file, file.FindTrack(MatroskaTrackType.Audio));
                byte[] pcm = MediaPcm.FitToRange(MediaPcm.ToMono24k(audio.Samples, audio.SampleRate, audio.Channels, 0),
                    audio.StartTime, chunk.Start, index.GetChunkEnd(i));

                double end = index.GetChunkEnd(i);
                Assert.InRange(pcm.Length / 2, (int)((end - chunk.Start) * 24000) - 1, (int)((end - chunk.Start) * 24000) + 1);

                int local = pcm.Length / 4 - window;
                int global = (int)Math.Round(chunk.Start * 24000) + local;
                bool aligned = false;
                for (int shift = -tolerance; shift <= tolerance && !aligned; shift++)
                {
                    aligned = true;
                    for (int s = 0; s < window * 2 && aligned; s++)
                        aligned = expected[(global + shift) * 2 + s] == pcm[local * 2 + s];
                }

                Assert.True(aligned, "chunk " + i + " audio is more than 1 ms off the whole track");
            }
        }

        static byte[] Rgba(Mpeg1Picture picture)
        {
            Assert.NotNull(picture);
            var rgba = new byte[picture.Width * picture.Height * 4];
            picture.ToRgba(rgba);
            return rgba;
        }
    }
}
