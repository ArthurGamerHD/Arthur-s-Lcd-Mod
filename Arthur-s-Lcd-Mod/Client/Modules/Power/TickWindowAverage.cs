using System.Collections.Generic;
using Sandbox.ModAPI;

namespace LcdMod.Client.Modules.Power
{
    /// <summary>Mean of the values sampled over a window of recent gameplay ticks, or over every sample.</summary>
    internal sealed class TickWindowAverage
    {
        /// <summary>
        /// One full cycle of vanilla Auto batteries: a full battery drops its charge request on one 100-tick update and
        /// requests again on the next, so demand is a 200-tick square wave. A shorter window turns it into a ramp.
        /// </summary>
        public const int BATTERY_CYCLE_TICKS = 200;

        readonly Queue<KeyValuePair<long, double>> _samples = new Queue<KeyValuePair<long, double>>();
        readonly int _maxWindowTicks;
        double _lifetimeSum;
        long _lifetimeCount;

        public TickWindowAverage(int maxWindowTicks)
        {
            _maxWindowTicks = maxWindowTicks;
        }

        public static long CurrentTick => MyAPIGateway.Session.GameplayFrameCounter;

        /// <summary>
        /// Adds a sample and returns the mean over the last <paramref name="windowTicks"/> ticks (capped at the maximum
        /// window), or over every sample since the last <see cref="Clear"/> when it is 0 or less.
        /// </summary>
        public double Add(double value, int windowTicks)
        {
            long tick = CurrentTick;
            _samples.Enqueue(new KeyValuePair<long, double>(tick, value));
            _lifetimeSum += value;
            _lifetimeCount++;
            while (_samples.Count > 1 && _samples.Peek().Key <= tick - _maxWindowTicks)
                _samples.Dequeue();

            if (windowTicks <= 0)
                return _lifetimeSum / _lifetimeCount;

            double sum = 0;
            int count = 0;
            foreach (var sample in _samples)
            {
                if (sample.Key <= tick - windowTicks)
                    continue;
                sum += sample.Value;
                count++;
            }

            return count > 0 ? sum / count : value;
        }

        public void Clear()
        {
            _samples.Clear();
            _lifetimeSum = 0;
            _lifetimeCount = 0;
        }
    }
}
