using System.Collections.Generic;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates
{
    public delegate void InteractiveRenderHandler(ControlTemplate entry, ControlRenderContext context, List<MySprite> sprites);
    
    public sealed class ControlRenderContext
    {
        public ControlRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            Vector2 cursorPosition,
            IVisualStyleScope styleScope)
        {
            Surface = surface;
            Scale = scale;
            FontScale = fontScale;
            CursorPosition = cursorPosition;
            StyleScope = styleScope;
            Style = new ControlStyle(Color.White, Color.Transparent);
        }

        public ControlRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            Color textColor,
            Color panelColor,
            Vector2 cursorPosition)
            : this(surface, scale, fontScale, new ControlStyle(textColor, panelColor), null, cursorPosition, null)
        {
        }

        public ControlRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            ControlStyle style,
            Vector2 cursorPosition)
            : this(surface, scale, fontScale, style, null, cursorPosition, null)
        {
        }

        public ControlRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            ControlStyle style,
            IReadOnlyDictionary<string, Color> theme,
            Vector2 cursorPosition)
            : this(surface, scale, fontScale, style, theme, cursorPosition, null)
        {
        }

        public ControlRenderContext(
            Sandbox.ModAPI.Ingame.IMyTextSurface surface,
            float scale,
            float fontScale,
            ControlStyle style,
            IReadOnlyDictionary<string, Color> theme,
            Vector2 cursorPosition,
            IVisualStyleScope styleScope)
        {
            Surface = surface;
            Scale = scale;
            FontScale = fontScale;
            var resolvedStyle = style ?? new ControlStyle(Color.White, Color.Gray);
            Theme = resolvedStyle.ThemeColors ?? theme;
            Style = resolvedStyle.ResolveTheme(Theme);
            CursorPosition = cursorPosition;
            StyleScope = styleScope;
        }

        public Sandbox.ModAPI.Ingame.IMyTextSurface Surface { get; private set; }
        public float Scale { get; private set; }
        public float FontScale { get; private set; }
        public ControlStyle Style { get; private set; }
        public IReadOnlyDictionary<string, Color> Theme { get; private set; }
        public IVisualStyleScope StyleScope { get; private set; }
        public Color TextColor => Style.GetTextColor(false);
        public Color PanelColor => Style.GetPanelColor(false);
        public Color HoverTextColor => Style.GetTextColor(true);
        public Color HoverPanelColor => Style.GetPanelColor(true);
        public Vector2 CursorPosition { get; private set; }

        public ControlRenderContext WithStyle(ControlStyle style)
        {
            return new ControlRenderContext(
                Surface,
                Scale,
                FontScale,
                style,
                Theme,
                CursorPosition,
                StyleScope);
        }

        public ControlRenderContext WithStyleScope(IVisualStyleScope styleScope)
        {
            return new ControlRenderContext(
                Surface,
                Scale,
                FontScale,
                Style,
                Theme,
                CursorPosition,
                styleScope);
        }


        public Color ResolveColor(ResourceKey<Color> key)
        {
            Color value;
            if (ScopedResourceResolver.TryResolve(StyleScope, key, out value))
                return value;

            throw new ResourceKeyNotFoundException(key.Name, "ResourceTree");
        }
    }
}
