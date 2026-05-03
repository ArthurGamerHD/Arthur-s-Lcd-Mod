using VRageMath;

namespace Graph.Apps.Games.Chess.Extensions
{
    public static class RectangleExtension
    {
        public static bool Intersects(this RectangleF reference, RectangleF value)
        {
            return value.X < reference.X + reference.Width && reference.X < value.X + value.Width &&
                   value.Y < reference.Y + reference.Height && reference.Y < value.Y + value.Height;
        }
    }
}