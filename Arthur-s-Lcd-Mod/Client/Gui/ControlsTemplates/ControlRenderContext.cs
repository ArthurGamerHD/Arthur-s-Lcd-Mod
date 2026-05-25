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
            : this(surface, scale, fontScale, new ControlStyle(textColor, panelColor), null, cursorPosition)
        {
        }

        public ControlRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            ControlStyle style,
            Vector2 cursorPosition)
            : this(surface, scale, fontScale, style, null, cursorPosition)
        {
        }

        public ControlRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            ControlStyle style,
            IReadOnlyDictionary<string, Color> theme,
            Vector2 cursorPosition)
        {
            Surface = surface;
            Scale = scale;
            FontScale = fontScale;
            var resolvedStyle = style ?? new ControlStyle(Color.White, Color.Gray);
            Theme = resolvedStyle.ThemeColors ?? theme;
            Style = resolvedStyle.ResolveTheme(Theme);
            CursorPosition = cursorPosition;
        }

        public Sandbox.ModAPI.Ingame.IMyTextSurface Surface { get; private set; }
        public float Scale { get; private set; }
        public float FontScale { get; private set; }
        public ControlStyle Style { get; private set; }
        public IReadOnlyDictionary<string, Color> Theme { get; private set; }
        public Color TextColor => Style.GetTextColor(false);
        public Color PanelColor => Style.GetPanelColor(false);
        public Color HoverTextColor => Style.GetTextColor(true);
        public Color HoverPanelColor => Style.GetPanelColor(true);
        public Vector2 CursorPosition { get; private set; }

        public Color GetThemeColor(string role)
        {
            if (Theme == null || string.IsNullOrEmpty(role))
                throw new ResourceKeyNotFoundException(role, "Theme");

            Color color;
            if (!Theme.TryGetValue(role, out color))
                throw new ResourceKeyNotFoundException(role, "Theme");

            return color;
        }
    }
}
