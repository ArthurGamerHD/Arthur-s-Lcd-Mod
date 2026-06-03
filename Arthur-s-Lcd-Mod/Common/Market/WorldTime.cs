using System;
using Sandbox.ModAPI;

namespace LcdMod.Common.Market
{
    internal static class WorldTime
    {
        static readonly DateTime Epoch =
            new DateTime(2081, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long NowElapsedTicks()
        {
            var session = MyAPIGateway.Session;
            if (session == null)
                return 0L;

            return (session.GameDateTime - Epoch).Ticks;
        }

        public static long FromSeconds(double seconds)
        {
            return TimeSpan.FromSeconds(seconds).Ticks;
        }

        public static long FromMilliseconds(double milliseconds)
        {
            return TimeSpan.FromMilliseconds(milliseconds).Ticks;
        }

        public static double ToSeconds(long ticks)
        {
            return TimeSpan.FromTicks(Math.Max(0L, ticks)).TotalSeconds;
        }
    }
}
