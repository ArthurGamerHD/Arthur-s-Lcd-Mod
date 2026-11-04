using System;
using LcdMod.Common.Helpers;
using VRageMath;

namespace LcdMod.Client.Apps.InGameClockDashboard
{
    /// <summary>A clock dashboard view model the view sizes as it measures (the real one and its design copy).</summary>
    internal interface IClockDashboardViewport
    {
        void SetViewport(Vector2 size);
    }

    /// <summary>
    /// The clock dashboard's sizes for a viewport: one rule for the real view model and its design copy, so the
    /// preview lays the dashboard out exactly as the LCD does.
    /// </summary>
    internal sealed class ClockDashboardLayout
    {
        const float TINY_HEIGHT_TO_WIDTH_RATIO = Constants.MIN_SCREEN_HEIGHT_TO_WIDTH_RATIO;

        public bool IsSquare, IsWide, IsTiny;
        public Vector4 TopCardMargin, ForecastCardMargin, BottomCardMargin, CardPadding, ForecastCardPadding;
        public Vector4 BarMargin, MetricTextMargin, TrailingTextMargin;
        public float CardRadius, WeatherTemperatureFontSize, DateFontSize, TimeMinFontSize, TimeMaxFontSize;
        public float ForecastTitleFontSize, ForecastArrivalFontSize, StatusFontSize, TrailingFontSize, EnvironmentFontSize;
        public float MetricIconRatio, TrailingIconRatio, EnvironmentIconRatio, BarHeight, BarRadius, Gap;

        /// <param name="viewport">The view's size.</param>
        /// <param name="configScale">The app's card scale (1 in the preview).</param>
        /// <param name="fontScale">The surface's font size (1 in the preview).</param>
        public void Update(Vector2 viewport, float configScale, float fontScale)
        {
            float width = Math.Max(1f, viewport.X * configScale);
            float height = Math.Max(1f, viewport.Y * configScale);
            bool tiny = height / width < TINY_HEIGHT_TO_WIDTH_RATIO;
            bool wide = !tiny && width / height >= 1.25f;
            IsTiny = tiny;
            IsWide = wide;
            IsSquare = !tiny && !wide;

            float timeScale = MathHelper.Clamp(configScale * Math.Max(0.55f, Math.Min(width / 512f, height / 256f)),
                0.45f, 2.5f);
            float scale = MathHelper.Clamp(configScale * Math.Max(0.55f, Math.Min(width, height) / 512f), 0.45f, 2.5f);
            float text = fontScale * scale;

            WeatherTemperatureFontSize = 1.1f * text;
            DateFontSize = 1.0f * text;
            TimeMinFontSize = 1.0f * timeScale;
            TimeMaxFontSize = 3.2f * timeScale;
            ForecastTitleFontSize = 0.8f * text;
            ForecastArrivalFontSize = 0.7f * text;
            StatusFontSize = (tiny ? 0.78f : 1.0f) * text;
            TrailingFontSize = (tiny ? 0.70f : 0.9f) * text;
            EnvironmentFontSize = (tiny ? 0.78f : 1.0f) * text;
            MetricIconRatio = tiny ? 0.96f : 0.78f;
            TrailingIconRatio = tiny ? 0.96f : 0.76f;
            EnvironmentIconRatio = tiny ? 0.86f : 0.6f;

            float cardScale = MathHelper.Clamp(scale, 0.65f, 1.6f) / configScale;
            CardRadius = 10f * cardScale;
            CardPadding = new Vector4(7f * cardScale);
            ForecastCardPadding = new Vector4(6f * cardScale);
            TopCardMargin = new Vector4(3f, 3f, 3f, 5f) * cardScale;
            ForecastCardMargin = new Vector4(3f, 5f, 3f, 3f) * cardScale;
            BottomCardMargin = new Vector4(3f) * cardScale;

            Gap = 2f;
            MetricTextMargin = new Vector4(Gap, 0f, 0f, 0f);
            TrailingTextMargin = new Vector4(0f, 0f, Gap, 0f);
            BarHeight = 9f;
            BarRadius = BarHeight * .5f;
            BarMargin = new Vector4(8f, 0f, 8f, 0f);
        }
    }
}
