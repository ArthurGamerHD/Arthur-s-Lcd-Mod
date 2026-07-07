#if EXPERIMENTAL
using System;
using System.IO;
using LcdMod.Common.Audio;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Audio
{
    internal sealed class AudioImportService
    {
        const string AUDIO_IMPORT_LIST_FILE = "audio_import.txt";
        const string AUDIO_METADATA_FILE = "audio.xml";
        const int MAX_SOURCE_WAVE_BYTES = 64 * 1024 * 1024;

        public void ImportLocalAudioCommand(string[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                Show("Usage: /lcdmod importlocalaudio filename.wav", "Red");
                return;
            }

            ImportAudio(args[0].Trim(), GetLocalOwnerSteamId(), true);
        }

        public void ImportAudiosCommand(string[] args)
        {
            var utilities = MyAPIGateway.Utilities;
            if (utilities == null)
                return;

            if (!utilities.FileExistsInLocalStorage(AUDIO_IMPORT_LIST_FILE, typeof(LcdModClientComponent)))
            {
                Show("audio_import.txt does not exist in mod storage.", "Red");
                return;
            }

            var count = 0;

            try
            {
                using (var reader = utilities.ReadFileInLocalStorage(AUDIO_IMPORT_LIST_FILE, typeof(LcdModClientComponent)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0)
                            continue;

                        ImportAudio(line, GetLocalOwnerSteamId(), false);
                        count++;
                    }
                }
            }
            catch (Exception error)
            {
                NotifyFailure(AUDIO_IMPORT_LIST_FILE, "Could not read audio_import.txt: " + error.Message);
                return;
            }

            Show("Queued " + count + " audio import(s).");
        }

        void ImportAudio(string sourcePath, ulong ownerSteamId, bool notifyQueued)
        {
            if (!IsSafeFlatWaveFileName(sourcePath))
            {
                NotifyFailure(sourcePath, "Use a flat .wav filename without folders.");
                return;
            }

            byte[] sourceBytes;
            string failureReason;
            if (!TryReadSourceBytes(sourcePath, out sourceBytes, out failureReason))
            {
                NotifyFailure(sourcePath, failureReason);
                return;
            }

            var sourceSha256 = AudioImportProcessor.Sha256Hex(sourceBytes);
            string assetId;
            try
            {
                assetId = GetDefaultAssetId(sourcePath);
            }
            catch (Exception error)
            {
                NotifyFailure(sourcePath, error.Message);
                return;
            }

            if (IsAlreadyImported(assetId, sourceSha256))
            {
                LogHelper.LogInfo("LCD audio import skipped; source already imported: id=" + assetId +
                                  ", source=" + sourcePath +
                                  ", sourceHash=" + sourceSha256);
                Show("Audio already imported: " + assetId);
                return;
            }

            var work = new AudioImportWork
            {
                AssetId = assetId,
                OwnerSteamId = ownerSteamId,
                SourcePath = sourcePath,
                SourceBytes = sourceBytes,
                SourceSha256 = sourceSha256
            };

            MyAPIGateway.Parallel.Start(
                delegate { AudioImportProcessor.ProcessImport(work); },
                delegate { CompleteImport(work); });

            if (notifyQueued)
                Show("Queued audio import: " + sourcePath);
        }

        void CompleteImport(AudioImportWork work)
        {
            if (work == null)
                return;

            if (work.Error != null)
            {
                NotifyFailure(work.SourcePath, work.Error.Message);
                return;
            }

            if (work.RuntimeWaveBytes == null || work.RuntimeWaveBytes.Length == 0)
            {
                NotifyFailure(work.SourcePath, "Import produced no runtime WAV bytes.");
                return;
            }

            if (work.RuntimeWaveBytes.Length > AudioImportProcessor.MaxRuntimeWaveBytes)
            {
                NotifyFailure(work.SourcePath, "Runtime WAV exceeds size limit.");
                return;
            }

            try
            {
                WriteCanonicalRuntimeFile(work.RuntimePath, work.RuntimeWaveBytes);
                UpsertAudioMetadata(new AudioAssetMetadata
                {
                    Id = work.AssetId,
                    OwnerSteamId = work.OwnerSteamId,
                    SourcePath = work.SourcePath,
                    SourceSha256 = work.SourceSha256,
                    RuntimePath = work.RuntimePath,
                    RuntimeSha256 = work.RuntimeSha256,
                    RuntimeByteLength = work.RuntimeWaveBytes.LongLength,
                    PcmByteLength = work.PcmByteLength,
                    DurationTicks = work.DurationTicks,
                    SampleRate = AudioImportProcessor.TargetSampleRate,
                    Channels = AudioImportProcessor.TargetChannels,
                    BitsPerSample = AudioImportProcessor.TargetBitsPerSample
                });
            }
            catch (Exception error)
            {
                NotifyFailure(work.SourcePath, "Could not save imported audio: " + error.Message);
                return;
            }

            LogImportSuccess(work);
            Show("Imported audio " + work.AssetId + " (" + TimeSpan.FromTicks(work.DurationTicks).TotalSeconds.ToString("0.00") + "s)");
        }

        bool TryReadSourceBytes(string sourcePath, out byte[] sourceBytes, out string failureReason)
        {
            sourceBytes = null;
            failureReason = string.Empty;

            var utilities = MyAPIGateway.Utilities;
            if (utilities == null)
            {
                failureReason = "Utilities are not ready.";
                return false;
            }

            if (!utilities.FileExistsInLocalStorage(sourcePath, typeof(LcdModClientComponent)))
            {
                failureReason = "Local WAV file not found: " + sourcePath;
                return false;
            }

            try
            {
                using (var reader = utilities.ReadBinaryFileInLocalStorage(sourcePath, typeof(LcdModClientComponent)))
                {
                    var stream = reader.BaseStream;
                    if (stream.Length > MAX_SOURCE_WAVE_BYTES)
                    {
                        failureReason = "Source WAV exceeds size limit.";
                        return false;
                    }

                    sourceBytes = reader.ReadBytes((int)stream.Length);
                }
            }
            catch (Exception error)
            {
                failureReason = "Could not read source WAV: " + error.Message;
                return false;
            }

            if (sourceBytes == null || sourceBytes.Length == 0)
            {
                failureReason = "Source WAV is empty.";
                return false;
            }

            return true;
        }

        static bool IsAlreadyImported(string assetId, string sourceSha256)
        {
            var metadata = LoadAudioMetadata();
            var existing = FindAsset(metadata, assetId);
            return existing != null &&
                   string.Equals(existing.SourceSha256, sourceSha256, StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(existing.RuntimePath) &&
                   MyAPIGateway.Utilities.FileExistsInLocalStorage(existing.RuntimePath, typeof(LcdModClientComponent));
        }

        static void WriteCanonicalRuntimeFile(string runtimePath, byte[] runtimeWaveBytes)
        {
            if (MyAPIGateway.Utilities.FileExistsInLocalStorage(runtimePath, typeof(LcdModClientComponent)))
                return;

            using (var writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(runtimePath, typeof(LcdModClientComponent)))
                writer.Write(runtimeWaveBytes);
        }

        static void UpsertAudioMetadata(AudioAssetMetadata asset)
        {
            var metadata = LoadAudioMetadata();
            var existing = FindAsset(metadata, asset.Id);

            if (existing != null)
                metadata.Assets.Remove(existing);

            metadata.Assets.Add(asset);
            SaveAudioMetadata(metadata);
        }

        static AudioLibraryMetadata LoadAudioMetadata()
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(AUDIO_METADATA_FILE, typeof(LcdModClientComponent)))
                    return new AudioLibraryMetadata();

                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(AUDIO_METADATA_FILE, typeof(LcdModClientComponent)))
                {
                    var xml = reader.ReadToEnd();
                    return MyAPIGateway.Utilities.SerializeFromXML<AudioLibraryMetadata>(xml) ?? new AudioLibraryMetadata();
                }
            }
            catch (Exception error)
            {
                LogHelper.Log(MyLogSeverity.Warning, "Could not read audio.xml; starting with empty audio metadata: " + error.Message);
                return new AudioLibraryMetadata();
            }
        }

        static void SaveAudioMetadata(AudioLibraryMetadata metadata)
        {
            if (metadata == null)
                metadata = new AudioLibraryMetadata();

            using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(AUDIO_METADATA_FILE, typeof(LcdModClientComponent)))
                writer.Write(MyAPIGateway.Utilities.SerializeToXML(metadata));
        }

        static AudioAssetMetadata FindAsset(AudioLibraryMetadata metadata, string assetId)
        {
            if (metadata == null || metadata.Assets == null || string.IsNullOrWhiteSpace(assetId))
                return null;

            for (var i = 0; i < metadata.Assets.Count; i++)
            {
                var asset = metadata.Assets[i];
                if (asset != null && string.Equals(asset.Id, assetId, StringComparison.OrdinalIgnoreCase))
                    return asset;
            }

            return null;
        }

        static bool IsSafeFlatWaveFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
                return false;

            return string.Equals(Path.GetExtension(fileName), ".wav", StringComparison.OrdinalIgnoreCase);
        }

        static string GetDefaultAssetId(string sourcePath)
        {
            var name = Path.GetFileNameWithoutExtension(sourcePath) ?? string.Empty;
            var builder = new System.Text.StringBuilder(name.Length);

            for (var i = 0; i < name.Length; i++)
            {
                var c = char.ToLowerInvariant(name[i]);
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                    builder.Append(c);
                else if (char.IsWhiteSpace(c))
                    builder.Append('_');
            }

            var id = builder.ToString().Trim('_');
            if (id.Length == 0)
                throw new InvalidOperationException("Could not derive audio asset id from filename.");

            return id;
        }

        static ulong GetLocalOwnerSteamId()
        {
            return MyAPIGateway.Session?.Player?.SteamUserId ?? 0;
        }

        static void NotifyFailure(string sourcePath, string failureReason)
        {
            var message = "Audio import failed" +
                          (string.IsNullOrWhiteSpace(sourcePath) ? string.Empty : " for " + sourcePath) +
                          ": " + failureReason;
            Show(message, "Red");
            LogHelper.Log(MyLogSeverity.Warning, message);
        }

        static void LogImportSuccess(AudioImportWork work)
        {
            LogHelper.LogInfo("LCD audio imported:" +
                              " id=" + work.AssetId +
                              ", owner=" + work.OwnerSteamId +
                              ", source=" + work.SourcePath +
                              ", sourceBytes=" + work.SourceBytes.Length +
                              ", sourceFormat=" + BuildSourceFormat(work) +
                              ", normalized=" + work.WasNormalized +
                              ", runtime=" + work.RuntimePath +
                              ", runtimeBytes=" + work.RuntimeWaveBytes.Length +
                              ", duration=" + TimeSpan.FromTicks(work.DurationTicks).TotalSeconds.ToString("0.00") + "s" +
                              ", sourceHash=" + work.SourceSha256 +
                              ", runtimeHash=" + work.RuntimeSha256);
        }

        static string BuildSourceFormat(AudioImportWork work)
        {
            return work.SourceEncodingName + "_s" + work.SourceBitsPerSample + "le_" +
                   work.SourceSampleRate + "_" +
                   (work.SourceChannels == 1 ? "mono" : "stereo");
        }

        static void Show(string text, string font = "White")
        {
            MyAPIGateway.Utilities?.ShowNotification(text, 5000, font);
        }
    }
}
#endif
