using Generated;
using LcdMod.Client.Helpers;
using LcdMod.Client.Images;
using Sandbox.ModAPI;

namespace LcdMod.Client.ChatCommands
{
    internal static class TextureChatCommands
    {
        /// <summary>
        /// Preloads block textures as LCD sprites.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_PreloadTextures_Summary</loc>
        [ChatCommand("PreloadTextures")]
        public static void PreloadTextures()
        {
            TextureHelper.PreloadAllTextures();
        }

        /// <summary>
        /// Removes an image from the local image library.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_RemoveLocalTexture_Summary</loc>
        [ChatCommand("RemoveLocalTexture")]
        public static void RemoveLocalTexture(string name)
        {
            var library = LocalImageLibrary.Instance;
            MyAPIGateway.Utilities.ShowNotification(library != null && library.Remove(name)
                ? "Image " + name + " removed from your library"
                : "Image " + name + " not found in your library (or an import is still running)");
        }

        /// <summary>
        /// Imports new .png and .dds images from the mod storage folder.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_ImportTextures_Summary</loc>
        [ChatCommand("ImportTextures")]
        public static void ImportTextures()
        {
            LocalImageLibrary.Instance?.Rescan(true);
        }
    }
}
