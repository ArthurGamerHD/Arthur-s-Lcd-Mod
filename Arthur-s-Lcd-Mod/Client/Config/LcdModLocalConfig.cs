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
        public HashSet<string> LocalTextures { get; set; }
    }
}
