using System;
using System.Collections.Generic;
using System.Globalization;
using LcdMod.Common.Helpers;
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
        /// Generates a theme from this color. "Inspired" in Google's Material Design https://m3.material.io/
        /// </summary>
        public static Dictionary<string, Color> ToTheme(this Color seed) => ToTheme(seed, false);

        /// <summary>
        /// Generates a Material-like light or dark theme from this color.
        /// </summary>
        public static Dictionary<string, Color> ToTheme(this Color seed, bool dark)
        {
            Oklch seedOklch = seed.ToOklch();

            // If the seed is near grayscale, choose a stable default accent hue.
            double hue = seedOklch.C < 0.0001
                ? Math.PI * 1.5
                : seedOklch.H;

            byte alpha = seed.A;

            // Material-style palette families using OKLCH chroma. Keep chroma tied
            // to the seed so deliberately muted faction/header colors stay muted.
            double seedChroma = ClampDouble(seedOklch.C, 0.0, 0.32);
            TonalPalette primary = new TonalPalette(
                hue,
                seedChroma,
                alpha);

            TonalPalette secondary = new TonalPalette(hue, ScaledSeedChroma(seedChroma, 0.45, 0.12), alpha);
            TonalPalette tertiary = new TonalPalette(
                WrapRadians(hue + Math.PI / 3.0),
                ScaledSeedChroma(seedChroma, 0.65, 0.16),
                alpha);
            TonalPalette neutral = new TonalPalette(hue, ScaledSeedChroma(seedChroma, 0.08, 0.015), alpha);
            TonalPalette neutralVariant = new TonalPalette(hue, ScaledSeedChroma(seedChroma, 0.18, 0.035), alpha);
            TonalPalette error = new TonalPalette(25.0 * Math.PI / 180.0, 0.22, alpha);

            Dictionary<string, Color> theme = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

            theme["seed"] = seed;

            if (dark)
            {
                AddDarkThemeRoles(theme, primary, secondary, tertiary, neutral, neutralVariant, error);
            }
            else
            {
                AddLightThemeRoles(theme, primary, secondary, tertiary, neutral, neutralVariant, error);
            }

            AddStateThemeRoles(theme);

            return theme;
        }

        static double ScaledSeedChroma(double seedChroma, double multiplier, double maxChroma)
        {
            return ClampDouble(seedChroma * multiplier, 0.0, maxChroma);
        }

        /// <summary>
        /// Same as ToTheme(), but returns hex strings for serialization/debugging.
        /// </summary>
        public static Dictionary<string, string> ToThemeHex(this Color seed)
        {
            return ToThemeHex(seed, false, true);
        }

        /// <summary>
        /// Same as ToTheme(dark, includeTonalPalettes), but returns hex strings for serialization/debugging.
        /// </summary>
        public static Dictionary<string, string> ToThemeHex(this Color seed, bool dark, bool includeTonalPalettes)
        {
            Dictionary<string, Color> colors = seed.ToTheme(dark);
            Dictionary<string, string> hex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, Color> item in colors)
            {
                hex[item.Key] = item.Value.ToAHex();
            }

            return hex;
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

        static bool TryFindContrastingAccent(
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

        static void AddLightThemeRoles(
            Dictionary<string, Color> theme,
            TonalPalette primary,
            TonalPalette secondary,
            TonalPalette tertiary,
            TonalPalette neutral,
            TonalPalette neutralVariant,
            TonalPalette error)
        {
            theme[Constants.PRIMARY] = primary.Tone(40);
            theme[Constants.ON_PRIMARY] = primary.Tone(100);
            theme[Constants.PRIMARY_CONTAINER] = primary.Tone(90);
            theme[Constants.ON_PRIMARY_CONTAINER] = primary.Tone(10);

            theme[Constants.SECONDARY] = secondary.Tone(40);
            theme[Constants.ON_SECONDARY] = secondary.Tone(100);
            theme[Constants.SECONDARY_CONTAINER] = secondary.Tone(90);
            theme[Constants.ON_SECONDARY_CONTAINER] = secondary.Tone(10);

            theme[Constants.TERTIARY] = tertiary.Tone(40);
            theme[Constants.ON_TERTIARY] = tertiary.Tone(100);
            theme[Constants.TERTIARY_CONTAINER] = tertiary.Tone(90);
            theme[Constants.ON_TERTIARY_CONTAINER] = tertiary.Tone(10);

            theme[Constants.ERROR] = error.Tone(40);
            theme[Constants.ON_ERROR] = error.Tone(100);
            theme[Constants.ERROR_CONTAINER] = error.Tone(90);
            theme[Constants.ON_ERROR_CONTAINER] = error.Tone(10);

            theme[Constants.BACKGROUND] = neutral.Tone(98);
            theme[Constants.ON_BACKGROUND] = neutral.Tone(10);

            theme[Constants.SURFACE] = neutral.Tone(98);
            theme[Constants.ON_SURFACE] = neutral.Tone(10);
            theme[Constants.SURFACE_VARIANT] = neutralVariant.Tone(90);
            theme[Constants.ON_SURFACE_VARIANT] = neutralVariant.Tone(30);

            theme[Constants.SURFACE_DIM] = neutral.Tone(87);
            theme[Constants.SURFACE_BRIGHT] = neutral.Tone(98);
            theme[Constants.SURFACE_CONTAINER_LOWEST] = neutral.Tone(100);
            theme[Constants.SURFACE_CONTAINER_LOW] = neutral.Tone(96);
            theme[Constants.SURFACE_CONTAINER] = neutral.Tone(94);
            theme[Constants.SURFACE_CONTAINER_HIGH] = neutral.Tone(92);
            theme[Constants.SURFACE_CONTAINER_HIGHEST] = neutral.Tone(90);

            theme[Constants.OUTLINE] = neutralVariant.Tone(50);
            theme[Constants.OUTLINE_VARIANT] = neutralVariant.Tone(80);

            theme[Constants.INVERSE_SURFACE] = neutral.Tone(20);
            theme[Constants.INVERSE_ON_SURFACE] = neutral.Tone(95);
            theme[Constants.INVERSE_PRIMARY] = primary.Tone(80);

            theme[Constants.SURFACE_TINT] = primary.Tone(40);
            theme[Constants.SHADOW] = neutral.Tone(0);
            theme[Constants.SCRIM] = neutral.Tone(0);

            theme[Constants.DISABLED_BACKGROUND] = Overlay(theme[Constants.SURFACE], theme[Constants.ON_SURFACE], 0.12);
            theme[Constants.DISABLED_FOREGROUND] = Overlay(theme[Constants.SURFACE], theme[Constants.ON_SURFACE], 0.38);
        }

        static void AddDarkThemeRoles(
            Dictionary<string, Color> theme,
            TonalPalette primary,
            TonalPalette secondary,
            TonalPalette tertiary,
            TonalPalette neutral,
            TonalPalette neutralVariant,
            TonalPalette error)
        {
            theme[Constants.PRIMARY] = primary.Tone(80);
            theme[Constants.ON_PRIMARY] = primary.Tone(20);
            theme[Constants.PRIMARY_CONTAINER] = primary.Tone(30);
            theme[Constants.ON_PRIMARY_CONTAINER] = primary.Tone(90);

            theme[Constants.SECONDARY] = secondary.Tone(80);
            theme[Constants.ON_SECONDARY] = secondary.Tone(20);
            theme[Constants.SECONDARY_CONTAINER] = secondary.Tone(30);
            theme[Constants.ON_SECONDARY_CONTAINER] = secondary.Tone(90);

            theme[Constants.TERTIARY] = tertiary.Tone(80);
            theme[Constants.ON_TERTIARY] = tertiary.Tone(20);
            theme[Constants.TERTIARY_CONTAINER] = tertiary.Tone(30);
            theme[Constants.ON_TERTIARY_CONTAINER] = tertiary.Tone(90);

            theme[Constants.ERROR] = error.Tone(80);
            theme[Constants.ON_ERROR] = error.Tone(20);
            theme[Constants.ERROR_CONTAINER] = error.Tone(30);
            theme[Constants.ON_ERROR_CONTAINER] = error.Tone(90);

            theme[Constants.BACKGROUND] = neutral.Tone(6);
            theme[Constants.ON_BACKGROUND] = neutral.Tone(90);

            theme[Constants.SURFACE] = neutral.Tone(6);
            theme[Constants.ON_SURFACE] = neutral.Tone(90);
            theme[Constants.SURFACE_VARIANT] = neutralVariant.Tone(30);
            theme[Constants.ON_SURFACE_VARIANT] = neutralVariant.Tone(80);

            theme[Constants.SURFACE_DIM] = neutral.Tone(6);
            theme[Constants.SURFACE_BRIGHT] = neutral.Tone(24);
            theme[Constants.SURFACE_CONTAINER_LOWEST] = neutral.Tone(4);
            theme[Constants.SURFACE_CONTAINER_LOW] = neutral.Tone(10);
            theme[Constants.SURFACE_CONTAINER] = neutral.Tone(12);
            theme[Constants.SURFACE_CONTAINER_HIGH] = neutral.Tone(17);
            theme[Constants.SURFACE_CONTAINER_HIGHEST] = neutral.Tone(22);

            theme[Constants.OUTLINE] = neutralVariant.Tone(60);
            theme[Constants.OUTLINE_VARIANT] = neutralVariant.Tone(30);

            theme[Constants.INVERSE_SURFACE] = neutral.Tone(90);
            theme[Constants.INVERSE_ON_SURFACE] = neutral.Tone(20);
            theme[Constants.INVERSE_PRIMARY] = primary.Tone(40);

            theme[Constants.SURFACE_TINT] = primary.Tone(80);
            theme[Constants.SHADOW] = neutral.Tone(0);
            theme[Constants.SCRIM] = neutral.Tone(0);

            theme[Constants.DISABLED_BACKGROUND] = Overlay(theme[Constants.SURFACE], theme[Constants.ON_SURFACE], 0.12);
            theme[Constants.DISABLED_FOREGROUND] = Overlay(theme[Constants.SURFACE], theme[Constants.ON_SURFACE], 0.38);
        }

        static void AddStateThemeRoles(Dictionary<string, Color> theme)
        {
            AddStateThemeRoles(theme, Constants.PRIMARY, Constants.ON_PRIMARY);
            AddStateThemeRoles(theme, Constants.PRIMARY_CONTAINER, Constants.ON_PRIMARY_CONTAINER);
            AddStateThemeRoles(theme, Constants.SECONDARY, Constants.ON_SECONDARY);
            AddStateThemeRoles(theme, Constants.SECONDARY_CONTAINER, Constants.ON_SECONDARY_CONTAINER);
            AddStateThemeRoles(theme, Constants.TERTIARY, Constants.ON_TERTIARY);
            AddStateThemeRoles(theme, Constants.TERTIARY_CONTAINER, Constants.ON_TERTIARY_CONTAINER);
            AddStateThemeRoles(theme, Constants.SURFACE, Constants.ON_SURFACE);
            AddStateThemeRoles(theme, Constants.SURFACE_VARIANT, Constants.ON_SURFACE_VARIANT);
            AddStateThemeRoles(theme, Constants.ERROR, Constants.ON_ERROR);
        }

        static void AddStateThemeRoles(Dictionary<string, Color> theme, string baseRole, string contentRole)
        {
            Color background = theme[baseRole];
            Color foreground = theme[contentRole];

            theme[baseRole + Constants.HOVER] = Overlay(background, foreground, 0.08);
            theme[baseRole + Constants.FOCUS] = Overlay(background, foreground, 0.10);
            theme[baseRole + Constants.ACTIVE] = Overlay(background, foreground, 0.10);
            theme[baseRole + Constants.PRESSED] = Overlay(background, foreground, 0.10);
            theme[baseRole + Constants.DRAGGED] = Overlay(background, foreground, 0.16);
        }

        static Color Overlay(Color background, Color foreground, double alpha)
        {
            alpha = MathHelper.Clamp(alpha, 0.0, 1.0);

            byte r = BlendByte(background.R, foreground.R, alpha);
            byte g = BlendByte(background.G, foreground.G, alpha);
            byte b = BlendByte(background.B, foreground.B, alpha);

            // Keep the resulting color in your Color(r,g,b,a) shape.
            // State colors are pre-blended/opaque, so alpha follows the background role.
            return new Color(r, g, b, background.A);
        }

        static byte BlendByte(byte background, byte foreground, double alpha)
        {
            double value = foreground * alpha + background * (1.0 - alpha);
            return ToByte(value / 255.0);
        }

        public static double ContrastRatio(this Color a, Color b)
        {
            double l1 = RelativeLuminance(a);
            double l2 = RelativeLuminance(b);

            double lighter = Math.Max(l1, l2);
            double darker = Math.Min(l1, l2);

            return (lighter + 0.05) / (darker + 0.05);
        }

        static double RelativeLuminance(Color color)
        {
            double r = SrgbToLinear(color.R / 255.0);
            double g = SrgbToLinear(color.G / 255.0);
            double b = SrgbToLinear(color.B / 255.0);

            return 0.2126 * r +
                   0.7152 * g +
                   0.0722 * b;
        }

        static double SrgbToLinear(double value)
        {
            value = MathHelper.Clamp(value, 0.0, 1.0);

            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        static double LinearToSrgb(double value)
        {
            value = Math.Max(0.0, value);

            return value <= 0.0031308
                ? 12.92 * value
                : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
        }

        sealed class TonalPalette
        {
            readonly double _hue;
            readonly double _chroma;
            readonly byte _alpha;
            readonly Dictionary<int, Color> _cache = new Dictionary<int, Color>();

            public TonalPalette(double hue, double chroma, byte alpha)
            {
                _hue = WrapRadians(hue);
                _chroma = Math.Max(0.0, chroma);
                _alpha = alpha;
            }

            public Color Tone(int tone)
            {
                tone = ClampInt(tone, 0, 100);

                Color cached;
                if (_cache.TryGetValue(tone, out cached))
                    return cached;

                Color color = FromOklchInGamut(tone / 100.0, _chroma, _hue);
                color = WithAlpha(color, _alpha);

                _cache[tone] = color;
                return color;
            }
        }

        struct Oklch
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

        struct Oklab
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

        struct RgbDouble
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

        static Oklch ToOklch(this Color color)
        {
            Oklab lab = ToOklab(color);

            double c = Math.Sqrt(lab.A * lab.A + lab.B * lab.B);
            double h = Math.Atan2(lab.B, lab.A);

            return new Oklch(lab.L, c, h);
        }

        static Oklab ToOklab(Color color)
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

        static Color FromOklchInGamut(double lightness, double chroma, double hue)
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

        static RgbDouble OklchToRgb(Oklch lch)
        {
            double a = lch.C * Math.Cos(lch.H);
            double b = lch.C * Math.Sin(lch.H);

            return OklabToRgb(new Oklab(lch.L, a, b));
        }

        static RgbDouble OklabToRgb(Oklab lab)
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

        static Color WithAlpha(Color color, byte alpha)
        {
            return new Color(color.R, color.G, color.B, alpha);
        }

        static int ClampInt(int value, int min, int max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        static double ClampDouble(double value, double min, double max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        static double WrapRadians(double radians)
        {
            double twoPi = Math.PI * 2.0;

            radians = radians % twoPi;

            if (radians < 0.0)
                radians += twoPi;

            return radians;
        }

        static byte ToByte(double normalized)
        {
            int value = (int)Math.Round(MathHelper.Clamp(normalized, 0.0, 1.0) * 255.0);

            if (value < 0)
                return 0;

            if (value > 255)
                return 255;

            return (byte)value;
        }

        static double Cbrt(double value)
        {
            if (value < 0.0)
                return -Math.Pow(-value, 1.0 / 3.0);

            return Math.Pow(value, 1.0 / 3.0);
        }
    }
}