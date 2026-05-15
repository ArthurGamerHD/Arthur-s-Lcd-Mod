using System;
using System.IO;
using LcdMod.Client.Extensions;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.Config
{
    public static class LocalConfigManager
    {
        public static LcdModLocalConfig Config { get; private set; } = new LcdModLocalConfig();

        public static bool AdvancedTweakables => Config != null && Config.AdvancedTweekables;

        public static void Load()
        {
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(Constants.CONFIG_FILE, typeof(LocalConfigManager)))
                {
                    Config = new LcdModLocalConfig();
                    return;
                }

                using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(Constants.CONFIG_FILE, typeof(LocalConfigManager)))
                    Config = DeserializeConfig(reader.ReadToEnd());
            }
            catch
            {
                Config = new LcdModLocalConfig();
            }
        }

        public static void Save()
        {
            using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(Constants.CONFIG_FILE, typeof(LocalConfigManager)))
                writer.Write(MyAPIGateway.Utilities.SerializeToXML(Config ?? new LcdModLocalConfig()));
        }

        public static void SetAdvancedTweakablesCommand(string[] args)
        {
            bool enabled;
            if (args == null || args.Length != 1 || !TryParseBoolean(args[0], out enabled))
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Usage: /lcdmod advanced <true|false>");
                return;
            }

            if (Config == null)
                Config = new LcdModLocalConfig();

            Config.AdvancedTweekables = enabled;
            Save();
            LcdModSessionComponent.LastSelectedBlock?.RefreshTerminal();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                "AdvancedTweekables mode " + (AdvancedTweakables ? "enabled." : "disabled."));
        }

        static LcdModLocalConfig DeserializeConfig(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return new LcdModLocalConfig();

            return MyAPIGateway.Utilities.SerializeFromXML<LcdModLocalConfig>(xml) ?? new LcdModLocalConfig();
        }

        static bool TryParseBoolean(string value, out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            switch (value.Trim().ToLowerInvariant())
            {
                case "true":
                case "on":
                case "yes":
                case "1":
                    result = true;
                    return true;
                case "false":
                case "off":
                case "no":
                case "0":
                    result = false;
                    return true;
                default:
                    return false;
            }
        }
    }
}
