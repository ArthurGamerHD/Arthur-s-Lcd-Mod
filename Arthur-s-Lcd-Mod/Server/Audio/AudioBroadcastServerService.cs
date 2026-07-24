using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Common.Audio;
using LcdMod.Common.Config;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using IMyTextSurfaceProvider = Sandbox.ModAPI.Ingame.IMyTextSurfaceProvider;

namespace LcdMod.Server.Audio
{
    internal sealed class AudioBroadcastServerService
    {
        const long COOLDOWN_TICKS = TimeSpan.TicksPerSecond * 10;
        const double MEDIA_COMMAND_RANGE_METERS = 25.0;
        const double MEDIA_SYNC_RANGE_METERS = 3000.0;
        const int MEDIA_COMMAND_SOURCE_MAX_LENGTH = 512;
        const int MEDIA_COMMAND_DISPLAY_MAX_LENGTH = 128;
        const int MEDIA_STREAM_CHUNK_MAX_BYTES = 64 * 1024;

        readonly Dictionary<ulong, long> _lastBroadcastTicksBySteamId = new Dictionary<ulong, long>();
        readonly Dictionary<MediaStreamKey, MediaStreamSession> _mediaStreams =
            new Dictionary<MediaStreamKey, MediaStreamSession>();
        long _nextPlaybackId;
        long _nextMediaStreamId;

        public void Unload()
        {
            _lastBroadcastTicksBySteamId.Clear();
            _mediaStreams.Clear();
        }

        public void HandleRequest(ulong senderSteamId, PacketRequestBroadcastAudio packet)
        {
            if (senderSteamId == 0 || packet == null || packet.Metadata == null || packet.RuntimeWaveBytes == null)
                return;

            if (!IsAllowedSender(senderSteamId))
            {
                Reject(senderSteamId, "Not authorized.");
                return;
            }

            if (!TryConsumeCooldown(senderSteamId))
            {
                Reject(senderSteamId, "Audio broadcast cooldown.");
                return;
            }

            if (packet.RuntimeWaveBytes.Length <= 0 || packet.RuntimeWaveBytes.Length > CanonicalWaveReader.MAX_BROADCAST_WAVE_BYTES)
            {
                Reject(senderSteamId, "Audio payload size rejected.");
                return;
            }

            CanonicalWavePayload wave;
            string failureReason;
            if (!CanonicalWaveReader.TryRead(packet.RuntimeWaveBytes, out wave, out failureReason))
            {
                Reject(senderSteamId, "Invalid runtime WAV: " + failureReason);
                return;
            }

            if (packet.Metadata.OwnerSteamId != 0 && packet.Metadata.OwnerSteamId != senderSteamId)
            {
                Reject(senderSteamId, "Owner does not match sender.");
                return;
            }

            var trusted = BuildTrustedMetadata(senderSteamId, packet.Metadata, packet.RuntimeWaveBytes, wave);
            var sync = new PacketSyncBroadcastAudio
            {
                PlaybackId = ++_nextPlaybackId,
                RequestedBySteamId = senderSteamId,
                Metadata = trusted,
                RuntimeWaveBytes = packet.RuntimeWaveBytes
            };

            var recipients = Broadcast(sync);
            LogHelper.LogInfo("LCD audio broadcast accepted: sender=" + senderSteamId +
                              ", asset=" + trusted.AssetId +
                              ", runtimeBytes=" + trusted.RuntimeByteLength +
                              ", pcmBytes=" + trusted.PcmByteLength +
                              ", duration=" + TimeSpan.FromTicks(trusted.DurationTicks).TotalSeconds.ToString("0.00") + "s" +
                              ", hash=" + trusted.RuntimeSha256 +
                              ", recipients=" + recipients);
        }

