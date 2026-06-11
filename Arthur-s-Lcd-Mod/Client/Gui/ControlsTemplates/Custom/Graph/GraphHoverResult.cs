using System.Collections.Generic;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Graph
{
    public struct GraphHoverResult
    {
        public bool HasPoint;
        public int PointIndex;
        public long GameplayFrame;
        public float ScreenX;
        public List<GraphSeriesHoverValue> Values;
    }
}
