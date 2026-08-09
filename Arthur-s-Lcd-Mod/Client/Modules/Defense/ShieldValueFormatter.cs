using System;

namespace LcdMod.Client.Modules.Defense
{
    internal static class ShieldValueFormatter
    {
        public static string Format(float value, string unit, bool useSiPrefixes)
        {
            string prefix = string.Empty;
            float displayValue = value;

            if (useSiPrefixes)
            {
                float absolute = Math.Abs(value);
                if (absolute >= 1000000000000f)
                {
                    displayValue = value / 1000000000000f;
                    prefix = "T";
                }
                else if (absolute >= 1000000000f)
                {
                    displayValue = value / 1000000000f;
                    prefix = "G";
                }
                else if (absolute >= 1000000f)
                {
                    displayValue = value / 1000000f;
                    prefix = "M";
                }
                else if (absolute >= 1000f)
                {
                    displayValue = value / 1000f;
                    prefix = "k";
                }
            }

            string number = useSiPrefixes && prefix.Length > 0
                ? displayValue.ToString("0.0")
                : displayValue.ToString(useSiPrefixes ? "0" : "0.##");
            return string.IsNullOrEmpty(unit) ? number : number + " " + prefix + unit;
        }
    }
}
