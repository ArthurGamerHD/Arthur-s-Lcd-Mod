using System;
using System.Globalization;

namespace Graph.Helpers
{
    internal static class FormatingHelper
    {
        public static CultureInfo Culture => CultureInfo.CurrentUICulture;
        
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
        
        public static string DistanceToString(float meters)
        {
            var distance = (double)meters;
            var abs = Math.Abs(distance);
            var sign = distance < 0d ? "-" : "";

            if (abs >= 299792458d)
                return sign + (abs / 299792458d).ToString("0.##", Culture) + " ls";
            if (abs >= 1000000000d)
                return sign + (abs / 1000000000d).ToString("0.##", Culture) + " Gm";
            if (abs >= 1000000d)
                return sign + (abs / 1000000d).ToString("0.##", Culture) + " Mm";
            if (abs >= 1000d)
                return sign + (abs / 1000d).ToString("0.##", Culture) + " km";
            if (abs >= 1d)
                return sign + abs.ToString("0.##", Culture) + " m";

            return sign + (abs * 100d).ToString("0.##", Culture) + " cm";
        }
        
        public static string LitersToString(double liters)
        {
            var a = Math.Abs(liters);
            var sign = liters < 0 ? "-" : "";
            if (a >= 1000000.0) return sign + (a / 1000000.0).ToString("0.##", Culture) + " ML";
            if (a >= 1000.0) return sign + (a / 1000.0).ToString("0.##", Culture) + " kL";
            return sign + a.ToString("0.#", Culture) + " L";
        }

        public static string LittersPerSecondToString(double lps)
        {
            var a = Math.Abs(lps);
            var sign = lps < 0 ? "-" : "";
            if (a >= 1000000.0) return sign + (a / 1000000.0).ToString("0.##", Culture) + " ML/s";
            if (a >= 1000.0) return sign + (a / 1000.0).ToString("0.##", Culture) + " kL/s";
            return sign + a.ToString("0.#", Culture) + " L/s";
        }
        
        
        public static string WattsToString(double watts)
        {
            double a = Math.Abs(watts);
            string sign = watts < 0 ? "-" : "";

            if (a < 1e-12)
                return "0 W";

            if (a >= 1e24) return sign + (a / 1e24).ToString("0.##", Culture) + " YW";
            if (a >= 1e21) return sign + (a / 1e21).ToString("0.##", Culture) + " ZW";
            if (a >= 1e18) return sign + (a / 1e18).ToString("0.##", Culture) + " EW";
            if (a >= 1e15) return sign + (a / 1e15).ToString("0.##", Culture) + " PW";
            if (a >= 1e12) return sign + (a / 1e12).ToString("0.##", Culture) + " TW";
            if (a >= 1e9) return sign + (a / 1e9).ToString("0.##", Culture) + " GW";
            if (a >= 1e6) return sign + (a / 1e6).ToString("0.##", Culture) + " MW";
            if (a >= 1e3) return sign + (a / 1e3).ToString("0.##", Culture) + " kW";
            if (a >= 1.0) return sign + a.ToString("0.##", Culture) + " W";
            if (a >= 1e-3) return sign + (a / 1e-3).ToString("0.##", Culture) + " mW";
            if (a >= 1e-6) return sign + (a / 1e-6).ToString("0.##", Culture) + " uW";
            if (a >= 1e-9) return sign + (a / 1e-9).ToString("0.##", Culture) + " nW";
            if (a >= 1e-12) return sign + (a / 1e-12).ToString("0.##", Culture) + " pW";
            return sign + a.ToString("0.##", Culture) + " W";
        }


        public static string NewtonForceToString(double newtons)
        {
            double a = Math.Abs(newtons);
            string sign = newtons < 0 ? "-" : "";

            if (a < 1e-12)
                return "0 N";

            if (a >= 1e24) return sign + (a / 1e24).ToString("0.##", Culture) + " YN";
            if (a >= 1e21) return sign + (a / 1e21).ToString("0.##", Culture) + " ZN";
            if (a >= 1e18) return sign + (a / 1e18).ToString("0.##", Culture) + " EN";
            if (a >= 1e15) return sign + (a / 1e15).ToString("0.##", Culture) + " PN";
            if (a >= 1e12) return sign + (a / 1e12).ToString("0.##", Culture) + " TN";
            if (a >= 1e9) return sign + (a / 1e9).ToString("0.##", Culture) + " GN";
            if (a >= 1e6) return sign + (a / 1e6).ToString("0.##", Culture) + " MN";
            if (a >= 1e3) return sign + (a / 1e3).ToString("0.##", Culture) + " kN";
            if (a >= 1e-3) return sign + (a / 1e-3).ToString("0.##", Culture) + " mN";
            if (a >= 1e-6) return sign + (a / 1e-6).ToString("0.##", Culture) + " uN";
            if (a >= 1e-9) return sign + (a / 1e-9).ToString("0.##", Culture) + " nN";
            return sign + a.ToString("0.##", Culture) + " N";
        }


        public static string PercentageToString(float f) => f.ToString("P0", Culture).Replace(" ", string.Empty);
    }
}
