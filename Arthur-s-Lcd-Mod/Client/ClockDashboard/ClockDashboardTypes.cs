using System;
using VRage.Game;

namespace LcdMod.Client.ClockDashboard
{
    internal enum DashboardClockMode
    {
        Default,
        LocalSolar,
        Polar,
        DeepSpace
    }

    internal enum DayMoment
    {
        Unknown,
        Night,
        Dawn,
        Morning,
        Noon,
        Afternoon,
        Dusk,
        NoLocalDayCycle,
        PolarDay
    }

    internal sealed class ClockDashboardSnapshot
    {
        public DateTime SessionGameDateTime;
        public DashboardClockMode ClockMode;
        public bool HasLocalSolarTime;
        public double LocalSolarHour;
        public DateTime DisplayDateTime;
        public double SolarDayLengthSeconds;
        public bool HasPlanet;
        public string PlanetName;
        public string WeatherSubtype;
        public string WeatherDisplayName;
        public float WeatherIntensity;
        public bool HasIncomingWeather;
        public string IncomingWeatherSubtype;
        public string IncomingWeatherDisplayName;
        public double IncomingWeatherEtaSeconds;
        public bool HasWindSpeed;
        public float WindSpeed;
        public DayMoment DayMoment;
        public double SolarElevationFactor;
        public bool HasTerrainSunrise;
        public double TerrainSunriseHour;
        public bool HasTerrainSunset;
        public double TerrainSunsetHour;
        public MyTemperatureLevel PlanetClimate;
        public bool HasPlanetClimate;
        public float AmbientTemperatureNormalized;
        public bool HasAmbientTemperature;
        public MyTemperatureLevel AmbientTemperatureLevel;
        public float InteriorTemperatureNormalized;
        public bool HasInteriorTemperature;
        public MyTemperatureLevel InteriorTemperatureLevel;
        public float OxygenRatio;
    }
}
