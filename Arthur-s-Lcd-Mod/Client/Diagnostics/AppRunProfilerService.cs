#if EXPERIMENTAL
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.SurfaceScripts.Abstract;
using Sandbox.ModAPI;

namespace LcdMod.Client.Diagnostics
{
    /// <summary>
    /// Experimental, opt-in profiler for app runs and named runtime/event-driven work.
    /// A bounded capture writes an aggregate summary and a chronological sample timeline.
    /// </summary>
    internal sealed class AppRunProfilerService
    {
        struct RuntimeProfileKey : IEquatable<RuntimeProfileKey>
        {
            public string Category;
            public string Operation;
            public string Context;
            public long EntityId;
            public int SurfaceIndex;

            public bool Equals(RuntimeProfileKey other)
            {
                return string.Equals(Category, other.Category, StringComparison.Ordinal) &&
                       string.Equals(Operation, other.Operation, StringComparison.Ordinal) &&
                       string.Equals(Context, other.Context, StringComparison.Ordinal) &&
                       EntityId == other.EntityId &&
                       SurfaceIndex == other.SurfaceIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is RuntimeProfileKey && Equals((RuntimeProfileKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = Category != null ? Category.GetHashCode() : 0;
                    hash = hash * 397 ^ (Operation != null ? Operation.GetHashCode() : 0);
                    hash = hash * 397 ^ (Context != null ? Context.GetHashCode() : 0);
                    hash = hash * 397 ^ EntityId.GetHashCode();
                    hash = hash * 397 ^ SurfaceIndex;
                    return hash;
                }
            }
        }

        sealed class RuntimeProfile
        {
            public readonly long InstanceId;
            public readonly string Kind;
            public readonly string Category;
            public readonly string Operation;
            public readonly string Context;
            public readonly long EntityId;
            public readonly int SurfaceIndex;
            public int Runs;
            public int SampledRuns;
            public double TotalMilliseconds;
            public double MaximumMilliseconds;

            public RuntimeProfile(
                long instanceId,
                string kind,
                string category,
                string operation,
                string context,
                long entityId,
                int surfaceIndex)
            {
                InstanceId = instanceId;
                Kind = kind;
                Category = category;
                Operation = operation;
                Context = context;
                EntityId = entityId;
                SurfaceIndex = surfaceIndex;
            }

            public double AverageMilliseconds
            {
                get { return Runs == 0 ? 0d : TotalMilliseconds / Runs; }
            }

            public bool Add(double elapsedMilliseconds)
            {
                if (elapsedMilliseconds < 0d || double.IsNaN(elapsedMilliseconds) ||
                    double.IsInfinity(elapsedMilliseconds))
                {
                    return false;
                }

                Runs++;
                TotalMilliseconds += elapsedMilliseconds;
                if (elapsedMilliseconds > MaximumMilliseconds)
                    MaximumMilliseconds = elapsedMilliseconds;
                return true;
            }
        }

        sealed class TimelineSample
        {
            public long Sequence;
            public double StartedAtMilliseconds;
            public double CompletedAtMilliseconds;
            public long GameFrame;
            public RuntimeProfile Profile;
            public double ElapsedMilliseconds;
        }

        const int DEFAULT_DURATION_SECONDS = 10;
        const int MAXIMUM_DURATION_SECONDS = 600;
        const int MAXIMUM_TIMELINE_SAMPLES = 250000;
        static readonly double StopwatchMillisecondsPerTick = 1000d / Stopwatch.Frequency;
        static readonly char[] CsvEscapedCharacters = {';', '"', '\r', '\n'};
        static AppRunProfilerService _activeRuntimeCapture;

        readonly Dictionary<SurfaceScriptBase, Dictionary<IApp, RuntimeProfile>> _profilesBySurface =
            new Dictionary<SurfaceScriptBase, Dictionary<IApp, RuntimeProfile>>();
        readonly Dictionary<RuntimeProfileKey, RuntimeProfile> _runtimeProfiles =
            new Dictionary<RuntimeProfileKey, RuntimeProfile>();
        readonly List<RuntimeProfile> _profiles = new List<RuntimeProfile>();
        readonly List<TimelineSample> _timeline = new List<TimelineSample>();
        readonly HashSet<SurfaceScriptBase> _hookedSurfaces = new HashSet<SurfaceScriptBase>();

