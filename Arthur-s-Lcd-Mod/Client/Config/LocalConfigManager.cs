using LcdMod.Client.Extensions;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.Config
{
    public static class LocalConfigManager
    {
        public static LcdModLocalConfig Config { get; private set; } = new LcdModLocalConfig();

        public static bool AdvancedTweakables => Config != null && Config.AdvancedTweekables;
        
#if DEBUG
        public static bool DebugInteractive => Config != null && Config.DebugInteractive;
        public static bool DebugSurface => Config != null && Config.DebugSurface;
        public static bool SpriteCountDebug => Config != null && Config.SpriteCountDebug;
        public static bool VisibleClip => Config != null && Config.VisibleClip;
#endif

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

            if (Config.LocalTextures == null)
                Config.LocalTextures = new System.Collections.Generic.HashSet<string>();

            foreach (var localTexture in Config.LocalTextures)
            {
                TextureHelper.LocalTexture(localTexture);
            }

            TextureHelper.LoadCachedTextures();

            TextureHelper.Import();
            
            TextureHelper.ExportConverter();
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

#if DEBUG
        public static void SetDebugInteractiveCommand(string[] args)
        {
            bool enabled;
            if (!TryParseOptionalBoolean(args, DebugInteractive, out enabled))
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Usage: /lcdmod debuginteractive [true|false]");
                return;
            }

            if (Config == null)
                Config = new LcdModLocalConfig();

            Config.DebugInteractive = enabled;
            Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                "DebugInteractive mode " + (DebugInteractive ? "enabled." : "disabled."));
        }

        public static void SetDebugSurfaceCommand(string[] args)
        {
            bool enabled;
            if (!TryParseOptionalBoolean(args, DebugSurface, out enabled))
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Usage: /lcdmod debugsurface [true|false]");
                return;
            }

            if (Config == null)
                Config = new LcdModLocalConfig();

            Config.DebugSurface = enabled;
            Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                "DebugSurface mode " + (DebugSurface ? "enabled." : "disabled."));
        }

        public static void SetSpriteCountDebugCommand(string[] args)
        {
            bool enabled;
            if (!TryParseOptionalBoolean(args, SpriteCountDebug, out enabled))
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Usage: /lcdmod spritecountdebug [true|false]");
                return;
            }

            if (Config == null)
                Config = new LcdModLocalConfig();

            Config.SpriteCountDebug = enabled;
            Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                "SpriteCountDebug mode " + (SpriteCountDebug ? "enabled." : "disabled."));
        }

        public static void SetVisibleClipCommand(string[] args)
        {
            bool enabled;
            if (!TryParseOptionalBoolean(args, VisibleClip, out enabled))
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Usage: /lcdmod visibleclip [true|false]");
                return;
            }

            if (Config == null)
                Config = new LcdModLocalConfig();

            Config.VisibleClip = enabled;
            Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                "VisibleClip mode " + (VisibleClip ? "enabled." : "disabled."));
        }
        
        
        static bool TryParseOptionalBoolean(string[] args, bool currentValue, out bool result)
        {
            result = !currentValue;
            if (args == null || args.Length == 0)
                return true;

            return args.Length == 1 && TryParseBoolean(args[0], out result);
        }
#endif
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
