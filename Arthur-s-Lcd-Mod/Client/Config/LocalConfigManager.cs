using System;
using System.Collections.Generic;
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
        public static bool RenderOtherUserTextures => Config == null || Config.RenderOtherUserTextures;
        public static bool UseLegacyLocalTextureStorage => Config != null && Config.UseLegacyLocalTextureStorage;
        public static bool AcceptMediaStreams => Config == null || Config.AcceptMediaStreams;
        
#if DEBUG
        public static bool DebugInteractive => Config != null && Config.DebugInteractive;
        public static bool DebugSurface => Config != null && Config.DebugSurface;
        public static bool SpriteCountDebug => Config != null && Config.SpriteCountDebug;
        public static bool VisibleClip => Config != null && Config.VisibleClip;
#endif

        public static void Load()
        {
            var hadLegacyLocalTextures = false;

            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(Constants.CONFIG_FILE, typeof(LocalConfigManager)))
                {
                    Config = new LcdModLocalConfig();
                }
                else
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(Constants.CONFIG_FILE, typeof(LocalConfigManager)))
                    {
                        var xml = reader.ReadToEnd();
                        hadLegacyLocalTextures = ContainsLegacyLocalTextures(xml);
                        Config = DeserializeConfig(xml);
                    }
                }
            }
            catch
            {
                Config = new LcdModLocalConfig();
            }

            var legacyLocalTextures = Config.LocalTextures == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(Config.LocalTextures, StringComparer.OrdinalIgnoreCase);

            TextureTransferHelper.UseLegacyLocalTextureStorage = UseLegacyLocalTextureStorage;

            if (UseLegacyLocalTextureStorage)
            {
                Config.LocalTextures = legacyLocalTextures;
                TextureHelper.LoadLegacyLocalTextures();
            }
            else
            {
                // LocalTextures is retained only as a one-time migration input in ZIP mode.
                // Runtime state is rebuilt from local_textures.zip and the property is not serialized.
                Config.LocalTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var localTexture in legacyLocalTextures)
                    TextureHelper.MigrateLegacyLocalTexture(localTexture);

                TextureHelper.LoadLocalTextures();
            }

            TextureHelper.LoadCachedTextures();

            TextureHelper.Import();
            TextureHelper.ExportConverter();

            if (hadLegacyLocalTextures && !UseLegacyLocalTextureStorage)
                Save();
        }

        public static void Save()
        {
            using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(Constants.CONFIG_FILE, typeof(LocalConfigManager)))
                writer.Write(MyAPIGateway.Utilities.SerializeToXML(Config ?? new LcdModLocalConfig()));
        }


        public static bool IsFtueTipCompleted(string tipId)
        {
            return !string.IsNullOrWhiteSpace(tipId) &&
                   Config != null &&
                   Config.CompletedFtueTips != null &&
                   Config.CompletedFtueTips.Contains(tipId);
        }

        public static void SetFtueTipCompleted(string tipId, bool completed)
        {
            if (string.IsNullOrWhiteSpace(tipId))
                return;

            if (Config == null)
                Config = new LcdModLocalConfig();

            if (Config.CompletedFtueTips == null)
                Config.CompletedFtueTips = new HashSet<string>(StringComparer.Ordinal);

            bool changed = completed
                ? Config.CompletedFtueTips.Add(tipId)
                : Config.CompletedFtueTips.Remove(tipId);

            if (!changed)
                return;

            try
            {
                Save();
            }
            catch
            {
                if (completed)
                    Config.CompletedFtueTips.Remove(tipId);
                else
                    Config.CompletedFtueTips.Add(tipId);

                throw;
            }
        }

        public static int ClearCompletedFtueTips()
        {
            if (Config == null)
                Config = new LcdModLocalConfig();

            if (Config.CompletedFtueTips == null || Config.CompletedFtueTips.Count == 0)
            {
                Config.CompletedFtueTips = new HashSet<string>(StringComparer.Ordinal);
                return 0;
            }

            var completedTips = new HashSet<string>(Config.CompletedFtueTips, StringComparer.Ordinal);
            Config.CompletedFtueTips.Clear();
            try
            {
                Save();
                return completedTips.Count;
            }
            catch
            {
                Config.CompletedFtueTips = completedTips;
                throw;
            }
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

        public static void RenderUserGeneratedTextures(string[] args)
        {
            bool enabled;
            if (!TryParseOptionalBoolean(args, RenderOtherUserTextures, out enabled))
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Usage: /lcdmod renderusergeneratedtextures [true|false]");
                return;
            }

            if (Config == null)
                Config = new LcdModLocalConfig();

            Config.RenderOtherUserTextures = enabled;
            Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                "Rendering textures owned by other users " + (RenderOtherUserTextures ? "enabled." : "disabled."));
        }

        public static void SetLegacyLocalTextureStorageCommand(string[] args)
        {
            bool enabled;
            if (!TryParseOptionalBoolean(args, UseLegacyLocalTextureStorage, out enabled))
            {
                MyAPIGateway.Utilities.ShowMessage("lcdMod", "Usage: /lcdmod legacylocaltexturestorage [true|false]");
                return;
            }

            if (Config == null)
                Config = new LcdModLocalConfig();

            if (Config.LocalTextures == null)
                Config.LocalTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Config.UseLegacyLocalTextureStorage = enabled;
            TextureTransferHelper.UseLegacyLocalTextureStorage = enabled;

            if (enabled)
            {
                TextureHelper.EnableLegacyLocalTextureStorage();
            }
            else
            {
                var legacyLocalTextures = new HashSet<string>(Config.LocalTextures, StringComparer.OrdinalIgnoreCase);
                Config.LocalTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var localTexture in legacyLocalTextures)
                    TextureHelper.MigrateLegacyLocalTexture(localTexture);
            }

            Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                "Legacy local texture storage " + (UseLegacyLocalTextureStorage ? "enabled." : "disabled.") +
                ". Restart the game if already loaded local textures do not refresh.");
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
#endif
        static bool ContainsLegacyLocalTextures(string xml)
        {
            return !string.IsNullOrWhiteSpace(xml) &&
                   xml.IndexOf("<LocalTextures", StringComparison.OrdinalIgnoreCase) >= 0;
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

        static bool TryParseOptionalBoolean(string[] args, bool currentValue, out bool result)
        {
            result = !currentValue;
            if (args == null || args.Length == 0)
                return true;

            return args.Length == 1 && TryParseBoolean(args[0], out result);
        }
    }
}
