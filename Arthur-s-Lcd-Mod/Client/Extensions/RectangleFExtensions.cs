using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Extensions
{
    public static class RectangleFExtensions
    {
        public static MySprite ToSprite(this RectangleF grid, Color color, float scale = 1f)
        {
            var sprite = MySprite.CreateSprite("SquareSimple", grid.Center, grid.Size * scale);
            sprite.Color = color;
            return sprite;
        }

        public static MySprite ToCircle(this RectangleF grid, Color color, float scale = .3f)
        {
            var sprite = MySprite.CreateSprite("Circle", grid.Center, grid.Size * scale);
            sprite.Color = color;
            return sprite;
        }

        public static MySprite ToCircleHollow(this RectangleF grid, Color color, float scale = .75f)
        {
            var sprite = MySprite.CreateSprite("CircleHollow", grid.Center, grid.Size * scale);
            sprite.Color = color;
            return sprite;
        }

        public static MySprite ToCross(this RectangleF grid, Color color, float scale = .5f)
        {
            var sprite = MySprite.CreateSprite("Cross", grid.Center, grid.Size * scale);
            sprite.Color = color;
            return sprite;
        }

        public static RectangleF Inflate(this RectangleF rectangle, float amount)
        {
            return new RectangleF(
                rectangle.X - amount,
                rectangle.Y - amount,
                rectangle.Width + amount * 2f,
                rectangle.Height + amount * 2f);
        }
    }
}