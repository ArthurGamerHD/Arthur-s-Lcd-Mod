using System.Collections.Generic;
using Graph.Helpers;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Graph.Panels
{
    public static class PieChartPanel
    {
        public static void CreateSprites(
            List<MySprite> sprites,
            string title,
            IMyTextSurface surface,
            Vector2 margin,
            Vector2 size,
            float value,
            Color? color = null,
            bool turnDarkOnComplete = false,
            bool showTitle = true)
        {
            if (color == null)
                color = surface.ScriptForegroundColor;

            var origo = new Vector2(margin.X, 512 - margin.Y);
            var backgroundColor = surface.ScriptForegroundColor;

            if (showTitle) DrawTitle(sprites, title, origo, size, value, color.Value);
            DrawBackground(sprites, origo, size, value, color.Value, backgroundColor, turnDarkOnComplete);
            
            if (value <= .01f)
                DrawPie(sprites, origo, size, .01f, color.Value, backgroundColor);
            else if (value <= .99f)
                DrawPie(sprites, origo, size, value, color.Value, backgroundColor);
        }

        internal static void DrawBackground(
            List<MySprite> sprites,
            Vector2 origo,
            Vector2 size,
            float value,
            Color color,
            Color backgroundColor,
            bool turnDarkOnComplete)
        {
            Vector2 position = new Vector2(origo.X - (size.X / 2f), origo.Y);

            float deg = 360 * value;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = position,
                Size = size,
                Color = deg > 358 && turnDarkOnComplete ? color : GetDarkenedColor(backgroundColor),
                Alignment = TextAlignment.LEFT
            });
        }
        
        internal static void DrawPie(
            List<MySprite> sprites,
            Vector2 origo,
            Vector2 size,
            float value,
            Color color,
            Color backgroundColor)
        {
            Vector2 position = new Vector2(origo.X - (size.X / 2f), origo.Y);

            float deg = 360 * value;
            float flip = value < 0.5f ? 1 : -1;

            if (value > .99) 
                return;

            float val = value < 0.5f ? 180 : 0;

            // Cover 1
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = position,
                Size = size,
                Color = color,
                RotationOrScale = MathHelper.ToRadians((flip * 90) + deg - val),
                Alignment = TextAlignment.LEFT
            });

            // Cover 2
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = position,
                Size = size,
                Color = value > 0.5f ? color : GetDarkenedColor(backgroundColor),
                RotationOrScale = MathHelper.ToRadians(flip * (-90)),
                Alignment = TextAlignment.LEFT
            });

#if LAYOUT_DEBUG
            // debug draw
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareHollow",
                Position = origo,
                Size = size,
                Color = Color.Red,
                Alignment = TextAlignment.CENTER
            });
#endif
        }

        internal static Color GetDarkenedColor(Color color) =>
            new Color((int)(color.R * 0.5f), (int)(color.G * 0.5f), (int)(color.B * 0.5f), color.A);

        internal static void DrawTitle(
            List<MySprite> sprites,
            string title,
            Vector2 origo,
            Vector2 size,
            float value,
            Color color)
        {
            Vector2 titleSize = new Vector2(size.X, 18);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = origo - new Vector2(size.X, size.Y + (titleSize.Y / 2) + 10),
                Size = new Vector2(size.X * 2, titleSize.Y),
                Color = new Color(0, 0, 0, 140),
                Alignment = TextAlignment.LEFT
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = (title ?? string.Empty) + FormatingHelper.PercentageToString(value),
                Position = origo - new Vector2(size.X - 4, size.Y + (titleSize.Y / 2) + 16),
                Color = color,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = 0.55f
            });
        }
    }
}
