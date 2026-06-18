using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Graph
{
    public sealed class GraphControl : ControlTemplate
    {
        readonly List<GraphPoint> _points = new List<GraphPoint>();
        GraphHoverResult _lastHover;
        RectangleF _bounds;
        RectangleF _plotBounds;

        public GraphControl(RectangleF bounds)
            : base()
        {
            _bounds = bounds;
            ValueFormatter = FormatingHelper.WattsToString;
            TooltipFormatter = DefaultTooltip;
            LineColor = Color.White;
            SetTooltip(new InteractiveTooltip(
                () => _lastHover.HasPoint && _lastHover.Values != null && _lastHover.Values.Count > 0 ? (_lastHover.Values[0].Label ?? Title ?? string.Empty) : string.Empty,
                GetTooltipLines));
        }

        public override RectangleF Bounds { get { return _bounds; } }
        public IReadOnlyList<GraphPoint> Points { get { return _points; } }
        public string Title { get; set; }
        public Color LineColor { get; set; }
        public Func<double, string> ValueFormatter { get; set; }
        public Func<GraphPoint, string> TooltipFormatter { get; set; }
        public float PointHoverThreshold { get; set; } = 12f;
        public RectangleF PlotBounds { get { return _plotBounds; } }

        public void SetPoints(IEnumerable<GraphPoint> points)
        {
            _points.Clear();
            if (points != null)
                _points.AddRange(points);
            MarkDirty();
        }

        public override void Arrange(RectangleF bounds)
        {
            _bounds = bounds;
            ValidateLayout();
            MarkDirty();
        }

        public GraphHoverResult GetHoveredPoint(Vector2 cursorPosition)
        {
            if (!_plotBounds.Contains(cursorPosition) || _points.Count == 0)
                return new GraphHoverResult();

            double max = GetMaxValue();
            var axis = GraphAxisScale.FromMaximum(max, 4);
            float bestDx = float.MaxValue;
            GraphHoverResult best = new GraphHoverResult();

            for (int i = 0; i < _points.Count; i++)
            {
                var pos = MapPoint(i, axis.Maximum);
                float dx = Math.Abs(pos.X - cursorPosition.X);
                if (dx < bestDx)
                {
                    bestDx = dx;
                    best.HasPoint = true;
                    best.PointIndex = i;
                    best.GameplayFrame = _points[i].GameplayFrame;
                    best.ScreenX = pos.X;
                    best.Values = new List<GraphSeriesHoverValue>
                    {
                        new GraphSeriesHoverValue
                        {
                            SeriesId = Title,
                            Label = _points[i].Label,
                            Color = LineColor,
                            Axis = GraphAxisSide.Right,
                            Value = _points[i].Value,
                            ScreenPosition = pos,
                            Overflow = false
                        }
                    };
                }
            }

            if (!best.HasPoint || bestDx > PointHoverThreshold)
                return new GraphHoverResult();

            return best;
        }

        protected override bool HitCore(Vector2 point)
        {
            if (!_bounds.Contains(point))
                return false;

            _lastHover = GetHoveredPoint(point);
            return _lastHover.HasPoint;
        }

        public override bool CanHover { get { return Visible && Enabled; } }

        public override bool Hover(object sender)
        {
            MarkDirty();
            return true;
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            if (_bounds.Width <= 0 || _bounds.Height <= 0)
                return;

            Color fg = ResolveColor(ThemeResources.OnSurfaceColor);
            float ts = 0.62f * LayoutScale * FontScale;
            string title = Title ?? string.Empty;
            float titleH = string.IsNullOrEmpty(title) ? 0f : FormatingHelper.GetSizeInPixel(title, this, ts, TextSurface).Y;
            double maxData = GetMaxValue();
            var axis = GraphAxisScale.FromMaximum(maxData, 4);
            string topLabel = FormatValue(axis.Maximum);
            float axisW = FormatingHelper.GetSizeInPixel(topLabel, this, ts, TextSurface).X + 4f * LayoutScale;

            _plotBounds = new RectangleF(
                _bounds.X + axisW,
                _bounds.Y + titleH + 2f * LayoutScale,
                Math.Max(1f, _bounds.Width - axisW),
                Math.Max(4f, _bounds.Height - titleH - 2f * LayoutScale));

            var graphBg = new Color(fg.R, fg.G, fg.B, 12);
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = _plotBounds.Center, Size = _plotBounds.Size, Color = graphBg, Alignment = TextAlignment.CENTER });

            if (!string.IsNullOrEmpty(title))
                sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = title, Position = new Vector2(_plotBounds.X, _bounds.Y), RotationOrScale = ts, Color = LineColor, Alignment = TextAlignment.LEFT, FontId = TextFont });

            RenderAxis(sprites, axis, ts, fg);

            if (_points.Count == 0)
                return;

            if (_points.Count == 1)
            {
                var pos = MapPoint(0, axis.Maximum);
                DrawPoint(sprites, pos, Math.Max(2f, LayoutScale * 2f), LineColor);
            }
            else
            {
                float thickness = Math.Max(1.5f, LayoutScale * 1.5f);
                var prev = MapPoint(0, axis.Maximum);
                for (int i = 1; i < _points.Count; i++)
                {
                    var next = MapPoint(i, axis.Maximum);
                    DrawLineSegment(sprites, prev, next, thickness, LineColor);
                    prev = next;
                }
            }

            _lastHover = GetHoveredPoint(new Vector2(float.NaN, float.NaN));
            if (_lastHover.HasPoint)
                RenderHover(sprites, _lastHover);
        }

        void RenderAxis(List<MySprite> sprites, GraphAxisScale axis, float ts, Color fg)
        {
            Color axisColor = new Color(fg.R, fg.G, fg.B, 170);
            Color gridColor = new Color(fg.R, fg.G, fg.B, 18);
            float labelHalf = FormatingHelper.GetSizeInPixel("0", this, ts, TextSurface).Y / 2f;
            for (int i = 0; i <= axis.Steps; i++)
            {
                double v = i * axis.Step;
                float lineY = _plotBounds.Y + _plotBounds.Height - (float)(v / axis.Maximum) * _plotBounds.Height;
                lineY = Math.Max(_plotBounds.Y, Math.Min(_plotBounds.Bottom, lineY));
                if (i > 0)
                    sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(_plotBounds.Center.X, lineY), Size = new Vector2(_plotBounds.Width, Math.Max(1f, LayoutScale * 0.5f)), Color = gridColor, Alignment = TextAlignment.CENTER });

                string label = FormatValue(v);
                float labelY = Math.Max(_plotBounds.Y, Math.Min(_plotBounds.Bottom - labelHalf * 2f, lineY - labelHalf));
                sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = label, Position = new Vector2(_plotBounds.X - 2f * LayoutScale, labelY), RotationOrScale = ts, Color = i == 0 ? new Color(fg.R, fg.G, fg.B, 110) : axisColor, Alignment = TextAlignment.RIGHT, FontId = TextFont });
            }
        }

        void RenderHover(List<MySprite> sprites, GraphHoverResult hover)
        {
            var guide = new Color(LineColor.R, LineColor.G, LineColor.B, 80);
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(hover.ScreenX, _plotBounds.Center.Y), Size = new Vector2(Math.Max(1f, LayoutScale), _plotBounds.Height), Color = guide, Alignment = TextAlignment.CENTER });
            if (hover.Values != null && hover.Values.Count > 0) DrawPoint(sprites, hover.Values[0].ScreenPosition, Math.Max(4f, LayoutScale * 4f), LineColor);
        }

        Vector2 MapPoint(int index, double axisMax)
        {
            float x = _points.Count <= 1
                ? _plotBounds.Center.X
                : _plotBounds.X + (float)index / (_points.Count - 1) * _plotBounds.Width;
            double value = _points[index].Value;
            float y = _plotBounds.Y + _plotBounds.Height - (float)(value / axisMax) * _plotBounds.Height;
            y = Math.Max(_plotBounds.Y, Math.Min(_plotBounds.Bottom, y));
            return new Vector2(x, y);
        }

        double GetMaxValue()
        {
            double max = 0;
            for (int i = 0; i < _points.Count; i++)
            {
                if (_points[i].Value > max)
                    max = _points[i].Value;
            }

            return max;
        }

        string FormatValue(double value)
        {
            return ValueFormatter != null ? ValueFormatter(value) : value.ToString("0.##");
        }

        string DefaultTooltip(GraphPoint point)
        {
            return (point.Label ?? Title ?? string.Empty) + "\n" + FormatValue(point.Value);
        }

        IList<ITooltipLine> GetTooltipLines()
        {
            var lines = new List<ITooltipLine>();
            if (!_lastHover.HasPoint)
                return lines;

            var text = GetSingleHoverTooltipText();
            if (string.IsNullOrEmpty(text))
                return lines;

            var split = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < split.Length; i++)
                lines.Add(new StaticTooltipLine(split[i]));

            return lines;
        }

        string GetSingleHoverTooltipText()
        {
            if (!_lastHover.HasPoint || _lastHover.PointIndex < 0 || _lastHover.PointIndex >= _points.Count)
                return string.Empty;

            var point = _points[_lastHover.PointIndex];
            return TooltipFormatter != null ? TooltipFormatter(point) : DefaultTooltip(point);
        }

        static void DrawPoint(List<MySprite> sprites, Vector2 pos, float size, Color color)
        {
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = pos, Size = new Vector2(size), Color = color, Alignment = TextAlignment.CENTER });
        }

        static void DrawLineSegment(List<MySprite> sprites, Vector2 p1, Vector2 p2, float thickness, Color color)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f)
                return;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2((p1.X + p2.X) / 2f, (p1.Y + p2.Y) / 2f),
                Size = new Vector2(len, thickness),
                RotationOrScale = (float)Math.Atan2(dy, dx),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }
    }
}
