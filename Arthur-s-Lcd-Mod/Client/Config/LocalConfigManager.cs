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
        public static PlanetTextureQuality TextureQuality => PlanetTextureQualitySettings.Normalize(
            Config?.TextureQuality ?? PlanetTextureQuality.High);

        public static event Action<PlanetTextureQuality> TextureQualityChanged;
        
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
                if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(LcdMod.Common.Helpers.Constants.CONFIG_FILE, typeof(LocalConfigManager)))
                {
                    Config = new LcdModLocalConfig();
                }
                else
                {
                    using (var reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(LcdMod.Common.Helpers.Constants.CONFIG_FILE, typeof(LocalConfigManager)))
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

            Config.TextureQuality = PlanetTextureQualitySettings.Normalize(Config.TextureQuality);

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
            using (var writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(LcdMod.Common.Helpers.Constants.CONFIG_FILE, typeof(LocalConfigManager)))
                writer.Write(MyAPIGateway.Utilities.SerializeToXML(Config ?? new LcdModLocalConfig()));
        }

        internal static LcdModLocalConfig EnsureConfig()
        {
            if (Config == null)
                Config = new LcdModLocalConfig();

            return Config;
        }

        public static void SetTextureQuality(PlanetTextureQuality quality)
        {
            quality = PlanetTextureQualitySettings.Normalize(quality);
            if (Config == null)
                Config = new LcdModLocalConfig();

            PlanetTextureQuality previous = TextureQuality;
            if (previous == quality)
                return;

            Config.TextureQuality = quality;
            try
            {
                Save();
            }
            catch
            {
                Config.TextureQuality = previous;
                throw;
            }

            Action<PlanetTextureQuality> changed = TextureQualityChanged;
            if (changed != null)
                changed(quality);
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
        
        public static bool ClearCompletedFtueTip(string tipId)
        {
            if (Config?.CompletedFtueTips == null || Config.CompletedFtueTips.Count == 0)
                return false;

            var completedTips = new HashSet<string>(Config.CompletedFtueTips, StringComparer.Ordinal);
            if (!completedTips.Remove(tipId)) return false;
            Save();
            return true;
        }

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

    }
}
