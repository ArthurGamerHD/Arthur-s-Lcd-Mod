using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Graph
{
    public struct GraphSeriesHoverValue
    {
        public string SeriesId;
        public string Label;
        public Color Color;
        public GraphAxisSide Axis;
        public double Value;
        public Vector2 ScreenPosition;
        public bool Overflow;
    }
}
