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
    /// Experimental, opt-in profiler for app SafeRun calls.
    /// A chat command opens a bounded capture window and writes one
    /// semicolon-delimited CSV row per app instance/screen to local storage.
    /// </summary>
    internal sealed class AppRunProfilerService
    {
        sealed class AppProfile
        {
            public readonly long InstanceId;
            public readonly string AppName;
            public readonly string BlockName;
            public readonly long BlockEntityId;
            public readonly int SurfaceIndex;
            public readonly List<double> Samples = new List<double>();
            public double TotalMilliseconds;

            public AppProfile(long instanceId, IApp app, SurfaceScriptBase surface)
            {
                InstanceId = instanceId;
                AppName = app.GetType().Name;
                BlockEntityId = surface.Block != null ? surface.Block.EntityId : 0;
                SurfaceIndex = surface.RotationOrSurfaceIndex;

                var terminalBlock = surface.Block as IMyTerminalBlock;
                BlockName = terminalBlock != null && !string.IsNullOrWhiteSpace(terminalBlock.CustomName)
                    ? terminalBlock.CustomName
                    : surface.Block != null
                        ? surface.Block.DefinitionDisplayNameText
                        : "unknown block";
            }

            public double AverageMilliseconds
            {
                get { return Samples.Count == 0 ? 0d : TotalMilliseconds / Samples.Count; }
            }

            public void Add(double elapsedMilliseconds)
            {
                if (elapsedMilliseconds < 0d || double.IsNaN(elapsedMilliseconds) ||
                    double.IsInfinity(elapsedMilliseconds))
                {
                    return;
                }

                Samples.Add(elapsedMilliseconds);
                TotalMilliseconds += elapsedMilliseconds;
            }
        }

        const int DEFAULT_DURATION_SECONDS = 10;
        const int MAXIMUM_DURATION_SECONDS = 600;
        static readonly char[] CsvEscapedCharacters = {';', '"', '\r', '\n'};

        readonly Dictionary<SurfaceScriptBase, Dictionary<IApp, AppProfile>> _profilesBySurface =
            new Dictionary<SurfaceScriptBase, Dictionary<IApp, AppProfile>>();
        readonly List<AppProfile> _profiles = new List<AppProfile>();
        readonly HashSet<SurfaceScriptBase> _hookedSurfaces = new HashSet<SurfaceScriptBase>();

        Stopwatch _captureTimer;
        int _durationSeconds;
        long _nextInstanceId;

        bool IsCapturing
        {
            get { return _captureTimer != null; }
        }

        internal void RunCommand(string[] args)
        {
            if (args != null && args.Length == 1 &&
                string.Equals(args[0], "cancel", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsCapturing)
                {
                    ShowMessage("No app profile is currently running.");
                    return;
                }

                StopCapture();
                ClearSamples();
                ShowMessage("App profile cancelled.");
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
            ShowMessage($"Profiling for {durationSeconds} seconds...");
            
            StopCapture();
            ClearSamples();

            _durationSeconds = durationSeconds;
            _captureTimer = Stopwatch.StartNew();

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
                string fileName = DateTime.UtcNow.ToString("yyyy-MM-dd-HH-mm-ss-fff") +
                                  "-AppRunProfile.csv";
                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(
                           fileName, typeof(LcdModClientComponent)))
                {
                    writer.Write(BuildCsv());
                    writer.Flush();
                }

                string path = Path.Combine(
                    MyAPIGateway.Utilities.GamePaths.UserDataPath,
                    "Storage",
                    MyAPIGateway.Utilities.GamePaths.ModScopeName,
                    fileName);
                ShowMessage("File saved to " + path);
            }
            catch (Exception error)
            {
                ShowMessage("Failed to save app profile: " + error.Message);
            }
            finally
            {
                ClearSamples();
            }
        }

        void StopCapture()
        {
            if (!IsCapturing)
                return;

            _captureTimer.Stop();
            _captureTimer = null;

            SurfaceScriptBase.Instances.Added -= HandleSurfaceAdded;
            SurfaceScriptBase.Instances.Removed -= HandleSurfaceRemoved;

            var surfaces = new List<SurfaceScriptBase>(_hookedSurfaces);
            for (int i = 0; i < surfaces.Count; i++)
                UnhookSurface(surfaces[i]);
        }

        void ClearSamples()
        {
            _profilesBySurface.Clear();
            _profiles.Clear();
            _nextInstanceId = 0;
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
            if (!IsCapturing || surface == null || app == null ||
                _captureTimer.ElapsedMilliseconds > _durationSeconds * 1000L)
            {
                return;
            }

            Dictionary<IApp, AppProfile> profilesForSurface;
            if (!_profilesBySurface.TryGetValue(surface, out profilesForSurface))
            {
                profilesForSurface = new Dictionary<IApp, AppProfile>();
                _profilesBySurface.Add(surface, profilesForSurface);
            }

            AppProfile profile;
            if (!profilesForSurface.TryGetValue(app, out profile))
            {
                profile = new AppProfile(++_nextInstanceId, app, surface);
                profilesForSurface.Add(app, profile);
                _profiles.Add(profile);
            }

            profile.Add(elapsedMilliseconds);
        }

        string BuildCsv()
        {
            var profiles = new List<AppProfile>(_profiles);
            profiles.Sort(CompareByTotalDescending);

            int maximumSampleCount = 0;
            for (int i = 0; i < profiles.Count; i++)
                maximumSampleCount = Math.Max(maximumSampleCount, profiles[i].Samples.Count);

            var csv = new StringBuilder();
            csv.Append(
                "window_seconds;app_instance;app;block_name;block_entity_id;surface_index;runs;average_ms;total_ms");
            for (int sampleIndex = 0; sampleIndex < maximumSampleCount; sampleIndex++)
                csv.Append(";run_").Append(sampleIndex + 1).Append("_ms");
            csv.AppendLine();

            for (int i = 0; i < profiles.Count; i++)
            {
                AppProfile profile = profiles[i];
                csv.Append(_durationSeconds)
                    .Append(';')
                    .Append(profile.InstanceId)
                    .Append(';')
                    .Append(EscapeCsv(profile.AppName))
                    .Append(';')
                    .Append(EscapeCsv(profile.BlockName))
                    .Append(';')
                    .Append(profile.BlockEntityId)
                    .Append(';')
                    .Append(profile.SurfaceIndex)
                    .Append(';')
                    .Append(profile.Samples.Count)
                    .Append(';')
                    .Append(profile.AverageMilliseconds.ToString("0.######", CultureInfo.InvariantCulture))
                    .Append(';')
                    .Append(profile.TotalMilliseconds.ToString("0.######", CultureInfo.InvariantCulture));

                for (int sampleIndex = 0; sampleIndex < maximumSampleCount; sampleIndex++)
                {
                    csv.Append(';');
                    if (sampleIndex < profile.Samples.Count)
                    {
                        csv.Append(profile.Samples[sampleIndex]
                            .ToString("0.######", CultureInfo.InvariantCulture));
                    }
                }

                csv.AppendLine();
            }

            return csv.ToString().TrimEnd();
        }

        static int CompareByTotalDescending(AppProfile left, AppProfile right)
        {
            int result = right.TotalMilliseconds.CompareTo(left.TotalMilliseconds);
            if (result != 0)
                return result;

            return left.InstanceId.CompareTo(right.InstanceId);
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
