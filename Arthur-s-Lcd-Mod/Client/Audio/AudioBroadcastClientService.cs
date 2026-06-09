#if EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.IO;
using LcdMod.Common.Audio;
using LcdMod.Common.Helpers;
using LcdMod.Common.Networking;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Utils;

namespace LcdMod.Client.Audio
{
    internal sealed class AudioBroadcastClientService
    {
        const string AudioMetadataFile = "audio.xml";
        const int MaxRecentPlaybackIds = 64;

        readonly List<MyEntity3DSoundEmitter> _activeEmitters = new List<MyEntity3DSoundEmitter>();
        readonly HashSet<long> _recentPlaybackIds = new HashSet<long>();
        readonly List<long> _recentPlaybackIdOrder = new List<long>();

        AudioLibraryMetadata _library;

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
            if (!TryReadRuntimeWave(asset.RuntimePath, out runtimeWaveBytes, out failureReason))
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

        public void Update()
        {
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
            _library = null;
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
                    string.Equals(asset.RuntimePath, query, StringComparison.OrdinalIgnoreCase))
                    return asset;
            }

            return null;
        }

        AudioLibraryMetadata LoadLibrary()
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(AudioMetadataFile, typeof(LcdModClientComponent)))
                {
                    _library = new AudioLibraryMetadata();
                    return _library;
                }

                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(AudioMetadataFile, typeof(LcdModClientComponent)))
                {
                    var xml = reader.ReadToEnd();
                    _library = MyAPIGateway.Utilities.SerializeFromXML<AudioLibraryMetadata>(xml) ?? new AudioLibraryMetadata();
                    return _library;
                }
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not read audio.xml: " + error.Message);
                _library = new AudioLibraryMetadata();
                return _library;
            }
        }

        static bool TryReadRuntimeWave(string runtimePath, out byte[] runtimeWaveBytes, out string failureReason)
        {
            runtimeWaveBytes = null;
            failureReason = string.Empty;

            if (string.IsNullOrWhiteSpace(runtimePath) || !IsSafeFlatWaveFileName(runtimePath))
            {
                failureReason = "Invalid runtime WAV path.";
                return false;
            }

            if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(runtimePath, typeof(LcdModClientComponent)))
            {
                failureReason = "Runtime WAV not found: " + runtimePath;
                return false;
            }

            try
            {
                using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(runtimePath, typeof(LcdModClientComponent)))
                {
                    var stream = reader.BaseStream;
                    if (stream.Length > CanonicalWaveReader.MaxBroadcastWaveBytes)
                    {
                        failureReason = "Runtime WAV exceeds broadcast size limit.";
                        return false;
                    }

                    runtimeWaveBytes = reader.ReadBytes((int)stream.Length);
                }
            }
            catch (Exception error)
            {
                failureReason = "Could not read runtime WAV: " + error.Message;
                return false;
            }

            return runtimeWaveBytes != null && runtimeWaveBytes.Length > 0;
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
            while (_recentPlaybackIdOrder.Count > MaxRecentPlaybackIds)
            {
                var id = _recentPlaybackIdOrder[0];
                _recentPlaybackIdOrder.RemoveAt(0);
                _recentPlaybackIds.Remove(id);
            }
        }

        static bool IsSafeFlatWaveFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
                return false;

            return string.Equals(Path.GetExtension(fileName), ".wav", StringComparison.OrdinalIgnoreCase);
        }

        static void Show(string text, string font = "White")
        {
            MyAPIGateway.Utilities?.ShowNotification(text, 5000, font);
        }
    }
}
#endif
