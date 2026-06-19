using LcdMod.Client.Helpers;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.ClockDashboard
{
    internal static class ClockDashboardLocalization
    {
        const string PREFIX = MOD_PREFIX + "ClockDashboard_";

        public const string TITLE_KEY = MOD_PREFIX + "InGameClockDashboard";
        public const string CONTROL_24_HOUR_TITLE_KEY = PREFIX + "Control_24Hour";
        public const string CONTROL_TEMPERATURE_TITLE_KEY = PREFIX + "Control_Temperature";
        public const string CONTROL_TEMPERATURE_TOOLTIP_KEY = PREFIX + "Control_Temperature_Tooltip";
        public const string TEMPERATURE_FUZZY_KEY = PREFIX + "Temperature_Fuzzy";
        public const string TEMPERATURE_CELSIUS_KEY = PREFIX + "Temperature_Celsius";
        public const string TEMPERATURE_KELVIN_KEY = PREFIX + "Temperature_Kelvin";
        public const string TEMPERATURE_FAHRENHEIT_KEY = PREFIX + "Temperature_Fahrenheit";

        public static string Unavailable => LocHelper.GetLoc(MOD_PREFIX + "Common_Value_Unavailable");
        public static string DeepSpace => Get("DeepSpace");
        public static string UnknownPlanet => Get("UnknownPlanet");
        public static string ClearWeather => Get("Weather_Clear");
        public static string UnknownWeather => Get("Weather_Unknown");

        public static string GetDayMoment(DayMoment moment)
        {
            switch (moment)
            {
                case DayMoment.Night:
                    return Get("DayMoment_Night");
                case DayMoment.Dawn:
                    return Get("DayMoment_Dawn");
                case DayMoment.Morning:
                    return Get("DayMoment_Morning");
                case DayMoment.Noon:
                    return Get("DayMoment_Noon");
                case DayMoment.Afternoon:
                    return Get("DayMoment_Afternoon");
                case DayMoment.Dusk:
                    return Get("DayMoment_Dusk");
                case DayMoment.NoLocalDayCycle:
                    return Get("DayMoment_NoLocalDay");
                case DayMoment.PolarDay:
                    return Get("DayMoment_PolarDay");
                default:
                    return Get("DayMoment_Unknown");
            }
        }

        static string Get(string suffix)
        {
            return LocHelper.GetLoc(PREFIX + suffix);
        }
    }
}
