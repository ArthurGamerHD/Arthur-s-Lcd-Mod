using System;
using VRageMath;

namespace LcdMod.Common.Helpers
{
    public static class Constants
    {
        public static Version Version => Generated.Constants.Version;
        public static string BuildConfiguration => Generated.Constants.BuildConfiguration;
        public static string VersionName => Generated.Constants.VersionName;

        public const int MAX_TEXTURE_BYTES = 5330000; // this is the approximated size of 32 bits 1024x1024 texture in .dds
        public const int MAX_SYNC_TEXTURE_DIMENSION = 2048;
        
        public const uint DDS_HEADER_SIZE = 124;
        public const int DDS_MINIMUM_HEADER_BYTES = 20;
        
        public const string CONFIG_FILE = "LcdMod.local.xml";
        public const string CACHED_TEXTURES_FILE = "cached_textures.xml";
        public static Guid StorageGuid = new Guid("9a502d67-7a3c-4502-b3e5-a44e76c0acfa");
        public static Guid StorageRemapGuid = new Guid("C08C59FB-B74E-46F6-A477-26909D107E86");

        public const ushort PORT = 46541;
        public const string GITHUB = "https://github.com/ArthurGamerHD/Arthur-s-Lcd-Mod";

        public const string BSOD_TITLE_FALLBACK =
            "Your Station ran into a problem and needs to Restart. We're waiting for a while, and then we'll restart it for you";

        public const string BSOD_INFO1_FALLBACK = "For more information about this issue,";
        public const string BSOD_INFO2_FALLBACK = "Visit ";
        public const string BSOD_INFO3_FALLBACK = "or read this QR code.";
        public const string BSOD_INFO4_FALLBACK = "If you call a support person, give them this info:";
        public const string BSOD_INFO5_FALLBACK = "Exception code:";


        #region Theme Colors

        public const string PRIMARY = "primary";
        public const string ON_PRIMARY = "onPrimary";
        public const string PRIMARY_CONTAINER = "primaryContainer";
        public const string ON_PRIMARY_CONTAINER = "onPrimaryContainer";

        public const string SECONDARY = "secondary";
        public const string ON_SECONDARY = "onSecondary";
        public const string SECONDARY_CONTAINER = "secondaryContainer";
        public const string ON_SECONDARY_CONTAINER = "onSecondaryContainer";

        public const string TERTIARY = "tertiary";
        public const string ON_TERTIARY = "onTertiary";
        public const string TERTIARY_CONTAINER = "tertiaryContainer";
        public const string ON_TERTIARY_CONTAINER = "onTertiaryContainer";

        public const string ERROR = "error";
        public const string ON_ERROR = "onError";
        public const string ERROR_CONTAINER = "errorContainer";
        public const string ON_ERROR_CONTAINER = "onErrorContainer";

        public const string BACKGROUND = "background";
        public const string ON_BACKGROUND = "onBackground";

        public const string SURFACE = "surface";
        public const string ON_SURFACE = "onSurface";
        public const string SURFACE_VARIANT = "surfaceVariant";
        public const string ON_SURFACE_VARIANT = "onSurfaceVariant";

        public const string SURFACE_DIM = "surfaceDim";
        public const string SURFACE_BRIGHT = "surfaceBright";
        public const string SURFACE_CONTAINER_LOWEST = "surfaceContainerLowest";
        public const string SURFACE_CONTAINER_LOW = "surfaceContainerLow";
        public const string SURFACE_CONTAINER = "surfaceContainer";
        public const string SURFACE_CONTAINER_HIGH = "surfaceContainerHigh";
        public const string SURFACE_CONTAINER_HIGHEST = "surfaceContainerHighest";

        public const string OUTLINE = "outline";
        public const string OUTLINE_VARIANT = "outlineVariant";

        public const string INVERSE_SURFACE = "inverseSurface";
        public const string INVERSE_ON_SURFACE = "inverseOnSurface";
        public const string INVERSE_PRIMARY = "inversePrimary";

        public const string SURFACE_TINT = "surfaceTint";
        public const string SHADOW = "shadow";
        public const string SCRIM = "scrim";

        public const string SUCCESS = "success";

        public const string DISABLED_BACKGROUND = "disabledBackground";

        public const string DISABLED_FOREGROUND = "disabledForeground";

        public const string HOVER = "Hover";
        public const string FOCUS = "Focus";
        public const string ACTIVE = "Active";
        public const string PRESSED = "Pressed";
        public const string DRAGGED = "Dragged";

        #endregion

        public static Color ColorCorrection { get; set; } = new Color(175,185,200);
    }
}
