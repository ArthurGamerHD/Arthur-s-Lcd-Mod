using System;
using LcdMod.Client.ClockDashboard;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.GridData
{
    public partial class GridLogic
    {
        private const double SOLAR_FORECAST_MOVE_TOLERANCE_SQUARED = 25d;
        private const double SOLAR_FORECAST_AXIS_DOT_TOLERANCE = 0.9999d;
        private const double SOLAR_FORECAST_SAMPLE_STEP_HOURS = 0.5d;
        private const int SOLAR_FORECAST_COARSE_SAMPLE_COUNT = 49;
        private const int SOLAR_FORECAST_REFINEMENT_ITERATIONS = 3;
        private const double SOLAR_FORECAST_SURFACE_OFFSET_METERS = 2.5d;
        private const double SOLAR_FORECAST_LINEAR_SPEED_TOLERANCE_SQUARED = 0.0001d;
        private const double SOLAR_FORECAST_ANGULAR_SPEED_TOLERANCE_SQUARED = 0.000001d;
        private const double SOLAR_FORECAST_GRAVITY_EXIT_MARGIN_METERS = 1d;
        private const int SOLAR_FORECAST_RAYCAST_DELAY_TICKS = 10;

        private enum TerrainSolarForecastPhase
        {
            Coarse,
            SunriseRefinement,
            SunsetRefinement
        }

        private sealed class TerrainSolarForecastScan
        {
            public int Version;
            public long PlanetId;
            public MyPlanet Planet;
            public Vector3D ReferenceSurfacePoint;
            public Vector3D SolarPosition;
            public Vector3D PlanetCenter;
            public Vector3D RotationAxis;
            public Vector3D RayOrigin;
            public double GravityRadius;
            public TerrainSolarForecastPhase Phase;
            public int SampleIndex;
            public bool HasPreviousSample;
            public bool PreviousVisible;
            public double PreviousHour;
            public bool HasSunriseBracket;
            public double SunriseLowHour;
            public double SunriseHighHour;
            public bool HasSunsetBracket;
            public double SunsetLowHour;
            public double SunsetHighHour;
            public int RefinementIteration;
            public double RefineLowHour;
            public double RefineHighHour;
            public bool HasSunrise;
            public double SunriseHour;
            public bool HasSunset;
            public double SunsetHour;
            public bool Scheduled;
        }

        private IMyGravityProviderSystem _terrainSolarGravityProvider;
        private bool _terrainSolarForecastInitialized;
        private long _terrainSolarForecastPlanetId;
        private Vector3D _terrainSolarForecastSurfacePoint;
        private Vector3D _terrainSolarForecastAxis;
        private bool _cachedHasTerrainSunrise;
        private double _cachedTerrainSunriseHour;
        private bool _cachedHasTerrainSunset;
        private double _cachedTerrainSunsetHour;
        private TerrainSolarForecastScan _terrainSolarForecastScan;
        private int _terrainSolarForecastScanVersion;

        private long _terrainSolarReferenceFrame = long.MinValue;
        private long _terrainSolarReferencePlanetId;
        private bool _terrainSolarReferenceValid;
        private Vector3D _terrainSolarReferenceSurfacePoint;
        private Vector3D _terrainSolarReferenceRayOrigin;
        private double _terrainSolarReferenceGravityRadius;

        internal bool TryGetTerrainSolarForecast(
            MyPlanet planet,
            Vector3D rotationAxis,
            out bool hasSunrise,
            out double sunriseHour,
            out bool hasSunset,
            out double sunsetHour)
        {
            hasSunrise = false;
            sunriseHour = 0d;
            hasSunset = false;
            sunsetHour = 0d;

            Vector3D surfacePoint;
            Vector3D rayOrigin;
            double gravityRadius;
            if (!TryResolveTerrainSolarReference(
                    planet,
                    out surfacePoint,
                    out rayOrigin,
                    out gravityRadius) ||
                rotationAxis.LengthSquared() <= 1e-8)
            {
                CancelTerrainSolarForecast();
                return false;
            }

            rotationAxis.Normalize();
            if (IsTerrainSolarCacheContextMatch(planet.EntityId, surfacePoint, rotationAxis))
            {
                hasSunrise = _cachedHasTerrainSunrise;
                sunriseHour = _cachedTerrainSunriseHour;
                hasSunset = _cachedHasTerrainSunset;
                sunsetHour = _cachedTerrainSunsetHour;
                return true;
            }

            if (!IsTerrainSolarGridStationary())
            {
                CancelTerrainSolarForecast();
                return false;
            }

            if (!IsMatchingTerrainSolarForecastScan(planet.EntityId, surfacePoint, rotationAxis))
            {
                StartTerrainSolarForecast(
                    surfacePoint,
                    rayOrigin,
                    gravityRadius,
                    planet,
                    rotationAxis);
            }

            return false;
        }

        private bool TryResolveTerrainSolarReference(
            MyPlanet planet,
            out Vector3D surfacePoint,
            out Vector3D rayOrigin,
            out double gravityRadius)
        {
            surfacePoint = Vector3D.Zero;
            rayOrigin = Vector3D.Zero;
            gravityRadius = 0d;

            if (Grid == null || Grid.MarkedForClose || planet == null || planet.MarkedForClose)
                return false;

            long frame = MyAPIGateway.Session != null
                ? MyAPIGateway.Session.GameplayFrameCounter
                : long.MinValue;
            if (_terrainSolarReferenceFrame == frame &&
                _terrainSolarReferencePlanetId == planet.EntityId)
            {
                surfacePoint = _terrainSolarReferenceSurfacePoint;
                rayOrigin = _terrainSolarReferenceRayOrigin;
                gravityRadius = _terrainSolarReferenceGravityRadius;
                return _terrainSolarReferenceValid;
            }

            _terrainSolarReferenceFrame = frame;
            _terrainSolarReferencePlanetId = planet.EntityId;
            _terrainSolarReferenceValid = false;
            _terrainSolarReferenceSurfacePoint = Vector3D.Zero;
            _terrainSolarReferenceRayOrigin = Vector3D.Zero;
            _terrainSolarReferenceGravityRadius = 0d;

            Vector3D gridPosition = Grid.WorldAABB.Center;
            Vector3D planetCenter = planet.WorldMatrix.Translation;
            surfacePoint = planet.GetClosestSurfacePointGlobal(gridPosition);

            Vector3D surfaceNormal = surfacePoint - planetCenter;
            if (surfaceNormal.LengthSquared() <= 1e-8)
                return false;

            surfaceNormal.Normalize();
            rayOrigin = surfacePoint + surfaceNormal * SOLAR_FORECAST_SURFACE_OFFSET_METERS;

            IMyNaturalGravityComponent gravityComponent;
            var gravityProvider = GetTerrainSolarGravityProvider();
            if (gravityProvider == null ||
                gravityProvider.GetStrongestNaturalGravityWell(rayOrigin, out gravityComponent) <= 0d ||
                gravityComponent == null ||
                Vector3D.DistanceSquared(gravityComponent.Position, planetCenter) > 1d ||
                gravityComponent.GravityLimit <= 0f)
                return false;

            gravityRadius = gravityComponent.GravityLimit;
            if (Vector3D.DistanceSquared(gridPosition, planetCenter) > gravityRadius * gravityRadius)
                return false;

            _terrainSolarReferenceValid = true;
            _terrainSolarReferenceSurfacePoint = surfacePoint;
            _terrainSolarReferenceRayOrigin = rayOrigin;
            _terrainSolarReferenceGravityRadius = gravityRadius;
            return true;
        }

        private IMyGravityProviderSystem GetTerrainSolarGravityProvider()
        {
            if (_terrainSolarGravityProvider == null && MyAPIGateway.Session != null)
            {
                _terrainSolarGravityProvider = (IMyGravityProviderSystem)MyAPIGateway.Session
                    .GetComponentByInterfaceType<IMyGravityProviderSystem>();
            }

            return _terrainSolarGravityProvider;
        }

        private bool IsTerrainSolarGridStationary()
        {
            if (Grid == null || Grid.MarkedForClose)
                return false;

            if (Grid.IsStatic)
                return true;

            var physics = Grid.Physics;
            if (physics == null)
                return false;

            return physics.LinearVelocity.LengthSquared() <=
                   SOLAR_FORECAST_LINEAR_SPEED_TOLERANCE_SQUARED &&
                   physics.AngularVelocity.LengthSquared() <=
                   SOLAR_FORECAST_ANGULAR_SPEED_TOLERANCE_SQUARED;
        }

        private bool IsTerrainSolarCacheContextMatch(
            long planetId,
            Vector3D surfacePoint,
            Vector3D rotationAxis)
        {
            if (!_terrainSolarForecastInitialized ||
                _terrainSolarForecastPlanetId != planetId ||
                Vector3D.DistanceSquared(_terrainSolarForecastSurfacePoint, surfacePoint) >
                    SOLAR_FORECAST_MOVE_TOLERANCE_SQUARED ||
                rotationAxis.LengthSquared() <= 1e-8 ||
                _terrainSolarForecastAxis.LengthSquared() <= 1e-8)
                return false;

            rotationAxis.Normalize();
            return Math.Abs(Vector3D.Dot(rotationAxis, _terrainSolarForecastAxis)) >=
                   SOLAR_FORECAST_AXIS_DOT_TOLERANCE;
        }

        private bool IsMatchingTerrainSolarForecastScan(
            long planetId,
            Vector3D surfacePoint,
            Vector3D rotationAxis)
        {
            var scan = _terrainSolarForecastScan;
            if (scan == null ||
                scan.PlanetId != planetId ||
                Vector3D.DistanceSquared(scan.ReferenceSurfacePoint, surfacePoint) >
                    SOLAR_FORECAST_MOVE_TOLERANCE_SQUARED ||
                rotationAxis.LengthSquared() <= 1e-8 ||
                scan.RotationAxis.LengthSquared() <= 1e-8)
                return false;

            rotationAxis.Normalize();
            return Math.Abs(Vector3D.Dot(rotationAxis, scan.RotationAxis)) >=
                   SOLAR_FORECAST_AXIS_DOT_TOLERANCE;
        }

        private void StartTerrainSolarForecast(
            Vector3D surfacePoint,
            Vector3D rayOrigin,
            double gravityRadius,
            MyPlanet planet,
            Vector3D rotationAxis)
        {
            CancelTerrainSolarForecast();

            rotationAxis.Normalize();
            var scan = new TerrainSolarForecastScan
            {
                Version = ++_terrainSolarForecastScanVersion,
                PlanetId = planet.EntityId,
                Planet = planet,
                ReferenceSurfacePoint = surfacePoint,
                SolarPosition = surfacePoint,
                PlanetCenter = planet.WorldMatrix.Translation,
                RotationAxis = rotationAxis,
                RayOrigin = rayOrigin,
                GravityRadius = gravityRadius,
                Phase = TerrainSolarForecastPhase.Coarse,
                SampleIndex = 0
            };

            _terrainSolarForecastScan = scan;
            ScheduleTerrainSolarForecastStep(scan);
        }

        private void CancelTerrainSolarForecast()
        {
            if (_terrainSolarForecastScan == null)
                return;

            _terrainSolarForecastScan = null;
            _terrainSolarForecastScanVersion++;
        }

        private void ScheduleTerrainSolarForecastStep(TerrainSolarForecastScan scan)
        {
            if (scan == null || scan.Scheduled || !ReferenceEquals(_terrainSolarForecastScan, scan))
                return;

            scan.Scheduled = true;
            LcdModClientComponent.ScheduleOnePerFrame(delegate
            {
                scan.Scheduled = false;
                RunTerrainSolarForecastStep(scan);
            }, SOLAR_FORECAST_RAYCAST_DELAY_TICKS);
        }

        private void RunTerrainSolarForecastStep(TerrainSolarForecastScan scan)
        {
            if (scan == null || !ReferenceEquals(_terrainSolarForecastScan, scan))
                return;

            Vector3D currentSurfacePoint;
            Vector3D currentRayOrigin;
            double currentGravityRadius;
            if (scan.Version != _terrainSolarForecastScanVersion ||
                scan.Planet == null ||
                scan.Planet.MarkedForClose ||
                !IsTerrainSolarGridStationary() ||
                !TryResolveTerrainSolarReference(
                    scan.Planet,
                    out currentSurfacePoint,
                    out currentRayOrigin,
                    out currentGravityRadius) ||
                Vector3D.DistanceSquared(currentSurfacePoint, scan.ReferenceSurfacePoint) >
                    SOLAR_FORECAST_MOVE_TOLERANCE_SQUARED)
            {
                CancelTerrainSolarForecast();
                return;
            }

            try
            {
                // Exactly one terrain line test is performed by each scheduled step.
                if (scan.Phase == TerrainSolarForecastPhase.Coarse)
                    RunTerrainSolarCoarseStep(scan);
                else
                    RunTerrainSolarRefinementStep(scan);
            }
            catch
            {
                CancelTerrainSolarForecast();
                return;
            }

            if (ReferenceEquals(_terrainSolarForecastScan, scan))
                ScheduleTerrainSolarForecastStep(scan);
        }

        private void RunTerrainSolarCoarseStep(TerrainSolarForecastScan scan)
        {
            double hour = scan.SampleIndex * SOLAR_FORECAST_SAMPLE_STEP_HOURS;
            double sampleHour = hour >= 24d ? 0d : hour;
            bool visible = IsTerrainSunVisible(scan, sampleHour);

            if (!scan.HasPreviousSample)
            {
                scan.HasPreviousSample = true;
            }
            else
            {
                if (!scan.PreviousVisible && visible && !scan.HasSunriseBracket)
                {
                    scan.HasSunriseBracket = true;
                    scan.SunriseLowHour = scan.PreviousHour;
                    scan.SunriseHighHour = hour;
                }

                if (scan.PreviousVisible && !visible)
                {
                    scan.HasSunsetBracket = true;
                    scan.SunsetLowHour = scan.PreviousHour;
                    scan.SunsetHighHour = hour;
                }
            }

            scan.PreviousVisible = visible;
            scan.PreviousHour = hour;
            scan.SampleIndex++;

            if (scan.SampleIndex < SOLAR_FORECAST_COARSE_SAMPLE_COUNT)
                return;

            BeginNextTerrainSolarRefinementOrComplete(scan);
        }

        private void BeginNextTerrainSolarRefinementOrComplete(TerrainSolarForecastScan scan)
        {
            scan.RefinementIteration = 0;

            if (scan.HasSunriseBracket && !scan.HasSunrise)
            {
                scan.Phase = TerrainSolarForecastPhase.SunriseRefinement;
                scan.RefineLowHour = scan.SunriseLowHour;
                scan.RefineHighHour = scan.SunriseHighHour;
                return;
            }

            if (scan.HasSunsetBracket && !scan.HasSunset)
            {
                scan.Phase = TerrainSolarForecastPhase.SunsetRefinement;
                scan.RefineLowHour = scan.SunsetLowHour;
                scan.RefineHighHour = scan.SunsetHighHour;
                return;
            }

            CompleteTerrainSolarForecast(scan);
        }

        private void RunTerrainSolarRefinementStep(TerrainSolarForecastScan scan)
        {
            double middleHour = (scan.RefineLowHour + scan.RefineHighHour) * 0.5d;
            bool visible = IsTerrainSunVisible(
                scan,
                ClockDashboardSolarTime.PositiveModulo(middleHour, 24d));

            if (scan.Phase == TerrainSolarForecastPhase.SunriseRefinement)
            {
                if (visible)
                    scan.RefineHighHour = middleHour;
                else
                    scan.RefineLowHour = middleHour;
            }
            else
            {
                if (visible)
                    scan.RefineLowHour = middleHour;
                else
                    scan.RefineHighHour = middleHour;
            }

            scan.RefinementIteration++;
            if (scan.RefinementIteration < SOLAR_FORECAST_REFINEMENT_ITERATIONS)
                return;

            double resultHour = ClockDashboardSolarTime.PositiveModulo(
                (scan.RefineLowHour + scan.RefineHighHour) * 0.5d,
                24d);

            if (scan.Phase == TerrainSolarForecastPhase.SunriseRefinement)
            {
                scan.HasSunrise = true;
                scan.SunriseHour = resultHour;
            }
            else
            {
                scan.HasSunset = true;
                scan.SunsetHour = resultHour;
            }

            BeginNextTerrainSolarRefinementOrComplete(scan);
        }

        private void CompleteTerrainSolarForecast(TerrainSolarForecastScan scan)
        {
            if (!ReferenceEquals(_terrainSolarForecastScan, scan))
                return;

            _terrainSolarForecastInitialized = true;
            _terrainSolarForecastPlanetId = scan.PlanetId;
            _terrainSolarForecastSurfacePoint = scan.ReferenceSurfacePoint;
            _terrainSolarForecastAxis = scan.RotationAxis;
            _cachedHasTerrainSunrise = scan.HasSunrise;
            _cachedTerrainSunriseHour = scan.SunriseHour;
            _cachedHasTerrainSunset = scan.HasSunset;
            _cachedTerrainSunsetHour = scan.SunsetHour;
            _terrainSolarForecastScan = null;
        }

        private static bool IsTerrainSunVisible(
            TerrainSolarForecastScan scan,
            double localSolarHour)
        {
            Vector3D sunDirection;
            if (!ClockDashboardSolarTime.TryCalculateSunDirectionForLocalHour(
                    scan.SolarPosition,
                    scan.PlanetCenter,
                    scan.RotationAxis,
                    localSolarHour,
                    out sunDirection))
                return false;

            double rayDistance;
            if (!TryCalculateGravitySphereExitDistance(
                    scan.RayOrigin,
                    sunDirection,
                    scan.PlanetCenter,
                    scan.GravityRadius,
                    out rayDistance))
                return false;

            var line = new LineD(
                scan.RayOrigin,
                scan.RayOrigin + sunDirection * rayDistance);
            Vector3D? hitPosition;
            return !scan.Planet.GetIntersectionWithLine(ref line, out hitPosition);
        }

        private static bool TryCalculateGravitySphereExitDistance(
            Vector3D rayOrigin,
            Vector3D rayDirection,
            Vector3D sphereCenter,
            double sphereRadius,
            out double distance)
        {
            distance = 0d;
            if (sphereRadius <= 0d || rayDirection.LengthSquared() <= 1e-8)
                return false;

            rayDirection.Normalize();
            Vector3D offset = rayOrigin - sphereCenter;
            double b = Vector3D.Dot(offset, rayDirection);
            double c = offset.LengthSquared() - sphereRadius * sphereRadius;
            double discriminant = b * b - c;
            if (discriminant < 0d)
                return false;

            distance = -b + Math.Sqrt(discriminant) + SOLAR_FORECAST_GRAVITY_EXIT_MARGIN_METERS;
            return distance > 0d;
        }
    }
}
