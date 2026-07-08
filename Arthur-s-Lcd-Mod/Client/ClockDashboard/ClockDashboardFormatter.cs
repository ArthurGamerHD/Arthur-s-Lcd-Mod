using LcdMod.Common.Config.Components;
using System;
using System.Globalization;
using System.Text;

namespace LcdMod.Client.ClockDashboard
{
    internal static class ClockDashboardFormatter
    {
        const float TEMPERATURE_MIN_KELVIN = 270f;
        const float TEMPERATURE_MAX_KELVIN = 320f;
        const float KELVIN_TO_CELSIUS_OFFSET = 273.15f;
        const double SECONDS_PER_DISPLAYED_DAY = 86400d;

        static readonly DateTime SpaceEngineersGameDateEpoch =
            new DateTime(2081, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static DateTime BuildDisplayDateTime(
            DateTime sessionGameDateTime,
            DashboardClockMode mode,
            bool hasLocalSolarTime,
            double localSolarHour,
            double dayLengthSeconds)
        {
            if (dayLengthSeconds <= 0d)
                return sessionGameDateTime;

            double elapsedSeconds = Math.Max(0d, (sessionGameDateTime - SpaceEngineersGameDateEpoch).TotalSeconds);
            double stardateCycle = elapsedSeconds / dayLengthSeconds;
            long localDayIndex = (long)Math.Floor(stardateCycle);
            double stardateDayFraction = ClockDashboardSolarTime.PositiveModulo(stardateCycle, 1d);
            double localDayFraction = mode == DashboardClockMode.LocalSolar && hasLocalSolarTime
                ? ClockDashboardSolarTime.PositiveModulo(localSolarHour, 24d) / 24d
                : stardateDayFraction;

            if (mode == DashboardClockMode.LocalSolar)
            {
                double fractionDelta = localDayFraction - stardateDayFraction;
                if (fractionDelta > 0.5d)
                    localDayIndex--;
                else if (fractionDelta < -0.5d)
                    localDayIndex++;
            }

            int totalSeconds = (int)Math.Floor(localDayFraction * 86400d);
            totalSeconds = ((totalSeconds % 86400) + 86400) % 86400;
            return SpaceEngineersGameDateEpoch.AddDays(localDayIndex).AddSeconds(totalSeconds);
        }

        public static string FormatCompactTime(DateTime value, ClockDashboardConfigComponent config)
        {
            bool use24 = config == null || config.Use24HourClock;
            return use24
                ? value.ToString("HH:mm", CultureInfo.CurrentCulture)
                : value.ToString("hh:mmtt", CultureInfo.CurrentCulture).ToLower(CultureInfo.CurrentCulture);
        }

        public static string FormatSolarEventTime(
            double localSolarHour,
            ClockDashboardConfigComponent config)
        {
            if (double.IsNaN(localSolarHour) || double.IsInfinity(localSolarHour))
                return ClockDashboardLocalization.Unavailable;

            localSolarHour = ClockDashboardSolarTime.PositiveModulo(localSolarHour, 24d);
            int totalMinutes = (int)Math.Round(localSolarHour * 60d) % (24 * 60);
            DateTime value = SpaceEngineersGameDateEpoch.Date.AddMinutes(totalMinutes);
            bool use24 = config == null || config.Use24HourClock;
            return use24
                ? value.ToString("HH:mm", CultureInfo.CurrentCulture)
                : value.ToString("h:mmtt", CultureInfo.CurrentCulture)
                    .ToLower(CultureInfo.CurrentCulture);
        }

        public static string FormatCompactDate(DateTime value)
        {
            return value.ToString("yyyy/MM/dd", CultureInfo.CurrentCulture);
        }

        public static string FormatShortWeekday(DateTime value)
        {
            return value.ToString("ddd", CultureInfo.CurrentCulture);
        }

        public static string FormatWindSpeed(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                value = 0f;

            return value.ToString("0", CultureInfo.CurrentCulture);
        }

        public static DateTime BuildIncomingArrivalDateTime(ClockDashboardSnapshot snapshot)
        {
            if (snapshot == null ||
                !snapshot.HasIncomingWeather ||
                double.IsNaN(snapshot.IncomingWeatherEtaSeconds) ||
                double.IsInfinity(snapshot.IncomingWeatherEtaSeconds) ||
                snapshot.IncomingWeatherEtaSeconds < 0d ||
                snapshot.DisplayDateTime == DateTime.MinValue)
            {
                return DateTime.MinValue;
            }

            double displaySeconds = snapshot.IncomingWeatherEtaSeconds;
            if (snapshot.SolarDayLengthSeconds > 0d)
            {
                displaySeconds *= SECONDS_PER_DISPLAYED_DAY /
                                  snapshot.SolarDayLengthSeconds;
            }

            return snapshot.DisplayDateTime.AddSeconds(displaySeconds);
        }

        public static string FormatIncomingArrival(
            ClockDashboardSnapshot snapshot,
            ClockDashboardConfigComponent config)
        {
            DateTime arrival = BuildIncomingArrivalDateTime(snapshot);
            return arrival == DateTime.MinValue
                ? string.Empty
                : FormatCompactTime(arrival, config);
        }

        public static string FormatDayMoment(DayMoment moment)
        {
            return ClockDashboardLocalization.GetDayMoment(moment);
        }

        public static string FormatTemperature(float value)
        {
            float normalized = MathHelperClamp01(value);
            float kelvin = TEMPERATURE_MIN_KELVIN + normalized * (TEMPERATURE_MAX_KELVIN - TEMPERATURE_MIN_KELVIN);
            float celsius = kelvin - KELVIN_TO_CELSIUS_OFFSET;
            string prefix = value <= 0f ? "<" : value >= 1f ? ">" : "";
            return prefix + celsius.ToString("0.#", CultureInfo.CurrentCulture) + " C";
        }

        public static string FormatOxygen(float value)
        {
            return MathHelperClamp01(value).ToString("P0", CultureInfo.CurrentCulture);
        }

        public static string PrettifySubtype(string subtype)
        {
            if (string.IsNullOrWhiteSpace(subtype))
                return string.Empty;

            var builder = new StringBuilder(subtype.Length + 8);
            for (int i = 0; i < subtype.Length; i++)
            {
                char c = subtype[i];
                if (i > 0 && (c == '_' || c == '-'))
                {
                    builder.Append(' ');
                    continue;
                }

                if (i > 0 && char.IsUpper(c) && char.IsLower(subtype[i - 1]))
                    builder.Append(' ');

                builder.Append(c);
            }

            return builder.ToString();
        }

        static float MathHelperClamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }
    }
}