        public void HandleMediaPlayerCommandRequest(ulong senderSteamId, PacketRequestMediaPlayerCommand packet)
        {
            IMyTerminalBlock block;
            if (!TryValidateMediaPlayerCommand(senderSteamId, packet, out block))
            {
                RejectMediaCommand(senderSteamId, packet, "validation failed");
                return;
            }

            var sync = new PacketSyncMediaPlayerCommand
            {
                BlockEntityId = packet.BlockEntityId,
                SurfaceIndex = packet.SurfaceIndex,
                AppTypeId = packet.AppTypeId,
                Command = packet.Command,
                SourceKind = packet.SourceKind,
                SourceId = SanitizeMediaCommandText(packet.SourceId, MEDIA_COMMAND_SOURCE_MAX_LENGTH),
                DisplayName = SanitizeMediaCommandText(packet.DisplayName, MEDIA_COMMAND_DISPLAY_MAX_LENGTH),
                PositionSeconds = SanitizePlaybackPosition(packet.PositionSeconds),
                ClientFrame = packet.ClientFrame,
                RequestedBySteamId = senderSteamId,
                ServerFrame = MyAPIGateway.Session == null ? 0L : MyAPIGateway.Session.GameplayFrameCounter
            };

            var recipients = BroadcastMediaPlayerCommand(sync, block);
            LogHelper.LogInfo("Media player command accepted: sender=" + senderSteamId +
                              ", block=" + block.EntityId +
                              ", surface=" + packet.SurfaceIndex +
                              ", command=" + packet.Command +
                              ", recipients=" + recipients);
        }

        public void HandleMediaStreamControl(ulong senderSteamId, PacketMediaStreamControl packet)
        {
            if (packet == null)
                return;

            switch (packet.Intent)
            {
                case MediaStreamControlIntent.Request:
                    HandleMediaStreamControlRequest(senderSteamId, packet);
                    break;
                case MediaStreamControlIntent.Accepted:
                case MediaStreamControlIntent.Refused:
                    HandleAcceptMediaStreamControl(senderSteamId, packet);
                    break;
                case MediaStreamControlIntent.Refresh:
                    HandleRefreshMediaStreamListenersControl(senderSteamId, packet);
                    break;
                case MediaStreamControlIntent.Close:
                    HandleCloseMediaStreamControl(senderSteamId, packet);
                    break;
            }
        }

        void HandleMediaStreamControlRequest(ulong senderSteamId, PacketMediaStreamControl packet)
        {
            IMyTerminalBlock block;
            if (!TryValidateMediaStreamRequest(senderSteamId, packet, out block))
            {
                RejectMediaStream(senderSteamId, packet == null ? 0L : packet.ClientStreamId, "open validation failed");
                return;
            }

            var key = new MediaStreamKey(senderSteamId, packet.ClientStreamId);
            var session = new MediaStreamSession
            {
                SenderSteamId = senderSteamId,
                ClientStreamId = packet.ClientStreamId,
                ServerStreamId = ++_nextMediaStreamId,
                BlockEntityId = packet.BlockEntityId,
                SurfaceIndex = packet.SurfaceIndex,
                Title = SanitizeMediaCommandText(packet.Title, MEDIA_COMMAND_DISPLAY_MAX_LENGTH),
                ListenerSteamIds = new HashSet<ulong>(),
                InRangeCandidateSteamIds = new HashSet<ulong>()
            };
            _mediaStreams[key] = session;

            var sync = new PacketMediaStreamControl
            {
                Intent = MediaStreamControlIntent.Invite,
                ServerStreamId = session.ServerStreamId,
                ClientStreamId = session.ClientStreamId,
                RequestedBySteamId = senderSteamId,
                BlockEntityId = session.BlockEntityId,
                SurfaceIndex = session.SurfaceIndex,
                AppTypeId = packet.AppTypeId,
                Title = session.Title,
                TotalDurationTicks = packet.TotalDurationTicks < 0L ? 0L : packet.TotalDurationTicks,
                ServerFrame = MyAPIGateway.Session == null ? 0L : MyAPIGateway.Session.GameplayFrameCounter
            };

            var recipients = BroadcastMediaStreamInvite(sync, session, block);
            SendMediaStreamListeners(session);
            LogHelper.LogInfo("Media stream opened: sender=" + senderSteamId +
                              ", stream=" + session.ServerStreamId +
                              ", block=" + block.EntityId +
                              ", surface=" + packet.SurfaceIndex +
                              ", recipients=" + recipients);
        }

