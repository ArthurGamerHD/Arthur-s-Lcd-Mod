using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.InGameClockDashboard;
using LcdMod.Client.ClockDashboard;
using LcdMod.Client.Helpers;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;

using LcdMod.Common.Config.Generation;
using LcdMod.Common.Helpers;

namespace LcdMod.Client.Apps
{
    [LcdApp(20, Name = "ClockDashboard")]
    [MultiFunction(SurfaceScripts.InGameClockDashboardSurfaceScript.TITLE, "WeatherSun")]
    [ConfigComponent(Constants.APP, typeof(ClockDashboardConfigComponent), PropertyName = "ClockDashboardComponent")]
    internal sealed partial class InGameClockDashboardApp : App, IApp
    {
        readonly ClockEnvironmentReader _reader = new ClockEnvironmentReader();
        readonly List<MySprite> _emptySprites = new List<MySprite>();
        readonly InGameClockDashboardViewModel _viewModel;
        ClockDashboardSnapshot _snapshot;

        public InGameClockDashboardApp(IAppHost host)
            : base(host)
        {
            _viewModel = new InGameClockDashboardViewModel(this);
            SetContent(new InGameClockDashboardView { DataContext = _viewModel });
        }

        internal float ConfigScale => CardScale;
        internal override float WindowRenderScaling => CardScale;
        internal float SurfaceFontScale => Host.Surface.FontSize > 0f ? Host.Surface.FontSize : 1f;

        public override void Update()
        {
            _snapshot = _reader.Read(Host.Block, Host.GridLogic, _snapshot);
            BindSnapshot();
        }

        public override List<MySprite> GetSprites()
        {
            return _emptySprites;
        }

        void BindSnapshot()
        {
            if (_snapshot == null)
                return;

            var temperatureMode = (ClockDashboardTemperatureMode)ClockDashboardComponent.TemperatureModeInternal;
            string weatherName = _snapshot.WeatherDisplayName ?? ClockDashboardLocalization.Unavailable;
            string compactTemperature = _snapshot.HasAmbientTemperature
                ? FormatingHelper.TemperatureToString(_snapshot.AmbientTemperatureNormalized,
                    _snapshot.AmbientTemperatureLevel, temperatureMode)
                : ClockDashboardLocalization.Unavailable;
            string momentIcon = ClockDashboardSpriteMap.ResolveDayMomentIcon(_snapshot.DayMoment);
            bool hasSolarEvents = _snapshot.ClockMode == DashboardClockMode.LocalSolar && _snapshot.HasLocalSolarTime;

            // Property by property: each raises only when its text or value changed.
            InGameClockDashboardViewModel model = _viewModel;
            model.WeatherTemperature = weatherName + ", " + compactTemperature;
            model.Time = ClockDashboardFormatter.FormatCompactTime(_snapshot.DisplayDateTime, ClockDashboardComponent) ?? string.Empty;
            model.Date = ClockDashboardFormatter.FormatShortWeekday(_snapshot.DisplayDateTime) + ", " +
                         ClockDashboardFormatter.FormatCompactDate(_snapshot.DisplayDateTime);
            model.WeatherIcon = (IsClearWeather(_snapshot)
                ? momentIcon
                : ClockDashboardSpriteMap.ResolveWeatherIcon(_snapshot.WeatherSubtype)) ?? "MissingIcon";
            model.MomentIcon = momentIcon ?? "WeatherSun";
            model.Moment = ClockDashboardFormatter.FormatDayMoment(_snapshot.DayMoment) ?? string.Empty;
            model.Wind = _snapshot.HasWindSpeed
                ? ClockDashboardFormatter.FormatWindSpeed(_snapshot.WindSpeed) ?? string.Empty
                : ClockDashboardLocalization.Unavailable;
            model.Sunrise = hasSolarEvents && _snapshot.HasTerrainSunrise
                ? ClockDashboardFormatter.FormatSolarEventTime(_snapshot.TerrainSunriseHour, ClockDashboardComponent) ?? string.Empty
                : ClockDashboardLocalization.Unavailable;
            model.Sunset = hasSolarEvents && _snapshot.HasTerrainSunset
                ? ClockDashboardFormatter.FormatSolarEventTime(_snapshot.TerrainSunsetHour, ClockDashboardComponent) ?? string.Empty
                : ClockDashboardLocalization.Unavailable;
            model.Oxygen = ClockDashboardFormatter.FormatOxygen(_snapshot.OxygenRatio) ?? string.Empty;
            model.OxygenRatio = _snapshot.OxygenRatio;
            model.OxygenColor = OxygenColor(_snapshot.OxygenRatio);
            model.Temperature = _snapshot.HasInteriorTemperature
                ? FormatingHelper.TemperatureToString(_snapshot.InteriorTemperatureNormalized,
                    _snapshot.InteriorTemperatureLevel, temperatureMode) ?? string.Empty
                : ClockDashboardLocalization.Unavailable;
            model.TemperatureRatio = _snapshot.HasInteriorTemperature ? _snapshot.InteriorTemperatureNormalized : 0f;
            model.TemperatureColor = TemperatureColor(_snapshot.InteriorTemperatureLevel);

            if (_snapshot.HasIncomingWeather)
            {
                model.ForecastIcon = ResolveForecastIcon(_snapshot) ?? "WeatherSun";
                model.ForecastTitle = _snapshot.IncomingWeatherDisplayName ?? ClockDashboardLocalization.UnknownWeather;
                model.ForecastArrival = ClockDashboardFormatter.FormatIncomingArrival(_snapshot, ClockDashboardComponent) ?? string.Empty;
            }
            else
            {
                model.ForecastIcon = "WeatherSun";
                model.ForecastTitle = ClockDashboardLocalization.ClearWeather;
                model.ForecastArrival = string.Empty;
            }
        }

