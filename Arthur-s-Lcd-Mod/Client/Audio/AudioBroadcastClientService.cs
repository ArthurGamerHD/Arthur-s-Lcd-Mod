using System;
using System.Collections.Generic;
using LcdMod.Common.Audio;
using LcdMod.Client.Config;
using LcdMod.Client.GridData;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
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
            public long ServerStreamId;
            public long BlockEntityId;
            public int SurfaceIndex;
            public GridMediaPlayer Player;
        }

        sealed class OutgoingMediaStream
        {
            public long ClientStreamId;
            public long ServerStreamId;
            public byte[] PcmBytes;
            public int Offset;
            public int ChunkIndex;
            public int InitialChunksSent;
            public long NextSendFrame;
            public long NextListenerRefreshFrame;
            public bool CloseSent;
            public ulong[] ListenerSteamIds;
        }

        public void StreamAudioCommand(string[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                Show("Usage: /lcdmod streamaudio <audio-id>", "Red");
                return;
            }

            var query = args[0].Trim();
            var asset = ResolveAsset(query);
            if (asset == null)
            {
                Show("Audio asset not found: " + query, "Red");
                return;
            }

            byte[] runtimeWaveBytes;
            string failureReason;
            if (!TryReadRuntimeWave(asset, out runtimeWaveBytes, out failureReason))
            {
                Show(failureReason, "Red");
                return;
            }

            CanonicalWavePayload wave;
            if (!CanonicalWaveReader.TryRead(runtimeWaveBytes, out wave, out failureReason))
            {
                Show("Runtime WAV rejected: " + failureReason, "Red");
                return;
            }

            var metadata = BuildBroadcastMetadata(asset, runtimeWaveBytes, wave);
            var packet = new PacketRequestBroadcastAudio(metadata, runtimeWaveBytes);

            if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
            {
                LcdModSessionComponent.Server.HandleLocalRequestBroadcastAudio(packet);
            }
            else
            {
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, sendToAllPlayers: false, sendToSender: false);
            }

            Show("Audio broadcast requested: " + asset.Id);
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
                Show(failureReason, "Red");
                return false;
            }

            CanonicalWavePayload wave;
            if (!CanonicalWaveReader.TryRead(runtimeWaveBytes, out wave, out failureReason))
            {
                Show("Runtime WAV rejected: " + failureReason, "Red");
                return false;
            }

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
                PcmBytes = wave.PcmBytes,
                Offset = 0,
                ChunkIndex = 0,
                InitialChunksSent = 0,
                NextSendFrame = MyAPIGateway.Session == null ? 0L : MyAPIGateway.Session.GameplayFrameCounter + 1L,
                NextListenerRefreshFrame = MyAPIGateway.Session == null
                    ? STREAM_LISTENER_REFRESH_FRAMES
                    : MyAPIGateway.Session.GameplayFrameCounter + STREAM_LISTENER_REFRESH_FRAMES
            });

            SendOpenStream(open);

            return true;
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

            var player = gridLogic.GetMediaPlayer(packet.BlockEntityId, packet.SurfaceIndex);
            if (player == null)
            {
                SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Refused, ServerStreamId = packet.ServerStreamId });
                return;
            }

            player.StartStream(block, packet.Title);
            gridLogic.MarkRequested();
            _incomingMediaStreams[packet.ServerStreamId] = new IncomingMediaStream
            {
                ServerStreamId = packet.ServerStreamId,
                BlockEntityId = packet.BlockEntityId,
                SurfaceIndex = packet.SurfaceIndex,
                Player = player
            };
            SendStreamControl(new PacketMediaStreamControl { Intent = MediaStreamControlIntent.Accepted, ServerStreamId = packet.ServerStreamId });
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

            player.AppendStreamChunk(packet.PcmBytes, packet.DurationTicks / (double)TimeSpan.TicksPerSecond);
            if (packet.IsFinal)
            {
                player.EndStream();
                _incomingMediaStreams.Remove(packet.ServerStreamId);
            }
        }

        void HandleStreamClose(PacketMediaStreamControl packet)
        {
            if (packet == null || packet.ServerStreamId == 0)
                return;

            IncomingMediaStream stream;
            if (!_incomingMediaStreams.TryGetValue(packet.ServerStreamId, out stream))
                return;

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

            for (var i = 0; i < _outgoingMediaStreams.Count; i++)
            {
                var stream = _outgoingMediaStreams[i];
                if (stream == null || stream.ClientStreamId != packet.ClientStreamId)
                    continue;

                stream.ServerStreamId = packet.ServerStreamId;
                stream.ListenerSteamIds = packet.ListenerSteamIds ?? new ulong[0];
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
            for (var i = 0; i < _activeEmitters.Count; i++)
            {
                var emitter = _activeEmitters[i];
                if (emitter != null)
                    emitter.StopSound(forced: true);
            }

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

            var frame = MyAPIGateway.Session == null ? 0L : MyAPIGateway.Session.GameplayFrameCounter;
            for (var i = _outgoingMediaStreams.Count - 1; i >= 0; i--)
            {
                var stream = _outgoingMediaStreams[i];
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

            stream.Player = gridLogic.GetMediaPlayer(stream.BlockEntityId, stream.SurfaceIndex);
            return stream.Player;
        }

        static void SendOpenStream(PacketMediaStreamControl packet)
        {
            if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
                LcdModSessionComponent.Server.HandleLocalMediaStreamControl(packet);
            else
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, sendToAllPlayers: false, sendToSender: false);
        }

        static void SendStreamChunk(PacketRequestMediaStreamChunk packet)
        {
            if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
                LcdModSessionComponent.Server.HandleLocalRequestMediaStreamChunk(packet);
            else
                LcdModSessionComponent.NetworkManager.TransmitToServer(packet, sendToAllPlayers: false, sendToSender: false);
        }

        static void SendStreamControl(PacketMediaStreamControl packet)
        {
            if (MyAPIGateway.Session != null && MyAPIGateway.Session.IsServer && LcdModSessionComponent.Server != null)
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

            for (var i = 0; i < library.Assets.Count; i++)
            {
                var asset = library.Assets[i];
                if (asset == null)
                    continue;

                if (string.Equals(asset.Id, query, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(asset.SourcePath, query, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(asset.SourceArchivePath, query, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(asset.RuntimePath, query, StringComparison.OrdinalIgnoreCase))
                    return asset;
            }

            return null;
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
            var player = MyAPIGateway.Session?.LocalHumanPlayer;
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
            MyAPIGateway.Utilities?.ShowNotification(text, 5000, font);
        }
    }
}
