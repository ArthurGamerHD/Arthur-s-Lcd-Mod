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

        public static readonly ResourceKey<Color> SuccessColor =
            ResourceKey.Register<Color>("successColor");

        public static readonly ResourceKey<Color> MutedTextColor =
            ResourceKey.Register<Color>("mutedTextColor");

        public static readonly ResourceKey<Color> DividerColor =
            ResourceKey.Register<Color>("dividerColor");

        public static readonly ResourceKey<Color> DisabledColor =
            ResourceKey.Register<Color>("disabledColor");

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

        public static readonly ResourceKey<Color> ScrollBarTrackColor =
            ResourceKey.Register<Color>("scrollBarTrackColor");

        public static readonly ResourceKey<Color> ScrollBarThumbColor =
            ResourceKey.Register<Color>("scrollBarThumbColor");

        public static readonly ResourceKey<Color> ScrollBarThumbHoverColor =
            ResourceKey.Register<Color>("scrollBarThumbHoverColor");

        public static readonly ResourceKey<Color> ScrollBarThumbPressedColor =
            ResourceKey.Register<Color>("scrollBarThumbPressedColor");

        public static readonly ResourceKey<float> LayoutScale =
            ResourceKey.Register<float>("layoutScale");

        public static readonly ResourceKey<float> FontScale =
            ResourceKey.Register<float>("fontScale");

        public static readonly ResourceKey<float> AutoScrollSecondsPerStep =
            ResourceKey.Register<float>("autoScrollSecondsPerStep");
        
        public static readonly ResourceKey<string> TextFont =
            ResourceKey.Register<string>("textFont");

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
                case Constants.SUCCESS:
                    return SuccessColor;
                case Constants.DISABLED_FOREGROUND:
                    return DisabledColor;
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
                case Constants.DISABLED_BACKGROUND:
                    return SurfaceContainerLowColor;
                case Constants.SHADOW:
                    return ShadowColor;
                default:
                    throw new ResourceKeyNotFoundException(role, "ThemeResources");
            }
        }
    }
}
