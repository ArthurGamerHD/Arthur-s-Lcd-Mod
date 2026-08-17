#if EXPERIMENTAL
using System;
using System.Diagnostics;

namespace LcdMod.Client.Diagnostics
{
    /// <summary>
    /// Lightweight named profiling scopes compiled only into experimental builds.
    /// </summary>
    internal static class RuntimeProfiler
    {
        public static RuntimeProfileScope Measure(
            string category,
            string operation,
            string context = null,
            long entityId = 0L,
            int surfaceIndex = -1)
        {
#if EXPERIMENTAL
            if (AppRunProfilerService.IsRuntimeCaptureActive)
            {
                return new RuntimeProfileScope(
                    category,
                    operation,
                    context,
                    entityId,
                    surfaceIndex,
                    Stopwatch.GetTimestamp());
            }
#endif
            return default(RuntimeProfileScope);
        }

        public static void RunScheduled(string category, Action action)
        {
            if (action == null)
                return;

#if EXPERIMENTAL
            using (Measure(category, "callback"))
                action();
#else
            action();
#endif
        }
    }

    internal struct RuntimeProfileScope : IDisposable
    {
#if EXPERIMENTAL
        readonly string _category;
        readonly string _operation;
        readonly string _context;
        readonly long _entityId;
        readonly int _surfaceIndex;
        readonly long _startedAt;

        internal RuntimeProfileScope(
            string category,
            string operation,
            string context,
            long entityId,
            int surfaceIndex,
            long startedAt)
        {
            _category = category;
            _operation = operation;
            _context = context;
            _entityId = entityId;
            _surfaceIndex = surfaceIndex;
            _startedAt = startedAt;
        }
#endif

        public void Dispose()
        {
#if EXPERIMENTAL
            if (_startedAt != 0L)
            {
                AppRunProfilerService.RecordRuntimeMeasurement(
                    _category,
                    _operation,
                    _context,
                    _entityId,
                    _surfaceIndex,
                    _startedAt);
            }
#endif
        }
    }
}
#endif
