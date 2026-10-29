using System;
using LcdMod.Common.Helpers;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.InGameClockDashboard
{
    internal sealed class ClockDashboardData
    {
        public string WeatherTemperature;
        public string Time;
        public string Date;
        public string WeatherIcon;
        public string MomentIcon;
        public string Moment;
        public string Wind;
        public string Sunrise;
        public string Sunset;
        public string ForecastIcon;
        public string ForecastTitle;
        public string ForecastArrival;
        public string Oxygen;
        public float OxygenRatio;
        public Color OxygenColor;
        public string Temperature;
        public float TemperatureRatio;
        public Color TemperatureColor;
    }

    internal sealed class InGameClockDashboardViewModel : ObservableObject
    {
        const float TINY_HEIGHT_TO_WIDTH_RATIO = Constants.MIN_SCREEN_HEIGHT_TO_WIDTH_RATIO;

        readonly InGameClockDashboardApp _app;
        Vector2 _viewport;
        ClockDashboardData _data = new ClockDashboardData();
        bool _isSquare = true;
        bool _isWide;
        bool _isTiny;

        public InGameClockDashboardViewModel(InGameClockDashboardApp app)
        {
            _app = app;
        }

        public string WeatherTemperature => _data.WeatherTemperature ?? string.Empty;
        public string Time => _data.Time ?? string.Empty;
        public string Date => _data.Date ?? string.Empty;
        public string WeatherIcon => _data.WeatherIcon ?? "MissingIcon";
        public string MomentIcon => _data.MomentIcon ?? "WeatherSun";
        public string Moment => _data.Moment ?? string.Empty;
        public string Wind => _data.Wind ?? string.Empty;
        public string Sunrise => _data.Sunrise ?? string.Empty;
        public string Sunset => _data.Sunset ?? string.Empty;
        public string ForecastIcon => _data.ForecastIcon ?? "WeatherSun";
        public string ForecastTitle => _data.ForecastTitle ?? string.Empty;
        public string ForecastArrival => _data.ForecastArrival ?? string.Empty;
        public string Oxygen => _data.Oxygen ?? string.Empty;
        public float OxygenRatio => _data.OxygenRatio;
        public Color OxygenColor => _data.OxygenColor;
        public string Temperature => _data.Temperature ?? string.Empty;
        public float TemperatureRatio => _data.TemperatureRatio;
        public Color TemperatureColor => _data.TemperatureColor;

        public bool IsSquare => _isSquare;
        public bool IsWide => _isWide;
        public bool IsTiny => _isTiny;
        public bool IsNotTiny => !_isTiny;
        public bool IsSquareOrWide => !_isTiny;

        public Vector4 TopCardMargin { get; private set; }
        public Vector4 ForecastCardMargin { get; private set; }
        public Vector4 BottomCardMargin { get; private set; }
        public float CardRadius { get; private set; }
        public Vector4 CardPadding { get; private set; }
        public Vector4 ForecastCardPadding { get; private set; }
        public float WeatherTemperatureFontSize { get; private set; }
        public float DateFontSize { get; private set; }
        public float TimeMinFontSize { get; private set; }
        public float TimeMaxFontSize { get; private set; }
        public float ForecastTitleFontSize { get; private set; }
        public float ForecastArrivalFontSize { get; private set; }
        public float StatusFontSize { get; private set; }
        public float TrailingFontSize { get; private set; }
        public float EnvironmentFontSize { get; private set; }
        public float MetricIconRatio { get; private set; }
        public float TrailingIconRatio { get; private set; }
        public float EnvironmentIconRatio { get; private set; }
        public float BarHeight { get; private set; }
        public float BarRadius { get; private set; }
        public Vector4 BarMargin { get; private set; }
        public float Gap { get; private set; }
        public Vector4 MetricTextMargin { get; private set; }
        public Vector4 TrailingTextMargin { get; private set; }

        public void Update(ClockDashboardData data)
        {
            _data = data ?? new ClockDashboardData();
            RaisePropertyChanged<object>(string.Empty);
        }

        public void SetViewport(Vector2 size)
        {
            if (_viewport == size)
                return;
            _viewport = size;
            UpdateLayout();
        }

        void UpdateLayout()
        {
            float configScale = _app.ConfigScale;
            float width = Math.Max(1f, _viewport.X * configScale);
            float height = Math.Max(1f, _viewport.Y * configScale);
            bool tiny = height / width < TINY_HEIGHT_TO_WIDTH_RATIO;
            bool wide = !tiny && width / height >= 1.25f;
            _isTiny = tiny;
            _isWide = wide;
            _isSquare = !tiny && !wide;

            float fontScale = _app.SurfaceFontScale;
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
            RaisePropertyChanged<object>(string.Empty);
        }
    }
}
