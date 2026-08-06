using System.IO;
using Generated;
using LcdMod.Client.Helpers;
using LcdMod.Common.Helpers;
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
        /// Imports a local texture file.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_ImportLocalTexture_Summary</loc>
        [ChatCommand("ImportLocalTexture")]
        public static void ImportLocalTexture(string[] args)
        {
            if (args.Length == 1)
            {
                TextureHelper.ImportTexture(args[0], true);
            }
#if DEBUG
            else if (args.Length == 2)
            {
                TextureHelper.ForceLoadTexture(args[0], args[1]);
            }
#endif
            else
            {
                MyAPIGateway.Utilities.ShowNotification("Invalid argument");
            }
        }

        /// <summary>
        /// Removes a local texture so it is not loaded after restart.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_RemoveLocalTexture_Summary</loc>
        [ChatCommand("RemoveLocalTexture")]
        public static void RemoveLocalTexture(string file)
        {
            if (!string.IsNullOrWhiteSpace(file))
            {
                file = file.Trim();
                var baseName = Path.GetFileNameWithoutExtension(file);
                var sourceFileName = file;
                TextureTransferHelper.TextureMetadata metadata;
                if (TextureTransferHelper.TryReadTextureMetadata(baseName, out metadata) &&
                    metadata != null &&
                    !string.IsNullOrWhiteSpace(metadata.SourceFileName))
                {
                    sourceFileName = metadata.SourceFileName;
                }

                var removed = false;
                if (MyAPIGateway.Utilities.FileExistsInLocalStorage(sourceFileName, typeof(LcdModSessionComponent)))
                {
                    MyAPIGateway.Utilities.DeleteFileInLocalStorage(sourceFileName, typeof(LcdModSessionComponent));
                    removed = true;
                }

                removed = TextureTransferHelper.TryRemoveLocalTextureFile(sourceFileName) || removed;
                if (!removed)
                {
                    MyAPIGateway.Utilities.ShowNotification("Texture " + file + " not loaded");
                    return;
                }

                if (!string.IsNullOrEmpty(baseName))
                    TextureTransferHelper.TryRemoveTextureMetadata(baseName);

                MyAPIGateway.Utilities.ShowNotification(
                    "Texture " + file + " will not be loaded next time the game is restarted");
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("Invalid argument");
            }
        }

        /// <summary>
        /// Imports the texture files listed in import.txt.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_ImportTextures_Summary</loc>
        [ChatCommand("ImportTextures")]
        public static void ImportTextures()
        {
            TextureHelper.Import(true);
        }

        /// <summary>
        /// Clears cached remote textures while keeping local textures.
        /// </summary>
        /// <loc>LcdMod_ChatCommand_ClearCache_Summary</loc>
        [ChatCommand("ClearCache")]
        public static void ClearCache()
        {
            var deletedFiles = TextureTransferHelper.ClearCachedTextures();

            TextureHelper.ClearRuntimeTextureState();
            TextureHelper.LoadLocalTextures();

            LogHelper.LogInfo("Cleared remote texture cache (" + deletedFiles +
                              " files) and pending texture requests");
            MyAPIGateway.Utilities.ShowMessage(
                "lcdMod",
                LocHelper.GetLoc("LcdMod_ChatCommand_ClearCache_Completed_Message"));
        }
    }
}
