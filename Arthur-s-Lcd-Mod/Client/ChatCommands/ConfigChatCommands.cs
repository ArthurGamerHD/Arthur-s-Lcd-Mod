using System;
using System.Collections.Generic;
using Generated;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;

namespace LcdMod.Client.ChatCommands
{
    internal static class ConfigChatCommands
    {
        /// <summary>
        /// Enables or disables advanced terminal tweakables.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_Advanced_Summary</loc>
        [ChatCommand("Advanced")]
        public static void SetAdvancedTweakables(bool enabled)
        {
            var config = LocalConfigManager.EnsureConfig();

            config.AdvancedTweekables = enabled;
            LocalConfigManager.Save();
            LcdModSessionComponent.LastSelectedBlock?.RefreshTerminal();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                LocalConfigManager.AdvancedTweakables
                    ? LocHelper.GetLoc("LcdMod_ChatCommand_Advanced_Enabled_Message")
                    : LocHelper.GetLoc("LcdMod_ChatCommand_Advanced_Disabled_Message"));
        }

        /// <summary>
        /// Enables, disables, or toggles rendering of textures owned by other users.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_RenderUserGeneratedTextures_Summary</loc>
        [ChatCommand("RenderUserGeneratedTextures")]
        public static void SetRenderUserGeneratedTextures(bool? enabled)
        {
            var config = LocalConfigManager.EnsureConfig();

            config.RenderOtherUserTextures = enabled ?? !LocalConfigManager.RenderOtherUserTextures;
            LocalConfigManager.Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                LocalConfigManager.RenderOtherUserTextures
                    ? LocHelper.GetLoc("LcdMod_ChatCommand_RenderUserGeneratedTextures_Enabled_Message")
                    : LocHelper.GetLoc("LcdMod_ChatCommand_RenderUserGeneratedTextures_Disabled_Message"));
        }

        /// <summary>
        /// Enables, disables, or toggles legacy local texture storage.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_LegacyLocalTextureStorage_Summary</loc>
        [ChatCommand("LegacyLocalTextureStorage")]
        public static void SetLegacyLocalTextureStorage(bool? enabled)
        {
            var config = LocalConfigManager.EnsureConfig();

            if (config.LocalTextures == null)
                config.LocalTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool useLegacyLocalTextureStorage = enabled ?? !LocalConfigManager.UseLegacyLocalTextureStorage;
            config.UseLegacyLocalTextureStorage = useLegacyLocalTextureStorage;
            TextureTransferHelper.UseLegacyLocalTextureStorage = useLegacyLocalTextureStorage;

            if (useLegacyLocalTextureStorage)
            {
                TextureHelper.EnableLegacyLocalTextureStorage();
            }
            else
            {
                var legacyLocalTextures = new HashSet<string>(config.LocalTextures, StringComparer.OrdinalIgnoreCase);
                config.LocalTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var localTexture in legacyLocalTextures)
                    TextureHelper.MigrateLegacyLocalTexture(localTexture);
            }

            LocalConfigManager.Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                LocalConfigManager.UseLegacyLocalTextureStorage
                    ? LocHelper.GetLoc("LcdMod_ChatCommand_LegacyLocalTextureStorage_Enabled_Message")
                    : LocHelper.GetLoc("LcdMod_ChatCommand_LegacyLocalTextureStorage_Disabled_Message"));
        }

#if DEBUG
        /// <summary>
        /// Enables, disables, or toggles interactive control-boundary debugging.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_DebugInteractive_Summary</loc>
        [ChatCommand("DebugInteractive")]
        public static void SetDebugInteractive(bool? enabled)
        {
            var config = LocalConfigManager.EnsureConfig();

            config.DebugInteractive = enabled ?? !LocalConfigManager.DebugInteractive;
            LocalConfigManager.Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                LocalConfigManager.DebugInteractive
                    ? LocHelper.GetLoc("LcdMod_ChatCommand_DebugInteractive_Enabled_Message")
                    : LocHelper.GetLoc("LcdMod_ChatCommand_DebugInteractive_Disabled_Message"));
        }

        /// <summary>
        /// Enables, disables, or toggles debug surface mode.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_DebugSurface_Summary</loc>
        [ChatCommand("DebugSurface")]
        public static void SetDebugSurface(bool? enabled)
        {
            var config = LocalConfigManager.EnsureConfig();

            config.DebugSurface = enabled ?? !LocalConfigManager.DebugSurface;
            LocalConfigManager.Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                LocalConfigManager.DebugSurface
                    ? LocHelper.GetLoc("LcdMod_ChatCommand_DebugSurface_Enabled_Message")
                    : LocHelper.GetLoc("LcdMod_ChatCommand_DebugSurface_Disabled_Message"));
        }

        /// <summary>
        /// Enables, disables, or toggles the sprite-count debug overlay.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_SpriteCountDebug_Summary</loc>
        [ChatCommand("SpriteCountDebug")]
        public static void SetSpriteCountDebug(bool? enabled)
        {
            var config = LocalConfigManager.EnsureConfig();

            config.SpriteCountDebug = enabled ?? !LocalConfigManager.SpriteCountDebug;
            LocalConfigManager.Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                LocalConfigManager.SpriteCountDebug
                    ? LocHelper.GetLoc("LcdMod_ChatCommand_SpriteCountDebug_Enabled_Message")
                    : LocHelper.GetLoc("LcdMod_ChatCommand_SpriteCountDebug_Disabled_Message"));
        }

        /// <summary>
        /// Enables, disables, or toggles visible clipping debug overlays.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_VisibleClip_Summary</loc>
        [ChatCommand("VisibleClip")]
        public static void SetVisibleClip(bool? enabled)
        {
            var config = LocalConfigManager.EnsureConfig();

            config.VisibleClip = enabled ?? !LocalConfigManager.VisibleClip;
            LocalConfigManager.Save();

            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                LocalConfigManager.VisibleClip
                    ? LocHelper.GetLoc("LcdMod_ChatCommand_VisibleClip_Enabled_Message")
                    : LocHelper.GetLoc("LcdMod_ChatCommand_VisibleClip_Disabled_Message"));
        }
#endif
    }
}
