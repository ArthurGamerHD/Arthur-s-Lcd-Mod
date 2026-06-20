using System;
using LcdMod.Client.ClockDashboard;
using LcdMod.Client.GridData;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace LcdMod.Client.Helpers
{
    internal sealed class ClockEnvironmentReader
    {
        bool _sunRotationAxisInitialized;
        Vector3D _sunRotationAxis;
        public ClockDashboardSnapshot Read(
            IMyCubeBlock block,
            GridLogic gridLogic,
            ClockDashboardSnapshot previous)
        {
            var snapshot = CreateDefaultSnapshot();
            if (MyAPIGateway.Session == null || block == null || block.MarkedForClose)
            {
                return snapshot;
            }

            snapshot.SessionGameDateTime = MyAPIGateway.Session.GameDateTime;
            snapshot.SolarDayLengthSeconds = GetSunRotationDayLengthSeconds();
            Vector3D position = block.GetPosition();

            MyPlanet planet = FindRelevantPlanet(position);
            snapshot.HasPlanet = planet != null;
            snapshot.PlanetName = planet != null ? ResolvePlanetName(planet) : ClockDashboardLocalization.DeepSpace;

            ReadWeather(position, planet, snapshot);
            ReadWind(position, planet, snapshot);
            ReadTemperature(position, planet, snapshot);
            ReadGridRoomEnvironment(block, gridLogic, previous, snapshot);

            if (planet == null)
            {
                snapshot.ClockMode = DashboardClockMode.DeepSpace;
                snapshot.DayMoment = DayMoment.NoLocalDayCycle;
                snapshot.DisplayDateTime = ClockDashboardFormatter.BuildDisplayDateTime(
                    snapshot.SessionGameDateTime,
                    snapshot.ClockMode,
                    snapshot.HasLocalSolarTime,
                    snapshot.LocalSolarHour,
                    snapshot.SolarDayLengthSeconds);
                return snapshot;
            }

            Vector3D planetCenter = planet.WorldMatrix.Translation;
            Vector3D sunDirection = MyVisualScriptLogicProvider.GetSunDirection();
            Vector3D rotationAxis = ResolveSunRotationAxis(sunDirection);

            bool polar = ClockDashboardSolarTime.IsInsidePolarUtcZone(
                position,
                planetCenter,
                rotationAxis,
                previous?.ClockMode ?? DashboardClockMode.Default);

            if (polar)
            {
                snapshot.ClockMode = DashboardClockMode.Polar;
                snapshot.DayMoment = DayMoment.PolarDay;
                snapshot.DisplayDateTime = ClockDashboardFormatter.BuildDisplayDateTime(
                    snapshot.SessionGameDateTime,
                    snapshot.ClockMode,
                    snapshot.HasLocalSolarTime,
                    snapshot.LocalSolarHour,
                    snapshot.SolarDayLengthSeconds);
                return snapshot;
            }

            double localHour;
            double elevation;
            if (ClockDashboardSolarTime.TryCalculateLocalSolarHour(
                    position,
                    planetCenter,
                    sunDirection,
                    rotationAxis,
                    out localHour,
                    out elevation))
            {
                snapshot.ClockMode = DashboardClockMode.LocalSolar;
                snapshot.HasLocalSolarTime = true;
                snapshot.LocalSolarHour = localHour;
                snapshot.SolarElevationFactor = elevation;
                snapshot.DayMoment = ClockDashboardSolarTime.ClassifyLocalSolarHour(localHour);
                snapshot.DisplayDateTime = ClockDashboardFormatter.BuildDisplayDateTime(
                    snapshot.SessionGameDateTime,
                    snapshot.ClockMode,
                    snapshot.HasLocalSolarTime,
                    snapshot.LocalSolarHour,
                    snapshot.SolarDayLengthSeconds);
                ReadTerrainSolarEvents(gridLogic, planet, rotationAxis, snapshot);
                return snapshot;
            }

            snapshot.ClockMode = DashboardClockMode.Default;
            snapshot.DayMoment = DayMoment.Unknown;
            snapshot.DisplayDateTime = ClockDashboardFormatter.BuildDisplayDateTime(
                snapshot.SessionGameDateTime,
                snapshot.ClockMode,
                snapshot.HasLocalSolarTime,
                snapshot.LocalSolarHour,
                snapshot.SolarDayLengthSeconds);
            return snapshot;
        }

        static ClockDashboardSnapshot CreateDefaultSnapshot()
        {
            return new ClockDashboardSnapshot
            {
                SessionGameDateTime = DateTime.MinValue,
                ClockMode = DashboardClockMode.Default,
                PlanetName = ClockDashboardLocalization.DeepSpace,
                WeatherDisplayName = ClockDashboardLocalization.Unavailable,
                PlanetClimate = MyTemperatureLevel.Cozy,
                AmbientTemperatureLevel = MyTemperatureLevel.Cozy,
                InteriorTemperatureLevel = MyTemperatureLevel.Cozy,
                DisplayDateTime = DateTime.MinValue,
                SolarDayLengthSeconds = 86400d
            };
        }

        static MyPlanet FindRelevantPlanet(Vector3D position)
        {
            MyPlanet best = null;
            double bestDistance = double.MaxValue;

            foreach (var entry in PlanetHelper.PlanetsById)
            {
                var planet = entry.Value;
                if (planet == null || planet.MarkedForClose)
                    continue;

                double radius = Math.Max(planet.MaximumRadius, planet.AverageRadius);
                if (radius <= 0d)
                    continue;

                double distance = Vector3D.Distance(position, planet.WorldMatrix.Translation);
                double relevantRadius = planet.HasAtmosphere && planet.AtmosphereRadius > 0f
                    ? Math.Max(planet.AtmosphereRadius, radius)
                    : radius * 1.25d;

                if (distance > relevantRadius || distance >= bestDistance)
                    continue;

                best = planet;
                bestDistance = distance;
            }

            return best;
        }

        static string ResolvePlanetName(MyPlanet planet)
        {
            if (planet == null)
                return ClockDashboardLocalization.UnknownPlanet;

            string name;
            if (PlanetHelper.PlanetNamesById.TryGetValue(planet.EntityId, out name) &&
                !string.IsNullOrWhiteSpace(name))
                return name;

            name = PlanetHelper.GetPlanetGeneratorName(planet);
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            if (!string.IsNullOrWhiteSpace(planet.Name))
                return planet.Name;

            return ClockDashboardLocalization.UnknownPlanet;
        }

        static void ReadWeather(Vector3D position, MyPlanet planet, ClockDashboardSnapshot snapshot)
        {
            try
            {
                var weatherApi = MyAPIGateway.Session != null ? MyAPIGateway.Session.WeatherEffects : null;
                if (weatherApi == null)
                {
                    snapshot.WeatherDisplayName = ClockDashboardLocalization.Unavailable;
                    return;
                }

                string subtype = weatherApi.GetWeather(position);
                snapshot.WeatherSubtype = subtype;
                snapshot.WeatherIntensity = weatherApi.GetWeatherIntensity(position);
                snapshot.WeatherDisplayName = ResolveWeatherDisplayName(subtype);

                if (planet != null)
                {
                    if (snapshot.WeatherIntensity <= 0.001f)
                        ReadIncomingWeather(position, planet, weatherApi, snapshot);
                    else
                        ReadWeatherClearForecast(position, planet, weatherApi, snapshot);
                }
            }
            catch
            {
                snapshot.WeatherDisplayName = ClockDashboardLocalization.Unavailable;
                snapshot.HasIncomingWeather = false;
            }
        }


        static void ReadWeatherClearForecast(
            Vector3D position,
            MyPlanet planet,
            IMyWeatherEffects weatherApi,
            ClockDashboardSnapshot snapshot)
        {
            var planetData = weatherApi.GetWeatherPlanetData();
            if (planetData == null)
                return;

            bool foundActiveEffect = false;
            double clearSeconds = 0d;

            for (int i = 0; i < planetData.Count; i++)
            {
                var data = planetData[i];
                if (data == null || data.PlanetId != planet.EntityId || data.Weathers == null)
                    continue;

                for (int j = 0; j < data.Weathers.Count; j++)
                {
                    var effect = data.Weathers[j];
                    if (effect == null || effect.Radius <= 0f || string.IsNullOrWhiteSpace(effect.Weather) ||
                        string.Equals(effect.Weather, "Clear", StringComparison.OrdinalIgnoreCase))
                        continue;

                    double radius = effect.Radius;
                    if (Vector3D.DistanceSquared(effect.Position, position) > radius * radius)
                        continue;

                    double entrySeconds;
                    double exitSeconds;
                    if (!TryCalculateWeatherIntersection(position, effect, out entrySeconds, out exitSeconds))
                        continue;

                    foundActiveEffect = true;
                    clearSeconds = Math.Max(clearSeconds, exitSeconds);
                }
            }

            if (!foundActiveEffect)
                return;

            snapshot.HasIncomingWeather = true;
            snapshot.IncomingWeatherSubtype = "Clear";
            snapshot.IncomingWeatherDisplayName = ClockDashboardLocalization.ClearWeather;
            snapshot.IncomingWeatherEtaSeconds = clearSeconds;
        }

        static void ReadIncomingWeather(
            Vector3D position,
            MyPlanet planet,
            IMyWeatherEffects weatherApi,
            ClockDashboardSnapshot snapshot)
        {
            var planetData = weatherApi.GetWeatherPlanetData();
            if (planetData == null)
                return;

            double bestEntrySeconds = double.PositiveInfinity;
            for (int i = 0; i < planetData.Count; i++)
            {
                var data = planetData[i];
                if (data == null || data.PlanetId != planet.EntityId || data.Weathers == null)
                    continue;

                for (int j = 0; j < data.Weathers.Count; j++)
                {
                    var effect = data.Weathers[j];
                    if (effect == null || effect.Radius <= 0f || string.IsNullOrWhiteSpace(effect.Weather) ||
                        string.Equals(effect.Weather, "Clear", StringComparison.OrdinalIgnoreCase))
                        continue;

                    double entrySeconds;
                    double exitSeconds;
                    if (!TryCalculateWeatherIntersection(position, effect, out entrySeconds, out exitSeconds) ||
                        entrySeconds >= bestEntrySeconds)
                        continue;

                    bestEntrySeconds = entrySeconds;
                    snapshot.HasIncomingWeather = true;
                    snapshot.IncomingWeatherSubtype = effect.Weather;
                    snapshot.IncomingWeatherDisplayName = ResolveWeatherDisplayName(effect.Weather);
                    snapshot.IncomingWeatherEtaSeconds = entrySeconds;
                }
            }
        }

        static bool TryCalculateWeatherIntersection(
            Vector3D position,
            MyObjectBuilder_WeatherEffect effect,
            out double entrySeconds,
            out double exitSeconds)
        {
            entrySeconds = 0d;
            exitSeconds = 0d;

            Vector3D offset = effect.Position - position;
            Vector3D velocity = effect.Velocity;
            double radius = effect.Radius;
            double a = velocity.LengthSquared();
            double c = offset.LengthSquared() - radius * radius;

            if (a <= 1e-8)
            {
                if (c > 0d)
                    return false;

                exitSeconds = GetRemainingWeatherLifetimeSeconds(effect);
                return exitSeconds > 0d || double.IsPositiveInfinity(exitSeconds);
            }

            double b = 2d * Vector3D.Dot(offset, velocity);
            double discriminant = b * b - 4d * a * c;
            if (discriminant < 0d)
                return false;

            double root = Math.Sqrt(discriminant);
            double t1 = (-b - root) / (2d * a);
            double t2 = (-b + root) / (2d * a);
            if (t2 < 0d)
                return false;

            entrySeconds = Math.Max(0d, t1);
            exitSeconds = Math.Max(0d, t2);

            double remainingLifetime = GetRemainingWeatherLifetimeSeconds(effect);
            if (entrySeconds > remainingLifetime)
                return false;

            exitSeconds = Math.Min(exitSeconds, remainingLifetime);
            return exitSeconds >= entrySeconds;
        }

        static double GetRemainingWeatherLifetimeSeconds(MyObjectBuilder_WeatherEffect effect)
        {
            if (effect.MaxLife <= 0)
                return double.PositiveInfinity;

            return Math.Max(0, effect.MaxLife - effect.Life) / 60d;
        }

        static string ResolveWeatherDisplayName(string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype) ||
                string.Equals(subtype, "Clear", StringComparison.OrdinalIgnoreCase))
                return ClockDashboardLocalization.ClearWeather;

            var definition = MyDefinitionManager.Static != null
                ? MyDefinitionManager.Static.GetWeatherEffect(subtype)
                : null;
            return definition != null && !string.IsNullOrWhiteSpace(definition.DisplayNameText)
                ? definition.DisplayNameText
                : ClockDashboardFormatter.PrettifySubtype(subtype);
        }

        static void ReadWind(Vector3D position, MyPlanet planet, ClockDashboardSnapshot snapshot)
        {
            if (planet == null)
                return;

            try
            {
                float speed = Math.Max(0f, planet.GetWindSpeed(position));
                var weatherApi = MyAPIGateway.Session != null ? MyAPIGateway.Session.WeatherEffects : null;
                if (weatherApi != null)
                    speed *= Math.Max(0f, weatherApi.GetWindMultiplier(position));

                snapshot.WindSpeed = speed;
                snapshot.HasWindSpeed = true;
            }
            catch
            {
                snapshot.HasWindSpeed = false;
                snapshot.WindSpeed = 0f;
            }
        }

        static void ReadGridRoomEnvironment(
            IMyCubeBlock block,
            GridLogic gridLogic,
            ClockDashboardSnapshot previous,
            ClockDashboardSnapshot snapshot)
        {
            float ambientTemperature = snapshot.HasAmbientTemperature
                ? snapshot.AmbientTemperatureNormalized
                : 0.5f;

            snapshot.OxygenRatio = 0f;
            snapshot.InteriorTemperatureNormalized = ambientTemperature;
            snapshot.InteriorTemperatureLevel = TemperatureToLevel(ambientTemperature);
            snapshot.HasInteriorTemperature = snapshot.HasAmbientTemperature;

            GridRoomEnvironmentSample roomSample;
            if (gridLogic == null ||
                !gridLogic.TryGetGridRoomEnvironment(block, out roomSample))
            {
                RestorePreviousGridRoomEnvironment(previous, snapshot);
                return;
            }

            float sealedOxygen = MathHelper.Clamp(roomSample.OxygenRatio, 0f, 1f);
            float environmentOxygen = ReadEnvironmentOxygen(block.GetPosition());
            snapshot.OxygenRatio = roomSample.IsSealed
                ? sealedOxygen
                : environmentOxygen;

            // Space Engineers moves effective character temperature toward Cozy
            // only in a sealed room, using the authoritative internal oxygen level.
            float pressurization = roomSample.IsSealed ? sealedOxygen : 0f;
            float interiorTemperature = MathHelper.Lerp(
                ambientTemperature,
                0.5f,
                pressurization);
            snapshot.InteriorTemperatureNormalized = MathHelper.Clamp(interiorTemperature, 0f, 1f);
            snapshot.InteriorTemperatureLevel = TemperatureToLevel(
                snapshot.InteriorTemperatureNormalized);
            snapshot.HasInteriorTemperature = snapshot.HasAmbientTemperature || pressurization >= 1f;
        }

        static float ReadEnvironmentOxygen(Vector3D position)
        {
            try
            {
                var oxygenProvider = MyAPIGateway.Session != null
                    ? MyAPIGateway.Session.OxygenProviderSystem
                    : null;
                return oxygenProvider != null
                    ? MathHelper.Clamp(oxygenProvider.GetOxygenInPoint(position), 0f, 1f)
                    : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        static void RestorePreviousGridRoomEnvironment(
            ClockDashboardSnapshot previous,
            ClockDashboardSnapshot snapshot)
        {
            if (previous == null)
                return;

            snapshot.OxygenRatio = previous.OxygenRatio;
            snapshot.InteriorTemperatureNormalized = previous.InteriorTemperatureNormalized;
            snapshot.InteriorTemperatureLevel = previous.InteriorTemperatureLevel;
            snapshot.HasInteriorTemperature = previous.HasInteriorTemperature;
        }

        static void ReadTemperature(Vector3D position, MyPlanet planet, ClockDashboardSnapshot snapshot)
        {
            if (planet != null && planet.Generator != null)
            {
                snapshot.PlanetClimate = planet.Generator.DefaultSurfaceTemperature;
                snapshot.HasPlanetClimate = true;
            }

            try
            {
                float local = MathHelper.Clamp(MyVisualScriptLogicProvider.GetTemperatureInPoint(position), 0f, 1f);
                snapshot.AmbientTemperatureNormalized = local;
                snapshot.AmbientTemperatureLevel = TemperatureToLevel(local);
                snapshot.HasAmbientTemperature = true;
            }
            catch
            {
                snapshot.HasAmbientTemperature = false;
            }
        }

        static MyTemperatureLevel TemperatureToLevel(float temperature)
        {
            if (temperature < 0.125f)
                return MyTemperatureLevel.ExtremeFreeze;
            if (temperature < 0.375f)
                return MyTemperatureLevel.Freeze;
            if (temperature < 0.625f)
                return MyTemperatureLevel.Cozy;
            if (temperature < 0.875f)
                return MyTemperatureLevel.Hot;
            return MyTemperatureLevel.ExtremeHot;
        }

        static double GetSunRotationDayLengthSeconds()
        {
            try
            {
                return Math.Max(1d, MyVisualScriptLogicProvider.SunRotationGetDayLength());
            }
            catch
            {
                return 86400d;
            }
        }

        static void ReadTerrainSolarEvents(
            GridLogic gridLogic,
            MyPlanet planet,
            Vector3D rotationAxis,
            ClockDashboardSnapshot snapshot)
        {
            if (gridLogic == null || planet == null)
                return;

            bool hasSunrise;
            double sunriseHour;
            bool hasSunset;
            double sunsetHour;
            if (!gridLogic.TryGetTerrainSolarForecast(
                    planet,
                    rotationAxis,
                    out hasSunrise,
                    out sunriseHour,
                    out hasSunset,
                    out sunsetHour))
                return;

            snapshot.HasTerrainSunrise = hasSunrise;
            snapshot.TerrainSunriseHour = sunriseHour;
            snapshot.HasTerrainSunset = hasSunset;
            snapshot.TerrainSunsetHour = sunsetHour;
        }

        Vector3D ResolveSunRotationAxis(Vector3D sunDirection)
        {
            if (_sunRotationAxisInitialized)
                return _sunRotationAxis;

            Vector3D axis = Vector3D.Zero;
            try
            {
                var session = MyAPIGateway.Session;
                var sector = session?.GetSector();
                var environment = sector?.Environment;
                if (environment != null)
                {
                    Vector3 baseSunDirection;
                    Vector3.CreateFromAzimuthAndElevation(
                        environment.SunAzimuth,
                        environment.SunElevation,
                        out baseSunDirection);
                    axis = CalculateRotationAxis(baseSunDirection);
                }
            }
            catch
            {
                axis = Vector3D.Zero;
            }

            if (axis.LengthSquared() <= 1e-8)
                axis = CalculateFallbackRotationAxis(sunDirection);

            if (axis.LengthSquared() <= 1e-8)
                axis = Vector3D.Up;
            else
                axis.Normalize();

            _sunRotationAxis = axis;
            _sunRotationAxisInitialized = true;
            return _sunRotationAxis;
        }

        static Vector3D CalculateRotationAxis(Vector3D baseSunDirection)
        {
            if (baseSunDirection.LengthSquared() <= 1e-8)
                return Vector3D.Zero;

            baseSunDirection.Normalize();
            Vector3D reference = Math.Abs(Vector3D.Dot(baseSunDirection, Vector3D.Up)) > 0.95d
                ? Vector3D.Left
                : Vector3D.Up;
            Vector3D axis = Vector3D.Cross(
                Vector3D.Cross(baseSunDirection, reference),
                baseSunDirection);
            if (axis.LengthSquared() <= 1e-8)
                return Vector3D.Zero;

            axis.Normalize();
            return axis;
        }

        static Vector3D CalculateFallbackRotationAxis(Vector3D sunDirection)
        {
            if (sunDirection.LengthSquared() <= 1e-8)
                return Vector3D.Up;

            sunDirection.Normalize();
            Vector3D reference = Math.Abs(Vector3D.Dot(sunDirection, Vector3D.Up)) > 0.95d
                ? Vector3D.Left
                : Vector3D.Up;
            Vector3D axis = Vector3D.Cross(Vector3D.Cross(sunDirection, reference), sunDirection);
            if (axis.LengthSquared() <= 1e-8)
                return Vector3D.Up;

            axis.Normalize();
            return axis;
        }
    }
}