        public void HandleMediaStreamChunkRequest(ulong senderSteamId, PacketRequestMediaStreamChunk packet)
        {
            if (senderSteamId == 0 || packet == null || packet.ClientStreamId == 0)
                return;

            MediaStreamSession session;
            if (!_mediaStreams.TryGetValue(new MediaStreamKey(senderSteamId, packet.ClientStreamId), out session))
                return;

            if (packet.PcmBytes == null ||
                packet.PcmBytes.Length == 0 ||
                packet.PcmBytes.Length > MEDIA_STREAM_CHUNK_MAX_BYTES ||
                (packet.PcmBytes.Length & 1) != 0 ||
                packet.ChunkIndex < 0 ||
                packet.DurationTicks <= 0L)
            {
                RejectMediaStream(senderSteamId, packet.ClientStreamId, "chunk validation failed");
                return;
            }

            var block = MyEntities.GetEntityById(session.BlockEntityId) as IMyTerminalBlock;
            if (block == null || block.Closed || block.MarkedForClose)
            {
                _mediaStreams.Remove(new MediaStreamKey(senderSteamId, packet.ClientStreamId));
                return;
            }

            var sync = new PacketSyncMediaStreamChunk
            {
                ServerStreamId = session.ServerStreamId,
                ChunkIndex = packet.ChunkIndex,
                PcmBytes = packet.PcmBytes,
                DurationTicks = packet.DurationTicks,
                IsFinal = packet.IsFinal,
                ServerFrame = MyAPIGateway.Session == null ? 0L : MyAPIGateway.Session.GameplayFrameCounter
            };

            BroadcastMediaStreamChunkToListeners(sync, session, block);

            if (packet.IsFinal)
                _mediaStreams.Remove(new MediaStreamKey(senderSteamId, packet.ClientStreamId));
        }

        public void HandleCloseMediaStreamControl(ulong senderSteamId, PacketMediaStreamControl packet)
        {
            if (senderSteamId == 0 || packet == null || packet.ClientStreamId == 0)
                return;

            var key = new MediaStreamKey(senderSteamId, packet.ClientStreamId);
            MediaStreamSession session;
            if (!_mediaStreams.TryGetValue(key, out session))
                return;

            _mediaStreams.Remove(key);
            var block = MyEntities.GetEntityById(session.BlockEntityId) as IMyTerminalBlock;
            if (block == null)
                return;

            BroadcastMediaStreamCloseToListeners(new PacketMediaStreamControl
            {
                Intent = MediaStreamControlIntent.Close,
                ServerStreamId = session.ServerStreamId,
                StopPlayback = packet.StopPlayback
            }, session, block);
        }

        public void HandleAcceptMediaStreamControl(ulong senderSteamId, PacketMediaStreamControl packet)
        {
            if (senderSteamId == 0 || packet == null || packet.ServerStreamId == 0)
                return;

            MediaStreamSession session;
            if (!TryGetMediaStreamByServerId(packet.ServerStreamId, out session))
                return;

            var block = MyEntities.GetEntityById(session.BlockEntityId) as IMyTerminalBlock;
            if (block == null || block.Closed || block.MarkedForClose)
                return;

            if (packet.Intent == MediaStreamControlIntent.Accepted && IsSteamUserInMediaSyncRange(senderSteamId, block))
                session.ListenerSteamIds.Add(senderSteamId);
            else
                session.ListenerSteamIds.Remove(senderSteamId);

            SendMediaStreamListeners(session);
        }

