using System.Collections.Generic;

namespace LcdMod.Client.Config
{
    public sealed class LcdModLocalConfig
    {
        public LcdModLocalConfig()
        {
            LocalTextures = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            RenderOtherUserTextures = true;
        }

        public bool AdvancedTweekables { get; set; }
        public bool RenderOtherUserTextures { get; set; }
        public bool DebugInteractive { get; set; }
        public bool DebugSurface { get; set; }
        public bool SpriteCountDebug { get; set; }
        public bool VisibleClip { get; set; }
        public bool UseLegacyLocalTextureStorage { get; set; }

        // Legacy migration input for ZIP mode, and active runtime state when legacy storage is enabled.
        public HashSet<string> LocalTextures { get; set; }

        public bool ShouldSerializeLocalTextures()
        {
            return UseLegacyLocalTextureStorage && LocalTextures != null && LocalTextures.Count > 0;
        }
    }
}
