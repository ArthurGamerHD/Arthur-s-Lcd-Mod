using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Basic
{
    internal sealed class FitTextControl : RectangleControl
    {
        public FitTextControl()
            : base(default(RectangleF))
        {
            Text = string.Empty;
            MinFontScale = 0.6f;
            MaxFontScale = 6.0f;
            WidthFill = 0.94f;
            HeightFill = 0.88f;
        }

        public string Text { get; set; }
        public float MinFontScale { get; set; }
        public float MaxFontScale { get; set; }
        public float WidthFill { get; set; }
        public float HeightFill { get; set; }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            if (string.IsNullOrEmpty(Text) || TextSurface == null)
                return;

            RectangleF rect = GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            Vector2 unitSize = MeasureText(Text, 1f);
            if (unitSize.X <= 0f || unitSize.Y <= 0f)
                return;

            float fitWidth = rect.Width * MathHelper.Clamp(WidthFill, 0.1f, 1f) / unitSize.X;
            float fitHeight = rect.Height * MathHelper.Clamp(HeightFill, 0.1f, 1f) / unitSize.Y;
            float scale = MathHelper.Clamp(
                Math.Min(fitWidth, fitHeight),
                Math.Max(0.01f, MinFontScale * LayoutScale),
                Math.Max(0.01f, MaxFontScale * LayoutScale));

            Vector2 size = MeasureText(Text, scale);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = Text,
                Position = new Vector2(rect.Center.X, rect.Center.Y - size.Y * 0.5f),
                RotationOrScale = scale,
                Color = GetRenderTextColor(),
                Alignment = TextAlignment.CENTER,
                FontId = TextFont
            });
        }
    }
}