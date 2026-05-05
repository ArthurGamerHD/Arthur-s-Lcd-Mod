using System;
using System.Globalization;
using VRage.Game;
using VRageMath;

namespace LcdMod.Client.Extensions
{
    public static class ColorExtensions
    {
        public static bool TryParseHexColor(string value, out Color color)
        {
            color = new Color(255, 255, 255);

            if (string.IsNullOrWhiteSpace(value))
                return false;

            var hex = value.Trim();

            if (hex[0] == '#')
                hex = hex.Substring(1);

            if (hex.Length == 3)
            {
                hex = string.Concat(
                    hex[0], hex[0],
                    hex[1], hex[1],
                    hex[2], hex[2]);
            }
            else if (hex.Length != 6)
            {
                return false;
            }

            byte r;
            byte g;
            byte b;
            if (!byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
                !byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
                !byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
            {
                return false;
            }

            color = new Color(r, g, b);
            return true;
        }
        
        public static Vector3 ToFactionColor(this Color color) =>
            MyColorPickerConstants.HSVToHSVOffset(color.ColorToHSV());

        public static bool TryParseHexFactionColor(string value, out Vector3 factionColor)
        {
            Color parsed;
            if (TryParseHexColor(value, out parsed))
            {
                factionColor = parsed.ToFactionColor();
                return true;
            }

            factionColor = Vector3.Zero;
            return false;
        }

        public static string ToHex(this Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        
        public static string ToAHex(this Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public static string ToHex(this Color? color)
        {
            return (color ?? new Color(255, 255, 255)).ToHex();
        }

        public static Color Invert(this Color color)
        {
            return new Color(
                (byte)(255 - color.R),
                (byte)(255 - color.G),
                (byte)(255 - color.B));
        }

        public static Color Invert(this Color? color)
        {
            return (color ?? new Color(255, 255, 255)).Invert();
        }
        
        public static Color MulSaturation(this Color color, double multiplier)
        {
            Vector3 hsv = color.ColorToHSV();
            hsv.Y = (float)MathHelper.Clamp(hsv.Y * multiplier, 0.0, 1.0);
            return hsv.HSVtoColor();
        }

        public static Color MulValue(this Color color, double multiplier)
        {
            Vector3 hsv = color.ColorToHSV();
            hsv.Z = (float)MathHelper.Clamp(hsv.Z * multiplier, 0.0, 1.0);

            return hsv.HSVtoColor();
        }

        /// <summary>
        /// Derives a same-hue color with maximum contrast against the base color.
        /// Best for text, icons, outlines, and readability.
        /// </summary>
        public static Color DeriveTextAccentColor(this Color @base)
        {
            Oklch oklch = @base.ToOklch();

            Color dark = FromOklchInGamut(0.04, oklch.C, oklch.H);
            Color light = FromOklchInGamut(0.96, oklch.C, oklch.H);

            double darkContrast = ContrastRatio(@base, dark);
            double lightContrast = ContrastRatio(@base, light);

            return lightContrast >= darkContrast ? light : dark;
        }

        
        public static Color DeriveAccentColor(
            this Color @base,
            float lightness = 1f,
            double minContrast = 3.0)
        {
            lightness = MathHelper.Clamp(lightness, 0f, 1f);

            Oklch oklch = @base.ToOklch();

            double baseL = MathHelper.Clamp(oklch.L, 0, 1);

            // Keep your original steering behavior:
            // 0.0 -> darkest
            // 0.5 -> original/base lightness
            // 1.0 -> brightest
            double preferredL = lightness < 0.5f
                ? MathHelper.Lerp(0, baseL, lightness * 2)
                : MathHelper.Lerp(baseL, 1, (lightness - 0.5f) * 2);

            preferredL = MathHelper.Clamp(preferredL, 0, 1);

            Color preferred = FromOklchInGamut(preferredL, oklch.C, oklch.H);

            if (ContrastRatio(preferred, @base) >= minContrast)
                return preferred;

            // Respect the user's requested direction.
            // If lightness is exactly neutral, choose the side opposite the base.
            bool preferLighter =
                lightness > 0.5f || baseL < lightness;

            Color result;
            if (TryFindContrastingAccent(
                    @base,
                    oklch,
                    preferredL,
                    preferLighter,
                    minContrast,
                    out result))
            {
                return result;
            }

            // Fallback: if the requested direction cannot produce enough contrast,
            // try the opposite direction.
            if (TryFindContrastingAccent(
                    @base,
                    oklch,
                    preferredL,
                    !preferLighter,
                    minContrast,
                    out result))
            {
                return result;
            }

            // Last resort: return the preferred color even if contrast is insufficient.
            return preferred;
        }
        
        private static bool TryFindContrastingAccent(
            Color background,
            Oklch oklch,
            double preferredL,
            bool lighter,
            double minContrast,
            out Color result)
        {
            double extremeL = lighter ? 1.0 : 0.0;

            Color extreme = FromOklchInGamut(extremeL, oklch.C, oklch.H);

            if (ContrastRatio(extreme, background) < minContrast)
            {
                result = extreme;
                return false;
            }

            double low;
            double high;

            if (lighter)
            {
                low = preferredL;
                high = 1.0;
            }
            else
            {
                low = 0.0;
                high = preferredL;
            }

            result = extreme;

            for (int i = 0; i < 24; i++)
            {
                double mid = (low + high) / 2.0;

                Color candidate = FromOklchInGamut(mid, oklch.C, oklch.H);

                bool passes = ContrastRatio(candidate, background) >= minContrast;

                if (passes)
                {
                    result = candidate;

                    // Move closer to the user's preferred value.
                    if (lighter)
                        high = mid;
                    else
                        low = mid;
                }
                else
                {
                    // Move farther away from the base/preferred color.
                    if (lighter)
                        low = mid;
                    else
                        high = mid;
                }
            }

            return true;
        }

        public static double ContrastRatio(Color a, Color b)
        {
            double l1 = RelativeLuminance(a);
            double l2 = RelativeLuminance(b);

            double lighter = Math.Max(l1, l2);
            double darker = Math.Min(l1, l2);

            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double RelativeLuminance(Color color)
        {
            double r = SrgbToLinear(color.R / 255.0);
            double g = SrgbToLinear(color.G / 255.0);
            double b = SrgbToLinear(color.B / 255.0);

            return 0.2126 * r +
                   0.7152 * g +
                   0.0722 * b;
        }

        private static double SrgbToLinear(double value)
        {
            value = MathHelper.Clamp(value, 0.0, 1.0);

            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        private static double LinearToSrgb(double value)
        {
            value = Math.Max(0.0, value);

            return value <= 0.0031308
                ? 12.92 * value
                : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
        }

        private struct Oklch
        {
            public readonly double L;
            public readonly double C;
            public readonly double H;

            public Oklch(double l, double c, double h)
            {
                L = l;
                C = c;
                H = h;
            }
        }

        private struct Oklab
        {
            public readonly double L;
            public readonly double A;
            public readonly double B;

            public Oklab(double l, double a, double b)
            {
                L = l;
                A = a;
                B = b;
            }
        }

        private struct RgbDouble
        {
            public readonly double R;
            public readonly double G;
            public readonly double B;

            public RgbDouble(double r, double g, double b)
            {
                R = r;
                G = g;
                B = b;
            }

            public bool IsInGamut =>
                R >= 0.0 && R <= 1.0 &&
                G >= 0.0 && G <= 1.0 &&
                B >= 0.0 && B <= 1.0;

            public Color ToColor()
            {
                return new Color(
                    (float)R,
                    (float)G,
                    (float)B);
            }

            public Color ToColorClamped()
            {
                return new Color(
                    (float)MathHelper.Clamp(R, 0.0, 1.0),
                    (float)MathHelper.Clamp(G, 0.0, 1.0),
                    (float)MathHelper.Clamp(B, 0.0, 1.0));
            }
        }

        private static Oklch ToOklch(this Color color)
        {
            Oklab lab = ToOklab(color);

            double c = Math.Sqrt(lab.A * lab.A + lab.B * lab.B);
            double h = Math.Atan2(lab.B, lab.A);

            return new Oklch(lab.L, c, h);
        }

        private static Oklab ToOklab(Color color)
        {
            double r = SrgbToLinear(color.R / 255.0);
            double g = SrgbToLinear(color.G / 255.0);
            double b = SrgbToLinear(color.B / 255.0);

            double l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
            double m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
            double s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;

            double lRoot = Cbrt(l);
            double mRoot = Cbrt(m);
            double sRoot = Cbrt(s);

            return new Oklab(
                0.2104542553 * lRoot + 0.7936177850 * mRoot - 0.0040720468 * sRoot,
                1.9779984951 * lRoot - 2.4285922050 * mRoot + 0.4505937099 * sRoot,
                0.0259040371 * lRoot + 0.7827717662 * mRoot - 0.8086757660 * sRoot
            );
        }

        private static Color FromOklchInGamut(double lightness, double chroma, double hue)
        {
            chroma = Math.Max(0.0, chroma);

            for (int i = 0; i < 24; i++)
            {
                RgbDouble candidate = OklchToRgb(new Oklch(lightness, chroma, hue));

                if (candidate.IsInGamut)
                    return candidate.ToColor();

                chroma *= 0.9;
            }

            return OklchToRgb(new Oklch(lightness, 0.0, hue)).ToColorClamped();
        }

        private static RgbDouble OklchToRgb(Oklch lch)
        {
            double a = lch.C * Math.Cos(lch.H);
            double b = lch.C * Math.Sin(lch.H);

            return OklabToRgb(new Oklab(lch.L, a, b));
        }

        private static RgbDouble OklabToRgb(Oklab lab)
        {
            double lRoot = lab.L + 0.3963377774 * lab.A + 0.2158037573 * lab.B;
            double mRoot = lab.L - 0.1055613458 * lab.A - 0.0638541728 * lab.B;
            double sRoot = lab.L - 0.0894841775 * lab.A - 1.2914855480 * lab.B;

            double l = lRoot * lRoot * lRoot;
            double m = mRoot * mRoot * mRoot;
            double s = sRoot * sRoot * sRoot;

            double rLinear = 4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
            double gLinear = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
            double bLinear = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

            return new RgbDouble(
                LinearToSrgb(rLinear),
                LinearToSrgb(gLinear),
                LinearToSrgb(bLinear));
        }

        private static double Cbrt(double value)
        {
#if NET5_0_OR_GREATER
        return Math.Cbrt(value);
#else
            if (value < 0.0)
                return -Math.Pow(-value, 1.0 / 3.0);

            return Math.Pow(value, 1.0 / 3.0);
#endif
        }
    }
}