using System;
using System.Globalization;

namespace LcdMod.Client.Modules.Defense.Providers.Deflector
{
    internal struct DeflectorShieldCustomInfo
    {
        public float ShipCurrentPoints;
        public float ShipMaximumPoints;
        public float LocalCurrentPoints;
        public float LocalMaximumPoints;
        public float RechargePointsPerSecond;
        public float EffectivenessRatio;
        public bool HasShipCapacity;
        public bool HasLocalCapacity;
        public bool HasRecharge;
        public bool HasEffectiveness;
    }

    internal static class DeflectorShieldCustomInfoParser
    {
        const string SHIP_PREFIX = "Ship Shield:";
        const string LOCAL_PREFIX = "Local Shield:";
        const string RECHARGE_PREFIX = "Recharge:";
        const string EFFECTIVITY_PREFIX = "Effectivity:";

        public static bool TryParse(string customInfo, out DeflectorShieldCustomInfo result)
        {
            result = new DeflectorShieldCustomInfo();
            if (string.IsNullOrWhiteSpace(customInfo))
                return false;

            var lines = customInfo.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith(SHIP_PREFIX, StringComparison.Ordinal))
                {
                    result.HasShipCapacity = TryParsePointPair(
                        line.Substring(SHIP_PREFIX.Length),
                        out result.ShipCurrentPoints,
                        out result.ShipMaximumPoints);
                }
                else if (line.StartsWith(LOCAL_PREFIX, StringComparison.Ordinal))
                {
                    result.HasLocalCapacity = TryParsePointPair(
                        line.Substring(LOCAL_PREFIX.Length),
                        out result.LocalCurrentPoints,
                        out result.LocalMaximumPoints);
                }
                else if (line.StartsWith(RECHARGE_PREFIX, StringComparison.Ordinal))
                {
                    var value = line.Substring(RECHARGE_PREFIX.Length);
                    int unit = value.IndexOf("Pt/s", StringComparison.Ordinal);
                    if (unit >= 0)
                        value = value.Substring(0, unit);
                    result.HasRecharge = TryParseScaledValue(value, out result.RechargePointsPerSecond);
                }
                else if (line.StartsWith(EFFECTIVITY_PREFIX, StringComparison.Ordinal))
                {
                    result.HasEffectiveness = TryParsePercentage(line, out result.EffectivenessRatio);
                }
            }

            return result.HasShipCapacity || result.HasLocalCapacity;
        }

        static bool TryParsePointPair(string value, out float current, out float maximum)
        {
            current = 0f;
            maximum = 0f;
            int separator = value.IndexOf('/');
            if (separator < 0)
                return false;

            return TryParseScaledValue(RemovePointUnit(value.Substring(0, separator)), out current) &&
                   TryParseScaledValue(RemovePointUnit(value.Substring(separator + 1)), out maximum);
        }

        static string RemovePointUnit(string value)
        {
            value = value.Trim();
            int pointUnit = value.IndexOf("Pt", StringComparison.Ordinal);
            return pointUnit >= 0 ? value.Substring(0, pointUnit).Trim() : value;
        }

        internal static bool TryParseScaledValue(string value, out float result)
        {
            result = 0f;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            value = value.Trim();
            float multiplier = 1f;
            char suffix = value[value.Length - 1];
            switch (suffix)
            {
                case 'k': multiplier = 1000f; break;
                case 'M': multiplier = 1000000f; break;
                case 'G': multiplier = 1000000000f; break;
                case 'T': multiplier = 1000000000000f; break;
                default: suffix = '\0'; break;
            }

            if (suffix != '\0')
                value = value.Substring(0, value.Length - 1).Trim();

            float parsed;
            if (!TryParseNumber(value, out parsed) || float.IsNaN(parsed) || float.IsInfinity(parsed))
                return false;

            result = parsed * multiplier;
            return !float.IsInfinity(result) && !float.IsNaN(result);
        }

        static bool TryParsePercentage(string value, out float ratio)
        {
            ratio = 0f;
            int open = value.LastIndexOf('(');
            int percent = value.LastIndexOf('%');
            if (open < 0 || percent <= open)
                return false;

            float parsed;
            if (!TryParseNumber(value.Substring(open + 1, percent - open - 1).Trim(), out parsed))
                return false;

            ratio = parsed / 100f;
            if (ratio < 0f)
                ratio = 0f;
            else if (ratio > 1f)
                ratio = 1f;
            return true;
        }

        static bool TryParseNumber(string value, out float result)
        {
            const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
            // Effectivity uses the host culture while the value formatter uses a dot. Accept
            // decimal-comma output before invariant parsing can mistake it for a group separator.
            if (value.IndexOf(',') >= 0 && value.IndexOf('.') < 0 &&
                float.TryParse(value.Replace(',', '.'), styles, CultureInfo.InvariantCulture, out result))
                return true;
            if (float.TryParse(value, styles, CultureInfo.InvariantCulture, out result))
                return true;
            if (float.TryParse(value, styles, CultureInfo.CurrentCulture, out result))
                return true;
            return false;
        }
    }
}
