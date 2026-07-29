namespace LcdMod.Client.Config
{
    /// <summary>
    /// Client-local cap for the square sampling grid used to draw a planet.
    /// Ultra is zero so older config files that do not contain this setting
    /// preserve the previous unrestricted behavior.
    /// </summary>
    public enum PlanetTextureQuality
    {
        Ultra = 0,
        Poop = 1,
        Low = 2,
        Medium = 3,
        High = 4
    }

    public static class PlanetTextureQualitySettings
    {
        public static readonly PlanetTextureQuality[] Options =
        {
            PlanetTextureQuality.Poop,
            PlanetTextureQuality.Low,
            PlanetTextureQuality.Medium,
            PlanetTextureQuality.High,
            PlanetTextureQuality.Ultra
        };

        public static PlanetTextureQuality Normalize(PlanetTextureQuality quality)
        {
            switch (quality)
            {
                case PlanetTextureQuality.Poop:
                case PlanetTextureQuality.Low:
                case PlanetTextureQuality.Medium:
                case PlanetTextureQuality.High:
                case PlanetTextureQuality.Ultra:
                    return quality;
                default:
                    return PlanetTextureQuality.Ultra;
            }
        }

        public static int GetMaximumFaceSide(PlanetTextureQuality quality)
        {
            switch (Normalize(quality))
            {
                case PlanetTextureQuality.Poop:
                    return 64;
                case PlanetTextureQuality.Low:
                    return 128;
                case PlanetTextureQuality.Medium:
                    return 256;
                case PlanetTextureQuality.High:
                    return 512;
                default:
                    return 0;
            }
        }

        public static string GetLocalizationKey(PlanetTextureQuality quality)
        {
            switch (Normalize(quality))
            {
                case PlanetTextureQuality.Poop:
                    return "LcdMod_PlanetTextureQuality_VeryLow";
                case PlanetTextureQuality.Low:
                    return "LcdMod_PlanetTextureQuality_Low";
                case PlanetTextureQuality.Medium:
                    return "LcdMod_PlanetTextureQuality_Medium";
                case PlanetTextureQuality.High:
                    return "LcdMod_PlanetTextureQuality_High";
                default:
                    return "LcdMod_PlanetTextureQuality_Ultra";
            }
        }
    }
}
