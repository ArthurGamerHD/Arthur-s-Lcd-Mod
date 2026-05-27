using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Common.Helpers;
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

        public TextInputModel TextModel
        {
            get { return DataContext as TextInputModel; }
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            var rect = GetViewBox();
            var hovered = rect.Contains(context.CursorPosition);
            var backgroundColor = context.Style.GetPanelColor(hovered);
            Border.CreateSpritesFromRect(rect, sprites, backgroundColor,
                radiusScale: context.Scale);

            // inner input container using a different container role and use its matching on* text role.
            var innerRect = Inset(rect, GetInnerPadding(rect));
            var innerContainerColor = context.GetThemeColor(
                hovered ? Constants.SECONDARY_CONTAINER + Constants.HOVER : Constants.SECONDARY_CONTAINER);
            var innerTextColor = context.GetThemeColor(Constants.ON_SECONDARY_CONTAINER);

            Border.CreateSpritesFromRect(innerRect, sprites, innerContainerColor,
                radiusScale: context.Scale);

            var innerStyle = new ControlStyle(innerTextColor, innerContainerColor)
            {
                BorderRadiusPixels = context.Style.BorderRadiusPixels
            };

            RenderDefaultText(innerRect, new ControlRenderContext(
                context.Surface,
                context.Scale,
                context.FontScale,
                innerStyle,
                context.Theme,
                context.CursorPosition), sprites);
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
