using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using Color = VRageMath.Color;

namespace Graph.Extensions
{
    public static class MySpriteExtensions
    {
        static readonly List<MySprite> SpritesBuffer = new List<MySprite>();
        
        public static MySprite Shadow(this MySprite sprite, float offset, Color? color = null)
        {
            color = color ?? (sprite.Color ?? Color.White).MulValue(0.2f);
            return new MySprite(sprite.Type,
                sprite.Data,
                sprite.Position + offset,
                sprite.Size,
                color,
                sprite.FontId,
                sprite.Alignment,
                sprite.RotationOrScale);
        }
        
        public static MySprite[] Shadow(this MySprite[] sprites, float offset, Color? color = null)
        {
            SpritesBuffer.Clear();
            foreach (var sprite in sprites) 
                SpritesBuffer.Add(sprite.Shadow(offset, color));

            return SpritesBuffer.ToArray();
        }
    }
}