using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Inputs
{
    public sealed class TextInput : RectangleControl
    {
        const float INNER_PADDING_PERCENTAGE = 0.08f;
        const float MINIMUM_INNER_PADDING_PIXELS = 2f;

        public TextInput(RectangleF bounds, TextInputModel model = null)
            : base(bounds, CursorType.Hand, model ?? new TextInputModel())
        {
        }

        public TextInputModel TextModel => DataContext as TextInputModel;

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var rect = GetViewBox();
            var backgroundColor = GetRenderBackgroundColor();
            BorderRenderer.CreateSpritesFromRect(rect, sprites, backgroundColor,
                radiusScale: LayoutScale);

            // inner input container using a different container role and use its matching on* text role.
            var innerRect = Inset(rect, GetInnerPadding(rect));
            var innerContainerColor = ResolveColor(ThemeResources.SecondaryContainerColor);
            var innerTextColor = ResolveColor(ThemeResources.OnSecondaryContainerColor);

            BorderRenderer.CreateSpritesFromRect(innerRect, sprites, innerContainerColor,
                radiusScale: LayoutScale);

            RenderDefaultText(innerRect, sprites, innerTextColor);
        }

        static float GetInnerPadding(RectangleF rect)
        {
            var shortestSide = Math.Min(rect.Width, rect.Height);
            return Math.Max(MINIMUM_INNER_PADDING_PIXELS, shortestSide * INNER_PADDING_PERCENTAGE);
        }

        static RectangleF Inset(RectangleF rect, float amount)
        {
            var width = Math.Max(0f, rect.Width - amount * 2f);
            var height = Math.Max(0f, rect.Height - amount * 2f);
            return new RectangleF(rect.X + amount, rect.Y + amount, width, height);
        }
    }
}
