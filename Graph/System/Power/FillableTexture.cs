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

        public FillableTexture(string name, float margin, float left, float right, float top, float bottom)
        {
            Name = name;
            Margin = margin;
            Left = left;
            Right = right;
            Top = top;
            Bottom = bottom;
        }
    }
}
