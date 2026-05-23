using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public delegate void InteractiveRenderHandler(ControlBase entry, ControlRenderContext context, List<MySprite> sprites);
    
    public sealed class ControlRenderContext
    {
        public ControlRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            Color textColor,
            Color panelColor,
            Vector2 cursorPosition)
        {
            Surface = surface;
            Scale = scale;
            FontScale = fontScale;
            TextColor = textColor;
            PanelColor = panelColor;
            CursorPosition = cursorPosition;
        }

        public Sandbox.ModAPI.Ingame.IMyTextSurface Surface { get; private set; }
        public float Scale { get; private set; }
        public float FontScale { get; private set; }
        public Color TextColor { get; private set; }
        public Color PanelColor { get; private set; }
        public Vector2 CursorPosition { get; private set; }
    }
}