        Stopwatch _captureTimer;
        int _durationSeconds;
        long _nextInstanceId;
        long _nextSampleSequence;
        bool _timelineTruncated;

        bool IsCapturing
        {
            get { return _captureTimer != null; }
        }

        internal static bool IsRuntimeCaptureActive
        {
            get
            {
                var capture = _activeRuntimeCapture;
                return capture != null && capture.IsCapturing;
            }
        }

        internal static void RecordRuntimeMeasurement(
            string category,
            string operation,
            string context,
            long entityId,
            int surfaceIndex,
            long startedAt)
        {
            var capture = _activeRuntimeCapture;
            if (capture == null || !capture.IsCapturing || startedAt == 0L)
                return;

            var elapsedMilliseconds =
                (Stopwatch.GetTimestamp() - startedAt) * StopwatchMillisecondsPerTick;
            capture.RecordRuntime(
                category,
                operation,
                context,
                entityId,
                surfaceIndex,
                elapsedMilliseconds);
        }

        internal void RunCommand(string[] args)
        {
            if (args != null && args.Length == 1 &&
                string.Equals(args[0], "cancel", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsCapturing)
                {
                    ShowMessage("No runtime profile is currently running.");
                    return;
                }

                StopCapture();
                ClearSamples();
                ShowMessage("Runtime profile cancelled.");
                return;
            }

            int durationSeconds;
            if (!TryParseDuration(args, out durationSeconds))
            {
                ShowUsage();
                return;
            }

            StartCapture(durationSeconds);
        }

        public void Update()
        {
            if (!IsCapturing || _captureTimer.ElapsedMilliseconds < _durationSeconds * 1000L)
                return;

            FinishCapture();
        }

        public void Unload()
        {
            StopCapture();
            ClearSamples();
        }

        void StartCapture(int durationSeconds)
        {
            ShowMessage($"Profiling apps and runtime events for {durationSeconds} seconds...");

            StopCapture();
            ClearSamples();

            _durationSeconds = durationSeconds;
            _captureTimer = Stopwatch.StartNew();
            _activeRuntimeCapture = this;

            SurfaceScriptBase.Instances.Added += HandleSurfaceAdded;
            SurfaceScriptBase.Instances.Removed += HandleSurfaceRemoved;

            foreach (var surface in SurfaceScriptBase.Instances)
                HookSurface(surface);
        }

        void FinishCapture()
        {
            StopCapture();

            try
            {
                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss-fff");
                var summaryFileName = timestamp + "-RuntimeProfileSummary.csv";
                var timelineFileName = timestamp + "-RuntimeProfileTimeline.csv";
                WriteLocalFile(summaryFileName, BuildSummaryCsv());
                WriteLocalFile(timelineFileName, BuildTimelineCsv());

                var storagePath = Path.Combine(
                    MyAPIGateway.Utilities.GamePaths.UserDataPath,
                    "Storage",
                    MyAPIGateway.Utilities.GamePaths.ModScopeName);
                var message = "Files saved to " + Path.Combine(storagePath, summaryFileName) +
                              " and " + Path.Combine(storagePath, timelineFileName);
                if (_timelineTruncated)
                    message += " (timeline sample limit reached; summary totals remain complete)";
                ShowMessage(message);
            }
            catch (Exception error)
            {
                ShowMessage("Failed to save runtime profile: " + error.Message);
            }
            finally
            {
                ClearSamples();
            }
        }

        static void WriteLocalFile(string fileName, string contents)
        {
            using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(
                       fileName, typeof(LcdModClientComponent)))
            {
                writer.Write(contents);
                writer.Flush();
            }
        }

        void StopCapture()
        {
            if (!IsCapturing)
                return;

            if (ReferenceEquals(_activeRuntimeCapture, this))
                _activeRuntimeCapture = null;

            _captureTimer.Stop();
            _captureTimer = null;

            SurfaceScriptBase.Instances.Added -= HandleSurfaceAdded;
            SurfaceScriptBase.Instances.Removed -= HandleSurfaceRemoved;

            var surfaces = new List<SurfaceScriptBase>(_hookedSurfaces);
            for (var i = 0; i < surfaces.Count; i++)
                UnhookSurface(surfaces[i]);
        }