        public void HandleRefreshMediaStreamListenersControl(ulong senderSteamId, PacketMediaStreamControl packet)
        {
            if (senderSteamId == 0 || packet == null || packet.ClientStreamId == 0)
                return;

            MediaStreamSession session;
            if (!_mediaStreams.TryGetValue(new MediaStreamKey(senderSteamId, packet.ClientStreamId), out session))
                return;

            var block = MyEntities.GetEntityById(session.BlockEntityId) as IMyTerminalBlock;
            if (block == null || block.Closed || block.MarkedForClose)
            {
                _mediaStreams.Remove(new MediaStreamKey(senderSteamId, packet.ClientStreamId));
                return;
            }

            PruneMediaStreamListeners(session, block);
            var invite = new PacketMediaStreamControl
            {
                Intent = MediaStreamControlIntent.Invite,
                ServerStreamId = session.ServerStreamId,
                ClientStreamId = session.ClientStreamId,
                RequestedBySteamId = session.SenderSteamId,
                BlockEntityId = session.BlockEntityId,
                SurfaceIndex = session.SurfaceIndex,
                AppTypeId = (int)AppType.MediaPlayer,
                Title = session.Title,
                TotalDurationTicks = 0L,
                ServerFrame = MyAPIGateway.Session == null ? 0L : MyAPIGateway.Session.GameplayFrameCounter
            };

            BroadcastMediaStreamInvite(invite, session, block);
            SendMediaStreamListeners(session);
        }

        bool TryValidateMediaPlayerCommand(ulong senderSteamId, PacketRequestMediaPlayerCommand packet, out IMyTerminalBlock block)
        {
            block = null;

            if (senderSteamId == 0 || packet == null)
                return false;

            if (packet.BlockEntityId == 0 ||
                packet.SurfaceIndex < 0 ||
                packet.AppTypeId != (int)AppType.MediaPlayer ||
                !IsValidMediaPlayerCommand(packet.Command))
            {
                return false;
            }

            if (!IsValidMediaCommandPayload(packet))
                return false;

            return TryValidateMediaPlayerBlock(senderSteamId, packet.BlockEntityId, packet.SurfaceIndex, out block);
        }

        bool TryValidateMediaStreamRequest(ulong senderSteamId, PacketMediaStreamControl packet, out IMyTerminalBlock block)
        {
            block = null;

            if (senderSteamId == 0 || packet == null)
                return false;

            if (packet.ClientStreamId == 0 ||
                packet.BlockEntityId == 0 ||
                packet.SurfaceIndex < 0 ||
                packet.AppTypeId != (int)AppType.MediaPlayer ||
                !IsBoundedText(packet.Title, MEDIA_COMMAND_DISPLAY_MAX_LENGTH))
            {
                return false;
            }

            return TryValidateMediaPlayerBlock(senderSteamId, packet.BlockEntityId, packet.SurfaceIndex, out block);
        }

        static bool TryValidateMediaPlayerBlock(ulong senderSteamId, long blockEntityId, int surfaceIndex, out IMyTerminalBlock block)
        {
            var entity = MyEntities.GetEntityById(blockEntityId);
            block = entity as IMyTerminalBlock;
            if (block == null || block.Closed || block.MarkedForClose)
                return false;

            var functional = block as IMyFunctionalBlock;
            if (functional != null && !functional.IsFunctional)
                return false;

            var surfaceProvider = block as IMyTextSurfaceProvider;
            if (surfaceProvider == null ||
                surfaceProvider.SurfaceCount <= 0 ||
                surfaceIndex >= surfaceProvider.SurfaceCount)
            {
                return false;
            }

            var identityId = MyAPIGateway.Players.TryGetIdentityId(senderSteamId);
            if (identityId == 0 || !block.HasPlayerAccess(identityId))
                return false;

            if (!IsSenderInMediaCommandRange(senderSteamId, block))
                return false;

            var providerConfig = ScreenProviderConfigStorage.TryLoad(block);
            var surfaceConfig = providerConfig == null ? null : providerConfig.GetSurfaceConfig(surfaceIndex);
            return surfaceConfig != null && surfaceConfig.AppTypeId == (int)AppType.MediaPlayer;
        }

