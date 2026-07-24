using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using LcdMod.Client;
using LcdMod.Common.Helpers;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace LcdMod.Client.Audio
{
    internal sealed class GameAudioTestReportService
    {
        const string REPORT_FILE_PREFIX = "lcdmod_audio_test_report_";
        bool _isRunning;
        IMyHudNotification _progressNotification;

        sealed class AudioReference
        {
            public string Path;
            public string FirstReference;
            public int ReferenceCount;
        }

        sealed class AudioTestResult
        {
            public string Path;
            public string FirstReference;
            public int ReferenceCount;
            public bool Success;
            public string FailureReason;
            public string ResolvedPath;
            public bool UsedWavFallback;
            public GameAudioContainerKind ContainerKind;
            public double DurationSeconds;
            public int PcmByteLength;
            public ushort SourceChannels;
            public uint SourceSampleRate;
            public ushort SourceBitsPerSample;
            public ushort Channels;
            public uint SampleRate;
            public ushort BitsPerSample;
            public bool WasDownmixedToMono;
            public bool WasResampled;
            public string SourceFormatDisplayName;
        }

        sealed class AudioTestWork
        {
            public List<AudioReference> References;
            public List<AudioTestResult> Results;
            public string BuildFailureReason;
            public string ReportText;
            public string ReportFileName;
        }

        public void TestAllGameAudioCommand(string[] args)
        {
            if (_isRunning)
            {
                Show("Game audio test is already running.", "Yellow");
                return;
            }

            if (MyDefinitionManager.Static == null)
            {
                Show("Sound definitions are not ready.", "Red");
                return;
            }

            var work = new AudioTestWork
            {
                References = CollectAudioReferences()
            };

            if (work.References.Count == 0)
            {
                Show("No game audio references were found.", "Yellow");
                return;
            }

            _isRunning = true;
            ShowProgress(0, work.References.Count);

            MyAPIGateway.Parallel.Start(
                delegate { RunAudioTests(work); },
                delegate { CompleteAudioTests(work); });
        }

        static List<AudioReference> CollectAudioReferences()
        {
            var references = new Dictionary<string, AudioReference>(StringComparer.OrdinalIgnoreCase);

            foreach (MyAudioDefinition definition in MyDefinitionManager.Static.GetSoundDefinitions())
            {
                if (definition == null)
                    continue;

                AudioWavesDefinition data = null;

                try
                {
                    data = definition;
                }
                catch (Exception error)
                {
                    LogHelper.Log(
                        MyLogSeverity.Warning,
                        "Could not inspect game audio definition " + definition.Id.SubtypeName + ": " + error.Message);
                    continue;
                }

                if (data == null || data.Waves == null)
                    continue;

                for (int i = 0; i < data.Waves.Count; i++)
                {
                    WaveData wave = data.Waves[i];
                    if (wave == null)
                        continue;

                    AddWaveReference(
                        references,
                        wave.Start,
                        definition.Id.SubtypeName,
                        "Start");
                    AddWaveReference(
                        references,
                        wave.Loop,
                        definition.Id.SubtypeName,
                        "Loop");
                    AddWaveReference(
                        references,
                        wave.End,
                        definition.Id.SubtypeName,
                        "End");
                }
            }

            var list = new List<AudioReference>(references.Values);
            list.Sort(delegate(AudioReference left, AudioReference right)
            {
                return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
            });

            return list;
        }

        static void AddWaveReference(
            Dictionary<string, AudioReference> references,
            string path,
            string soundSubtype,
            string waveSlot)
        {
            path = NormalizeDefinitionPath(path);
            if (string.IsNullOrEmpty(path))
                return;

            AudioReference reference;
            if (!references.TryGetValue(path, out reference))
            {
                reference = new AudioReference
                {
                    Path = path,
                    FirstReference = soundSubtype + "." + waveSlot,
                    ReferenceCount = 0
                };
                references.Add(path, reference);
            }

            reference.ReferenceCount++;
        }

        static string NormalizeDefinitionPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            path = path.Trim();
            while (path.StartsWith("/", StringComparison.Ordinal) ||
                   path.StartsWith("\\", StringComparison.Ordinal))
            {
                path = path.Substring(1);
            }

            if (path.StartsWith("Audio/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Audio\\", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring("Audio/".Length);
            }

            return path.Replace('\\', '/');
        }

        static string ToGameContentPath(string definitionPath)
        {
            return GameAudioPcmLoader.ToAudioGameContentPath(definitionPath);
        }

        void RunAudioTests(AudioTestWork work)
        {
            work.Results = new List<AudioTestResult>();

            try
            {
                for (int i = 0; i < work.References.Count; i++)
                {
                    AudioReference reference = work.References[i];
                    var result = new AudioTestResult
                    {
                        Path = reference.Path,
                        FirstReference = reference.FirstReference,
                        ReferenceCount = reference.ReferenceCount,
                        ContainerKind = GameAudioPcmLoader.GetContainerKind(reference.Path)
                    };

                    PcmWaveData pcm;
                    string failureReason;
                    GameAudioContainerKind containerKind;
                    string resolvedPath;
                    bool usedWavFallback;
                    bool success = GameAudioPcmLoader.TryReadInGameContent(
                        ToGameContentPath(reference.Path),
                        out pcm,
                        out failureReason,
                        out containerKind,
                        out resolvedPath,
                        out usedWavFallback);

                    result.Success = success;
                    result.FailureReason = failureReason;
                    result.ResolvedPath = resolvedPath;
                    result.UsedWavFallback = usedWavFallback;
                    result.ContainerKind = containerKind;

                    if (pcm != null)
                    {
                        result.DurationSeconds = pcm.DurationSeconds;
                        result.PcmByteLength = pcm.Samples == null ? 0 : pcm.Samples.Length;
                        result.SourceChannels = pcm.SourceChannels;
                        result.SourceSampleRate = pcm.SourceSampleRate;
                        result.SourceBitsPerSample = pcm.SourceBitsPerSample;
                        result.Channels = pcm.Channels;
                        result.SampleRate = pcm.SampleRate;
                        result.BitsPerSample = pcm.BitsPerSample;
                        result.WasDownmixedToMono = pcm.WasDownmixedToMono;
                        result.WasResampled = pcm.WasResampled;
                        result.SourceFormatDisplayName = pcm.SourceFormatDisplayName;
                    }

                    work.Results.Add(result);
                    UpdateProgress(i + 1, work.References.Count);
                }

                work.ReportText = BuildReport(work.Results);
                work.ReportFileName = REPORT_FILE_PREFIX +
                                      DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) +
                                      ".csv";
            }
            catch (Exception error)
            {
                work.BuildFailureReason = error.Message;
            }
        }

        void CompleteAudioTests(AudioTestWork work)
        {
            _isRunning = false;
            HideProgress();

            if (work == null)
                return;

            if (!string.IsNullOrEmpty(work.BuildFailureReason))
            {
                Show("Game audio test failed: " + work.BuildFailureReason, "Red");
                return;
            }

            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(
                    work.ReportFileName,
                    typeof(LcdModClientComponent)))
                {
                    writer.Write(work.ReportText ?? string.Empty);
                    writer.Flush();
                }
            }
            catch (Exception error)
            {
                Show("Could not save game audio report: " + error.Message, "Red");
                return;
            }

            int ok = 0;
            int failed = 0;
            if (work.Results != null)
            {
                for (int i = 0; i < work.Results.Count; i++)
                {
                    if (work.Results[i].Success)
                        ok++;
                    else
                        failed++;
                }
            }

            string path = Path.Combine(
                MyAPIGateway.Utilities.GamePaths.UserDataPath,
                "Storage",
                MyAPIGateway.Utilities.GamePaths.ModScopeName,
                work.ReportFileName);

            Show("Audio test report saved: " + ok + " ok, " + failed + " failed. " + path);
        }

        void ShowProgress(int tested, int total)
        {
            if (MyAPIGateway.Utilities == null)
                return;

            _progressNotification = MyAPIGateway.Utilities.CreateNotification(
                BuildProgressText(tested, total),
                int.MaxValue,
                "White");
            _progressNotification.Show();
        }

        void UpdateProgress(int tested, int total)
        {
            if (MyAPIGateway.Utilities == null)
                return;

            MyAPIGateway.Utilities.InvokeOnGameThread(delegate
            {
                if (_progressNotification == null)
                    return;

                _progressNotification.Text = BuildProgressText(tested, total);
                _progressNotification.Hide();
                _progressNotification.Show();
            });
        }

        void HideProgress()
        {
            if (_progressNotification == null)
                return;

            _progressNotification.Hide();
            _progressNotification = null;
        }

        static string BuildProgressText(int tested, int total)
        {
            return "Tested " + tested.ToString(CultureInfo.InvariantCulture) + "/" +
                   total.ToString(CultureInfo.InvariantCulture) + " game audio files";
        }

        static string BuildReport(List<AudioTestResult> results)
        {
            var builder = new StringBuilder();
            builder.AppendLine("file;format;stats;time;size;reference");

            if (results == null)
                return builder.ToString();

            for (int i = 0; i < results.Count; i++)
                builder.AppendLine(FormatReportLine(results[i]));

            return builder.ToString();
        }

        internal static string FormatReportLineForTests(
            string path,
            bool success,
            GameAudioContainerKind containerKind,
            PcmWaveData pcm,
            string failureReason,
            string resolvedPath = null,
            bool usedWavFallback = false)
        {
            return FormatReportLine(new AudioTestResult
            {
                Path = path,
                Success = success,
                ContainerKind = containerKind,
                DurationSeconds = pcm == null ? 0.0 : pcm.DurationSeconds,
                PcmByteLength = pcm == null || pcm.Samples == null ? 0 : pcm.Samples.Length,
                SourceChannels = pcm == null ? (ushort)0 : pcm.SourceChannels,
                SourceSampleRate = pcm == null ? 0u : pcm.SourceSampleRate,
                SourceBitsPerSample = pcm == null ? (ushort)0 : pcm.SourceBitsPerSample,
                Channels = pcm == null ? (ushort)0 : pcm.Channels,
                SampleRate = pcm == null ? 0u : pcm.SampleRate,
                BitsPerSample = pcm == null ? (ushort)0 : pcm.BitsPerSample,
                WasDownmixedToMono = pcm != null && pcm.WasDownmixedToMono,
                WasResampled = pcm != null && pcm.WasResampled,
                SourceFormatDisplayName = pcm == null ? null : pcm.SourceFormatDisplayName,
                FailureReason = failureReason,
                ResolvedPath = resolvedPath,
                UsedWavFallback = usedWavFallback
            });
        }

        static string FormatReportLine(AudioTestResult result)
        {
            if (result == null)
                return "<null>;unknown;failed - missing test result;;;";

            string format = GetResultFormatDisplayName(result);
            if (result.UsedWavFallback)
            {
                format += " (wav fallback: " +
                          ToReportPath(GameAudioPcmLoader.ToDefinitionAudioPath(result.ResolvedPath)) + ")";
            }

            string stats = result.Success
                ? BuildNormalizationSummary(result)
                : "failed - " + (string.IsNullOrEmpty(result.FailureReason)
                    ? "unknown decoder error"
                    : result.FailureReason);
            string time = result.Success
                ? result.DurationSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "s"
                : string.Empty;
            string size = result.Success
                ? result.PcmByteLength.ToString(CultureInfo.InvariantCulture) + " pcm bytes"
                : string.Empty;

            return EscapeReportField(ToReportPath(result.Path)) + ";" +
                   EscapeReportField(format) + ";" +
                   EscapeReportField(stats) + ";" +
                   EscapeReportField(time) + ";" +
                   EscapeReportField(size) + ";" +
                   EscapeReportField(BuildReferenceSummary(result));
        }

        static string GetResultFormatDisplayName(AudioTestResult result)
        {
            if (result != null &&
                result.Success &&
                !string.IsNullOrWhiteSpace(result.SourceFormatDisplayName))
            {
                return result.SourceFormatDisplayName.ToLowerInvariant();
            }

            return GameAudioPcmLoader.GetContainerDisplayName(
                result == null
                    ? GameAudioContainerKind.Unknown
                    : result.ContainerKind).ToLowerInvariant();
        }

        static string EscapeReportField(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.IndexOfAny(new[] { ';', '"', '\r', '\n' }) < 0)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        static string BuildNormalizationSummary(AudioTestResult result)
        {
            if (result == null)
                return "no pcm";

            if (result.WasResampled || result.WasDownmixedToMono ||
                result.SourceBitsPerSample != result.BitsPerSample)
            {
                var builder = new StringBuilder();
                builder.Append(result.SourceSampleRate.ToString(CultureInfo.InvariantCulture));
                builder.Append("hz ");
                builder.Append(result.SourceChannels.ToString(CultureInfo.InvariantCulture));
                builder.Append("ch ");
                builder.Append(result.SourceBitsPerSample.ToString(CultureInfo.InvariantCulture));
                builder.Append("bit");
                return builder.ToString();
            }

            return "native 24khz mono pcm";
        }

        static string BuildReferenceSummary(AudioTestResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.FirstReference))
                return string.Empty;

            string reference = result.FirstReference;

            if (result.ReferenceCount > 1)
            {
                reference += " (+" +
                             (result.ReferenceCount - 1).ToString(CultureInfo.InvariantCulture) +
                             " more)";
            }

            return reference;
        }

        static string ToReportPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "<empty>";

            return path.Replace('/', '\\');
        }

        static void Show(string text, string font = "White")
        {
            MyAPIGateway.Utilities?.ShowNotification(text, 5000, font);
        }
    }
}
