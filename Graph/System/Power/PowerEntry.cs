using VRageMath;

namespace Graph.System.Power
{
    internal sealed class PowerEntry
    {
        public FillableTexture FillableTexture { get; }
        public float Ratio { get; }
        public string PercentText { get; }
        public string PowerText { get; }
        public Color FillColor { get; }

        public PowerEntry(
            FillableTexture fillableTexture,
            float ratio,
            string percentText,
            string powerText,
            Color fillColor)
        {
            FillableTexture = fillableTexture;
            Ratio = ratio;
            PercentText = percentText;
            PowerText = powerText;
            FillColor = fillColor;
        }
    }
}
