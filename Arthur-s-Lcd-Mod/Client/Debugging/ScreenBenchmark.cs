#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Layout;
using LcdMod.Client.SurfaceScripts;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Client.Debugging
{
    /// <summary>A repeatable, local-only workload for profiling interactive LCDs.</summary>
    internal static class ScreenBenchmark
    {
        const string LockFile = "benchmark.lock";
        const int Seed = 14351;
        static readonly TimeSpan Duration = TimeSpan.FromSeconds(15);
        static readonly List<VisualElement> Pending = new List<VisualElement>();
        static readonly HashSet<VisualElement> Visited = new HashSet<VisualElement>();
        static readonly Stopwatch Clock = new Stopwatch();
        static List<ScreenState> _screens;
        static int _frames;
        static int _lookAts;
        static int _scrolls;
        static int _pageChanges;
        static int _errors;
        static bool _lookAtAll;

        sealed class ScreenState
        {
            public InteractiveSurfaceScript Screen;
            public Random Random = new Random(Seed);
        }

        public static bool IsRunning => _screens != null;

        public static void ClearStaleLock()
        {
            DeleteLock();
        }

        public static void Start(bool lookAtAll)
        {
            if (IsRunning)
            {
                Stop("cancelled");
                return;
            }

            var screens = new List<ScreenState>();
            foreach (SurfaceScriptBase surface in SurfaceScriptBase.Instances)
            {
                var interactive = surface as InteractiveSurfaceScript;
                // Proxy look-at forwards to its host; running both would exercise the same UI twice.
                if (interactive != null && interactive.Block != null && !(interactive is RenderProxySurfaceScript))
                    screens.Add(new ScreenState { Screen = interactive });
            }
            screens.Sort((a, b) =>
            {
                int block = a.Screen.Block.EntityId.CompareTo(b.Screen.Block.EntityId);
                return block != 0 ? block : a.Screen.SurfaceIndex.CompareTo(b.Screen.SurfaceIndex);
            });

            if (screens.Count == 0)
            {
                MyAPIGateway.Utilities.ShowNotification("Screen benchmark: no interactive screens found");
                return;
            }

            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(LockFile, typeof(LcdModClientComponent)))
                {
                    writer.WriteLine("seed=" + Seed);
                    writer.WriteLine("screens=" + screens.Count);
                    writer.WriteLine("started=" + DateTime.UtcNow.ToString("O"));
                }
            }
            catch (Exception e)
            {
                DeleteLock();
                LogHelper.LogInfo("[ScreenBenchmark] Could not create benchmark.lock: " + e);
                MyAPIGateway.Utilities.ShowNotification("Screen benchmark: could not create lock file", 3000, "Red");
                return;
            }

            _screens = screens;
            _lookAtAll = lookAtAll;
            _frames = _lookAts = _scrolls = _pageChanges = _errors = 0;
            Clock.Restart();
            LogHelper.LogInfo("[ScreenBenchmark] Started 15-second run; screens=" + screens.Count + ", seed=" + Seed +
                              ", look-at=" + (lookAtAll ? "all screens" : "one screen per frame"));
            MyAPIGateway.Utilities.ShowNotification("Screen benchmark started (15 s, " + screens.Count + " screens)");
            LcdModClientComponent.RunNextFrame.Add(Update);
        }

        public static void Stop(string reason)
        {
            if (!IsRunning)
            {
                DeleteLock();
                return;
            }

            Clock.Stop();
            _screens = null;
            Pending.Clear();
            Visited.Clear();
            DeleteLock();
            string result = "[ScreenBenchmark] " + reason + ": " + Clock.Elapsed.TotalSeconds.ToString("F2") +
                            " s, " + _frames + " frames, " + _lookAts + " look-ats, " + _scrolls +
                            " scrolls, " + _pageChanges + " carousel selections, " + _errors + " errors";
            LogHelper.LogInfo(result);
            MyAPIGateway.Utilities.ShowNotification("Screen benchmark " + reason + " (" + _frames + " frames)");
        }

        static void Update()
        {
            if (!IsRunning)
                return;
            if (Clock.Elapsed >= Duration)
            {
                Stop("finished");
                return;
            }

            try
            {
                // A player looks at one screen at a time: one screen per frame gets the look-at (input and hit-test
                // layout), the rest only request a render, so the run measures rendering rather than pointer work.
                int lookedAt = _frames % _screens.Count;
                for (int i = 0; i < _screens.Count; i++)
                {
                    ScreenState state = _screens[i];
                    try
                    {
                        RunScreen(state, _lookAtAll || i == lookedAt);
                    }
                    catch (Exception e)
                    {
                        _errors++;
                        if (_errors <= 10)
                            LogHelper.LogInfo("[ScreenBenchmark] Screen failed: " + e);
                    }
                }
                _frames++;
            }
            catch (Exception e)
            {
                LogHelper.LogInfo("[ScreenBenchmark] Run failed: " + e);
                Stop("failed");
                return;
            }

            if (Clock.Elapsed >= Duration)
                Stop("finished");
            else
                LcdModClientComponent.RunNextFrame.Add(Update);
        }

        static void RunScreen(ScreenState state, bool lookAt)
        {
            InteractiveSurfaceScript screen = state.Screen;
            if (screen.Block == null || screen.Block.Closed || screen.Block.MarkedForClose)
                return;

            // The host ViewBox expands as proxies register, so it covers their logical screen pages too.
            RectangleF bounds = screen.ViewBox;
            if (bounds.Width <= 0f || bounds.Height <= 0f)
                return;

            Random random = state.Random;
            var position = new Vector2(
                bounds.X + (float)random.NextDouble() * bounds.Width,
                bounds.Y + (float)random.NextDouble() * bounds.Height);

            var host = screen.RetainedAppGuiHost;
            if (host != null && !host.IsDisposed)
            {
                try
                {
                    RandomizeControls(host.Root, random);
                }
                catch (Exception e)
                {
                    _errors++;
                    if (_errors <= 10)
                        LogHelper.LogInfo("[ScreenBenchmark] UI control update failed: " + e);
                }
            }

            if (!lookAt)
            {
                screen.RenderSprites();
                return;
            }
            screen.LookAt(position);
            _lookAts++;
        }

        static void RandomizeControls(VisualElement root, Random random)
        {
            Pending.Clear();
            Visited.Clear();
            Pending.Add(root);
            while (Pending.Count > 0)
            {
                int last = Pending.Count - 1;
                VisualElement element = Pending[last];
                Pending.RemoveAt(last);
                if (element == null || !Visited.Add(element))
                    continue;

                var viewer = element as ScrollViewer;
                if (viewer != null)
                {
                    float horizontal = (float)random.NextDouble() * viewer.MaxHorizontalOffset;
                    float vertical = (float)random.NextDouble() * viewer.MaxVerticalOffset;
                    viewer.ScrollTo(horizontal, vertical);
                    _scrolls++;
                }

                var carousel = element as Carousel;
                if (carousel != null)
                {
                    double fraction = random.NextDouble();
                    if (carousel.ItemCount > 0 && carousel.ItemsPanelRoot != null)
                    {
                        carousel.SelectedIndex = Math.Min(carousel.ItemCount - 1,
                            (int)(fraction * carousel.ItemCount));
                        _pageChanges++;
                    }
                }

                var children = element.VisualChildren;
                for (int i = children.Count - 1; i >= 0; i--)
                    Pending.Add(children[i]);
            }
        }

        static void DeleteLock()
        {
            try
            {
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(LockFile, typeof(LcdModClientComponent)))
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(LockFile, typeof(LcdModClientComponent));
            }
            catch (Exception e)
            {
                LogHelper.LogInfo("[ScreenBenchmark] Could not remove benchmark.lock: " + e);
            }
        }
    }
}
#endif
