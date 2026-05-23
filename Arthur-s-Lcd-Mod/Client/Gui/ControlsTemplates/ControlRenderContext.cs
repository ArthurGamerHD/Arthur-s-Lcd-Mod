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
            : this(surface, scale, fontScale, new ControlStyle(textColor, panelColor), cursorPosition)
        {
        }

        public ControlRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            ControlStyle style,
            Vector2 cursorPosition)
        {
            Surface = surface;
            Scale = scale;
            FontScale = fontScale;
            Style = style ?? new ControlStyle(Color.White, Color.Gray);
            CursorPosition = cursorPosition;
        }

        public Sandbox.ModAPI.Ingame.IMyTextSurface Surface { get; private set; }
        public float Scale { get; private set; }
        public float FontScale { get; private set; }
        public ControlStyle Style { get; private set; }
        public Color TextColor => Style.TextColor;
        public Color PanelColor => Style.PanelColor;
        public Vector2 CursorPosition { get; private set; }
    }
}
