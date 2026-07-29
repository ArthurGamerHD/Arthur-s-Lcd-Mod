using System.Collections.Generic;

namespace LcdMod.Client.Config
{
    public sealed class LcdModLocalConfig
    {
        public LcdModLocalConfig()
        {
            LocalTextures = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            CompletedFtueTips = new HashSet<string>(System.StringComparer.Ordinal);
            RenderOtherUserTextures = true;
            AcceptMediaStreams = true;
            TextureQuality = PlanetTextureQuality.Ultra;
        }

        public bool AdvancedTweekables { get; set; }
        public bool RenderOtherUserTextures { get; set; }
        public bool DebugInteractive { get; set; }
        public bool DebugSurface { get; set; }
        public bool SpriteCountDebug { get; set; }
        public bool VisibleClip { get; set; }
        public bool UseLegacyLocalTextureStorage { get; set; }
        public bool AcceptMediaStreams { get; set; }
        public PlanetTextureQuality TextureQuality { get; set; }

        public HashSet<string> CompletedFtueTips { get; set; }

        // Legacy migration input for ZIP mode, and active runtime state when legacy storage is enabled.
        public HashSet<string> LocalTextures { get; set; }

        public bool ShouldSerializeCompletedFtueTips()
        {
            return CompletedFtueTips != null && CompletedFtueTips.Count > 0;
        }

        public bool ShouldSerializeLocalTextures()
        {
            return UseLegacyLocalTextureStorage && LocalTextures != null && LocalTextures.Count > 0;
        }
    }
}