        static bool IsValidMediaPlayerCommand(MediaPlayerCommandKind command)
        {
            return command == MediaPlayerCommandKind.Play ||
                   command == MediaPlayerCommandKind.Pause ||
                   command == MediaPlayerCommandKind.Resume ||
                   command == MediaPlayerCommandKind.Stop ||
                   command == MediaPlayerCommandKind.Seek;
        }

        static bool IsValidMediaCommandPayload(PacketRequestMediaPlayerCommand packet)
        {
            if (packet == null)
                return false;

            if ((packet.Command == MediaPlayerCommandKind.Play || packet.Command == MediaPlayerCommandKind.Seek) &&
                !IsFinite(packet.PositionSeconds))
            {
                return false;
            }

            if (!IsBoundedText(packet.SourceId, MEDIA_COMMAND_SOURCE_MAX_LENGTH) ||
                !IsBoundedText(packet.DisplayName, MEDIA_COMMAND_DISPLAY_MAX_LENGTH))
            {
                return false;
            }

            if (packet.Command != MediaPlayerCommandKind.Play)
                return true;

            if (string.IsNullOrWhiteSpace(packet.SourceId))
                return false;

            return packet.SourceKind == MediaPlayerSourceKind.SoundSubtype ||
                   packet.SourceKind == MediaPlayerSourceKind.ContentPath;
        }

        static bool IsBoundedText(string value, int maxLength)
        {
            return value == null || value.Length <= maxLength;
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static string SanitizeMediaCommandText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            value = value.Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        static double SanitizePlaybackPosition(double value)
        {
            if (!IsFinite(value) || value < 0.0)
                return 0.0;

            return value;
        }

        static bool IsSenderInMediaCommandRange(ulong senderSteamId, IMyTerminalBlock block)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player.IsBot || player.SteamUserId != senderSteamId)
                    continue;

                return IsPlayerInRange(player, block, MEDIA_COMMAND_RANGE_METERS);
            }

