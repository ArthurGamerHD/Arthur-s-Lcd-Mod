using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.Controls
{
    public static class PieDualChartPanel
    {
        public static void CreateSprites(
            List<MySprite> sprites,
            string title,
            IMyTextSurface surface,
            Vector2 margin,
            Vector2 size,
            float value,
            float value2,
            Color? color = null,
            bool turnDarkOnComplete = false,
            bool showTitle = true)
        {
            if (color == null)
                color = surface.ScriptForegroundColor;

            var origo = new Vector2(margin.X, 512 - margin.Y);
            var backgroundColor = surface.ScriptForegroundColor;

            if (showTitle) PieChartPanel.DrawTitle(sprites, title, origo, size, value, color.Value);
            PieChartPanel.DrawBackground(sprites, origo, size, Math.Max(value, value2),
                (value > value2) ? backgroundColor : color.Value, backgroundColor, turnDarkOnComplete);

            if(value > 0 && value > value2) // draw only if > 0 and bigger than the second value
                PieChartPanel.DrawPie(sprites, origo, size, value, backgroundColor, backgroundColor);
            
            if(value2 > 0 && value2 < .99)  // draw only if > 0 and not 100% (turnDarkOnComplete already draws 100%)
                DrawPieWithTransparency(sprites, origo, size, value2, color.Value);
        }

        static void DrawPieWithTransparency(List<MySprite> sprites, Vector2 origo, Vector2 size, float value, Color color)
        {
            Vector2 position = new Vector2(origo.X - (size.X / 2f), origo.Y);

            float deg = 360 * value;
            float flip = value < 0.5f ? 1 : -1;

            if (value > .99)
                return;

            float val = value < 0.5f ? 180 : 0;

            // Cover 1
            var semiCircle = new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = position,
                Size = size,
                Color = color,
                RotationOrScale = MathHelper.ToRadians((flip * 90) + deg - val),
                Alignment = TextAlignment.LEFT
            };

            if (value < 0.5f)
            {
                sprites.Add(new MySprite(SpriteType.CLIP_RECT, null,
                    new Vector2(position.X + size.X / 2, position.Y - size.Y / 2),
                    size // the X is bigger, but we don't care about width
                ));
                sprites.Add(semiCircle);
                sprites.Add(MySprite.CreateClearClipRect());
            }
            else
            {
                sprites.Add(semiCircle);
            }

            if (value <= 0.5f)
                return;

            // Cover 2
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = position,
                Size = size,
                Color = color,
                RotationOrScale = MathHelper.ToRadians(flip * (-90)),
                Alignment = TextAlignment.LEFT
            });
        }
    }
}
