using System.Collections.Generic;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Graph
{
    public sealed class GraphSeries
    {
        readonly List<GraphPoint> _points = new List<GraphPoint>();

        public string Id { get; set; }
        public string Label { get; set; }
        public Color LineColor { get; set; }
        public GraphAxisSide Axis { get; set; }
        public IReadOnlyList<GraphPoint> Points => _points;

        public void SetPoints(IEnumerable<GraphPoint> points)
        {
            _points.Clear();
            if (points != null)
                _points.AddRange(points);
        }
    }
}
