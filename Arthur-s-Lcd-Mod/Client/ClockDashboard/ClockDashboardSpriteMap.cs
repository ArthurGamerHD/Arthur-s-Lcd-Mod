using System;

namespace LcdMod.Client.ClockDashboard
{
    internal static class ClockDashboardSpriteMap
    {
        public static string ResolveDayMomentIcon(DayMoment moment)
        {
            switch (moment)
            {
                case DayMoment.Night:
                    return "WeatherMoon";
                case DayMoment.Dawn:
                    return "WeatherSunRise";
                case DayMoment.Dusk:
                    return "WeatherSunSet";
                default:
                    return "WeatherSun";
            }
        }

        public static string ResolveWeatherIcon(string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype) ||
                subtype.Equals("Clear", StringComparison.OrdinalIgnoreCase))
                return "WeatherSun";

            string value = subtype.ToLowerInvariant();

            if (value.Contains("hail"))
                return "WeatherHailstorm";

            if (value.Contains("toxic") ||
                value.Contains("caustic") ||
                value.Contains("alienrain"))
                return "WeatherToxicRain";

            if (value.Contains("thunder") ||
                value.Contains("electric"))
                return "WeatherThunderStorm";

            if (value.Contains("rain"))
                return "WeatherRain";

            if (value.Contains("snow") ||
                value.Contains("cold"))
                return "WeatherSnow";

            if (value.Contains("fog") ||
                value.Contains("haze"))
                return "WeatherFog";

            if (value.Contains("sand") ||
                value.Contains("dust") ||
                value.Contains("marsstorm"))
                return "WeatherDustStorm";

            if (value.Contains("heat"))
                return "WeatherHeatWave";

            if (value.Contains("wind"))
                return "WeatherHeavyWind";

            return "Warning";
        }
    }
}
