using VRageMath;

namespace LcdMod.Client.Gui.UserControls.Power
{
    internal sealed class FillableTexture
    {
        public const float DEFAULT_TEXTURE_SIZE = 192f;

        public string Name { get; }
        public float Margin { get; }
        public float Left { get; }
        public float Right { get; }
        public float Top { get; }
        public float Bottom { get; }
        public float TextureSize { get; }
        public string CenterIconTexture { get; }
        public bool RotateCenterIconByRatio { get; }

        public FillableTexture(
            string name,
            float margin,
            float left,
            float right,
            float top,
            float bottom,
            string centerIconTexture = null,
            bool rotateCenterIconByRatio = false,
            float textureSize = DEFAULT_TEXTURE_SIZE)
        {
            Name = name;
            Margin = margin;
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
            TextureSize = textureSize > 0f ? textureSize : DEFAULT_TEXTURE_SIZE;
            CenterIconTexture = centerIconTexture;
            RotateCenterIconByRatio = rotateCenterIconByRatio;
        }

        public RectangleF GetInnerRect(Vector2 center, float iconSize)
        {
            if (iconSize <= 0f)
                return new RectangleF(center.X, center.Y, 0f, 0f);

            float texScale = iconSize / TextureSize;
            float spriteLeft = center.X - iconSize / 2f;
            float spriteTop = center.Y - iconSize / 2f;
            float innerLeft = spriteLeft + (Left + Margin) * texScale;
            float innerTop = spriteTop + (Top + Margin) * texScale;
            float innerRight = center.X + iconSize / 2f - (Right + Margin) * texScale;
            float innerBottom = center.Y + iconSize / 2f - (Bottom + Margin) * texScale;

            return new RectangleF(
                innerLeft,
                innerTop,
                MathHelper.Max(0f, innerRight - innerLeft),
                MathHelper.Max(0f, innerBottom - innerTop));
        }
    }
}
