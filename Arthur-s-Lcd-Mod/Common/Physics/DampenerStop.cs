using System;

namespace LcdMod.Common.Physics
{
    /// <summary>
    /// Replays SE's inertial dampeners (MyEntityThrustComponent.ComputeBaseThrust) to predict a stop. Vectors are in the
    /// grid's frame as [forward, right, up]. Each axis requests (velocity / 0.5 s + gravity) x mass and is clamped on its
    /// own to the thrust available in that direction, so a weak axis never slows the others, and the request tapers as
    /// the ship slows. Integrated at the game's 60 Hz until the ship is below <see cref="StopSpeed"/>.
    /// </summary>
    public static class DampenerStop
    {
        public const double Tick = 1d / 60d;
        public const double ResponseSeconds = .5d;
        public const double StopSpeed = .05d;
        const int MaxTicks = 60 * 600;

        public static bool Simulate(double[] velocity, double[] gravity, double mass, double[] positiveThrust,
            double[] negativeThrust, out double seconds, out double distance, out double initialForce)
        {
            seconds = 0d;
            distance = 0d;
            initialForce = 0d;
            if (mass <= 0d || velocity == null || gravity == null || positiveThrust == null || negativeThrust == null)
                return false;

            double vx = velocity[0], vy = velocity[1], vz = velocity[2];
            for (int tick = 0; tick < MaxTicks; tick++)
            {
                if (Math.Sqrt(vx * vx + vy * vy + vz * vz) <= StopSpeed)
                    return true;

                double ax = Axis(vx, gravity[0], mass, positiveThrust[0], negativeThrust[0]) / mass + gravity[0];
                double ay = Axis(vy, gravity[1], mass, positiveThrust[1], negativeThrust[1]) / mass + gravity[1];
                double az = Axis(vz, gravity[2], mass, positiveThrust[2], negativeThrust[2]) / mass + gravity[2];
                if (tick == 0)
                    initialForce = Math.Sqrt(ax * ax + ay * ay + az * az) * mass;

                vx += ax * Tick;
                vy += ay * Tick;
                vz += az * Tick;
                distance += Math.Sqrt(vx * vx + vy * vy + vz * vz) * Tick;
                seconds += Tick;
            }
            return false;
        }

        static double Axis(double velocity, double gravity, double mass, double positiveThrust, double negativeThrust)
        {
            double requested = -(velocity / ResponseSeconds + gravity) * mass;
            return Math.Max(-Math.Max(0d, negativeThrust), Math.Min(Math.Max(0d, positiveThrust), requested));
        }
    }
}
