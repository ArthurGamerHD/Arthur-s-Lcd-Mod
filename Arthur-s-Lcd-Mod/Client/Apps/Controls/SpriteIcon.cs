using System;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    internal sealed class SpriteIcon : VisualElement
    {
        public static readonly StyledProperty<string> SourceProperty =
            StyledProperty.Register<SpriteIcon, string>(nameof(Source), "MissingIcon", UiInvalidation.Render);
        public string Source { get { return GetValue(SourceProperty); } set { SetValue(SourceProperty, value); } }

        public static readonly StyledProperty<Color> TintProperty =
            StyledProperty.Register<SpriteIcon, Color>(nameof(Tint), Color.White, UiInvalidation.Render);
        public Color Tint { get { return GetValue(TintProperty); } set { SetValue(TintProperty, value); } }

        public static readonly StyledProperty<float> SizeRatioProperty =
            StyledProperty.Register<SpriteIcon, float>(nameof(SizeRatio), 0.8f, UiInvalidation.Render);
        public float SizeRatio { get { return GetValue(SizeRatioProperty); } set { SetValue(SizeRatioProperty, value); } }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (Bounds.Width <= 0f || Bounds.Height <= 0f)
                return;
            float size = Math.Max(1f, Math.Min(Bounds.Width, Bounds.Height) * MathHelper.Clamp(SizeRatio, 0.1f, 1f));
            string source = string.IsNullOrWhiteSpace(Source) ? "MissingIcon" : Source;
            context.Texture(source, new RectangleF(Bounds.Center - new Vector2(size * .5f), new Vector2(size)), Tint, true);
        }
    }
}
