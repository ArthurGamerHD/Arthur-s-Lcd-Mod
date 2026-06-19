using System;
using VRageMath;

namespace LcdMod.Client.ClockDashboard
{
    internal static class ClockDashboardSolarTime
    {
        public const double PolarUtcEnterLatitudeDegrees = 70d;
        public const double PolarUtcExitLatitudeDegrees = 65d;
        
        const double PolarEpsilonSquared = 1e-8;

        public static bool TryCalculateLocalSolarHour(
            Vector3D position,
            Vector3D planetCenter,
            Vector3D sunDirection,
            Vector3D rotationAxis,
            out double hour,
            out double elevation)
        {
            hour = 0d;
            elevation = 0d;

            Vector3D surfaceNormal = position - planetCenter;
            if (surfaceNormal.LengthSquared() <= PolarEpsilonSquared)
                return false;

            if (sunDirection.LengthSquared() <= PolarEpsilonSquared ||
                rotationAxis.LengthSquared() <= PolarEpsilonSquared)
                return false;

            surfaceNormal.Normalize();
            sunDirection.Normalize();
            rotationAxis.Normalize();
            elevation = Vector3D.Dot(surfaceNormal, sunDirection);

            Vector3D localMeridian = Reject(surfaceNormal, rotationAxis);
            Vector3D projectedSun = Reject(sunDirection, rotationAxis);
            if (localMeridian.LengthSquared() <= PolarEpsilonSquared ||
                projectedSun.LengthSquared() <= PolarEpsilonSquared)
                return false;

            localMeridian.Normalize();
            projectedSun.Normalize();

            double sinAngle = Vector3D.Dot(rotationAxis, Vector3D.Cross(localMeridian, projectedSun));
            double cosAngle = Vector3D.Dot(localMeridian, projectedSun);
            double hourAngle = Math.Atan2(sinAngle, cosAngle);
            hour = PositiveModulo(12.0 + hourAngle * (12.0 / Math.PI), 24.0);
            return true;
        }

        public static bool TryCalculateSunDirectionForLocalHour(
            Vector3D position,
            Vector3D planetCenter,
            Vector3D rotationAxis,
            double localSolarHour,
            out Vector3D sunDirection)
        {
            sunDirection = Vector3D.Zero;

            Vector3D surfaceNormal = position - planetCenter;
            if (surfaceNormal.LengthSquared() <= PolarEpsilonSquared ||
                rotationAxis.LengthSquared() <= PolarEpsilonSquared)
                return false;

            surfaceNormal.Normalize();
            rotationAxis.Normalize();

            Vector3D localMeridian = Reject(surfaceNormal, rotationAxis);
            if (localMeridian.LengthSquared() <= PolarEpsilonSquared)
                return false;

            localMeridian.Normalize();
            Vector3D positiveHourTangent = Vector3D.Cross(rotationAxis, localMeridian);
            if (positiveHourTangent.LengthSquared() <= PolarEpsilonSquared)
                return false;

            positiveHourTangent.Normalize();

            double hourAngle =
                (PositiveModulo(localSolarHour, 24d) - 12d) *
                (Math.PI / 12d);

            sunDirection =
                localMeridian * Math.Cos(hourAngle) +
                positiveHourTangent * Math.Sin(hourAngle);

            if (sunDirection.LengthSquared() <= PolarEpsilonSquared)
                return false;

            sunDirection.Normalize();
            return true;
        }

        public static bool IsInsidePolarUtcZone(
            Vector3D position,
            Vector3D planetCenter,
            Vector3D rotationAxis,
            DashboardClockMode previousMode)
        {
            Vector3D surfaceNormal = position - planetCenter;
            if (surfaceNormal.LengthSquared() <= PolarEpsilonSquared ||
                rotationAxis.LengthSquared() <= PolarEpsilonSquared)
                return false;

            surfaceNormal.Normalize();
            rotationAxis.Normalize();

            double axisAlignment = Math.Abs(Vector3D.Dot(surfaceNormal, rotationAxis));
            axisAlignment = MathHelper.Clamp(axisAlignment, 0d, 1d);
            double latitudeDegrees = MathHelper.ToDegrees(Math.Asin(axisAlignment));
            double enter = PolarUtcEnterLatitudeDegrees;
            double exit = PolarUtcExitLatitudeDegrees;

            enter = MathHelper.Clamp(enter, 0d, 90d);
            exit = MathHelper.Clamp(exit, 0d, enter);

            return previousMode == DashboardClockMode.Polar
                ? latitudeDegrees >= exit
                : latitudeDegrees >= enter;
        }

        public static DayMoment ClassifyLocalSolarHour(double hour)
        {
            hour = PositiveModulo(hour, 24d);
            if (hour < 5d)
                return DayMoment.Night;
            if (hour < 7d)
                return DayMoment.Dawn;
            if (hour < 11d)
                return DayMoment.Morning;
            if (hour < 13d)
                return DayMoment.Noon;
            if (hour < 17d)
                return DayMoment.Afternoon;
            if (hour < 19d)
                return DayMoment.Dusk;
            return DayMoment.Night;
        }

        public static double PositiveModulo(double value, double modulus)
        {
            return ((value % modulus) + modulus) % modulus;
        }

        static Vector3D Reject(Vector3D value, Vector3D axis)
        {
            return value - axis * Vector3D.Dot(value, axis);
        }
    }
}
