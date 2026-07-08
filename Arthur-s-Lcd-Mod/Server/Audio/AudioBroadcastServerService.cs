#if EXPERIMENTAL
using System;
using System.Collections.Generic;
using LcdMod.Common.Audio;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace LcdMod.Server.Audio
{
    internal sealed class AudioBroadcastServerService
    {
        const long COOLDOWN_TICKS = TimeSpan.TicksPerSecond * 10;

        readonly Dictionary<ulong, long> _lastBroadcastTicksBySteamId = new Dictionary<ulong, long>();
        long _nextPlaybackId;

        public void Unload()
        {
            _lastBroadcastTicksBySteamId.Clear();
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

        static void Reject(ulong senderSteamId, string reason)
        {
            LogHelper.Log(MyLogSeverity.Warning, "LCD audio broadcast rejected: sender=" + senderSteamId + ", reason=" + reason);
        }
    }
}
#endif
