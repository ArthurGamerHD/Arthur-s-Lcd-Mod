using System;

namespace LcdMod.Common.Helpers
{
    public static class Constants
    {
        public static Version Version => Generated.Constants.Version;
        public static string BuildConfiguration => Generated.Constants.BuildConfiguration;
        public static string VersionName => Generated.Constants.VersionName;

        public const string CONFIG_FILE = "LcdMod.local.xml";
        public static Guid StorageGuid = new Guid("9a502d67-7a3c-4502-b3e5-a44e76c0acfa");
        public static Guid StorageRemapGuid = new Guid("C08C59FB-B74E-46F6-A477-26909D107E86");

        public const ushort PORT = 46541;
        public const string GITHUB = "https://github.com/ArthurGamerHD/Arthur-s-Lcd-Mod";
        public const string BSOD_TITLE_FALLBACK = "Your Station ran into a problem and needs to Restart. We're waiting for a while, and then we'll restart it for you";
        public const string BSOD_INFO1_FALLBACK = "For more information about this issue,";
        public const string BSOD_INFO2_FALLBACK = "Visit ";
        public const string BSOD_INFO3_FALLBACK = "or read this QR code.";
        public const string BSOD_INFO4_FALLBACK = "If you call a support person, give them this info:";
        public const string BSOD_INFO5_FALLBACK = "Exception code:";
    }
}
