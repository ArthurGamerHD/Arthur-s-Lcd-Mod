using System;
using VRageMath;

namespace Graph.Apps.Games.Chess.Extensions
{
    public static class PointExtensions
    {
        public static char ToChar(this Point point)
        {
            if (point.X < 0 || point.X > 127 || point.Y < 0 || point.Y > 127)
                throw new Exception("X and Y must be between 0 and 127.");
            
            return (char)((point.X << 1) | (point.Y << 8));
        }
        
        public static Point ToPoint(this char value)
        {
            var x = (value & 0x7E) >> 1;
            var y = (value >> 8) & 0x7F;
            return new Point(x, y);
        }
    }
}