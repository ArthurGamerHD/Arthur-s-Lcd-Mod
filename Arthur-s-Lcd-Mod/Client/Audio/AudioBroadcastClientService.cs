using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Common.Audio;
using LcdMod.Client.Config;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using LcdMod.Client.Video;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Utils;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace LcdMod.Client.Audio
{
    internal sealed class AudioBroadcastClientService
    {
        const int MAX_RECENT_PLAYBACK_IDS = 64;
        const int STREAM_SAMPLE_RATE = 24000;
        const int STREAM_BLOCK_ALIGN = 2;
        const int STREAM_CHUNK_PCM_BYTES = STREAM_SAMPLE_RATE * STREAM_BLOCK_ALIGN;
        const int STREAM_INITIAL_CHUNKS = 3;
        const long STREAM_LISTENER_REFRESH_FRAMES = 300L;
        const int VIDEO_PIECE_BYTES = 60 * 1024;
        const long VIDEO_PIECE_INTERVAL_FRAMES = 3L;
        const int VIDEO_MANIFEST_SEGMENT = Constants.VIDEO_MANIFEST_SEGMENT;
        const int VIDEO_SUBTITLE_SEGMENT = Constants.VIDEO_SUBTITLE_SEGMENT;
        const int VIDEO_CHUNKS_AHEAD = 2;

        readonly List<MyEntity3DSoundEmitter> _activeEmitters = new List<MyEntity3DSoundEmitter>();
        readonly HashSet<long> _recentPlaybackIds = new HashSet<long>();
        readonly List<long> _recentPlaybackIdOrder = new List<long>();
        readonly Dictionary<long, IncomingMediaStream> _incomingMediaStreams =
            new Dictionary<long, IncomingMediaStream>();
        readonly List<OutgoingMediaStream> _outgoingMediaStreams = new List<OutgoingMediaStream>();

        AudioLibraryMetadata _library;
        long _nextClientStreamId;

        sealed class IncomingMediaStream
        {
            public long BlockEntityId;
            public int SurfaceIndex;
            public GridMediaPlayer Player;
            public string Title;
            public bool IsVideo;
            public byte[] Manifest;
            public HashSet<int> ManifestOffsets;
            public int ManifestReceived;
            public RemoteVideoChunkSource Remote;
            public byte[] SubtitleData;
            public HashSet<int> SubtitleOffsets;
            public int SubtitleReceived;
            public SubtitleTrack Subtitles;
        }

        sealed class OutgoingMediaStream
        {
            public long ClientStreamId;
            public long BlockEntityId;
            public int SurfaceIndex;
            public byte[] PcmBytes;
            public int Offset;
            public int ChunkIndex;
            public int InitialChunksSent;
            public long NextSendFrame;
            public long NextListenerRefreshFrame;
            public bool CloseSent;
            public ulong[] ListenerSteamIds;
            public MediaStreamContentKind Kind;
            public LocalVideoChunkSource Video;
            public readonly HashSet<int> SentChunks = new HashSet<int>();
            public bool ManifestPending = true;
            public bool SubtitlesPending;
            public int SendingSegment = int.MinValue;
            public byte[] SendingData;
            public int SendingOffset;
            public long SendingPositionTicks;
        }

        internal void StreamAudio(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                Show(LocHelper.GetLoc("LcdMod_AudioBroadcast_Usage"), "Red");
                return;
            }

            query = query.Trim();
            var asset = ResolveAsset(query);
            if (asset == null)
            {
                Show(string.Format(LocHelper.GetLoc("LcdMod_AudioBroadcast_AssetNotFoundFormat"), query), "Red");
                return;
            }

            byte[] runtimeWaveBytes;
            string failureReason;
            if (!TryReadRuntimeWave(asset, out runtimeWaveBytes, out failureReason))
            {
                Show(string.Format(LocHelper.GetLoc("LcdMod_AudioBroadcast_WaveRejectedFormat"), failureReason), "Red");
                return;
            }

            CanonicalWavePayload wave;
            if (!CanonicalWaveReader.TryRead(runtimeWaveBytes, out wave, out failureReason))
            {
                Show(string.Format(LocHelper.GetLoc("LcdMod_AudioBroadcast_WaveRejectedFormat"), failureReason), "Red");
                return;
            }

            var metadata = BuildBroadcastMetadata(asset, runtimeWaveBytes, wave);
            var packet = new PacketRequestBroadcastAudio(metadata, runtimeWaveBytes);

            if (MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
            {
                LcdModSessionComponent.Server.HandleLocalRequestBroadcastAudio(packet);
            }
            else
            {
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, sendToAllPlayers: false, sendToSender: false);
            }

            Show(string.Format(LocHelper.GetLoc("LcdMod_AudioBroadcast_RequestedFormat"), asset.Id));
        }

        public void HandleSync(PacketSyncBroadcastAudio packet)
        {
            if (packet == null || packet.Metadata == null || packet.RuntimeWaveBytes == null)
                return;

            if (!_recentPlaybackIds.Add(packet.PlaybackId))
                return;

            _recentPlaybackIdOrder.Add(packet.PlaybackId);
            TrimRecentPlaybackIds();

            CanonicalWavePayload wave;
            string failureReason;
            if (!CanonicalWaveReader.TryRead(packet.RuntimeWaveBytes, out wave, out failureReason))
            {
                LogHelper.Log(MyLogSeverity.Warning, "Rejected broadcast audio: " + failureReason);
                return;
            }

            string metadataMismatch;
            if (!MetadataMatches(packet.Metadata, packet.RuntimeWaveBytes, wave, out metadataMismatch))
                LogHelper.Log(MyLogSeverity.Warning,
                    "Broadcast audio metadata mismatch; playing server-trusted canonical WAV anyway. " +
                    metadataMismatch);

            PlayPcm(wave.PcmBytes);
            LogHelper.LogInfo("Client playing broadcast audio: playbackId=" + packet.PlaybackId +
                              ", asset=" + packet.Metadata.AssetId +
                              ", runtimeBytes=" + wave.RuntimeByteLength +
                              ", duration=" + TimeSpan.FromTicks(wave.DurationTicks).TotalSeconds.ToString("0.00") + "s");
        }

        public bool StartMediaPlayerLocalAudioStream(IMyTerminalBlock block, int surfaceIndex, AudioAssetMetadata asset, string title)
        {
            if (block == null || asset == null)
                return false;

            byte[] runtimeWaveBytes;
            string failureReason;
            if (!TryReadRuntimeWave(asset, out runtimeWaveBytes, out failureReason))
            {
                Show(string.Format(LocHelper.GetLoc("LcdMod_AudioBroadcast_WaveRejectedFormat"), failureReason), "Red");
                return false;
            }

            CanonicalWavePayload wave;
            if (!CanonicalWaveReader.TryRead(runtimeWaveBytes, out wave, out failureReason))
            {
                Show(string.Format(LocHelper.GetLoc("LcdMod_AudioBroadcast_WaveRejectedFormat"), failureReason), "Red");
                return false;
            }

            CancelMediaPlayerLocalAudioStream(
                block.EntityId,
                surfaceIndex,
                stopPlayback: true);

            var clientStreamId = ++_nextClientStreamId;
            if (clientStreamId == 0)
                clientStreamId = ++_nextClientStreamId;

            var open = new PacketMediaStreamControl
            {
                Intent = MediaStreamControlIntent.Request,
                ClientStreamId = clientStreamId,
                BlockEntityId = block.EntityId,
                SurfaceIndex = surfaceIndex,
                AppTypeId = (int)Generated.AppType.MediaPlayer,
                Title = string.IsNullOrWhiteSpace(title) ? asset.Id : title,
                TotalDurationTicks = wave.DurationTicks
            };

            _outgoingMediaStreams.Add(new OutgoingMediaStream
            {
                ClientStreamId = clientStreamId,
                BlockEntityId = block.EntityId,
                SurfaceIndex = surfaceIndex,
                PcmBytes = wave.PcmBytes,
                Offset = 0,
                ChunkIndex = 0,
                InitialChunksSent = 0,
                NextSendFrame = MyAPIGateway.Session.GameplayFrameCounter + 1L,
                NextListenerRefreshFrame = MyAPIGateway.Session.GameplayFrameCounter + STREAM_LISTENER_REFRESH_FRAMES
            });

            SendOpenStream(open);

            return true;
        }

        /// <summary>
        /// Shares a local video with the players near the screen, the way streaming services do: a small
        /// manifest (header and chunk table) goes first, then the chunks around the sender's playhead as it
        /// plays, in pieces through the server. Late joiners get the manifest and the current chunks, so they
        /// join where the video is, and no limit applies to the length of the file.
        /// </summary>
        public void StartMediaPlayerLocalVideoStream(IMyTerminalBlock block, int surfaceIndex, LocalVideoChunkSource source, string title)
        {
            if (block == null || source == null)
                return;

            CancelMediaPlayerLocalAudioStream(block.EntityId, surfaceIndex, stopPlayback: true);
            var clientStreamId = ++_nextClientStreamId;
            if (clientStreamId == 0)
                clientStreamId = ++_nextClientStreamId;

            var frame = MyAPIGateway.Session.GameplayFrameCounter;
            _outgoingMediaStreams.Add(new OutgoingMediaStream
            {
                ClientStreamId = clientStreamId,
                BlockEntityId = block.EntityId,
                SurfaceIndex = surfaceIndex,
                Kind = MediaStreamContentKind.Video,
                Video = source,
                SubtitlesPending = source.Subtitles != null,
                NextSendFrame = frame + 1L,
                NextListenerRefreshFrame = frame + STREAM_LISTENER_REFRESH_FRAMES
            });

            SendOpenStream(new PacketMediaStreamControl
            {
                Intent = MediaStreamControlIntent.Request,
                ClientStreamId = clientStreamId,
                BlockEntityId = block.EntityId,
                SurfaceIndex = surfaceIndex,
                AppTypeId = (int)Generated.AppType.MediaPlayer,
                Title = string.IsNullOrWhiteSpace(title) ? source.Video.Name : title,
                TotalDurationTicks = TimeSpan.FromSeconds(source.Index.Duration).Ticks,
                ContentKind = MediaStreamContentKind.Video
            });
        }

        public bool CancelMediaPlayerLocalAudioStream(
            long blockEntityId,
            int surfaceIndex,
            bool stopPlayback)
        {
            bool cancelled = false;
            for (var i = _outgoingMediaStreams.Count - 1; i >= 0; i--)
            {
                var stream = _outgoingMediaStreams[i];
                if (stream == null ||
                    stream.BlockEntityId != blockEntityId ||
                    stream.SurfaceIndex != surfaceIndex)
                {
                    continue;
                }

                SendStreamControl(new PacketMediaStreamControl
                {
                    Intent = MediaStreamControlIntent.Close,
                    ClientStreamId = stream.ClientStreamId,
                    StopPlayback = stopPlayback
                });
                _outgoingMediaStreams.RemoveAt(i);
                cancelled = true;
            }

            return cancelled;
        }

        public void HandleStreamControl(PacketMediaStreamControl packet)
        {
            if (packet == null)
                return;

            switch (packet.Intent)
            {
                case MediaStreamControlIntent.Invite:
                    HandleStreamOpen(packet);
                    break;
                case MediaStreamControlIntent.Clients:
                    HandleStreamListeners(packet);
                    break;
                case MediaStreamControlIntent.Close:
                    HandleStreamClose(packet);
                    break;
            }
        }

        void HandleStreamOpen(PacketMediaStreamControl packet)
        {
            if (packet == null || packet.ServerStreamId == 0 || packet.BlockEntityId == 0 || packet.SurfaceIndex < 0)
                return;

            if (IsLocallyOriginatedStream(packet))
            {
                SendStreamControl(new PacketMediaStreamControl
                {
                    Intent = MediaStreamControlIntent.Refused,
                    ServerStreamId = packet.ServerStreamId
                });
                return;
            }

            if (!LocalConfigManager.AcceptMediaStreams)
            {
                SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Refused, ServerStreamId = packet.ServerStreamId });
                return;
            }

            var block = MyEntities.GetEntityById(packet.BlockEntityId) as IMyTerminalBlock;
            if (block == null || block.Closed || block.MarkedForClose || block.CubeGrid == null)
            {
                SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Refused, ServerStreamId = packet.ServerStreamId });
                return;
            }

            var gridLogic = LcdModSessionComponent.GetOrCreateGridLogic(block.CubeGrid);
            if (gridLogic == null)
            {
                SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Refused, ServerStreamId = packet.ServerStreamId });
                return;
            }

            var player = gridLogic.MediaPlayers.Get(packet.BlockEntityId, packet.SurfaceIndex);
            if (player == null)
            {
                SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Refused, ServerStreamId = packet.ServerStreamId });
                return;
            }

            if (_incomingMediaStreams.ContainsKey(packet.ServerStreamId))
            {
                SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Accepted, ServerStreamId = packet.ServerStreamId });
                return;
            }

            CancelMediaPlayerLocalAudioStream(
                packet.BlockEntityId,
                packet.SurfaceIndex,
                stopPlayback: true);
            var incoming = new IncomingMediaStream
            {
                BlockEntityId = packet.BlockEntityId,
                SurfaceIndex = packet.SurfaceIndex,
                Player = player,
                Title = packet.Title,
                IsVideo = packet.ContentKind == MediaStreamContentKind.Video
            };
            if (incoming.IsVideo)
            {
                player.ResetPlaybackEngine();
            }
            else
            {
                player.StartStream(block, packet.Title);
            }

            _incomingMediaStreams[packet.ServerStreamId] = incoming;
            SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Accepted, ServerStreamId = packet.ServerStreamId });
        }

        bool IsLocallyOriginatedStream(PacketMediaStreamControl packet)
        {
            if (packet == null)
                return false;

            ulong localSteamId = MyAPIGateway.Session.Player?.SteamUserId ?? MyAPIGateway.Multiplayer.MyId;
            return localSteamId != 0 && packet.RequestedBySteamId == localSteamId;
        }

        public void HandleStreamChunk(PacketSyncMediaStreamChunk packet)
        {
            if (packet == null || packet.ServerStreamId == 0 || packet.PcmBytes == null || packet.PcmBytes.Length == 0)
                return;

            IncomingMediaStream stream;
            if (!_incomingMediaStreams.TryGetValue(packet.ServerStreamId, out stream))
                return;

            var player = stream.Player ?? ResolveStreamPlayer(stream);
            if (player == null)
                return;

            if (stream.IsVideo)
            {
                ReceiveVideoPiece(packet, stream, player);
                return;
            }

            player.AppendStreamChunk(packet.PcmBytes, packet.DurationTicks / (double)TimeSpan.TicksPerSecond);
            if (packet.IsFinal)
            {
                player.EndStream();
                _incomingMediaStreams.Remove(packet.ServerStreamId);
            }
        }

        void ReceiveVideoPiece(PacketSyncMediaStreamChunk packet, IncomingMediaStream stream, GridMediaPlayer player)
        {
            if (packet.Segment == VIDEO_SUBTITLE_SEGMENT)
            {
                ReceiveVideoSubtitles(packet, stream);
                return;
            }

            if (packet.Segment != VIDEO_MANIFEST_SEGMENT)
            {
                if (stream.Remote != null)
                    stream.Remote.Receive(packet.Segment, packet.SegmentLength, packet.Offset, packet.PcmBytes);
                return;
            }

            if (stream.Remote != null || packet.SegmentLength <= 0 || packet.SegmentLength > Constants.MAX_VIDEO_INDEX_BYTES)
                return;

            if (stream.Manifest == null || stream.Manifest.Length != packet.SegmentLength)
            {
                stream.Manifest = new byte[packet.SegmentLength];
                stream.ManifestOffsets = new HashSet<int>();
                stream.ManifestReceived = 0;
            }

            if (packet.Offset < 0 || packet.Offset > stream.Manifest.Length - packet.PcmBytes.Length ||
                !stream.ManifestOffsets.Add(packet.Offset))
            {
                return;
            }

            Buffer.BlockCopy(packet.PcmBytes, 0, stream.Manifest, packet.Offset, packet.PcmBytes.Length);
            stream.ManifestReceived += packet.PcmBytes.Length;
            if (stream.ManifestReceived < stream.Manifest.Length)
                return;

            var block = MyEntities.GetEntityById(stream.BlockEntityId) as IMyTerminalBlock;
            if (block == null)
                return;

            try
            {
                stream.Remote = new RemoteVideoChunkSource(
                    Adk.Video.Matroska.MatroskaChunkIndex.Deserialize(stream.Manifest, Constants.MAX_VIDEO_CHUNK_BYTES));
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Rejected a streamed video manifest: " + error.Message);
                return;
            }

            stream.Manifest = null;
            stream.ManifestOffsets = null;
            stream.Remote.Subtitles = stream.Subtitles;
            player.PlayVideo(block, stream.Title, stream.Remote, false,
                packet.PositionTicks / (double)TimeSpan.TicksPerSecond);
        }

        void ReceiveVideoSubtitles(PacketSyncMediaStreamChunk packet, IncomingMediaStream stream)
        {
            if (stream.Subtitles != null || packet.SegmentLength <= 0 || packet.SegmentLength > Constants.MAX_VIDEO_INDEX_BYTES)
                return;

            if (stream.SubtitleData == null || stream.SubtitleData.Length != packet.SegmentLength)
            {
                stream.SubtitleData = new byte[packet.SegmentLength];
                stream.SubtitleOffsets = new HashSet<int>();
                stream.SubtitleReceived = 0;
            }

            if (packet.Offset < 0 || packet.Offset > stream.SubtitleData.Length - packet.PcmBytes.Length ||
                !stream.SubtitleOffsets.Add(packet.Offset))
            {
                return;
            }

            Buffer.BlockCopy(packet.PcmBytes, 0, stream.SubtitleData, packet.Offset, packet.PcmBytes.Length);
            stream.SubtitleReceived += packet.PcmBytes.Length;
            if (stream.SubtitleReceived < stream.SubtitleData.Length)
                return;

            stream.Subtitles = SubtitleTrack.FromBytes(stream.SubtitleData);
            stream.SubtitleData = null;
            stream.SubtitleOffsets = null;
            if (stream.Remote != null)
                stream.Remote.Subtitles = stream.Subtitles;
        }

        void HandleStreamClose(PacketMediaStreamControl packet)
        {
            if (packet == null || packet.ServerStreamId == 0)
                return;

            IncomingMediaStream stream;
            if (!_incomingMediaStreams.TryGetValue(packet.ServerStreamId, out stream))
                return;

            if (stream.IsVideo && !packet.StopPlayback)
            {
                _incomingMediaStreams.Remove(packet.ServerStreamId);
                return;
            }

            if (packet.StopPlayback && stream.Player != null)
                stream.Player.ResetPlaybackEngine();
            else if (stream.Player != null)
                stream.Player.EndStream();

            _incomingMediaStreams.Remove(packet.ServerStreamId);
        }

        void HandleStreamListeners(PacketMediaStreamControl packet)
        {
            if (packet == null || packet.ClientStreamId == 0)
                return;

            foreach (var stream in _outgoingMediaStreams.Where(stream => stream != null && stream.ClientStreamId == packet.ClientStreamId))
            {
                var listeners = packet.ListenerSteamIds ?? Array.Empty<ulong>();
                if (stream.Kind == MediaStreamContentKind.Video &&
                    listeners.Any(id => stream.ListenerSteamIds == null || !stream.ListenerSteamIds.Contains(id)))
                {
                    stream.ManifestPending = true;
                    stream.SubtitlesPending = stream.Video != null && stream.Video.Subtitles != null;
                    stream.SentChunks.Clear();
                    stream.SendingData = null;
                }

                stream.ListenerSteamIds = listeners;
                return;
            }
        }

        public void Update()
        {
            UpdateOutgoingMediaStreams();

            for (var i = _activeEmitters.Count - 1; i >= 0; i--)
            {
                var emitter = _activeEmitters[i];
                if (emitter == null || !emitter.IsPlaying)
                    _activeEmitters.RemoveAt(i);
            }
        }

        public void Unload()
        {
            foreach (var emitter in _activeEmitters.Where(emitter => emitter != null)) 
                emitter.StopSound(forced: true);

            _activeEmitters.Clear();
            _recentPlaybackIds.Clear();
            _recentPlaybackIdOrder.Clear();
            _incomingMediaStreams.Clear();
            _outgoingMediaStreams.Clear();
            _library = null;
        }

        void UpdateOutgoingMediaStreams()
        {
            if (_outgoingMediaStreams.Count == 0)
                return;

            var frame = MyAPIGateway.Session.GameplayFrameCounter;
            for (var i = _outgoingMediaStreams.Count - 1; i >= 0; i--)
            {
                var stream = _outgoingMediaStreams[i];
                if (stream != null && stream.Kind == MediaStreamContentKind.Video)
                {
                    if (!UpdateOutgoingVideoStream(stream, frame))
                        _outgoingMediaStreams.RemoveAt(i);
                    continue;
                }

                if (stream == null || stream.PcmBytes == null || stream.Offset >= stream.PcmBytes.Length)
                {
                    if (stream != null && !stream.CloseSent)
                        SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Close, ClientStreamId = stream.ClientStreamId, StopPlayback = false });
                    _outgoingMediaStreams.RemoveAt(i);
                    continue;
                }

                if (frame >= stream.NextListenerRefreshFrame)
                {
                    SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Refresh, ClientStreamId = stream.ClientStreamId });
                    stream.NextListenerRefreshFrame = frame + STREAM_LISTENER_REFRESH_FRAMES;
                }

                if (stream.ListenerSteamIds == null || stream.ListenerSteamIds.Length == 0)
                    continue;

                if (stream.InitialChunksSent >= STREAM_INITIAL_CHUNKS && frame < stream.NextSendFrame)
                    continue;

                SendNextStreamChunk(stream, frame);
            }
        }

        void SendNextStreamChunk(OutgoingMediaStream stream, long frame)
        {
            var remaining = stream.PcmBytes.Length - stream.Offset;
            var size = Math.Min(STREAM_CHUNK_PCM_BYTES, remaining);
            size -= size % STREAM_BLOCK_ALIGN;
            if (size <= 0)
            {
                stream.Offset = stream.PcmBytes.Length;
                return;
            }

            var chunk = new byte[size];
            Buffer.BlockCopy(stream.PcmBytes, stream.Offset, chunk, 0, size);
            stream.Offset += size;

            var isFinal = stream.Offset >= stream.PcmBytes.Length;
            SendStreamChunk(new PacketRequestMediaStreamChunk
            {
                ClientStreamId = stream.ClientStreamId,
                ChunkIndex = stream.ChunkIndex++,
                PcmBytes = chunk,
                DurationTicks = TimeSpan.FromSeconds(size / (double)(STREAM_SAMPLE_RATE * STREAM_BLOCK_ALIGN)).Ticks,
                IsFinal = isFinal
            });

            if (stream.InitialChunksSent < STREAM_INITIAL_CHUNKS)
                stream.InitialChunksSent++;

            stream.NextSendFrame = frame + 60L;
            if (isFinal)
            {
                stream.CloseSent = true;
                SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Close, ClientStreamId = stream.ClientStreamId, StopPlayback = false });
            }
        }

        /// <summary>Sends the next video piece when one is due; false once the video stopped playing here.</summary>
        bool UpdateOutgoingVideoStream(OutgoingMediaStream stream, long frame)
        {
            GridMediaPlayer player = ResolvePlayer(stream.BlockEntityId, stream.SurfaceIndex);
            if (player == null || player.Video == null || !ReferenceEquals(player.Video.Source, stream.Video))
            {
                SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Close, ClientStreamId = stream.ClientStreamId, StopPlayback = false });
                return false;
            }

            if (frame >= stream.NextListenerRefreshFrame)
            {
                SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Refresh, ClientStreamId = stream.ClientStreamId });
                stream.NextListenerRefreshFrame = frame + STREAM_LISTENER_REFRESH_FRAMES;
            }

            if (stream.ListenerSteamIds == null || stream.ListenerSteamIds.Length == 0 || frame < stream.NextSendFrame)
                return true;

            if (stream.SendingData == null && !PickNextVideoSegment(stream, player))
                return true;

            int size = Math.Min(VIDEO_PIECE_BYTES, stream.SendingData.Length - stream.SendingOffset);
            var piece = new byte[size];
            Buffer.BlockCopy(stream.SendingData, stream.SendingOffset, piece, 0, size);
            SendStreamChunk(new PacketRequestMediaStreamChunk
            {
                ClientStreamId = stream.ClientStreamId,
                ChunkIndex = stream.ChunkIndex++,
                PcmBytes = piece,
                Offset = stream.SendingOffset,
                Segment = stream.SendingSegment,
                SegmentLength = stream.SendingData.Length,
                PositionTicks = stream.SendingPositionTicks
            });

            stream.SendingOffset += size;
            stream.NextSendFrame = frame + VIDEO_PIECE_INTERVAL_FRAMES;
            if (stream.SendingOffset >= stream.SendingData.Length)
            {
                if (stream.SendingSegment == VIDEO_MANIFEST_SEGMENT)
                    stream.ManifestPending = false;
                else if (stream.SendingSegment == VIDEO_SUBTITLE_SEGMENT)
                    stream.SubtitlesPending = false;
                else
                    stream.SentChunks.Add(stream.SendingSegment);
                stream.SendingData = null;
            }

            return true;
        }

        /// <summary>The manifest when a listener needs it, otherwise the first unsent chunk around the playhead.</summary>
        static bool PickNextVideoSegment(OutgoingMediaStream stream, GridMediaPlayer player)
        {
            stream.SendingOffset = 0;
            stream.SendingPositionTicks = TimeSpan.FromSeconds(player.CurrentPositionSeconds).Ticks;
            if (stream.ManifestPending)
            {
                stream.SendingSegment = VIDEO_MANIFEST_SEGMENT;
                stream.SendingData = stream.Video.Index.Serialize();
                return true;
            }

            if (stream.SubtitlesPending)
            {
                byte[] subtitles = stream.Video.Subtitles == null ? null : stream.Video.Subtitles.ToBytes();
                if (subtitles != null && subtitles.Length > 0 && subtitles.Length <= Constants.MAX_VIDEO_INDEX_BYTES)
                {
                    stream.SendingSegment = VIDEO_SUBTITLE_SEGMENT;
                    stream.SendingData = subtitles;
                    return true;
                }
                stream.SubtitlesPending = false;
            }

            var index = stream.Video.Index;
            int current = index.ChunkAt(player.CurrentPositionSeconds);
            int last = Math.Min(index.ChunkCount - 1, current + VIDEO_CHUNKS_AHEAD);
            for (int chunk = current; chunk <= last; chunk++)
            {
                if (stream.SentChunks.Contains(chunk))
                    continue;

                byte[] data = stream.Video.TryGet(chunk);
                if (data == null)
                {
                    stream.Video.Request(chunk);
                    return false;
                }

                stream.SendingSegment = chunk;
                stream.SendingData = data;
                return true;
            }

            return false;
        }

        static GridMediaPlayer ResolvePlayer(long blockEntityId, int surfaceIndex)
        {
            var block = MyEntities.GetEntityById(blockEntityId) as IMyTerminalBlock;
            if (block == null || block.CubeGrid == null)
                return null;

            var gridLogic = LcdModSessionComponent.GetOrCreateGridLogic(block.CubeGrid);
            return gridLogic == null ? null : gridLogic.MediaPlayers.Get(blockEntityId, surfaceIndex);
        }

        GridMediaPlayer ResolveStreamPlayer(IncomingMediaStream stream)
        {
            if (stream == null)
                return null;

            var block = MyEntities.GetEntityById(stream.BlockEntityId) as IMyTerminalBlock;
            if (block == null || block.CubeGrid == null)
                return null;

            var gridLogic = LcdModSessionComponent.GetOrCreateGridLogic(block.CubeGrid);
            if (gridLogic == null)
                return null;

            stream.Player = gridLogic.MediaPlayers.Get(stream.BlockEntityId, stream.SurfaceIndex);
            return stream.Player;
        }

        static void SendOpenStream(PacketMediaStreamControl packet)
        {
            if (MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
                LcdModSessionComponent.Server.HandleLocalMediaStreamControl(packet);
            else
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, sendToAllPlayers: false, sendToSender: false);
        }

        static void SendStreamChunk(PacketRequestMediaStreamChunk packet)
        {
            if (MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
                LcdModSessionComponent.Server.HandleLocalRequestMediaStreamChunk(packet);
            else
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, sendToAllPlayers: false, sendToSender: false);
        }

        static void SendStreamControl(PacketMediaStreamControl packet)
        {
            if (MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
                LcdModSessionComponent.Server.HandleLocalMediaStreamControl(packet);
            else
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, sendToAllPlayers: false, sendToSender: false);
        }

        AudioAssetMetadata ResolveAsset(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            var library = LoadLibrary();
            if (library == null || library.Assets == null)
                return null;

            return library.Assets.Where(asset => asset != null)
                .FirstOrDefault(asset => string.Equals(asset.Id, query, StringComparison.OrdinalIgnoreCase) || 
                                         string.Equals(asset.SourcePath, query, StringComparison.OrdinalIgnoreCase) || 
                                         string.Equals(asset.SourceArchivePath, query, StringComparison.OrdinalIgnoreCase) || 
                                         string.Equals(asset.RuntimePath, query, StringComparison.OrdinalIgnoreCase));
        }

        AudioLibraryMetadata LoadLibrary()
        {
            _library = AudioLibraryStorage.LoadMetadata();
            return _library;
        }

        static bool TryReadRuntimeWave(AudioAssetMetadata asset, out byte[] runtimeWaveBytes, out string failureReason)
        {
            if (!AudioLibraryStorage.TryReadRuntimeWave(asset, out runtimeWaveBytes, out failureReason))
                return false;

            if (runtimeWaveBytes.Length > CanonicalWaveReader.MAX_BROADCAST_WAVE_BYTES)
            {
                failureReason = "Runtime WAV exceeds broadcast size limit.";
                runtimeWaveBytes = null;
                return false;
            }

            return true;
        }

        static AudioBroadcastMetadata BuildBroadcastMetadata(AudioAssetMetadata asset, byte[] runtimeWaveBytes, CanonicalWavePayload wave)
        {
            return new AudioBroadcastMetadata
            {
                AssetId = asset.Id,
                OwnerSteamId = asset.OwnerSteamId,
                RuntimePath = asset.RuntimePath,
                RuntimeByteLength = wave.RuntimeByteLength,
                PcmByteLength = wave.PcmByteLength,
                DurationTicks = wave.DurationTicks,
                RuntimeSha256 = AudioImportProcessor.Sha256Hex(runtimeWaveBytes)
            };
        }

        static bool MetadataMatches(AudioBroadcastMetadata metadata, byte[] runtimeWaveBytes, CanonicalWavePayload wave, out string mismatch)
        {
            mismatch = string.Empty;
            var hash = AudioImportProcessor.Sha256Hex(runtimeWaveBytes);

            if (metadata.RuntimeByteLength != wave.RuntimeByteLength)
            {
                mismatch = "runtimeBytes metadata=" + metadata.RuntimeByteLength + ", actual=" + wave.RuntimeByteLength;
                return false;
            }

            if (metadata.PcmByteLength != wave.PcmByteLength)
            {
                mismatch = "pcmBytes metadata=" + metadata.PcmByteLength + ", actual=" + wave.PcmByteLength;
                return false;
            }

            if (Math.Abs(metadata.DurationTicks - wave.DurationTicks) > TimeSpan.TicksPerMillisecond)
            {
                mismatch = "durationTicks metadata=" + metadata.DurationTicks + ", actual=" + wave.DurationTicks;
                return false;
            }

            if (!string.Equals(metadata.RuntimeSha256, hash, StringComparison.OrdinalIgnoreCase))
            {
                mismatch = "runtimeHash metadata=" + metadata.RuntimeSha256 + ", actual=" + hash;
                return false;
            }

            return true;
        }

        void PlayPcm(byte[] pcmBytes)
        {
            var player = MyAPIGateway.Session.LocalHumanPlayer;
            var character = player?.Character as MyEntity;
            var emitter = new MyEntity3DSoundEmitter(character);

            if (character == null && player != null)
                emitter.SetPosition(player.GetPosition());

            emitter.PlaySound(pcmBytes, volume: 1f, maxDistance: 25f);
            _activeEmitters.Add(emitter);
        }

        void TrimRecentPlaybackIds()
        {
            while (_recentPlaybackIdOrder.Count > MAX_RECENT_PLAYBACK_IDS)
            {
                var id = _recentPlaybackIdOrder[0];
                _recentPlaybackIdOrder.RemoveAt(0);
                _recentPlaybackIds.Remove(id);
            }
        }

        static void Show(string text, string font = "White")
        {
            MyAPIGateway.Utilities.ShowNotification(text, 5000, font);
        }
    }
}
