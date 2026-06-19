using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LcdMod.Common.Config.Models.Apps;
using VRage.Game;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace LcdMod.Client.Helpers
{
    internal static class FormatingHelper
    {
        static readonly Dictionary<string, Vector2> FontSizeCache = new Dictionary<string, Vector2>();
        static readonly StringBuilder StringBuilderBuffer = new StringBuilder();

        public const char ELLIPSIS = '…';
        public static CultureInfo Culture => CultureInfo.CurrentUICulture;

        public static Vector2 GetSizeInPixel(string text, string font, float fontSize,
            IMyTextSurface surface)
        {
            Vector2 size;
            var key = text + font + fontSize;
            if (FontSizeCache.TryGetValue(key, out size)) return size;
            StringBuilderBuffer.Clear();
            StringBuilderBuffer.Append(text);
            size = surface.MeasureStringInPixels(StringBuilderBuffer, font, fontSize);
            FontSizeCache[key] = size;
            return size;
        }

        /// <summary>
        /// Measures UI text with the font resolved by the supplied control's style.
        /// The string-font overload remains available for non-UI callers such as games.
        /// </summary>
        public static Vector2 GetSizeInPixel(string text, ITextStyleProvider styleSource, float fontSize,
            IMyTextSurface surface)
        {
            if (styleSource == null)
                throw new ArgumentNullException("styleSource");

            var font = styleSource.ResolvedTextFont;
            return GetSizeInPixel(text, string.IsNullOrEmpty(font) ? "White" : font, fontSize, surface);
        }

        public static string FormatItemQty(double input)
        {
            if (input >= 1000000000)
                // Congratulations, you've successfully created a singularity
                return (input / 1000000000d).ToString("0.00", Culture) + "G";
            if (input >= 1000000)
                return (input / 1000000d).ToString("0.00", Culture) + "M";
            if (input >= 10000)
                return (input / 1000d).ToString("0.00", Culture) + "k";

            return input.ToString("0.##", Culture);
        }

        public static string FormatSpaceCredits(long credits)
        {
            var absolute = Math.Abs((double)credits);
            if (absolute >= 1000000d)
                return (credits / 1000000d).ToString("0.##", Culture) + "M";
            if (absolute >= 1000d)
                return (credits / 1000d).ToString("0.##", Culture) + "K";

            return credits.ToString(Culture);
        }

        public static string DistanceToString(float meters, string format = "0.##")
        {
            var distance = (double)meters;
            var abs = Math.Abs(distance);
            var sign = distance < 0d ? "-" : "";

            if (abs >= 299792458d)
                return sign + (abs / 299792458d).ToString(format, Culture) + " ls";
            if (abs >= 1000000000d)
                return sign + (abs / 1000000000d).ToString(format, Culture) + " Gm";
            if (abs >= 1000000d)
                return sign + (abs / 1000000d).ToString(format, Culture) + " Mm";
            if (abs >= 1000d)
                return sign + (abs / 1000d).ToString(format, Culture) + " km";
            if (abs >= 1d)
                return sign + abs.ToString(format, Culture) + " m";

            return sign + (abs * 100d).ToString(format, Culture) + " cm";
        }

        public static string GravityToString(double gravityG)
        {
            double metersPerSecondSquared = gravityG * 9.81d;
            return gravityG.ToString("0.##", Culture) + " g (" +
                   metersPerSecondSquared.ToString("0.##", Culture) + " m/s²)";
        }

        public static string WindToString(double wind)
        {
            var a = Math.Abs(wind);
            var sign = wind < 0 ? "-" : "";
            return sign + a.ToString("0.##", Culture) + "km/h";
        }

        public static string TemperatureToString(MyTemperatureLevel? level)
        {
            if (!level.HasValue)
                return LocHelper.GetLoc(MOD_PREFIX + "NotAvailable");
            return LocHelper.GetLoc("Temperature" + level.Value);
        }

        public static string TemperatureToString(
            float normalizedTemperature,
            MyTemperatureLevel fuzzyLevel,
            ClockDashboardTemperatureMode mode,
            string format = "0.#")
        {
            if (mode == ClockDashboardTemperatureMode.Fuzzy)
                return LocHelper.GetTemperatureLevelText(fuzzyLevel);

            float normalized = MathHelper.Clamp(
                float.IsNaN(normalizedTemperature) || float.IsInfinity(normalizedTemperature)
                    ? 0f
                    : normalizedTemperature,
                0f,
                1f);
            float kelvin = 270f + normalized * 50f;
            float celsius = kelvin - 273.15f;
            string prefix = normalizedTemperature <= 0f ? "<" : normalizedTemperature >= 1f ? ">" : "";

            switch (mode)
            {
                case ClockDashboardTemperatureMode.Kelvin:
                    return prefix + kelvin.ToString(format, Culture) + "K";
                case ClockDashboardTemperatureMode.Fahrenheit:
                    return prefix + (celsius * 9f / 5f + 32f).ToString(format, Culture) + "°F";
                default:
                    return prefix + celsius.ToString(format, Culture) + "°C";
            }
        }

        const float MW_TO_W_CONSTANT = 1000000.0f;

        public static string MegaWattsToString(float mw) => WattsToString(mw * MW_TO_W_CONSTANT);

        public static string MegaWattHoursToString(float mwh) => WattHoursToString(mwh * MW_TO_W_CONSTANT);

        public static string WattsToString(double watts) => WattsToString(watts, "0.##");

        public static string WattsToString(double watts, string format)
        {
            double a = Math.Abs(watts);
            string sign = watts < 0 ? "-" : "";

            if (a < 1e-12)
                return "0 W";

            if (a >= 1e24) // Keep up, two more 0's to go, and you reach type-II civilization
                return sign + (a / 1e24).ToString(format, Culture) + " YW";
            if (a >= 1e21) return sign + (a / 1e21).ToString(format, Culture) + " ZW";
            if (a >= 1e18) return sign + (a / 1e18).ToString(format, Culture) + " EW";
            if (a >= 1e15) return sign + (a / 1e15).ToString(format, Culture) + " PW";
            if (a >= 1e12) return sign + (a / 1e12).ToString(format, Culture) + " TW";
            if (a >= 1e9) return sign + (a / 1e9).ToString(format, Culture) + " GW";
            if (a >= 1e6) return sign + (a / 1e6).ToString(format, Culture) + " MW";
            if (a >= 1e3) return sign + (a / 1e3).ToString(format, Culture) + " kW";
            if (a >= 1.0) return sign + a.ToString(format, Culture) + " W";
            if (a >= 1e-3) return sign + (a / 1e-3).ToString(format, Culture) + " mW";
            if (a >= 1e-6) return sign + (a / 1e-6).ToString(format, Culture) + " uW";
            return sign + a.ToString(format, Culture) + " W";
        }

        public static string WattHoursToString(double wattsHour) => WattHoursToString(wattsHour, "0.##");

        public static string WattHoursToString(double wattsHour, string format)
        {
            double a = Math.Abs(wattsHour);
            string sign = wattsHour < 0 ? "-" : "";

            if (a < 1e-12)
                return "0 Wh";

            if (a >= 1e24) return sign + (a / 1e24).ToString(format, Culture) + " YWh";
            if (a >= 1e21) return sign + (a / 1e21).ToString(format, Culture) + " ZWh";
            if (a >= 1e18) return sign + (a / 1e18).ToString(format, Culture) + " EWh";
            if (a >= 1e15) return sign + (a / 1e15).ToString(format, Culture) + " PWh";
            if (a >= 1e12) return sign + (a / 1e12).ToString(format, Culture) + " TWh";
            if (a >= 1e9) return sign + (a / 1e9).ToString(format, Culture) + " GWh";
            if (a >= 1e6) return sign + (a / 1e6).ToString(format, Culture) + " MWh";
            if (a >= 1e3) return sign + (a / 1e3).ToString(format, Culture) + " kWh";
            if (a >= 1.0) return sign + a.ToString(format, Culture) + " Wh";
            if (a >= 1e-3) return sign + (a / 1e-3).ToString(format, Culture) + " mWh";
            if (a >= 1e-6)
                // what is that? an ant size generator?
                return sign + (a / 1e-6).ToString(format, Culture) + " uWh";
            return sign + a.ToString(format, Culture) + " Wh";
        }


        public static string NewtonForceToString(double newtons, string format = "0.##")
        {
            double a = Math.Abs(newtons);
            string sign = newtons < 0 ? "-" : "";

            if (a < 1e-12)
                return "0 N";

            if (a >= 1e24) return sign + (a / 1e24).ToString(format, Culture) + " YN";
            if (a >= 1e21) return sign + (a / 1e21).ToString(format, Culture) + " ZN";
            if (a >= 1e18) return sign + (a / 1e18).ToString(format, Culture) + " EN";
            if (a >= 1e15) return sign + (a / 1e15).ToString(format, Culture) + " PN";
            if (a >= 1e12) return sign + (a / 1e12).ToString(format, Culture) + " TN";
            if (a >= 1e9) return sign + (a / 1e9).ToString(format, Culture) + " GN";
            if (a >= 1e6) return sign + (a / 1e6).ToString(format, Culture) + " MN";
            if (a >= 1e3) return sign + (a / 1e3).ToString(format, Culture) + " kN";
            if (a >= 1e-3) return sign + (a / 1e-3).ToString(format, Culture) + " mN";
            if (a >= 1e-6) return sign + (a / 1e-6).ToString(format, Culture) + " uN";
            if (a >= 1e-9) return sign + (a / 1e-9).ToString(format, Culture) + " nN";
            return sign + a.ToString(format, Culture) + " N";
        }

        public static string PercentageToString(float f) => f.ToString("P0", Culture).Replace(" ", string.Empty);

        public static string TrimName(string value, int lenght = 8)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Length <= lenght + 2 || lenght < 5)
                return value.Length <= lenght - 1 ? value : value.Substring(0, lenght - 1) + ELLIPSIS;

            return value.Substring(0, lenght - 4) + ELLIPSIS + value.Substring(value.Length - 3, 3);
        }

        public static string FormatVector(Vector3D value, string format = "0.#") => string.Format(Culture,
            string.Format("{{0:{0}}}, {{1:{0}}}, {{2:{0}}}", format), value.X, value.Y, value.Z);

        public static string FormatBearing(MatrixD reference, Vector3D target)
        {
            var delta = target - reference.Translation;
            double distance = delta.Length();
            if (distance <= 2.5)
                return "0º | 0º | 0 m";

            double localX = Vector3D.Dot(delta, reference.Right);
            double localY = Vector3D.Dot(delta, reference.Up);
            double localZ = Vector3D.Dot(delta, reference.Forward);
            double horizontal = Math.Sqrt(localX * localX + localZ * localZ);
            double azimuth = MathHelper.ToDegrees(Math.Atan2(localX, localZ));
            if (azimuth < 0d)
                azimuth += 360d;

            double mark = MathHelper.ToDegrees(Math.Atan2(localY, horizontal));

            return azimuth.ToString("0", Culture) + "º | " +
                   mark.ToString("0", Culture) + "º | " +
                   DistanceToString((float)distance);
        }

        public static string FormatTimeHours(float hours)
        {
            if (float.IsNaN(hours) || float.IsInfinity(hours))
                hours = 0f;

            var ts = TimeSpan.FromHours(hours);

            if (ts.TotalDays > 365)
                return LocHelper.GetLoc("Unit_years");
            if ((int)ts.TotalHours >= 48)
                return
                    $"{ts.TotalDays:0} {LocHelper.GetLoc("Unit_days")} {ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            if ((int)ts.TotalMinutes > 60)
                return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return ts.TotalSeconds > 60 ? $"{ts.Minutes:D2}:{ts.Seconds:D2}" : $"{ts.Seconds}s";
        }

        public static float LineHeight(float scale, IMyTextSurface surface, string font = "White", string probe = "Ag")
            => GetSizeInPixel(probe, font, scale, surface).Y;

        /// <summary>
        /// Measures a UI line with the font resolved by the supplied control's style.
        /// </summary>
        public static float LineHeight(float scale, ITextStyleProvider styleSource, IMyTextSurface surface,
            string probe = "Ag")
            => GetSizeInPixel(probe, styleSource, scale, surface).Y;
    }
}
