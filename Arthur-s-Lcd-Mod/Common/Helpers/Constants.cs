// ReSharper disable RedundantUsingDirective
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

        public const string MOD_PREFIX = "LcdMod_";

        public const string CONFIG_FILE = "LcdMod.local.xml";
        public const string CACHED_TEXTURES_FILE = "cached_textures.xml";
        public const string CACHED_TEXTURES = "cached_textures.zip";
        public const string LOCAL_TEXTURES_FILE = "local_textures.xml";
        public const string LOCAL_TEXTURES = "local_textures.zip";
        // Read-only storage used by the inherited V0 config schema.
        public static Guid V0StorageGuid = new Guid("9a502d67-7a3c-4502-b3e5-a44e76c0acfa");

        // Active storage used by the componentized config schema.
        public static Guid StorageGuid = new Guid("5298e210-8c77-407d-83be-a8a3c2c5fd17");

        public static Guid StorageRemapGuid = new Guid("C08C59FB-B74E-46F6-A477-26909D107E86");

        public const ushort PORT = 46541;
        public const long WORKSHOP_ID = 3730092649L;
        public const string GITHUB = "https://github.com/ArthurGamerHD/Arthur-s-Lcd-Mod";

        public const string BSOD_TITLE_FALLBACK =
            "Your Station ran into a problem and needs to Restart. We're waiting for a while, and then we'll restart it for you";

        public const string BSOD_INFO1_FALLBACK = "For more information about this issue,";
        public const string BSOD_INFO2_FALLBACK = "Visit ";
        public const string BSOD_INFO3_FALLBACK = "or read this QR code.";
        public const string BSOD_INFO4_FALLBACK = "If you call a support person, give them this info:";
        public const string BSOD_INFO5_FALLBACK = "Exception code:";

        public const float MIN_SCREEN_HEIGHT_TO_WIDTH_RATIO = 0.2f;


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

        #region Config

        public const string GENERAL = "core.general";
        public const string COLORS = "core.colors";
        public const string INTERACTION = "core.interaction";
        public const string FILTERS = "data.filters";
        public const string BLOCKS = "data.blocks";
        public const string ITEMS = "data.items";
        public const string ITEM_DISPLAY = "view.items";
        public const string APP = "app.settings";
        public const string TABS = "app.tabs";

        // The slot, not the component CLR type, identifies the semantic use of a reference.
        public const string PROJECTOR_REFERENCE = "reference.projector";
        public const string DOCKABLE_REFERENCE = "reference.dockable";
        public const string RENDER_PROXY_REFERENCE = "reference.render-proxy-source";
        public const string VISIBLE_TREE_REFERENCE = "reference.visible-tree";

        #endregion
        
        public static Color ColorCorrection { get; set; } = new Color(175,185,200);
    }
}
