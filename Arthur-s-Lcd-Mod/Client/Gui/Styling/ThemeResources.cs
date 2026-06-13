using VRageMath;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Common.Helpers;

namespace LcdMod.Client.Gui.Styling
{
    public static class ThemeResources
    {
        public static readonly ResourceKey<Color> AccentColor =
            ResourceKey.Register<Color>("accentColor");

        public static readonly ResourceKey<Color> OnAccentColor =
            ResourceKey.Register<Color>("onAccentColor");

        public static readonly ResourceKey<Color> AccentContainerColor =
            ResourceKey.Register<Color>("accentContainerColor");

        public static readonly ResourceKey<Color> OnAccentContainerColor =
            ResourceKey.Register<Color>("onAccentContainerColor");

        public static readonly ResourceKey<Color> WarningColor =
            ResourceKey.Register<Color>("warningColor");

        public static readonly ResourceKey<Color> ErrorColor =
            ResourceKey.Register<Color>("errorColor");

        public static readonly ResourceKey<Color> BackgroundColor =
            ResourceKey.Register<Color>("backgroundColor");

        public static readonly ResourceKey<Color> FontColor =
            ResourceKey.Register<Color>("fontColor");

        public static readonly ResourceKey<Color> SurfaceColor =
            ResourceKey.Register<Color>("surfaceColor");

        public static readonly ResourceKey<Color> OnSurfaceColor =
            ResourceKey.Register<Color>("onSurfaceColor");

        public static readonly ResourceKey<Color> SurfaceVariantColor =
            ResourceKey.Register<Color>("surfaceVariantColor");

        public static readonly ResourceKey<Color> OnSurfaceVariantColor =
            ResourceKey.Register<Color>("onSurfaceVariantColor");

        public static readonly ResourceKey<Color> SurfaceContainerLowestColor =
            ResourceKey.Register<Color>("surfaceContainerLowestColor");

        public static readonly ResourceKey<Color> SurfaceContainerLowColor =
            ResourceKey.Register<Color>("surfaceContainerLowColor");

        public static readonly ResourceKey<Color> SurfaceContainerColor =
            ResourceKey.Register<Color>("surfaceContainerColor");

        public static readonly ResourceKey<Color> SurfaceContainerHighColor =
            ResourceKey.Register<Color>("surfaceContainerHighColor");

        public static readonly ResourceKey<Color> SurfaceContainerHighestColor =
            ResourceKey.Register<Color>("surfaceContainerHighestColor");

        public static readonly ResourceKey<Color> SecondaryContainerColor =
            ResourceKey.Register<Color>("secondaryContainerColor");

        public static readonly ResourceKey<Color> OnSecondaryContainerColor =
            ResourceKey.Register<Color>("onSecondaryContainerColor");

        public static readonly ResourceKey<Color> BorderColor =
            ResourceKey.Register<Color>("borderColor");

        public static readonly ResourceKey<Color> BorderVariantColor =
            ResourceKey.Register<Color>("borderVariantColor");

        public static readonly ResourceKey<Color> ShadowColor =
            ResourceKey.Register<Color>("shadowColor");

        public static ResourceKey<Color> FromThemeRole(string role)
        {
            switch (role)
            {
                case Constants.PRIMARY:
                    return AccentColor;
                case Constants.ON_PRIMARY:
                    return OnAccentColor;
                case Constants.PRIMARY_CONTAINER:
                    return AccentContainerColor;
                case Constants.PRIMARY_CONTAINER + Constants.HOVER:
                    return AccentContainerColor;
                case Constants.ON_PRIMARY_CONTAINER:
                    return OnAccentContainerColor;
                case Constants.TERTIARY:
                    return WarningColor;
                case Constants.ERROR:
                    return ErrorColor;
                case Constants.BACKGROUND:
                    return BackgroundColor;
                case Constants.ON_BACKGROUND:
                    return FontColor;
                case Constants.SURFACE:
                    return SurfaceColor;
                case Constants.SURFACE + Constants.HOVER:
                    return SurfaceContainerColor;
                case Constants.ON_SURFACE:
                    return OnSurfaceColor;
                case Constants.SURFACE_VARIANT:
                    return SurfaceVariantColor;
                case Constants.ON_SURFACE_VARIANT:
                    return OnSurfaceVariantColor;
                case Constants.SURFACE_CONTAINER_LOWEST:
                    return SurfaceContainerLowestColor;
                case Constants.SURFACE_CONTAINER_LOW:
                    return SurfaceContainerLowColor;
                case Constants.SURFACE_CONTAINER:
                    return SurfaceContainerColor;
                case Constants.SURFACE_CONTAINER_HIGH:
                    return SurfaceContainerHighColor;
                case Constants.SURFACE_CONTAINER_HIGHEST:
                    return SurfaceContainerHighestColor;
                case Constants.SECONDARY_CONTAINER:
                    return SecondaryContainerColor;
                case Constants.SECONDARY_CONTAINER + Constants.HOVER:
                    return SecondaryContainerColor;
                case Constants.ON_SECONDARY_CONTAINER:
                    return OnSecondaryContainerColor;
                case Constants.OUTLINE:
                    return BorderColor;
                case Constants.OUTLINE_VARIANT:
                    return BorderVariantColor;
                case Constants.SHADOW:
                    return ShadowColor;
                default:
                    throw new ResourceKeyNotFoundException(role, "ThemeResources");
            }
        }
    }
}
