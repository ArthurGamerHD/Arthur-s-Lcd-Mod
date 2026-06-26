using System.Collections.Generic;

namespace LcdMod.Client.Config
{
    public sealed class LcdModLocalConfig
    {
        public LcdModLocalConfig()
        {
            LocalTextures = new HashSet<string>();
            RenderOtherUserTextures = true;
        }

        public bool AdvancedTweekables { get; set; }
        public bool RenderOtherUserTextures { get; set; }
        public bool DebugInteractive { get; set; }
        public bool DebugSurface { get; set; }
        public bool SpriteCountDebug { get; set; }
        public bool VisibleClip { get; set; }

        // Legacy migration input only. Local textures are now discovered from local_textures.zip.
        // Keep this property deserializable so existing configs can be migrated, but never write it again.
        public HashSet<string> LocalTextures { get; set; }

        public bool ShouldSerializeLocalTextures()
        {
            return false;
        }
    }
}