        void ClearSamples()
        {
            _profilesBySurface.Clear();
            _runtimeProfiles.Clear();
            _profiles.Clear();
            _timeline.Clear();
            _nextInstanceId = 0L;
            _nextSampleSequence = 0L;
            _timelineTruncated = false;
        }

        void HandleSurfaceAdded(SurfaceScriptBase surface)
        {
            HookSurface(surface);
        }

        void HandleSurfaceRemoved(SurfaceScriptBase surface)
        {
            UnhookSurface(surface);
            _profilesBySurface.Remove(surface);
        }

        void HookSurface(SurfaceScriptBase surface)
        {
            if (!IsCapturing || surface == null || !_hookedSurfaces.Add(surface))
                return;

            surface.OnRunProfiled += HandleRunProfiled;
        }

        void UnhookSurface(SurfaceScriptBase surface)
        {
            if (surface == null || !_hookedSurfaces.Remove(surface))
                return;

            surface.OnRunProfiled -= HandleRunProfiled;
        }

        void HandleRunProfiled(SurfaceScriptBase surface, IApp app, double elapsedMilliseconds)
        {
            if (!CanRecord() || surface == null || app == null)
                return;

            Dictionary<IApp, RuntimeProfile> profilesForSurface;
            if (!_profilesBySurface.TryGetValue(surface, out profilesForSurface))
            {
                profilesForSurface = new Dictionary<IApp, RuntimeProfile>();
                _profilesBySurface.Add(surface, profilesForSurface);
            }

            RuntimeProfile profile;
            if (!profilesForSurface.TryGetValue(app, out profile))
            {
                var terminalBlock = surface.Block as IMyTerminalBlock;
                var blockName = terminalBlock != null && !string.IsNullOrWhiteSpace(terminalBlock.CustomName)
                    ? terminalBlock.CustomName
                    : surface.Block != null
                        ? surface.Block.DefinitionDisplayNameText
                        : "unknown block";
                profile = new RuntimeProfile(
                    ++_nextInstanceId,
                    "app",
                    app.GetType().Name,
                    "SafeRun",
                    blockName,
                    surface.Block != null ? surface.Block.EntityId : 0L,
                    surface.RotationOrSurfaceIndex);
                profilesForSurface.Add(app, profile);
                _profiles.Add(profile);
            }

            AddSample(profile, elapsedMilliseconds);
        }

        void RecordRuntime(
            string category,
            string operation,
            string context,
            long entityId,
            int surfaceIndex,
            double elapsedMilliseconds)
        {
            if (!CanRecord())
                return;

            var key = new RuntimeProfileKey
            {
                Category = category ?? "runtime",
                Operation = operation ?? "unknown",
                Context = context ?? string.Empty,
                EntityId = entityId,
                SurfaceIndex = surfaceIndex
            };
            RuntimeProfile profile;
            if (!_runtimeProfiles.TryGetValue(key, out profile))
            {
                profile = new RuntimeProfile(
                    ++_nextInstanceId,
                    "runtime",
                    key.Category,
                    key.Operation,
                    key.Context,
                    key.EntityId,
                    key.SurfaceIndex);
                _runtimeProfiles.Add(key, profile);
                _profiles.Add(profile);
            }

            AddSample(profile, elapsedMilliseconds);
        }

        bool CanRecord()
        {
            return IsCapturing && _captureTimer.ElapsedMilliseconds <= _durationSeconds * 1000L;
        }

        void AddSample(RuntimeProfile profile, double elapsedMilliseconds)
        {
            if (!profile.Add(elapsedMilliseconds))
                return;

            if (_timeline.Count >= MAXIMUM_TIMELINE_SAMPLES)
            {
                _timelineTruncated = true;
                return;
            }

            profile.SampledRuns++;
            var completedAtMilliseconds = _captureTimer.ElapsedTicks * StopwatchMillisecondsPerTick;
            _timeline.Add(new TimelineSample
            {
                Sequence = ++_nextSampleSequence,
                StartedAtMilliseconds = Math.Max(0d, completedAtMilliseconds - elapsedMilliseconds),
                CompletedAtMilliseconds = completedAtMilliseconds,
                GameFrame = MyAPIGateway.Session != null
                    ? MyAPIGateway.Session.GameplayFrameCounter
                    : 0L,
                Profile = profile,
                ElapsedMilliseconds = elapsedMilliseconds
            });
        }

