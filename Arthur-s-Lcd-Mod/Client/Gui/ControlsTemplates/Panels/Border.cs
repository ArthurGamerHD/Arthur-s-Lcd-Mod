using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Clock
{
    internal sealed class Border : RectangleControl
    {
        readonly ControlTemplate _content;

        public Border(ControlTemplate content)
            : base(default(RectangleF))
        {
            _content = content;
            if (_content != null)
                AddChild(_content);
            
            CornerRadiusPixels = 10f;
            StrokeThicknessPixels = 0f;
            ContentPaddingPixels = 8f;
            OuterInsetPixels = Vector4.Zero;
        }

        public float CornerRadiusPixels { get; set; }
        public float StrokeThicknessPixels { get; set; }
        public float ContentPaddingPixels { get; set; }
        public Vector4 OuterInsetPixels { get; set; }

        public override void Arrange(RectangleF bounds)
        {
            base.Arrange(bounds);
            ArrangeContent();
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            RectangleF cardRect = GetCardRect();
            if (cardRect.Width <= 0f || cardRect.Height <= 0f)
                return;

            // The translucent rounded-rectangle builder uses temporary corner
            // clips. Bracket the complete card render so those clips are reset
            // and the ancestor clip is restored before a sibling card renders.
            if (!BeginContentClip(sprites, cardRect))
                return;

            try
            {
                float radius = Math.Max(0f, CornerRadiusPixels * LayoutScale);
                float border = MathHelper.Clamp(
                    StrokeThicknessPixels * LayoutScale,
                    0f,
                    Math.Min(cardRect.Width, cardRect.Height) * 0.5f);

                Color borderColor = ApplyOpacity(BorderColor);
                Color backgroundColor = ApplyOpacity(BackgroundColor);

                BorderRenderer.CreateSpritesFromRect(
                    cardRect,
                    sprites,
                    backgroundColor,
                    radiusPixels: radius,
                    radiusScale: 1f,
                    strokeColor: borderColor,
                    strokeThicknessPixels: border);

                // Border restores its own rectangular clip after drawing the
                // four corner slices. Resolve the control clip again so an
                // intersecting ancestor clip remains in effect for content.
                BeginContentClip(sprites, cardRect);

                ArrangeContent();
                _content?.Render(sprites);
            }
            finally
            {
                EndContentClip(sprites);
            }
        }

        void ArrangeContent()
        {
            if (_content == null)
                return;

            RectangleF cardRect = GetCardRect();
            float inset = Math.Max(0f, (StrokeThicknessPixels + ContentPaddingPixels) * LayoutScale);
            _content.Arrange(Inset(cardRect, inset));
        }

        RectangleF GetCardRect()
        {
            float scale = Math.Max(0f, LayoutScale);
            return Inset(
                Bounds,
                OuterInsetPixels.X * scale,
                OuterInsetPixels.Y * scale,
                OuterInsetPixels.Z * scale,
                OuterInsetPixels.W * scale);
        }

        static RectangleF Inset(RectangleF rect, float amount)
        {
            return Inset(rect, amount, amount, amount, amount);
        }

        static RectangleF Inset(
            RectangleF rect,
            float left,
            float top,
            float right,
            float bottom)
        {
            float x = rect.X + Math.Max(0f, left);
            float y = rect.Y + Math.Max(0f, top);
            float width = Math.Max(0f, rect.Width - Math.Max(0f, left) - Math.Max(0f, right));
            float height = Math.Max(0f, rect.Height - Math.Max(0f, top) - Math.Max(0f, bottom));
            return new RectangleF(x, y, width, height);
        }
    }
}