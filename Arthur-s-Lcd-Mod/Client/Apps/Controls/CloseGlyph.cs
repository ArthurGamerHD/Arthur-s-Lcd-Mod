using System;
using Adk.Gui.Controls.Chrome;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    /// <summary>The window close glyph, centred in its bounds, for clear and remove buttons.</summary>
    internal sealed class CloseGlyph : VisualElement
    {
        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<CloseGlyph, Color>(nameof(Foreground), Color.White, UiInvalidation.Render);

        public CloseGlyph()
        {
            SetDynamicResource(ForegroundProperty, "onSurfaceColor");
        }

        public Color Foreground
        {
            get { return GetValue(ForegroundProperty); }
            set { SetValue(ForegroundProperty, value); }
        }

        protected override void DrawSelf(RenderContext context)
        {
            float side = Math.Min(Bounds.Width, Bounds.Height);
            TextureSource texture;
            if (side <= 0f || !CaptionGlyphs.TryGet(context, CaptionGlyph.Close, false, out texture))
                return;
            context.DrawTexture(texture, new RectangleF(Bounds.Center - new Vector2(side * .5f), new Vector2(side)), Foreground);
        }
    }
}
