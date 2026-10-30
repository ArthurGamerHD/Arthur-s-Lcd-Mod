using System;
using System.Collections.Generic;
using Adk.Video.Matroska;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Video
{
    /// <summary>
    /// Where the chunks of a video come from. Chunks are cached while they are inside the window the
    /// player keeps around its playhead (<see cref="Keep"/>); everything else is dropped. Game-thread only.
    /// </summary>
    internal abstract class VideoChunkSource
    {
        protected readonly Dictionary<int, byte[]> Chunks = new Dictionary<int, byte[]>();
        int _keepFirst;
        int _keepLast = int.MaxValue;

        protected VideoChunkSource(MatroskaChunkIndex index)
        {
            Index = index;
        }

        public MatroskaChunkIndex Index { get; }

        /// <summary>Captions from outside the file, used when its own chunks carry none.</summary>
        public SubtitleTrack Subtitles { get; set; }

        /// <summary>Cluster bytes of chunk <paramref name="chunk"/>, or null while it is not here yet.</summary>
        public byte[] TryGet(int chunk)
        {
            byte[] data;
            return Chunks.TryGetValue(chunk, out data) ? data : null;
        }

        /// <summary>Asks for a chunk the player will need soon.</summary>
        public abstract void Request(int chunk);

        /// <summary>Drops cached chunks outside <paramref name="first"/>..<paramref name="last"/>.</summary>
        public void Keep(int first, int last)
        {
            _keepFirst = first;
            _keepLast = last;
            var drop = new List<int>();
            foreach (int chunk in Chunks.Keys)
            {
                if (!IsKept(chunk))
                    drop.Add(chunk);
            }

            for (int i = 0; i < drop.Count; i++)
                Chunks.Remove(drop[i]);
            OnWindowChanged();
        }

        protected virtual void OnWindowChanged()
        {
        }

        protected bool IsKept(int chunk)
        {
            return chunk >= _keepFirst && chunk <= _keepLast;
        }
    }

    /// <summary>Reads chunks of a local video file on a worker, seeking to each one.</summary>
    internal sealed class LocalVideoChunkSource : VideoChunkSource
    {
        readonly HashSet<int> _reading = new HashSet<int>();

        public LocalVideoChunkSource(LocalVideo video)
            : base(video.Index)
        {
            Video = video;
            Subtitles = video.Subtitles;
        }

        public LocalVideo Video { get; }

        public override void Request(int chunk)
        {
            if (chunk < 0 || chunk >= Index.ChunkCount || Chunks.ContainsKey(chunk) || !_reading.Add(chunk))
                return;

            byte[] data = null;
            string error = null;
            MyAPIGateway.Parallel.StartBackground(
                delegate { LocalVideoLibrary.TryReadChunk(Video, Index.GetChunk(chunk), out data, out error); },
                delegate
                {
                    _reading.Remove(chunk);
                    if (data == null)
                    {
                        LogHelper.Log(MyLogSeverity.Warning, "Could not read chunk " + chunk + " of " + Video.FileName + ": " + error);
                        return;
                    }

                    if (IsKept(chunk))
                        Chunks[chunk] = data;
                });
        }
    }

    /// <summary>Chunks another player streams to this client, assembled from network pieces.</summary>
    internal sealed class RemoteVideoChunkSource : VideoChunkSource
    {
        sealed class Partial
        {
            public byte[] Data;
            public readonly HashSet<int> Offsets = new HashSet<int>();
            public int Received;
        }

        readonly Dictionary<int, Partial> _partials = new Dictionary<int, Partial>();

        public RemoteVideoChunkSource(MatroskaChunkIndex index)
            : base(index)
        {
        }

        public override void Request(int chunk)
        {
        }

        public void Receive(int chunk, int length, int offset, byte[] piece)
        {
            if (chunk < 0 || chunk >= Index.ChunkCount || piece == null || length != Index.GetChunk(chunk).Length ||
                offset < 0 || offset > length - piece.Length || Chunks.ContainsKey(chunk) || !IsKept(chunk))
            {
                return;
            }

            Partial partial;
            if (!_partials.TryGetValue(chunk, out partial))
            {
                partial = new Partial { Data = new byte[length] };
                _partials[chunk] = partial;
            }

            if (!partial.Offsets.Add(offset))
                return;

            Buffer.BlockCopy(piece, 0, partial.Data, offset, piece.Length);
            partial.Received += piece.Length;
            if (partial.Received < length)
                return;

            _partials.Remove(chunk);
            Chunks[chunk] = partial.Data;
        }

        protected override void OnWindowChanged()
        {
            var drop = new List<int>();
            foreach (int chunk in _partials.Keys)
            {
                if (!IsKept(chunk))
                    drop.Add(chunk);
            }

            for (int i = 0; i < drop.Count; i++)
                _partials.Remove(drop[i]);
        }
    }
}
