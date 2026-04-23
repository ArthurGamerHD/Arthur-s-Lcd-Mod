using System;
using System.Collections.Generic;
using System.Globalization;
using Graph.Helpers;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace Graph.Panels
{
    public class PieChartPanel
    {
        protected const float EPSILON = 0.0001f;

        protected readonly IMyTextSurface Surface;
        protected readonly string Title;
        protected Vector2 Origo;
        protected Vector2 Size;
        protected readonly bool ShowTitle;
        protected readonly List<MySprite> Sprites = new List<MySprite>();
        protected bool LayoutDirty = true;
        protected bool HasCachedState;
        protected float CachedValue;
        protected Color CachedColor;
        protected bool CachedTurnDarkOnComplete;
        protected Color CachedBackgroundColor;

        public PieChartPanel(string title, IMyTextSurface surface, Vector2 margin, Vector2 size, bool showTitle = true)
        {
            Title = title ?? "";
            Surface = surface;
            ShowTitle = showTitle;
            SetMargin(margin, size);
        }

        public void SetMargin(Vector2 margin, Vector2 size)
        {
            var newOrigo = new Vector2(margin.X, 512 - margin.Y);
            if (Origo != newOrigo || Size != size)
            {
                Origo = newOrigo;
                Size = size;
                LayoutDirty = true;
            }
        }

        public virtual List<MySprite> GetSprites(float value, Color? color = null, bool turnDarkOnComplete = false)
        {
            if (color == null)
                color = Surface.ScriptForegroundColor;

            var backgroundColor = Surface.ScriptForegroundColor;
            if (!LayoutDirty &&
                HasCachedState &&
                Math.Abs(CachedValue - value) <= EPSILON &&
                CachedColor == color.Value &&
                CachedTurnDarkOnComplete == turnDarkOnComplete &&
                CachedBackgroundColor == backgroundColor)
            {
                return Sprites;
            }

            Sprites.Clear();
            if (ShowTitle) DrawTitle(value, color.Value);
            DrawBackground(value, color.Value, backgroundColor, turnDarkOnComplete);
            
            if (value <= .01f)
                DrawPie(.01f, color.Value, backgroundColor);
            else if (value <= .99f)
                DrawPie(value, color.Value, backgroundColor);

            
            CachedValue = value;
            CachedColor = color.Value;
            CachedTurnDarkOnComplete = turnDarkOnComplete;
            CachedBackgroundColor = backgroundColor;
            LayoutDirty = false;
            HasCachedState = true;
            return Sprites;
        }

        protected virtual void DrawBackground(float value, Color color, Color backgroundColor, bool turnDarkOnComplete)
        {
            Vector2 position = Origo - (Size / 2);

            float deg = 360 * value;

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = position,
                Size = Size,
                Color = deg > 358 && turnDarkOnComplete ? color : DarkenColor(backgroundColor),
                Alignment = TextAlignment.LEFT
            });
        }
        
        protected virtual void DrawPie(float value, Color color, Color backgroundColor)
        {
            Vector2 position = Origo - (Size / 2);

            float deg = 360 * value;
            float flip = value < 0.5f ? 1 : -1;

            if (value > .99) 
                return;

            float val = value < 0.5f ? 180 : 0;

            // Cover 1
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = position,
                Size = Size,
                Color = color,
                RotationOrScale = MathHelper.ToRadians((flip * 90) + deg - val),
                Alignment = TextAlignment.LEFT
            });

            // Cover 2
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = position,
                Size = Size,
                Color = value > 0.5f ? color : DarkenColor(backgroundColor),
                RotationOrScale = MathHelper.ToRadians(flip * (-90)),
                Alignment = TextAlignment.LEFT
            });
        }

        Color DarkenColor(Color color) => new Color((int)(color.R * 0.5f), (int)(color.G * 0.5f), (int)(color.B * 0.5f), color.A);

        protected virtual void DrawTitle(float value, Color color)
        {
            Vector2 titleSize = new Vector2(Size.X, 18);
            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = Origo - new Vector2(Size.X, Size.Y + (titleSize.Y / 2) + 10),
                Size = new Vector2(Size.X * 2, titleSize.Y),
                Color = new Color(0, 0, 0, 140),
                Alignment = TextAlignment.LEFT
            });

            Sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = Title + FormatingHelper.PercentageToString(value),
                Position = Origo - new Vector2(Size.X - 4, Size.Y + (titleSize.Y / 2) + 16),
                Color = color,
                Alignment = TextAlignment.LEFT,
                RotationOrScale = 0.55f
            });
        }
    }
}