            return false;
        }

        bool TryGetMediaStreamByServerId(long serverStreamId, out MediaStreamSession session)
        {
            foreach (var candidate in _mediaStreams.Values)
            {
                if (candidate != null && candidate.ServerStreamId == serverStreamId)
                {
                    session = candidate;
                    return true;
                }
            }

            session = null;
            return false;
        }

        static bool IsSteamUserInMediaSyncRange(ulong steamUserId, IMyTerminalBlock block)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player != null && player.SteamUserId == steamUserId)
                    return IsPlayerInMediaSyncRange(player, block);
            }

            return false;
        }

        static bool IsPlayerInMediaSyncRange(IMyPlayer player, IMyTerminalBlock block)
        {
            return IsPlayerInRange(player, block, MEDIA_SYNC_RANGE_METERS);
        }

        static bool IsPlayerInRange(IMyPlayer player, IMyTerminalBlock block, double maxDistanceMeters)
        {
            if (player == null || player.IsBot || player.SteamUserId == 0 || block == null)
                return false;

            var character = player.Character;
            if (character == null || character.Closed || character.MarkedForClose)
                return false;

            var maxDistanceSquared = maxDistanceMeters * maxDistanceMeters;
            return Vector3D.DistanceSquared(character.GetPosition(), block.GetPosition()) <= maxDistanceSquared;
        }

        static bool IsAllowedSender(ulong senderSteamId)
        {
            return MyAPIGateway.Session != null && MyAPIGateway.Session.IsUserAdmin(senderSteamId);
        }

        bool TryConsumeCooldown(ulong senderSteamId)
        {
            var now = DateTime.UtcNow.Ticks;
            long last;
            if (_lastBroadcastTicksBySteamId.TryGetValue(senderSteamId, out last) && now - last < COOLDOWN_TICKS)
                return false;

            _lastBroadcastTicksBySteamId[senderSteamId] = now;
            return true;
        }

        static AudioBroadcastMetadata BuildTrustedMetadata(ulong senderSteamId, AudioBroadcastMetadata untrusted, byte[] runtimeWaveBytes, CanonicalWavePayload wave)
        {
            return new AudioBroadcastMetadata
            {
                AssetId = SanitizeLabel(untrusted.AssetId),
                OwnerSteamId = untrusted.OwnerSteamId != 0 ? untrusted.OwnerSteamId : senderSteamId,
                RuntimePath = SanitizeRuntimePath(untrusted.RuntimePath),
                RuntimeByteLength = wave.RuntimeByteLength,
                PcmByteLength = wave.PcmByteLength,
                DurationTicks = wave.DurationTicks,
                RuntimeSha256 = AudioImportProcessor.Sha256Hex(runtimeWaveBytes)
            };
        }

        static string SanitizeLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "audio";

            var builder = new System.Text.StringBuilder(Math.Min(value.Length, 64));
            for (var i = 0; i < value.Length && builder.Length < 64; i++)
            {
                var c = char.ToLowerInvariant(value[i]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                    builder.Append(c);
            }

            return builder.Length == 0 ? "audio" : builder.ToString();
        }

        static string SanitizeRuntimePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var fileName = System.IO.Path.GetFileName(value);
            return SanitizeLabel(System.IO.Path.GetFileNameWithoutExtension(fileName)) + ".wav";
        }

        int Broadcast(PacketSyncBroadcastAudio packet)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            var recipients = 0;
            var localSteamId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
            var deliveredToLocalClient = false;

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player.IsBot || player.SteamUserId == 0)
                    continue;

                if (localSteamId != 0 && player.SteamUserId == localSteamId && LcdModSessionComponent.Client != null)
                {
                    LcdModSessionComponent.Client.HandleLocalSyncBroadcastAudio(packet);
                    deliveredToLocalClient = true;
                    recipients++;
                    continue;
                }

                LcdModSessionComponent.NetworkManager.TransmitToPlayer(packet, player.SteamUserId);
                recipients++;
            }

            if (!deliveredToLocalClient && localSteamId != 0 && LcdModSessionComponent.Client != null)
            {
                LcdModSessionComponent.Client.HandleLocalSyncBroadcastAudio(packet);
                recipients++;
            }

            return recipients;
        }

        static int BroadcastMediaPlayerCommand(PacketSyncMediaPlayerCommand packet, IMyTerminalBlock block)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            var recipients = 0;
            var localSteamId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0UL;
            var deliveredToLocalClient = false;

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!IsPlayerInMediaSyncRange(player, block))
                    continue;

                if (localSteamId != 0 && player.SteamUserId == localSteamId && LcdModSessionComponent.Client != null)
                {
                    LcdModSessionComponent.Client.HandleLocalSyncMediaPlayerCommand(packet);
                    deliveredToLocalClient = true;
                    recipients++;
                    continue;
                }

                LcdModSessionComponent.NetworkManager.TransmitToPlayer(packet, player.SteamUserId);
                recipients++;
            }

            if (!deliveredToLocalClient && localSteamId != 0 && LcdModSessionComponent.Client != null)
            {
                var localPlayer = MyAPIGateway.Session == null ? null : MyAPIGateway.Session.LocalHumanPlayer;
                if (IsPlayerInMediaSyncRange(localPlayer, block))
                {
                    LcdModSessionComponent.Client.HandleLocalSyncMediaPlayerCommand(packet);
                    recipients++;
                }
            }

            return recipients;
        }

        static int BroadcastMediaStreamInvite(PacketMediaStreamControl packet, MediaStreamSession session, IMyTerminalBlock block)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            var recipients = 0;
            var currentCandidates = new HashSet<ulong>();
            var localSteamId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0UL;
            var deliveredToLocalClient = false;

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!IsPlayerInMediaSyncRange(player, block))
                    continue;
                if (session.ListenerSteamIds != null && session.ListenerSteamIds.Contains(player.SteamUserId))
                    continue;

                currentCandidates.Add(player.SteamUserId);
                if (session.InRangeCandidateSteamIds != null &&
                    session.InRangeCandidateSteamIds.Contains(player.SteamUserId))
                {
                    continue;
                }

                if (localSteamId != 0 && player.SteamUserId == localSteamId && LcdModSessionComponent.Client != null)
                {
                    DeliverLocalMediaStreamPacket(packet);
                    deliveredToLocalClient = true;
                    recipients++;
                    continue;
                }

                LcdModSessionComponent.NetworkManager.TransmitToPlayer(packet, player.SteamUserId);
                recipients++;
            }

            if (!deliveredToLocalClient && localSteamId != 0 && LcdModSessionComponent.Client != null)
            {
                var localPlayer = MyAPIGateway.Session == null ? null : MyAPIGateway.Session.LocalHumanPlayer;
                if (IsPlayerInMediaSyncRange(localPlayer, block) &&
                    (session.ListenerSteamIds == null || !session.ListenerSteamIds.Contains(localSteamId)) &&
                    (session.InRangeCandidateSteamIds == null || !session.InRangeCandidateSteamIds.Contains(localSteamId)))
                {
                    currentCandidates.Add(localSteamId);
                    DeliverLocalMediaStreamPacket(packet);
                    recipients++;
                }
            }

            session.InRangeCandidateSteamIds = currentCandidates;
            return recipients;
        }

        static void PruneMediaStreamListeners(MediaStreamSession session, IMyTerminalBlock block)
        {
            if (session == null || session.ListenerSteamIds == null || session.ListenerSteamIds.Count == 0)
                return;

            var staleListeners = new List<ulong>();
            foreach (var listener in session.ListenerSteamIds)
                if (!IsSteamUserInMediaSyncRange(listener, block))
                    staleListeners.Add(listener);

            for (var i = 0; i < staleListeners.Count; i++)
                session.ListenerSteamIds.Remove(staleListeners[i]);
        }

        static int BroadcastMediaStreamChunkToListeners(PacketSyncMediaStreamChunk packet, MediaStreamSession session, IMyTerminalBlock block)
        {
            if (session == null || session.ListenerSteamIds == null || session.ListenerSteamIds.Count == 0)
                return 0;

            return BroadcastMediaStreamToListeners(packet, session, block);
        }

        static int BroadcastMediaStreamCloseToListeners(PacketMediaStreamControl packet, MediaStreamSession session, IMyTerminalBlock block)
        {
            if (session == null || session.ListenerSteamIds == null || session.ListenerSteamIds.Count == 0)
                return 0;

            return BroadcastMediaStreamToListeners(packet, session, block);
        }

        static int BroadcastMediaStreamToListeners(NetworkPackage packet, MediaStreamSession session, IMyTerminalBlock block)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            var recipients = 0;
            var staleListeners = new List<ulong>();
            var seenListeners = new HashSet<ulong>();
            var localSteamId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0UL;

            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player.IsBot || player.SteamUserId == 0)
                    continue;

                if (!session.ListenerSteamIds.Contains(player.SteamUserId))
                    continue;

                seenListeners.Add(player.SteamUserId);
                if (!IsPlayerInMediaSyncRange(player, block))
                {
                    staleListeners.Add(player.SteamUserId);
                    continue;
                }

                if (localSteamId != 0 && player.SteamUserId == localSteamId && LcdModSessionComponent.Client != null)
                {
                    DeliverLocalMediaStreamPacket(packet);
                    recipients++;
                    continue;
                }

                LcdModSessionComponent.NetworkManager.TransmitToPlayer(packet, player.SteamUserId);
                recipients++;
            }

            foreach (var listener in session.ListenerSteamIds)
                if (!seenListeners.Contains(listener))
                    staleListeners.Add(listener);

            for (var i = 0; i < staleListeners.Count; i++)
                session.ListenerSteamIds.Remove(staleListeners[i]);

            if (staleListeners.Count > 0)
                SendMediaStreamListeners(session);

            return recipients;
        }

        static void SendMediaStreamListeners(MediaStreamSession session)
        {
            if (session == null)
                return;

            var packet = new PacketMediaStreamControl
            {
                Intent = MediaStreamControlIntent.Clients,
                ServerStreamId = session.ServerStreamId,
                ClientStreamId = session.ClientStreamId,
                ListenerSteamIds = ToArray(session.ListenerSteamIds)
            };

            var localSteamId = MyAPIGateway.Session?.Player?.SteamUserId ?? 0UL;
            if (localSteamId != 0 && session.SenderSteamId == localSteamId && LcdModSessionComponent.Client != null)
                LcdModSessionComponent.Client.HandleLocalMediaStreamControl(packet);
            else
                LcdModSessionComponent.NetworkManager.TransmitToPlayer(packet, session.SenderSteamId);
        }

        static ulong[] ToArray(HashSet<ulong> listeners)
        {
            if (listeners == null || listeners.Count == 0)
                return new ulong[0];

            var result = new ulong[listeners.Count];
            var index = 0;
            foreach (var listener in listeners)
                result[index++] = listener;
            return result;
        }

        static void DeliverLocalMediaStreamPacket(NetworkPackage packet)
        {
            var control = packet as PacketMediaStreamControl;
            if (control != null)
            {
                LcdModSessionComponent.Client.HandleLocalMediaStreamControl(control);
                return;
            }

            var chunk = packet as PacketSyncMediaStreamChunk;
            if (chunk != null)
            {
                LcdModSessionComponent.Client.HandleLocalSyncMediaStreamChunk(chunk);
                return;
            }
        }

        static void Reject(ulong senderSteamId, string reason)
        {
            LogHelper.Log(MyLogSeverity.Warning, "LCD audio broadcast rejected: sender=" + senderSteamId + ", reason=" + reason);
        }

        static void RejectMediaCommand(ulong senderSteamId, PacketRequestMediaPlayerCommand packet, string reason)
        {
            LogHelper.Log(MyLogSeverity.Warning,
                "Media player command rejected: sender=" + senderSteamId +
                ", block=" + (packet == null ? 0L : packet.BlockEntityId) +
                ", surface=" + (packet == null ? -1 : packet.SurfaceIndex) +
                ", reason=" + reason);
        }

        static void RejectMediaStream(ulong senderSteamId, long clientStreamId, string reason)
        {
            LogHelper.Log(MyLogSeverity.Warning,
                "Media stream rejected: sender=" + senderSteamId +
                ", clientStream=" + clientStreamId +
                ", reason=" + reason);
        }

        struct MediaStreamKey : IEquatable<MediaStreamKey>
        {
            readonly ulong _senderSteamId;
            readonly long _clientStreamId;

            public MediaStreamKey(ulong senderSteamId, long clientStreamId)
            {
                _senderSteamId = senderSteamId;
                _clientStreamId = clientStreamId;
            }

            public bool Equals(MediaStreamKey other)
            {
                return _senderSteamId == other._senderSteamId && _clientStreamId == other._clientStreamId;
            }

            public override bool Equals(object obj)
            {
                return obj is MediaStreamKey && Equals((MediaStreamKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_senderSteamId.GetHashCode() * 397) ^ _clientStreamId.GetHashCode();
                }
            }
        }

        sealed class MediaStreamSession
        {
            public ulong SenderSteamId;
            public long ClientStreamId;
            public long ServerStreamId;
            public long BlockEntityId;
            public int SurfaceIndex;
            public string Title;
            public HashSet<ulong> ListenerSteamIds;
            public HashSet<ulong> InRangeCandidateSteamIds;
        }
    }
}
