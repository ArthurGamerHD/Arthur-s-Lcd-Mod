using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Graph
{
    public sealed class DualAxisGraphControl : ControlTemplate
    {
        readonly List<GraphSeries> _series = new List<GraphSeries>();
        readonly List<ITooltipLine> _tooltipLines = new List<ITooltipLine>();
        GraphHoverResult _lastHover;
        RectangleF _bounds;
        RectangleF _plotBounds;

        public DualAxisGraphControl(RectangleF bounds)
            : base()
        {
            _bounds = bounds;
            LeftValueFormatter = FormatMWh;
            RightValueFormatter = FormatMW;
            Title = "Power history";
            PointHoverThreshold = 12f;
            SetTooltip(new InteractiveTooltip(GetTooltipTitle, GetTooltipLines));
        }

        public override RectangleF Bounds => _bounds;
        public IReadOnlyList<GraphSeries> Series => _series;
        public string Title { get; set; }
        public double LeftAxisMaximum { get; set; }
        public double RightAxisMaximum { get; set; }
        public Func<double, string> LeftValueFormatter { get; set; }
        public Func<double, string> RightValueFormatter { get; set; }
        public float PointHoverThreshold { get; set; }

        public void SetSeries(IEnumerable<GraphSeries> series)
        {
            _series.Clear();
            if (series != null)
                _series.AddRange(series);
            MarkDirty();
        }

        public override void Arrange(RectangleF bounds)
        {
            _bounds = bounds;
            ValidateLayout();
            MarkDirty();
        }

        protected override bool HitCore(Vector2 point)
        {
            if (!_bounds.Contains(point))
                return false;

            _lastHover = GetHoveredPoint(point);
            return _lastHover.HasPoint;
        }

        public override bool CanHover => Visible && Enabled;

        public override bool Hover(object sender)
        {
            MarkDirty();
            return true;
        }

        public GraphHoverResult GetHoveredPoint(Vector2 cursorPosition)
        {
            if (!_plotBounds.Contains(cursorPosition))
                return new GraphHoverResult();

            int count = GetPointCount();
            if (count <= 0)
                return new GraphHoverResult();

            float bestDx = float.MaxValue;
            int bestIndex = -1;
            float bestX = 0f;
            for (int i = 0; i < count; i++)
            {
                float x = MapX(i, count);
                float dx = Math.Abs(x - cursorPosition.X);
                if (dx < bestDx)
                {
                    bestDx = dx;
                    bestIndex = i;
                    bestX = x;
                }
            }

            if (bestIndex < 0 || bestDx > PointHoverThreshold)
                return new GraphHoverResult();

            var values = new List<GraphSeriesHoverValue>(_series.Count);
            long frame = 0;
            for (int i = 0; i < _series.Count; i++)
            {
                var series = _series[i];
                if (series == null || series.Points == null || bestIndex >= series.Points.Count)
                    continue;

                var point = series.Points[bestIndex];
                if (frame == 0)
                    frame = point.GameplayFrame;

                bool overflow;
                var pos = MapPoint(bestIndex, series, point.Value, out overflow);
                values.Add(new GraphSeriesHoverValue
                {
                    SeriesId = series.Id,
                    Label = series.Label,
                    Color = series.LineColor,
                    Axis = series.Axis,
                    Value = point.Value,
                    ScreenPosition = pos,
                    Overflow = overflow
                });
            }

            return new GraphHoverResult
            {
                HasPoint = values.Count > 0,
                PointIndex = bestIndex,
                GameplayFrame = frame,
                ScreenX = bestX,
                Values = values
            };
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            if (_bounds.Width <= 0 || _bounds.Height <= 0)
                return;

            Color fg = ResolveColor(ThemeResources.OnSurfaceColor);
            float ts = 0.58f * LayoutScale * FontScale;
            float titleH = string.IsNullOrEmpty(Title) ? 0f : FormatingHelper.GetSizeInPixel(Title, this, ts, TextSurface).Y;
            float legendH = RenderLegend(sprites, _bounds.X, _bounds.Y + titleH, _bounds.Width, ts);
            float headerH = titleH + legendH + 3f * LayoutScale;

            if (!string.IsNullOrEmpty(Title))
                sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = Title, Position = new Vector2(_bounds.X, _bounds.Y), RotationOrScale = ts, Color = fg, Alignment = TextAlignment.LEFT, FontId = TextFont });

            float leftAxisW = MeasureAxisWidth(LeftValueFormatter, LeftAxisMaximum, ts) + 8f * LayoutScale;
            float rightAxisW = MeasureAxisWidth(RightValueFormatter, RightAxisMaximum, ts) + 8f * LayoutScale;
            _plotBounds = new RectangleF(
                _bounds.X + leftAxisW,
                _bounds.Y + headerH,
                Math.Max(1f, _bounds.Width - leftAxisW - rightAxisW),
                Math.Max(4f, _bounds.Height - headerH));

            var graphBg = new Color(fg.R, fg.G, fg.B, 12);
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = _plotBounds.Center, Size = _plotBounds.Size, Color = graphBg, Alignment = TextAlignment.CENTER });
            RenderAxes(sprites, ts, fg);
            RenderSeries(sprites);

            _lastHover = GetHoveredPoint(new Vector2(float.NaN, float.NaN));
            if (_lastHover.HasPoint)
                RenderHover(sprites, _lastHover);
        }

        float RenderLegend(List<MySprite> sprites, float x, float y, float width, float ts)
        {
            float cursorX = x;
            float cursorY = y;
            float lineH = FormatingHelper.GetSizeInPixel("A", this, ts, TextSurface).Y + 2f * LayoutScale;
            float box = Math.Max(5f, 7f * LayoutScale);
            for (int i = 0; i < _series.Count; i++)
            {
                var s = _series[i];
                if (s == null)
                    continue;

                string label = s.Label ?? string.Empty;
                float textW = FormatingHelper.GetSizeInPixel(label, this, ts, TextSurface).X;
                float entryW = box + 4f * LayoutScale + textW + 12f * LayoutScale;
                if (cursorX > x && cursorX + entryW > x + width)
                {
                    cursorX = x;
                    cursorY += lineH;
                }

                sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(cursorX + box * 0.5f, cursorY + lineH * 0.5f), Size = new Vector2(box), Color = s.LineColor, Alignment = TextAlignment.CENTER });
                sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = label, Position = new Vector2(cursorX + box + 4f * LayoutScale, cursorY), RotationOrScale = ts, Color = ResolveColor(ThemeResources.OnSurfaceColor), Alignment = TextAlignment.LEFT, FontId = TextFont });
                cursorX += entryW;
            }

            return cursorY - y + lineH;
        }

        void RenderAxes(List<MySprite> sprites, float ts, Color fg)
        {
            Color axisColor = new Color(fg.R, fg.G, fg.B, 170);
            Color gridColor = new Color(fg.R, fg.G, fg.B, 20);
            float labelH = FormatingHelper.GetSizeInPixel("0", this, ts, TextSurface).Y;

            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = "MWh", Position = new Vector2(_bounds.X, _plotBounds.Y - labelH), RotationOrScale = ts, Color = axisColor, Alignment = TextAlignment.LEFT, FontId = TextFont });
            sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = "MW", Position = new Vector2(_bounds.Right, _plotBounds.Y - labelH), RotationOrScale = ts, Color = axisColor, Alignment = TextAlignment.RIGHT, FontId = TextFont });

            for (int i = 0; i <= 4; i++)
            {
                double ratio = i / 4.0;
                float y = _plotBounds.Bottom - (float)ratio * _plotBounds.Height;
                if (i > 0)
                    sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(_plotBounds.Center.X, y), Size = new Vector2(_plotBounds.Width, Math.Max(1f, LayoutScale * 0.5f)), Color = gridColor, Alignment = TextAlignment.CENTER });

                string left = FormatLeft(LeftAxisMaximum * ratio);
                string right = FormatRight(RightAxisMaximum * ratio);
                sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = left, Position = new Vector2(_plotBounds.X - 3f * LayoutScale, y - labelH * 0.5f), RotationOrScale = ts, Color = axisColor, Alignment = TextAlignment.RIGHT, FontId = TextFont });
                sprites.Add(new MySprite { Type = SpriteType.TEXT, Data = right, Position = new Vector2(_plotBounds.Right + 3f * LayoutScale, y - labelH * 0.5f), RotationOrScale = ts, Color = axisColor, Alignment = TextAlignment.LEFT, FontId = TextFont });
            }
        }

        void RenderSeries(List<MySprite> sprites)
        {
            int count = GetPointCount();
            if (count <= 0)
                return;

            float thickness = Math.Max(1.5f, LayoutScale * 1.5f);
            for (int i = 0; i < _series.Count; i++)
            {
                var series = _series[i];
                if (series == null || series.Points == null || series.Points.Count == 0)
                    continue;

                bool overflow;
                var prev = MapPoint(0, series, series.Points[0].Value, out overflow);
                if (series.Points.Count == 1)
                    DrawPoint(sprites, prev, Math.Max(3f, LayoutScale * 3f), series.LineColor);

                for (int p = 1; p < series.Points.Count; p++)
                {
                    var next = MapPoint(p, series, series.Points[p].Value, out overflow);
                    DrawLineSegment(sprites, prev, next, thickness, series.LineColor);
                    if (overflow)
                        DrawOverflowMarker(sprites, new Vector2(next.X, _plotBounds.Y), LayoutScale, series.LineColor);
                    prev = next;
                }
            }
        }

        void RenderHover(List<MySprite> sprites, GraphHoverResult hover)
        {
            var fg = ResolveColor(ThemeResources.OnSurfaceColor);
            Color guide = new Color(fg.R, fg.G, fg.B, 70);
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = new Vector2(hover.ScreenX, _plotBounds.Center.Y), Size = new Vector2(Math.Max(1f, LayoutScale), _plotBounds.Height), Color = guide, Alignment = TextAlignment.CENTER });
            if (hover.Values == null)
                return;

            for (int i = 0; i < hover.Values.Count; i++)
            {
                var value = hover.Values[i];
                DrawPoint(sprites, value.ScreenPosition, Math.Max(4f, LayoutScale * 4f), value.Color);
                if (value.Overflow)
                    DrawOverflowMarker(sprites, new Vector2(value.ScreenPosition.X, _plotBounds.Y), LayoutScale, value.Color);
            }
        }

        Vector2 MapPoint(int index, GraphSeries series, double value, out bool overflow)
        {
            int count = series.Points?.Count ?? 0;
            float x = MapX(index, count);
            double max = series.Axis == GraphAxisSide.Left ? LeftAxisMaximum : RightAxisMaximum;
            double normalized = max > 0 ? value / max : 0;
            overflow = normalized > 1.0;
            normalized = Math.Max(0, Math.Min(1, normalized));
            float y = _plotBounds.Bottom - (float)normalized * _plotBounds.Height;
            return new Vector2(x, y);
        }

        float MapX(int index, int count)
        {
            if (count <= 1)
                return _plotBounds.Center.X;
            return _plotBounds.X + (float)index / (count - 1) * _plotBounds.Width;
        }

        int GetPointCount()
        {
            int count = 0;
            for (int i = 0; i < _series.Count; i++)
            {
                var series = _series[i];
                if (series != null && series.Points != null && series.Points.Count > count)
                    count = series.Points.Count;
            }

            return count;
        }

        float MeasureAxisWidth(Func<double, string> formatter, double maximum, float ts)
        {
            float width = 0f;
            for (int i = 0; i <= 4; i++)
            {
                double ratio = i / 4.0;
                string text = formatter != null ? formatter(maximum * ratio) : (maximum * ratio).ToString("0.##");
                float w = FormatingHelper.GetSizeInPixel(text, this, ts, TextSurface).X;
                if (w > width)
                    width = w;
            }
            return width;
        }

        string FormatLeft(double value)
        {
            return LeftValueFormatter != null ? LeftValueFormatter(value) : value.ToString("0.##");
        }

        string FormatRight(double value)
        {
            return RightValueFormatter != null ? RightValueFormatter(value) : value.ToString("0.##");
        }

        static string FormatMWh(double wattHours)
        {
            return (wattHours / 1000000.0).ToString("0.##") + " MWh";
        }

        static string FormatMW(double watts)
        {
            return (watts / 1000000.0).ToString("0.##") + " MW";
        }

        string GetTooltipTitle()
        {
            if (!_lastHover.HasPoint)
                return string.Empty;
            return "Power history";
        }

        IList<ITooltipLine> GetTooltipLines()
        {
            _tooltipLines.Clear();
            if (!_lastHover.HasPoint || _lastHover.Values == null)
                return _tooltipLines;

            _tooltipLines.Add(new StaticTooltipLine("Frame " + _lastHover.GameplayFrame));
            for (int i = 0; i < _lastHover.Values.Count; i++)
            {
                var value = _lastHover.Values[i];
                string formatted = value.Axis == GraphAxisSide.Left ? FormatLeft(value.Value) : FormatRight(value.Value);
                string suffix = string.Empty;
                if (value.Axis == GraphAxisSide.Left && LeftAxisMaximum > 0)
                    suffix = " (" + (value.Value / LeftAxisMaximum * 100.0).ToString("0.#") + "%)";
                if (value.Axis == GraphAxisSide.Right && RightAxisMaximum > 0)
                    suffix = " (" + (value.Value / RightAxisMaximum * 100.0).ToString("0.#") + "% capacity)";
                _tooltipLines.Add(new StaticTooltipLine((value.Label ?? value.SeriesId ?? string.Empty) + ": " + formatted + suffix));
            }

            return _tooltipLines;
        }

        static void DrawPoint(List<MySprite> sprites, Vector2 pos, float size, Color color)
        {
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Circle", Position = pos, Size = new Vector2(size), Color = color, Alignment = TextAlignment.CENTER });
        }

        static void DrawOverflowMarker(List<MySprite> sprites, Vector2 pos, float scale, Color color)
        {
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "Triangle", Position = pos + new Vector2(0f, 4f * scale), Size = new Vector2(8f * scale), RotationOrScale = 0f, Color = color, Alignment = TextAlignment.CENTER });
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
