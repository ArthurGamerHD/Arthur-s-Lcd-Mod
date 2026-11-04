using Adk.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.InGameClockDashboard
{
    /// <summary>
    /// The clock dashboard's state. The app sets each reading on its own property, and so does the design data. A
    /// property raises PropertyChanged only when its value changes, so a refresh with the same readings (most
    /// frames) notifies nothing, and a new minute updates only the time. The layout values follow the viewport
    /// (<see cref="SetViewport"/>) and also raise one by one. The [ObservableProperty] fields get their notifying
    /// properties from ADK's generator; views bind them like any property. [PreviewWith] gives the IDE preview's
    /// values: Acrux generates the preview clone from them (InGameClockDashboardViewModelPreview.Default).
    /// </summary>
    internal sealed partial class InGameClockDashboardViewModel : ObservableObject, IClockDashboardViewport
    {
        readonly InGameClockDashboardApp _app;
        readonly ClockDashboardLayout _layout = new ClockDashboardLayout();
        Vector2 _viewport;

        // ------------------------------------------------------------------ readings (set by the app)

        [PreviewWith("Clear, 21 °C")] [ObservableProperty] string _weatherTemperature = string.Empty;
        [PreviewWith("14:32")] [ObservableProperty] string _time = string.Empty;
        [PreviewWith("Thu, 05/11")] [ObservableProperty] string _date = string.Empty;
        [PreviewWith("WeatherSun")] [ObservableProperty] string _weatherIcon = "MissingIcon";
        [PreviewWith("WeatherSun")] [ObservableProperty] string _momentIcon = "WeatherSun";
        [PreviewWith("Afternoon")] [ObservableProperty] string _moment = string.Empty;
        [PreviewWith("4.2 m/s")] [ObservableProperty] string _wind = string.Empty;
        [PreviewWith("06:12")] [ObservableProperty] string _sunrise = string.Empty;
        [PreviewWith("18:47")] [ObservableProperty] string _sunset = string.Empty;
        [PreviewWith("WeatherSun")] [ObservableProperty] string _forecastIcon = "WeatherSun";
        [PreviewWith("Clear skies")] [ObservableProperty] string _forecastTitle = string.Empty;
        [PreviewWith("Next 2 h")] [ObservableProperty] string _forecastArrival = string.Empty;
        [PreviewWith("86 %")] [ObservableProperty] string _oxygen = string.Empty;
        [PreviewWith(0.86f)] [ObservableProperty] float _oxygenRatio;
        [PreviewWith("#5AC878")] [ObservableProperty] Color _oxygenColor;
        [PreviewWith("21 °C")] [ObservableProperty] string _temperature = string.Empty;
        [PreviewWith(0.55f)] [ObservableProperty] float _temperatureRatio;
        [PreviewWith("#E6AA3C")] [ObservableProperty] Color _temperatureColor;

        public InGameClockDashboardViewModel(InGameClockDashboardApp app)
        {
            _app = app;
        }

        // ------------------------------------------------------------------ layout (follows the viewport)

        bool _isSquare = true;
        bool _isWide;
        bool _isTiny;

        [PreviewWith(true)]
        public bool IsSquare { get { return _isSquare; } private set { RaiseAndSetIfChanged(ref _isSquare, value, nameof(IsSquare)); } }
        [PreviewWith(false)]
        public bool IsWide { get { return _isWide; } private set { RaiseAndSetIfChanged(ref _isWide, value, nameof(IsWide)); } }

        [PreviewWith(false)]
        public bool IsTiny
        {
            get { return _isTiny; }
            private set
            {
                if (!RaiseAndSetIfChanged(ref _isTiny, value, nameof(IsTiny)))
                    return;
                RaisePropertyChanged<bool>(nameof(IsNotTiny));
                RaisePropertyChanged<bool>(nameof(IsSquareOrWide));
            }
        }

        [PreviewWith(true)] public bool IsNotTiny => !_isTiny;
        [PreviewWith(true)] public bool IsSquareOrWide => !_isTiny;

        [PreviewWith("3 3 3 5")] [ObservableProperty] Vector4 _topCardMargin;
        [PreviewWith("3 5 3 3")] [ObservableProperty] Vector4 _forecastCardMargin;
        [PreviewWith(3f)] [ObservableProperty] Vector4 _bottomCardMargin;
        [PreviewWith(10f)] [ObservableProperty] float _cardRadius;
        [PreviewWith(7f)] [ObservableProperty] Vector4 _cardPadding;
        [PreviewWith(6f)] [ObservableProperty] Vector4 _forecastCardPadding;
        [PreviewWith(1.1f)] [ObservableProperty] float _weatherTemperatureFontSize;
        [PreviewWith(1f)] [ObservableProperty] float _dateFontSize;
        [PreviewWith(1f)] [ObservableProperty] float _timeMinFontSize;
        [PreviewWith(3.2f)] [ObservableProperty] float _timeMaxFontSize;
        [PreviewWith(0.8f)] [ObservableProperty] float _forecastTitleFontSize;
        [PreviewWith(0.7f)] [ObservableProperty] float _forecastArrivalFontSize;
        [PreviewWith(1f)] [ObservableProperty] float _statusFontSize;
        [PreviewWith(0.9f)] [ObservableProperty] float _trailingFontSize;
        [PreviewWith(1f)] [ObservableProperty] float _environmentFontSize;
        [PreviewWith(0.78f)] [ObservableProperty] float _metricIconRatio;
        [PreviewWith(0.76f)] [ObservableProperty] float _trailingIconRatio;
        [PreviewWith(0.6f)] [ObservableProperty] float _environmentIconRatio;
        [PreviewWith(9f)] [ObservableProperty] float _barHeight;
        [PreviewWith(4.5f)] [ObservableProperty] float _barRadius;
        [PreviewWith("8 0 8 0")] [ObservableProperty] Vector4 _barMargin;
        [PreviewWith(2f)] [ObservableProperty] float _gap;
        [PreviewWith("2 0 0 0")] [ObservableProperty] Vector4 _metricTextMargin;
        [PreviewWith("0 0 2 0")] [ObservableProperty] Vector4 _trailingTextMargin;

        public void SetViewport(Vector2 size)
        {
            if (_viewport == size)
                return;
            _viewport = size;
            UpdateLayout();
        }

        void UpdateLayout()
        {
            _layout.Update(_viewport, _app != null ? _app.ConfigScale : 1f, _app != null ? _app.SurfaceFontScale : 1f);
            ApplyLayout(_layout);
        }

        // Each assignment raises only when that size changed.
        void ApplyLayout(ClockDashboardLayout layout)
        {
            IsTiny = layout.IsTiny;
            IsWide = layout.IsWide;
            IsSquare = layout.IsSquare;
            WeatherTemperatureFontSize = layout.WeatherTemperatureFontSize;
            DateFontSize = layout.DateFontSize;
            TimeMinFontSize = layout.TimeMinFontSize;
            TimeMaxFontSize = layout.TimeMaxFontSize;
            ForecastTitleFontSize = layout.ForecastTitleFontSize;
            ForecastArrivalFontSize = layout.ForecastArrivalFontSize;
            StatusFontSize = layout.StatusFontSize;
            TrailingFontSize = layout.TrailingFontSize;
            EnvironmentFontSize = layout.EnvironmentFontSize;
            MetricIconRatio = layout.MetricIconRatio;
            TrailingIconRatio = layout.TrailingIconRatio;
            EnvironmentIconRatio = layout.EnvironmentIconRatio;
            CardRadius = layout.CardRadius;
            CardPadding = layout.CardPadding;
            ForecastCardPadding = layout.ForecastCardPadding;
            TopCardMargin = layout.TopCardMargin;
            ForecastCardMargin = layout.ForecastCardMargin;
            BottomCardMargin = layout.BottomCardMargin;
            Gap = layout.Gap;
            MetricTextMargin = layout.MetricTextMargin;
            TrailingTextMargin = layout.TrailingTextMargin;
            BarHeight = layout.BarHeight;
            BarRadius = layout.BarRadius;
            BarMargin = layout.BarMargin;
        }
    }
}
