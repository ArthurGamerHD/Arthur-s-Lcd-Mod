using System;
using System.Globalization;
using Sandbox.Game.Gui;
using VRage.Game;
using VRageMath;

namespace Graph.Extensions
{
    public static class ColorExtensions
    {
        public static bool TryParseHexColor(string value, out Color color)
        {
            color = Color.White;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var hex = value.Trim();
            if (hex[0] == '#')
                hex = hex.Substring(1);

            if (hex.Length == 3)
                hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
            else if (hex.Length != 6)
                return false;

            byte r;
            byte g;
            byte b;
            if (!byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
                !byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
                !byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                return false;

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
        
        public static string ToHex(this Color? color)
        {
            if(color == null)
                color = Color.White;
            return ToHex(color.Value);
        }
        
        public static Color Invert(this Color color)
        { 
            return new Color(255 - color.R, 255 - color.G, 255 - color.B);
        }
        
        public static Color Invert(this Color? color)
        {
            if(color == null)
                color = Color.White;
            return Invert(color.Value);
        }
        
        public static Color MulSaturation(this Color color, float value)
        {
            var hsv = color.ColorToHSV();
            hsv.Y *= value;
            return hsv.HSVtoColor();
        }
        
        public static Color MulValue(this Color color, float value)
        {
            var hsv = color.ColorToHSV();
            hsv.Z *= value;
            return hsv.HSVtoColor();
        }
        
        public static Color DeriveAscentColor(this Color @base)
        {
            var hsv = @base.ColorToHSV();

            if (hsv.Y > 0.3f)
                hsv.Y = hsv.Y > 0.7f ? hsv.Y - 0.3f : hsv.Y + 0.3f;
            else
                hsv.Z = hsv.Z > 0.5f ? hsv.Z - 0.5f : hsv.Z + 0.5f;

            hsv.Y = MathHelper.Clamp(hsv.Y, 0f, 1f);
            hsv.Z = MathHelper.Clamp(hsv.Z, 0f, 1f);

            var color = hsv.HSVtoColor();
            color.A = @base.A;
            return color;
        }
    }
}