        static string ResolveForecastIcon(ClockDashboardSnapshot snapshot)
        {
            if (snapshot == null ||
                !string.Equals(snapshot.IncomingWeatherSubtype, "Clear", StringComparison.OrdinalIgnoreCase))
            {
                return ClockDashboardSpriteMap.ResolveWeatherIcon(snapshot?.IncomingWeatherSubtype);
            }

            DateTime arrival = ClockDashboardFormatter.BuildIncomingArrivalDateTime(snapshot);
            if (arrival == DateTime.MinValue)
                return "WeatherSun";

            if (snapshot.ClockMode == DashboardClockMode.LocalSolar && snapshot.HasLocalSolarTime)
            {
                double arrivalHour = arrival.TimeOfDay.TotalHours;
                if (snapshot.HasTerrainSunrise && snapshot.HasTerrainSunset)
                {
                    return IsSolarHourBetween(arrivalHour, snapshot.TerrainSunsetHour, snapshot.TerrainSunriseHour)
                        ? "WeatherMoon"
                        : "WeatherSun";
                }

                return ClockDashboardSolarTime.ClassifyLocalSolarHour(arrivalHour) == DayMoment.Night
                    ? "WeatherMoon"
                    : "WeatherSun";
            }

            return snapshot.DayMoment == DayMoment.Night ? "WeatherMoon" : "WeatherSun";
        }

        static bool IsSolarHourBetween(double hour, double startHour, double endHour)
        {
            hour = ClockDashboardSolarTime.PositiveModulo(hour, 24d);
            startHour = ClockDashboardSolarTime.PositiveModulo(startHour, 24d);
            endHour = ClockDashboardSolarTime.PositiveModulo(endHour, 24d);
            double interval = ClockDashboardSolarTime.PositiveModulo(endHour - startHour, 24d);
            if (interval <= 1e-6)
                return false;
            double offset = ClockDashboardSolarTime.PositiveModulo(hour - startHour, 24d);
            return offset < interval;
        }

        static bool IsClearWeather(ClockDashboardSnapshot snapshot)
        {
            if (snapshot == null)
                return true;
            string subtype = snapshot.WeatherSubtype;
            return string.IsNullOrWhiteSpace(subtype) ||
                   string.Equals(subtype, "Clear", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(snapshot.WeatherDisplayName, ClockDashboardLocalization.ClearWeather,
                       StringComparison.OrdinalIgnoreCase);
        }

        Color TemperatureColor(MyTemperatureLevel level)
        {
            switch (level)
            {
                case MyTemperatureLevel.ExtremeFreeze:
                case MyTemperatureLevel.Freeze:
                    return Color.CornflowerBlue;
                case MyTemperatureLevel.Hot:
                case MyTemperatureLevel.ExtremeHot:
                    return Color.OrangeRed;
                default:
                    return Host.ForegroundColor;
            }
        }

        Color OxygenColor(float ratio)
        {
            if (ratio < 0.2f)
                return Color.Red;
            if (ratio < 0.6f)
                return Color.Yellow;
            return Host.ForegroundColor;
        }
    }
}
