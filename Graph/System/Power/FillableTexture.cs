namespace Graph.System.Power
{
    internal sealed class FillableTexture
    {
        public string Name { get; }
        public float Margin { get; }
        public float Left { get; }
        public float Right { get; }
        public float Top { get; }
        public float Bottom { get; }
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
            bool rotateCenterIconByRatio = false)
        {
            Name = name;
            Margin = margin;
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
            CenterIconTexture = centerIconTexture;
            RotateCenterIconByRatio = rotateCenterIconByRatio;
        }
    }
}