        string BuildSummaryCsv()
        {
            var profiles = new List<RuntimeProfile>(_profiles);
            profiles.Sort(CompareByTotalDescending);

            var csv = new StringBuilder();
            csv.AppendLine(
                "window_seconds;profile_instance;kind;category;operation;context;entity_id;surface_index;" +
                "runs;sampled_runs;average_ms;maximum_ms;total_ms;timeline_truncated");
            for (var i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                csv.Append(_durationSeconds)
                    .Append(';').Append(profile.InstanceId)
                    .Append(';').Append(EscapeCsv(profile.Kind))
                    .Append(';').Append(EscapeCsv(profile.Category))
                    .Append(';').Append(EscapeCsv(profile.Operation))
                    .Append(';').Append(EscapeCsv(profile.Context))
                    .Append(';').Append(profile.EntityId)
                    .Append(';').Append(profile.SurfaceIndex)
                    .Append(';').Append(profile.Runs)
                    .Append(';').Append(profile.SampledRuns)
                    .Append(';').Append(Format(profile.AverageMilliseconds))
                    .Append(';').Append(Format(profile.MaximumMilliseconds))
                    .Append(';').Append(Format(profile.TotalMilliseconds))
                    .Append(';').Append(_timelineTruncated ? "true" : "false")
                    .AppendLine();
            }

            return csv.ToString().TrimEnd();
        }

        string BuildTimelineCsv()
        {
            var csv = new StringBuilder();
            csv.AppendLine(
                "window_seconds;sequence;started_at_ms;completed_at_ms;game_frame;profile_instance;kind;" +
                "category;operation;context;entity_id;surface_index;elapsed_ms");
            for (var i = 0; i < _timeline.Count; i++)
            {
                var sample = _timeline[i];
                var profile = sample.Profile;
                csv.Append(_durationSeconds)
                    .Append(';').Append(sample.Sequence)
                    .Append(';').Append(Format(sample.StartedAtMilliseconds))
                    .Append(';').Append(Format(sample.CompletedAtMilliseconds))
                    .Append(';').Append(sample.GameFrame)
                    .Append(';').Append(profile.InstanceId)
                    .Append(';').Append(EscapeCsv(profile.Kind))
                    .Append(';').Append(EscapeCsv(profile.Category))
                    .Append(';').Append(EscapeCsv(profile.Operation))
                    .Append(';').Append(EscapeCsv(profile.Context))
                    .Append(';').Append(profile.EntityId)
                    .Append(';').Append(profile.SurfaceIndex)
                    .Append(';').Append(Format(sample.ElapsedMilliseconds))
                    .AppendLine();
            }

            return csv.ToString().TrimEnd();
        }

        static int CompareByTotalDescending(RuntimeProfile left, RuntimeProfile right)
        {
            var result = right.TotalMilliseconds.CompareTo(left.TotalMilliseconds);
            return result != 0 ? result : left.InstanceId.CompareTo(right.InstanceId);
        }

        static string Format(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.IndexOfAny(CsvEscapedCharacters) < 0)
                return value;

            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        static bool TryParseDuration(string[] args, out int durationSeconds)
        {
            durationSeconds = DEFAULT_DURATION_SECONDS;
            if (args == null || args.Length == 0)
                return true;

            if (args.Length != 1 ||
                !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out durationSeconds))
            {
                return false;
            }

            return durationSeconds >= 1 && durationSeconds <= MAXIMUM_DURATION_SECONDS;
        }

        static void ShowUsage()
        {
            ShowMessage(
                "Usage: /lcdMod Profile [seconds] (default 10, maximum 600), or /lcdMod Profile cancel");
        }

        static void ShowMessage(string message)
        {
            MyAPIGateway.Utilities.ShowMessage("lcdMod profiler", message);
        }
    }
}
#endif
