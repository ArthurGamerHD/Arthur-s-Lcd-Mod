using System;

namespace LcdMod.Common.Helpers
{
    public static class Constants
    {
        public const string CONFIG_FILE = "LcdMod.local.xml";
        public static Guid StorageGuid = new Guid("9a502d67-7a3c-4502-b3e5-a44e76c0acfa");
        public static Guid StorageRemapGuid = new Guid("C08C59FB-B74E-46F6-A477-26909D107E86");
        public static ushort Port = 46541;
    }
}