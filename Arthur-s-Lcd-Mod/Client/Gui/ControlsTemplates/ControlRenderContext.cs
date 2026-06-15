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
        }

        public Sandbox.ModAPI.Ingame.IMyTextSurface Surface { get; private set; }
        public float Scale { get; private set; }
        public float FontScale { get; private set; }
        public IVisualStyleScope StyleScope { get; private set; }
        public Vector2 CursorPosition { get; private set; }

        public Color ResolveColor(ResourceKey<Color> key)
        {
            Color value;
            if (ScopedResourceResolver.TryResolve(StyleScope, key, out value))
                return value;

            throw new ResourceKeyNotFoundException(key.Name, "ResourceTree");
        }
    }
}
