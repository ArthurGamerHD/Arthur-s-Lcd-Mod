using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    /// <summary>Two counter-rotating loading bars, the game's wait indicator.</summary>
    internal sealed class LoadingWheel : VisualElement
    {
        const string TEXTURE = "Screen_LoadingBar";

        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<LoadingWheel, Color>(nameof(Foreground), Color.White, UiInvalidation.Render);

        public Color Foreground
        {
            get { return GetValue(ForegroundProperty); }
            set { SetValue(ForegroundProperty, value); }
        }

        protected override void DrawSelf(RenderContext context)
        {
            float outer = Math.Min(Bounds.Width, Bounds.Height);
            if (outer <= 0f)
                return;
            var session = MyAPIGateway.Session;
            float rotation = (float)((session.GameplayFrameCounter / 60.0) * 2.4);
            float inner = outer * .6f;
            context.Texture(TEXTURE, new RectangleF(Bounds.Center - new Vector2(outer * .5f), new Vector2(outer)), Foreground, false, rotation);
            context.Texture(TEXTURE, new RectangleF(Bounds.Center - new Vector2(inner * .5f), new Vector2(inner)), Foreground, false, -rotation);
            context.MarkVolatile();
        }
    }
}
