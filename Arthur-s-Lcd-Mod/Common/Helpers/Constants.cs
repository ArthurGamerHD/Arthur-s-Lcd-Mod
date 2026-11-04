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

        public const int MAX_IMAGE_PAYLOAD_BYTES = 6 * 1024 * 1024;
        public const int MAX_GRID_IMAGE_PAYLOAD_BYTES = 24 * 1024 * 1024;
        public const int MAX_IMAGE_DIMENSION = 2048;

        /// <summary>Videos play and travel in chunks of about five seconds; the limit is per chunk, not per file.</summary>
        public const int MAX_VIDEO_CHUNK_BYTES = 8 * 1024 * 1024;
        public const int MAX_VIDEO_INDEX_BYTES = 2 * 1024 * 1024;

        /// <summary>Video stream segments below zero: the chunk index, then captions from a separate subtitle file.</summary>
        public const int VIDEO_MANIFEST_SEGMENT = -1;
        public const int VIDEO_SUBTITLE_SEGMENT = -2;

        public const string MOD_PREFIX = "LcdMod_";

        public const string CONFIG_FILE = "LcdMod.local.xml";
        public const string LOCAL_TEXTURES = "local_textures.zip";
        // Read-only storage used by the inherited V0 config schema.
        public static Guid V0StorageGuid = new Guid("9a502d67-7a3c-4502-b3e5-a44e76c0acfa");

        // Active storage used by the componentized config schema.
        public static Guid StorageGuid = new Guid("5298e210-8c77-407d-83be-a8a3c2c5fd17");

        public static Guid StorageRemapGuid = new Guid("C08C59FB-B74E-46F6-A477-26909D107E86");

        public static Guid GridImageManifestGuid = new Guid("53382c89-f012-4593-a96c-12c3bb89dbac");
        public static Guid GridImagePayloadGuid = new Guid("965fd5b6-df4d-499d-9f2c-51602451416f");

        // Grid snapshots (Integrity Monitor, Target Info), on the screen's block.
        public static Guid GridSnapshotGuid = new Guid("b3f0c6d2-5a41-4e8f-9d27-6c1e8a4b7f15");
        public const int MAX_GRID_SNAPSHOT_BYTES = 4 * 1024 * 1024;
        public const int MAX_BLOCK_SNAPSHOT_BYTES = 12 * 1024 * 1024;

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
        public const string CAMERA_REFERENCE = "reference.camera";
        public const string DOCKABLE_REFERENCE = "reference.dockable";
        public const string RENDER_PROXY_REFERENCE = "reference.render-proxy-source";
        public const string VISIBLE_TREE_REFERENCE = "reference.visible-tree";

        #endregion
        
        public static Color ColorCorrection { get; set; } = new Color(175,185,200);
    }
}
