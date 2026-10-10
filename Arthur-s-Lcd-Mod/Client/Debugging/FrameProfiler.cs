#if DEBUG
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.Debugging
{
    /// <summary>
    /// Logs, every few seconds, how much frame time the mod spends on pointer tracking and on rendering screens, and
    /// which screens cost the most. Debug builds only.
    /// </summary>
    internal static class FrameProfiler
    {
        const long LOG_INTERVAL_FRAMES = 300;
        const int TOP_SCREENS = 5;

        sealed class ScreenCost
        {
            public string Name;
            public long Ticks;
            public int Renders;
            public int Uploads;
            public int MaxSprites;
        }

        static readonly Dictionary<string, ScreenCost> Screens = new Dictionary<string, ScreenCost>();
        static readonly Dictionary<string, int> Changes = new Dictionary<string, int>();
        static readonly List<KeyValuePair<string, int>> SortedChanges = new List<KeyValuePair<string, int>>();
        const int TOP_CHANGES = 12;

        /// <summary>Counts UI property and pseudo-class changes; anything near one per frame keeps a screen redrawing.</summary>
        public static void TraceChange(Adk.Gui.Core.Control control, string change)
        {
            var visual = control as Adk.Gui.Core.VisualElement;
            string parent = visual?.Parent == null ? "-" : visual.Parent.GetType().Name;
            string key = control.GetType().Name + (string.IsNullOrEmpty(control.StyleClass) ? "" : "." + control.StyleClass) +
                         " < " + parent + ": " + change;
            lock (Screens)
            {
                int count;
                Changes.TryGetValue(key, out count);
                Changes[key] = count + 1;
            }
        }
        static readonly List<ScreenCost> Sorted = new List<ScreenCost>();
        static long _windowStartFrame = -1;
        static long _eyeTrackingTicks;
        static long _lookAtTicks;
        static long _rayTestTicks;
        static long _eyeTrackingCalls;
        static long _screensTested;
        static long _screensLookedAt;
        static long _renderTicks;
        static int _renders;

        public static long Start()
        {
            return Stopwatch.GetTimestamp();
        }

        public static void EndEyeTracking(long start, int tested, int lookedAt)
        {
            _eyeTrackingTicks += Stopwatch.GetTimestamp() - start;
            _eyeTrackingCalls++;
            _screensTested += tested;
            _screensLookedAt += lookedAt;
            FlushIfDue();
        }

        public static void AddRayTest(long start)
        {
            _rayTestTicks += Stopwatch.GetTimestamp() - start;
        }

        public static void AddLookAt(long start)
        {
            _lookAtTicks += Stopwatch.GetTimestamp() - start;
        }

        public static void EndRender(long start, string screen, int sprites, bool uploaded)
        {
            long elapsed = Stopwatch.GetTimestamp() - start;
            // Screens can render in parallel.
            lock (Screens)
                RecordRender(elapsed, screen, sprites, uploaded);
        }

        static void RecordRender(long elapsed, string screen, int sprites, bool uploaded)
        {
            _renderTicks += elapsed;
            _renders++;
            ScreenCost cost;
            if (!Screens.TryGetValue(screen, out cost))
                Screens[screen] = cost = new ScreenCost { Name = screen };
            cost.Ticks += elapsed;
            cost.Renders++;
            if (uploaded)
                cost.Uploads++;
            if (sprites > cost.MaxSprites)
                cost.MaxSprites = sprites;
            FlushIfDue();
        }

        static void FlushIfDue()
        {
            long frame = MyAPIGateway.Session?.GameplayFrameCounter ?? 0;
            if (_windowStartFrame < 0)
                _windowStartFrame = frame;
            long frames = frame - _windowStartFrame;
            if (frames < LOG_INTERVAL_FRAMES)
                return;

            var text = new StringBuilder();
            text.Append("[Profile] per frame over ").Append(frames).Append(" frames: eye tracking ")
                .Append(Milliseconds(_eyeTrackingTicks, frames)).Append(" ms, of which LookAt ")
                .Append(Milliseconds(_lookAtTicks, frames)).Append(" ms, ray tests ")
                .Append(Milliseconds(_rayTestTicks, frames)).Append(" ms (")
                .Append(Average(_screensTested, _eyeTrackingCalls)).Append(" screens tested, ")
                .Append(Average(_screensLookedAt, _eyeTrackingCalls)).Append(" looked at); rendering ")
                .Append(Milliseconds(_renderTicks, frames)).Append(" ms (")
                .Append((_renders / (double)frames).ToString("0.##")).Append(" renders)");

            Sorted.Clear();
            Sorted.AddRange(Screens.Values);
            Sorted.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
            for (int i = 0; i < Sorted.Count && i < TOP_SCREENS; i++)
            {
                var cost = Sorted[i];
                text.Append("\n  ").Append(cost.Name).Append(": ")
                    .Append(Milliseconds(cost.Ticks, frames)).Append(" ms/frame, ")
                    .Append(cost.Renders).Append(" renders, ")
                    .Append(cost.Uploads).Append(" uploads, up to ")
                    .Append(cost.MaxSprites).Append(" sprites");
            }
            SortedChanges.Clear();
            SortedChanges.AddRange(Changes);
            SortedChanges.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (SortedChanges.Count > 0)
                text.Append("\n  UI changes (count per window):");
            for (int i = 0; i < SortedChanges.Count && i < TOP_CHANGES; i++)
                text.Append("\n    ").Append(SortedChanges[i].Value).Append("x ").Append(SortedChanges[i].Key);
            Changes.Clear();
            LogHelper.LogInfo(text.ToString());

            _windowStartFrame = frame;
            _eyeTrackingTicks = _lookAtTicks = _rayTestTicks = _eyeTrackingCalls = _screensTested = _screensLookedAt = _renderTicks = 0;
            _renders = 0;
            Screens.Clear();
        }

        static string Milliseconds(long ticks, long frames)
        {
            return (ticks * 1000.0 / Stopwatch.Frequency / frames).ToString("0.###");
        }

        static string Average(long total, long count)
        {
            return count <= 0 ? "0" : (total / (double)count).ToString("0.#");
        }
    }
}
#endif
