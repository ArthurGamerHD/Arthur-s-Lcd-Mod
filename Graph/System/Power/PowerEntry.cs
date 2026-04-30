using VRageMath;

namespace Graph.System.Power
{
    internal sealed class PowerEntry
    {
        public long EntryId { get; }
        public FillableTexture FillableTexture { get; }
        public float Ratio { get; }
        public string PercentText { get; }
        public Color FillColor { get; }
        public bool DrawCenterIcon { get; }
        public float CenterIconRotation { get; }
        
        public float CenterIconScale { get; }

        public PowerEntry(
            long entryId,
            FillableTexture fillableTexture,
            float ratio,
            string percentText,
            Color fillColor,
            bool drawCenterIcon = true,
            float centerIconRotation = 0f,
            float centerIconScale = 1f)
        {
            EntryId = entryId;
            FillableTexture = fillableTexture;
            Ratio = ratio;
            PercentText = percentText;
            FillColor = fillColor;
            DrawCenterIcon = drawCenterIcon;
            CenterIconRotation = centerIconRotation;
            CenterIconScale = centerIconScale;
        }
    }
}
