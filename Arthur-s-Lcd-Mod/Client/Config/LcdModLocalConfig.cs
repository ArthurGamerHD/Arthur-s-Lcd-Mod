using System.Collections.Generic;

namespace LcdMod.Client.Config
{
    public sealed class LcdModLocalConfig
    {
        public LcdModLocalConfig()
        {
            CompletedFtueTips = new HashSet<string>(System.StringComparer.Ordinal);
            RenderOtherUserTextures = true;
            AcceptMediaStreams = true;
            TextureQuality = PlanetTextureQuality.Medium;
            CaptionButtons = Adk.Gui.Controls.CaptionButtonsPlacement.Left;
        }

        public bool AdvancedTweekables { get; set; }
        public bool RenderOtherUserTextures { get; set; }
        public bool DebugInteractive { get; set; }
        public bool DebugSurface { get; set; }
        public bool SpriteCountDebug { get; set; }
        public bool VisibleClip { get; set; }
        public bool AcceptMediaStreams { get; set; }
        public PlanetTextureQuality TextureQuality { get; set; }
        public Adk.Gui.Controls.CaptionButtonsPlacement CaptionButtons { get; set; }

        public HashSet<string> CompletedFtueTips { get; set; }

        public LcdMod.Common.Config.Components.MediaVisualizerInstanceSettings[] MediaVisualizerInstances { get; set; }

        public bool ShouldSerializeCompletedFtueTips()
        {
            return CompletedFtueTips != null && CompletedFtueTips.Count > 0;
        }

        public bool ShouldSerializeMediaVisualizerInstances()
        {
            return MediaVisualizerInstances != null;
        }
    }
}
