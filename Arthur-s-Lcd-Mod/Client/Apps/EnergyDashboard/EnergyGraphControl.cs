using System;
using System.Collections.Generic;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Rendering;
using Acrux.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.EnergyDashboard
{
    internal sealed class EnergyGraphControl : TemplatedControl
    {
        const float AXIS_WIDTH = 43f;
        const string GRID_TEXTURE = "Grid";
        const float GRID_TEXTURE_SIZE = 2048f;
        const int GRID_TEXTURE_LINES = 24;
        const int GRAPH_TICKS = 10;
        static readonly Color PlotBackground = new Color(0, 0, 0, 64);


        public static readonly StyledProperty<string> TitleProperty = RegisterText(nameof(Title));
        public string Title { get { return GetValue(TitleProperty); } set { SetValue(TitleProperty, value); } }
        public static readonly StyledProperty<string> AxisLabel0Property = RegisterText(nameof(AxisLabel0));
        public string AxisLabel0 { get { return GetValue(AxisLabel0Property); } set { SetValue(AxisLabel0Property, value); } }
        public static readonly StyledProperty<string> AxisLabel1Property = RegisterText(nameof(AxisLabel1));
        public string AxisLabel1 { get { return GetValue(AxisLabel1Property); } set { SetValue(AxisLabel1Property, value); } }
        public static readonly StyledProperty<string> AxisLabel2Property = RegisterText(nameof(AxisLabel2));
        public string AxisLabel2 { get { return GetValue(AxisLabel2Property); } set { SetValue(AxisLabel2Property, value); } }
        public static readonly StyledProperty<string> AxisLabel3Property = RegisterText(nameof(AxisLabel3));
        public string AxisLabel3 { get { return GetValue(AxisLabel3Property); } set { SetValue(AxisLabel3Property, value); } }
        public static readonly StyledProperty<string> LeftTimeProperty = RegisterText(nameof(LeftTime));
        public string LeftTime { get { return GetValue(LeftTimeProperty); } set { SetValue(LeftTimeProperty, value); } }
        public static readonly StyledProperty<string> MiddleTimeProperty = RegisterText(nameof(MiddleTime));
        public string MiddleTime { get { return GetValue(MiddleTimeProperty); } set { SetValue(MiddleTimeProperty, value); } }
        public static readonly StyledProperty<string> RightTimeProperty = RegisterText(nameof(RightTime));
        public string RightTime { get { return GetValue(RightTimeProperty); } set { SetValue(RightTimeProperty, value); } }
        public static readonly StyledProperty<string> EmptyTextProperty = RegisterText(nameof(EmptyText));
        public string EmptyText { get { return GetValue(EmptyTextProperty); } set { SetValue(EmptyTextProperty, value); } }

        public static readonly StyledProperty<bool> HasTimeScaleProperty =
            StyledProperty.Register<EnergyGraphControl, bool>(nameof(HasTimeScale), false, UiInvalidation.Measure);
        public bool HasTimeScale { get { return GetValue(HasTimeScaleProperty); } set { SetValue(HasTimeScaleProperty, value); } }

        public static readonly StyledProperty<bool> IsEmptyProperty =
            StyledProperty.Register<EnergyGraphControl, bool>(nameof(IsEmpty), true, UiInvalidation.Measure);
        public bool IsEmpty { get { return GetValue(IsEmptyProperty); } set { SetValue(IsEmptyProperty, value); } }

        public static readonly StyledProperty<bool> HasAxisProperty =
            StyledProperty.Register<EnergyGraphControl, bool>(nameof(HasAxis), false, UiInvalidation.Measure);
        public bool HasAxis { get { return GetValue(HasAxisProperty); } set { SetValue(HasAxisProperty, value); } }

        public static readonly StyledProperty<IReadOnlyList<EnergyGraphSeries>> SeriesProperty =
            StyledProperty.Register<EnergyGraphControl, IReadOnlyList<EnergyGraphSeries>>(nameof(Series), null, UiInvalidation.Render);
        public IReadOnlyList<EnergyGraphSeries> Series { get { return GetValue(SeriesProperty); } set { SetValue(SeriesProperty, value); } }

        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<EnergyGraphControl, Color>(nameof(Foreground), Color.White, UiInvalidation.Measure);
        public Color Foreground { get { return GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }

        public static readonly StyledProperty<float> FontSizeProperty = RegisterSize(nameof(FontSize), 1f);
        public float FontSize { get { return GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<EnergyGraphControl, string>(nameof(FontFamily), "White", UiInvalidation.Measure);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<float> TitleFontSizeProperty = RegisterSize(nameof(TitleFontSize), 1f);
        public float TitleFontSize { get { return GetValue(TitleFontSizeProperty); } set { SetValue(TitleFontSizeProperty, value); } }
        public static readonly StyledProperty<float> AxisFontSizeProperty = RegisterSize(nameof(AxisFontSize), 1f);
        public float AxisFontSize { get { return GetValue(AxisFontSizeProperty); } set { SetValue(AxisFontSizeProperty, value); } }
        public static readonly StyledProperty<float> TimeFontSizeProperty = RegisterSize(nameof(TimeFontSize), 1f);
        public float TimeFontSize { get { return GetValue(TimeFontSizeProperty); } set { SetValue(TimeFontSizeProperty, value); } }
        public static readonly StyledProperty<float> EmptyFontSizeProperty = RegisterSize(nameof(EmptyFontSize), 1f);
        public float EmptyFontSize { get { return GetValue(EmptyFontSizeProperty); } set { SetValue(EmptyFontSizeProperty, value); } }
        public static readonly StyledProperty<float> TickWidthProperty = RegisterSize(nameof(TickWidth), 1f);
        public float TickWidth { get { return GetValue(TickWidthProperty); } set { SetValue(TickWidthProperty, value); } }
        public static readonly StyledProperty<float> TickHeightProperty = RegisterSize(nameof(TickHeight), 3f);
        public float TickHeight { get { return GetValue(TickHeightProperty); } set { SetValue(TickHeightProperty, value); } }

        public static readonly StyledProperty<Vector4> PlotMarginProperty = RegisterThickness(nameof(PlotMargin));
        public Vector4 PlotMargin { get { return GetValue(PlotMarginProperty); } set { SetValue(PlotMarginProperty, value); } }
        public static readonly StyledProperty<Vector4> AxisMargin0Property = RegisterThickness(nameof(AxisMargin0));
        public Vector4 AxisMargin0 { get { return GetValue(AxisMargin0Property); } set { SetValue(AxisMargin0Property, value); } }
        public static readonly StyledProperty<Vector4> AxisMargin1Property = RegisterThickness(nameof(AxisMargin1));
        public Vector4 AxisMargin1 { get { return GetValue(AxisMargin1Property); } set { SetValue(AxisMargin1Property, value); } }
        public static readonly StyledProperty<Vector4> AxisMargin2Property = RegisterThickness(nameof(AxisMargin2));
        public Vector4 AxisMargin2 { get { return GetValue(AxisMargin2Property); } set { SetValue(AxisMargin2Property, value); } }
        public static readonly StyledProperty<Vector4> AxisMargin3Property = RegisterThickness(nameof(AxisMargin3));
        public Vector4 AxisMargin3 { get { return GetValue(AxisMargin3Property); } set { SetValue(AxisMargin3Property, value); } }
        public static readonly StyledProperty<Vector4> TimeMarginProperty = RegisterThickness(nameof(TimeMargin));
        public Vector4 TimeMargin { get { return GetValue(TimeMarginProperty); } set { SetValue(TimeMarginProperty, value); } }
        public static readonly StyledProperty<Vector4> LeftTickMarginProperty = RegisterThickness(nameof(LeftTickMargin));
        public Vector4 LeftTickMargin { get { return GetValue(LeftTickMarginProperty); } set { SetValue(LeftTickMarginProperty, value); } }
        public static readonly StyledProperty<Vector4> MiddleTickMarginProperty = RegisterThickness(nameof(MiddleTickMargin));
        public Vector4 MiddleTickMargin { get { return GetValue(MiddleTickMarginProperty); } set { SetValue(MiddleTickMarginProperty, value); } }
        public static readonly StyledProperty<Vector4> RightTickMarginProperty = RegisterThickness(nameof(RightTickMargin));
        public Vector4 RightTickMargin { get { return GetValue(RightTickMarginProperty); } set { SetValue(RightTickMarginProperty, value); } }

        public static readonly StyledProperty<Color> AxisForegroundProperty =
            StyledProperty.Register<EnergyGraphControl, Color>(nameof(AxisForeground), Color.White, UiInvalidation.Render);
        public Color AxisForeground { get { return GetValue(AxisForegroundProperty); } set { SetValue(AxisForegroundProperty, value); } }

        public static readonly StyledProperty<BackgroundBrush> AxisBrushProperty =
            StyledProperty.Register<EnergyGraphControl, BackgroundBrush>(nameof(AxisBrush), new SolidColorBrush(Color.White), UiInvalidation.Render);
        public BackgroundBrush AxisBrush { get { return GetValue(AxisBrushProperty); } set { SetValue(AxisBrushProperty, value); } }

        public static readonly StyledProperty<Color> EmptyForegroundProperty =
            StyledProperty.Register<EnergyGraphControl, Color>(nameof(EmptyForeground), Color.White, UiInvalidation.Render);
        public Color EmptyForeground { get { return GetValue(EmptyForegroundProperty); } set { SetValue(EmptyForegroundProperty, value); } }

        public static readonly StyledProperty<Color> GridColorProperty =
            StyledProperty.Register<EnergyGraphControl, Color>(nameof(GridColor), Color.White, UiInvalidation.Render);
        public Color GridColor { get { return GetValue(GridColorProperty); } set { SetValue(GridColorProperty, value); } }

        static StyledProperty<string> RegisterText(string name)
        {
            return StyledProperty.Register<EnergyGraphControl, string>(name, string.Empty, UiInvalidation.Measure);
        }

        static StyledProperty<float> RegisterSize(string name, float value)
        {
            return StyledProperty.Register<EnergyGraphControl, float>(name, value, UiInvalidation.Measure);
        }

        static StyledProperty<Vector4> RegisterThickness(string name)
        {
            return StyledProperty.Register<EnergyGraphControl, Vector4>(name, Vector4.Zero, UiInvalidation.Measure);
        }

        Vector2 _measuredSize;
        string _measuredKey;
        float _measuredScaling = -1f;
        ITextMetrics _measuredMetrics;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            float scaling = EnergyText.RenderScaling(this);
            string key = string.Join("\n", Title, AxisLabel0, AxisLabel1, AxisLabel2, AxisLabel3, LeftTime,
                MiddleTime, RightTime, FontFamily, FontSize.ToString("R"), Foreground.PackedValue.ToString(),
                HasTimeScale ? "1" : "0", IsEmpty ? "1" : "0");
            if (_measuredSize != availableSize || _measuredKey != key || _measuredScaling != scaling ||
                !ReferenceEquals(_measuredMetrics, TextMetrics))
            {
                _measuredSize = availableSize; _measuredKey = key; _measuredScaling = scaling;
                _measuredMetrics = TextMetrics;
                UpdateLayout(availableSize, scaling);
            }
            return TemplateRoot == null ? Vector2.Zero : TemplateRoot.Measure(availableSize);
        }

        void UpdateLayout(Vector2 size, float scaling)
        {
            ITextMetrics metrics = TextMetrics;
            string font = FontFamily;
            Color fg = Foreground;
            float textSize = .56f * FontSize;
            TitleFontSize = textSize * 1.08f;
            EmptyFontSize = textSize;
            float labelHeight = EnergyText.Measure(metrics, font, "0", textSize).Y;
            float titleHeight = EnergyText.Measure(metrics, font,
                string.IsNullOrEmpty(Title) ? " " : Title, TitleFontSize).Y;
            float header = titleHeight + 6f;
            float timeScale = labelHeight + 5f;
            float plotWidth = Math.Max(1f, size.X - AXIS_WIDTH);
            float plotHeight = Math.Max(1f, size.Y - header - timeScale);
            float plotBottom = header + plotHeight;
            PlotMargin = new Vector4(AXIS_WIDTH, header, size.X - AXIS_WIDTH - plotWidth, size.Y - plotBottom);

            HasAxis = !IsEmpty;
            AxisFontSize = EnergyText.FitWidth(metrics, font, textSize, AXIS_WIDTH - 6f,
                AxisLabel0, AxisLabel1, AxisLabel2, AxisLabel3);
            AxisMargin0 = AxisMargin(AxisLabel0, 0, plotBottom, plotHeight, size.X);
            AxisMargin1 = AxisMargin(AxisLabel1, 1, plotBottom, plotHeight, size.X);
            AxisMargin2 = AxisMargin(AxisLabel2, 2, plotBottom, plotHeight, size.X);
            AxisMargin3 = AxisMargin(AxisLabel3, 3, plotBottom, plotHeight, size.X);

            TickWidth = 1f / scaling;
            TickHeight = Math.Max(2f / scaling, 3f);
            float tickTop = plotBottom + 2f;
            LeftTickMargin = TickMargin(AXIS_WIDTH, tickTop, size.X);
            MiddleTickMargin = TickMargin(AXIS_WIDTH + plotWidth * .5f, tickTop, size.X);
            RightTickMargin = TickMargin(AXIS_WIDTH + plotWidth, tickTop, size.X);
            TimeFontSize = EnergyText.FitWidth(metrics, font, textSize * .82f, Math.Max(1f, plotWidth * .32f),
                LeftTime, MiddleTime, RightTime);
            TimeMargin = new Vector4(AXIS_WIDTH, tickTop + TickHeight + 1f, size.X - AXIS_WIDTH - plotWidth, 0f);

            AxisForeground = new Color(fg.R, fg.G, fg.B, 150);
            AxisBrush = new SolidColorBrush(AxisForeground);
            EmptyForeground = new Color(fg.R, fg.G, fg.B, 140);
            GridColor = new Color(fg.R, fg.G, fg.B, 26);
        }

        Vector4 TickMargin(float x, float top, float width)
        {
            return new Vector4(x - TickWidth * .5f, top, width - x - TickWidth * .5f, 0f);
        }

        Vector4 AxisMargin(string label, int index, float plotBottom, float plotHeight, float width)
        {
            float height = EnergyText.Measure(TextMetrics, FontFamily, label, AxisFontSize).Y;
            float y = plotBottom - index / 3f * plotHeight;
            return new Vector4(0f, y - height * .5f, Math.Max(0f, width - AXIS_WIDTH + 3f), 0f);
        }

        protected override void DrawSelf(RenderContext context)
        {
            Vector4 margin = PlotMargin;
            var plot = new RectangleF(Bounds.X + margin.X, Bounds.Y + margin.Y,
                Math.Max(0f, Bounds.Width - margin.X - margin.Z), Math.Max(0f, Bounds.Height - margin.Y - margin.W));
            if (plot.Width <= 0f || plot.Height <= 0f) return;
            context.FillRectangle(plot, PlotBackground);
            using (context.PushClip(plot))
                DrawGrid(context, plot);

            var series = Series;
            if (series == null || IsEmpty) return;
            float scaling = EnergyText.RenderScaling(this);
            float thickness = Math.Max(1f / scaling, 1.25f);
            for (int s = 0; s < series.Count; s++)
            {
                Vector2[] points = series[s].Points;
                for (int i = 1; i < points.Length; i++)
                    DrawSegment(context, ToPlot(plot, points[i - 1]), ToPlot(plot, points[i]), thickness,
                        series[s].Color, scaling);
            }
        }

        void DrawGrid(RenderContext context, RectangleF plot)
        {
            float tile = plot.Width / (GRAPH_TICKS - 1) / (GRID_TEXTURE_SIZE / (GRID_TEXTURE_LINES - 1)) * GRID_TEXTURE_SIZE;
            if (tile <= 0f) return;
            for (float bottom = plot.Bottom; bottom > plot.Y; bottom -= tile)
                for (float x = plot.X; x < plot.Right; x += tile)
                    context.Texture(GRID_TEXTURE, new RectangleF(x, bottom - tile, tile, tile), GridColor);
        }

        static Vector2 ToPlot(RectangleF plot, Vector2 point)
        {
            return new Vector2(plot.X + point.X * plot.Width, plot.Bottom - point.Y * plot.Height);
        }

        static void DrawSegment(RenderContext context, Vector2 from, Vector2 to, float thickness, Color color,
            float scaling)
        {
            Vector2 delta = to - from;
            float length = delta.Length();
            if (length * scaling < .5f) return;
            Vector2 center = (from + to) * .5f;
            context.Texture("SquareSimple",
                new RectangleF(center - new Vector2(length, thickness) * .5f, new Vector2(length, thickness)),
                color, false, (float)Math.Atan2(delta.Y, delta.X));
        }
    }
}
