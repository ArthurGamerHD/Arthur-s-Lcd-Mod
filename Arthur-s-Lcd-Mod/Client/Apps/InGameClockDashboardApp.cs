using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.ClockDashboard;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates.Basic;
using LcdMod.Client.Gui.ControlsTemplates.Custom.Clock;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Progress;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Models.Apps;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Apps
{
    internal sealed class InGameClockDashboardApp : App, IApp
    {
        const float MIN_CONTENT_HEIGHT = 40f;
        const float TINY_HEIGHT_TO_WIDTH_RATIO = 0.2f;
        const string CLOCK_PROGRESS_BAR_STYLE_ID = "ClockDashboardMetric";

        enum ClockDashboardLayoutMode
        {
            Square,
            Wide,
            Tiny
        }

        readonly ScreenConfigClockDashboard _config;
        readonly ClockEnvironmentReader _reader = new ClockEnvironmentReader();
        
        // ReSharper disable once CollectionNeverUpdated.Local interactivity not implemented yet
        readonly List<Control> _interactiveChildren = new List<Control>();
        readonly Grid _rootGrid;

        // Square-screen hierarchy. The three cards remain self-contained so the
        // wide layout can reuse the same controls without a separate binding path.
        readonly Grid _wideRightGrid;
        readonly Grid _wideWeatherIconGrid;
        readonly Grid _squareTopGrid;
        readonly Grid _squareTopInfoGrid;
        readonly Grid _squareLowerGrid;
        readonly Grid _squareBottomGrid;
        readonly Grid _squareStatusGrid;
        readonly Grid _squareOxygenGrid;
        readonly Grid _squareTemperatureGrid;
        readonly Border _squareTopCard;
        readonly Border _wideWeatherIconCard;
        readonly Border _tinyForecastCard;
        readonly Border _squareForecastCard;
        readonly Border _squareBottomCard;

        readonly WeatherIconControl _squareWeatherIcon;
        readonly TextBlock _squareWeatherTemperatureText;
        readonly FitTextControl _squareTimeText;
        readonly TextBlock _squareDateText;
        readonly ForecastControl _squareForecast;
        readonly MetricValueControl _squareMomentValue;
        readonly MetricValueControl _squareWindValue;
        readonly TrailingIconValueControl _squareSunriseValue;
        readonly TrailingIconValueControl _squareSunsetValue;
        readonly SpriteIconControl _squareOxygenIcon;
        readonly TextBlock _squareOxygenText;
        readonly ProgressBar _squareOxygenBar;
        readonly SpriteIconControl _squareTemperatureIcon;
        readonly TextBlock _squareTemperatureText;
        readonly ProgressBar _squareTemperatureBar;
        readonly StyleTree _clockProgressBarStyles;

        ClockDashboardSnapshot _snapshot;
        bool _layoutConfigured;
        ClockDashboardLayoutMode _layoutMode;

        public InGameClockDashboardApp(ScreenConfigClockDashboard config, IAppHost host)
            : base(config, host)
        {
            _config = config;
            _rootGrid = AddChild(new Grid());
            _clockProgressBarStyles = BuildClockProgressBarStyles();

            _wideRightGrid = new Grid();
            _wideWeatherIconGrid = new Grid();
            _squareTopGrid = new Grid();
            _squareTopInfoGrid = new Grid();
            _squareLowerGrid = new Grid();
            _squareBottomGrid = new Grid();
            _squareStatusGrid = new Grid();
            _squareOxygenGrid = new Grid();
            _squareTemperatureGrid = new Grid();

            _squareWeatherIcon = new WeatherIconControl
            {
                SizeRatio = 0.8f
            };
            _squareWeatherTemperatureText = CreateTextBlock(
                1.2f,
                TextBlockVerticalAlignment.Center,
                TextAlignment.CENTER);
            _squareTimeText = new FitTextControl
            {
                MinFontScale = 1.1f,
                MaxFontScale = 3.2f
            };
            _squareDateText = CreateTextBlock(
                1.0f,
                TextBlockVerticalAlignment.Center,
                TextAlignment.CENTER);

            _squareForecast = new ForecastControl();
            _squareMomentValue = new MetricValueControl("WeatherSun");
            _squareWindValue = new MetricValueControl("WeatherHeavyWind");
            _squareSunriseValue = new TrailingIconValueControl("WeatherSunRise");
            _squareSunsetValue = new TrailingIconValueControl("WeatherSunSet");

            _squareOxygenIcon = new SpriteIconControl
            {
                SpriteName = "IconOxygen",
                SizeRatio = 0.6f
            };
            _squareOxygenText = CreateTextBlock(
                1.0f,
                TextBlockVerticalAlignment.Center);
            _squareOxygenBar = CreateProgressBar();

            _squareTemperatureIcon = new SpriteIconControl
            {
                SpriteName = "IconTemperature",
                SizeRatio = 0.6f
            };
            _squareTemperatureText = CreateTextBlock(
                1.0f,
                TextBlockVerticalAlignment.Center);
            _squareTemperatureBar = CreateProgressBar();

            _squareTopCard = new Border(_squareTopGrid)
            {
                OuterInsetPixels = new Vector4(3f, 3f, 3f, 5f),
                CornerRadiusPixels = 10f,
                ContentPaddingPixels = 5f
            };
            _wideWeatherIconCard = new Border(_wideWeatherIconGrid)
            {
                OuterInsetPixels = new Vector4(3f, 3f, 3f, 5f),
                CornerRadiusPixels = 10f,
                ContentPaddingPixels = 5f
            };
            _tinyForecastCard = new Border(_squareForecast)
            {
                OuterInsetPixels = new Vector4(3f, 3f, 3f, 5f),
                CornerRadiusPixels = 10f,
                ContentPaddingPixels = 5f
            };
            _squareForecastCard = new Border(_squareStatusGrid)
            {
                OuterInsetPixels = new Vector4(3f, 5f, 3f, 3f),
                ContentPaddingPixels = 5f
            };
            _squareBottomCard = new Border(_squareBottomGrid)
            {
                OuterInsetPixels = new Vector4(3f, 3f, 3f, 3f),
                ContentPaddingPixels = 5f
            };

            BuildSquareLayoutTree();
        }

        public override IReadOnlyList<Control> Children => _interactiveChildren;

        ScreenConfigClockDashboard Config => Host != null ? Host.Config as ScreenConfigClockDashboard ?? _config : _config;

        public override void Update()
        {
            _snapshot = _reader.Read(
                Host?.Block,
                Host?.GridLogic,
                _snapshot);
            BindSnapshot();
        }

        public override void LayoutChanged()
        {
            _layoutConfigured = false;
            _rootGrid.InvalidateLayout();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            if (Host == null)
                return sprites;

            var bounds = GetContentBounds();
            if (bounds.Width <= 5f || bounds.Height <= 5f)
                return sprites;

            if (_snapshot == null)
            {
                _snapshot = _reader.Read(
                    Host.Block,
                    Host.GridLogic,
                    null);
                BindSnapshot();
            }

            ConfigureLayout(GetLayoutMode(bounds));
            ConfigureResponsiveText(bounds);
            _rootGrid.Arrange(bounds);
            _rootGrid.Render(sprites);
            ClearDirtyAfterRender();
            return sprites;
        }

        static ClockDashboardLayoutMode GetLayoutMode(RectangleF bounds)
        {
            if (bounds.Height / Math.Max(1f, bounds.Width) < TINY_HEIGHT_TO_WIDTH_RATIO)
                return ClockDashboardLayoutMode.Tiny;

            return bounds.Width / Math.Max(1f, bounds.Height) >= 1.25f
                ? ClockDashboardLayoutMode.Wide
                : ClockDashboardLayoutMode.Square;
        }

        RectangleF GetContentBounds()
        {
            RectangleF view = Host.ViewBox;
            float scale = Config?.Scale ?? 1f;
            float topInset = Host.TitleVisible ? 48f * scale : 0f;
            float top = view.Y + topInset;
            float height = Math.Max(MIN_CONTENT_HEIGHT, view.Bottom - top);
            return new RectangleF(view.X, top, view.Width, height);
        }

        void BuildSquareLayoutTree()
        {
            ConfigureSquareTopLayout();
            ConfigureSquareLowerLayout();
            ConfigureDefaultStatusLayout();

            _squareBottomGrid.SetColumns(1f);
            _squareBottomGrid.SetRows(1f, 1f);
            _squareBottomGrid.Set(_squareOxygenGrid, 0, 0);
            _squareBottomGrid.Set(_squareTemperatureGrid, 0, 1);

            ConfigureMetricGrid(
                _squareOxygenGrid,
                _squareOxygenIcon,
                _squareOxygenText,
                _squareOxygenBar);
            ConfigureMetricGrid(
                _squareTemperatureGrid,
                _squareTemperatureIcon,
                _squareTemperatureText,
                _squareTemperatureBar);
        }

        void ConfigureSquareTopLayout()
        {
            _squareTopGrid.ClearChildren();

            // Top card: 33/66 icon/details split. The time row receives the
            // largest weight and uses auto-fit text to consume its cell.
            _squareTopGrid.SetColumns(1f, 2f);
            _squareTopGrid.SetRows(1f);
            _squareTopGrid.Set(_squareWeatherIcon, 0, 0);
            _squareTopGrid.Set(_squareTopInfoGrid, 1, 0);

            _squareTopInfoGrid.SetColumns(1f);
            _squareTopInfoGrid.SetRows(0.7f, 2.4f, 0.7f);
            _squareTopInfoGrid.Set(_squareWeatherTemperatureText, 0, 0);
            _squareTopInfoGrid.Set(_squareTimeText, 0, 1);
            _squareTopInfoGrid.Set(_squareDateText, 0, 2);
        }

        void ConfigureSquareLowerLayout()
        {
            _squareLowerGrid.ClearChildren();

            // Lower half: a dedicated forecast/status strip sits above a
            // separate environment-details card containing O2 and temperature.
            _squareLowerGrid.SetColumns(1f);
            _squareLowerGrid.SetRows(0.8f, 1.2f);
            _squareLowerGrid.Set(_squareForecastCard, 0, 0);
            _squareLowerGrid.Set(_squareBottomCard, 0, 1);
        }

        void ConfigureDefaultStatusLayout()
        {
            _squareForecast.IconHeightRatio = 0.60f;
            _squareForecast.TitleHeightRatio = 0.24f;

            _squareStatusGrid.ClearChildren();
            _squareStatusGrid.SetColumns(1, 1, 1);
            _squareStatusGrid.SetRows(1f, 1f);
            _squareStatusGrid.Set(_squareForecast, 0, 0, 1, 2);
            _squareStatusGrid.Set(_squareMomentValue, 1, 0);
            _squareStatusGrid.Set(_squareWindValue, 1, 1);
            _squareStatusGrid.Set(_squareSunriseValue, 2, 0);
            _squareStatusGrid.Set(_squareSunsetValue, 2, 1);
        }

        void ConfigureTinyStatusLayout()
        {
            _squareForecast.IconHeightRatio = 0.34f;
            _squareForecast.TitleHeightRatio = 0.32f;

            _squareStatusGrid.ClearChildren();
            _squareStatusGrid.SetColumns(1f, 1f);
            _squareStatusGrid.SetRows(1f, 1f);
            _squareStatusGrid.Set(_squareMomentValue, 0, 0);
            _squareStatusGrid.Set(_squareWindValue, 0, 1);
            _squareStatusGrid.Set(_squareSunriseValue, 1, 0);
            _squareStatusGrid.Set(_squareSunsetValue, 1, 1);
        }

        void ConfigureWideTopLayout()
        {
            _squareTopGrid.ClearChildren();
            _squareTopGrid.SetColumns(1f);
            _squareTopGrid.SetRows(1f);
            _squareTopGrid.Set(_squareTopInfoGrid, 0, 0);

            _wideWeatherIconGrid.ClearChildren();
            _wideWeatherIconGrid.SetColumns(1f);
            _wideWeatherIconGrid.SetRows(1f);
            _wideWeatherIconGrid.Set(_squareWeatherIcon, 0, 0);
        }

        void ConfigureWideLowerLayout()
        {
            _squareLowerGrid.ClearChildren();
            _squareLowerGrid.SetColumns(1f);
            _squareLowerGrid.SetRows(1f);
            _squareLowerGrid.Set(_squareForecastCard, 0, 0);
        }

        static void ConfigureMetricGrid(
            Grid grid,
            SpriteIconControl icon,
            TextBlock text,
            ProgressBar bar)
        {
            grid.SetColumns(0.75f, 1.15f, 3.1f);
            grid.SetRows(1f);
            grid.Set(icon, 0, 0);
            grid.Set(text, 1, 0);
            grid.Set(bar, 2, 0);
        }

        void ConfigureLayout(ClockDashboardLayoutMode mode)
        {
            if (_layoutConfigured && _layoutMode == mode)
                return;

            _rootGrid.ClearChildren();
            switch (mode)
            {
                case ClockDashboardLayoutMode.Tiny:
                    ConfigureTinyLayout();
                    break;
                case ClockDashboardLayoutMode.Wide:
                    ConfigureWideLayout();
                    break;
                default:
                    ConfigureSquareLayout();
                    break;
            }

            _layoutMode = mode;
            _layoutConfigured = true;
        }

        void ConfigureWideLayout()
        {
            ConfigureWideTopLayout();
            ConfigureWideLowerLayout();
            ConfigureDefaultStatusLayout();

            _wideRightGrid.ClearChildren();
            _wideRightGrid.SetColumns(1f);
            _wideRightGrid.SetRows(1f, 1f, 1f);
            _wideRightGrid.Set(_wideWeatherIconCard, 0, 0);
            _wideRightGrid.Set(_squareLowerGrid, 0, 1);
            _wideRightGrid.Set(_squareBottomCard, 0, 2);

            _rootGrid.SetColumns(1.15f, 1f);
            _rootGrid.SetRows(1f);
            _rootGrid.Set(_squareTopCard, 0, 0);
            _rootGrid.Set(_wideRightGrid, 1, 0);
        }

        void ConfigureTinyLayout()
        {
            ConfigureWideTopLayout();
            ConfigureWideLowerLayout();
            ConfigureTinyStatusLayout();
            _tinyForecastCard.AddChild(_squareForecast);

            _wideRightGrid.ClearChildren();
            _wideRightGrid.SetColumns(1f);
            _wideRightGrid.SetRows(1f, 1f);
            _wideRightGrid.Set(_squareLowerGrid, 0, 0);
            _wideRightGrid.Set(_squareBottomCard, 0, 1);

            _rootGrid.SetColumns(0.15f, 0.15f, 0.40f, 0.30f);
            _rootGrid.SetRows(1f);
            _rootGrid.Set(_wideWeatherIconCard, 0, 0);
            _rootGrid.Set(_tinyForecastCard, 1, 0);
            _rootGrid.Set(_squareTopCard, 2, 0);
            _rootGrid.Set(_wideRightGrid, 3, 0);
        }

        void ConfigureSquareLayout()
        {
            ConfigureSquareTopLayout();
            ConfigureSquareLowerLayout();
            ConfigureDefaultStatusLayout();

            _rootGrid.SetColumns(1f);
            _rootGrid.SetRows(1f, 1f);
            _rootGrid.Set(_squareTopCard, 0, 0);
            _rootGrid.Set(_squareLowerGrid, 0, 1);
        }

        void BindSnapshot()
        {
            if (_snapshot == null || Host == null)
                return;

            string weatherName = _snapshot.WeatherDisplayName ?? ClockDashboardLocalization.Unavailable;
            string compactTemperature = _snapshot.HasAmbientTemperature
                ? FormatingHelper.TemperatureToString(
                    _snapshot.AmbientTemperatureNormalized,
                    _snapshot.AmbientTemperatureLevel,
                    Config.TemperatureMode)
                : ClockDashboardLocalization.Unavailable;

            _squareWeatherTemperatureText.Text = weatherName + ", " + compactTemperature;
            _squareTimeText.Text = ClockDashboardFormatter.FormatCompactTime(_snapshot.DisplayDateTime, Config);
            _squareDateText.Text = ClockDashboardFormatter.FormatShortWeekday(_snapshot.DisplayDateTime) + ", " +
                                   ClockDashboardFormatter.FormatCompactDate(_snapshot.DisplayDateTime);

            _squareWeatherIcon.Tint = Color.White;
            _squareWeatherIcon.BaseSpriteName = ClockDashboardSpriteMap.ResolveDayMomentIcon(_snapshot.DayMoment);
            _squareWeatherIcon.EffectSpriteName = ClockDashboardSpriteMap.ResolveWeatherIcon(_snapshot.WeatherSubtype);
            _squareWeatherIcon.ShowEffect = !IsClearWeather(_snapshot);

            // Lower status area.
            _squareMomentValue.IconSpriteName = ClockDashboardSpriteMap.ResolveDayMomentIcon(_snapshot.DayMoment);
            _squareMomentValue.IconTint = Color.White;
            _squareMomentValue.Text = ClockDashboardFormatter.FormatDayMoment(_snapshot.DayMoment);

            _squareWindValue.IconSpriteName = "WeatherHeavyWind";
            _squareWindValue.IconTint = Color.White;
            _squareWindValue.Text = _snapshot.HasWindSpeed
                ? ClockDashboardFormatter.FormatWindSpeed(_snapshot.WindSpeed)
                : ClockDashboardLocalization.Unavailable;

            bool hasSolarEvents = _snapshot.ClockMode == DashboardClockMode.LocalSolar &&
                                  _snapshot.HasLocalSolarTime;
            _squareSunriseValue.IconTint = Color.White;
            _squareSunriseValue.Text = hasSolarEvents && _snapshot.HasTerrainSunrise
                ? ClockDashboardFormatter.FormatSolarEventTime(_snapshot.TerrainSunriseHour, Config)
                : ClockDashboardLocalization.Unavailable;
            _squareSunsetValue.IconTint = Color.White;
            _squareSunsetValue.Text = hasSolarEvents && _snapshot.HasTerrainSunset
                ? ClockDashboardFormatter.FormatSolarEventTime(_snapshot.TerrainSunsetHour, Config)
                : ClockDashboardLocalization.Unavailable;

            if (_snapshot.HasIncomingWeather)
            {
                _squareForecast.SpriteName = ResolveForecastIcon(_snapshot);
                _squareForecast.Title = _snapshot.IncomingWeatherDisplayName ?? ClockDashboardLocalization.UnknownWeather;
                _squareForecast.Arrival = ClockDashboardFormatter.FormatIncomingArrival(
                    _snapshot,
                    Config);
            }
            else
            {
                _squareForecast.SpriteName = "WeatherSun";
                _squareForecast.Title = ClockDashboardLocalization.ClearWeather;
                _squareForecast.Arrival = string.Empty;
            }

            _squareOxygenText.Text = ClockDashboardFormatter.FormatOxygen(_snapshot.OxygenRatio);
            _squareOxygenBar.Fraction = _snapshot.OxygenRatio;
            _squareOxygenBar.FillColor = OxygenColor(_snapshot.OxygenRatio);

            string interiorTemperature = _snapshot.HasInteriorTemperature
                ? FormatingHelper.TemperatureToString(
                    _snapshot.InteriorTemperatureNormalized,
                    _snapshot.InteriorTemperatureLevel,
                    Config.TemperatureMode)
                : ClockDashboardLocalization.Unavailable;
            _squareTemperatureText.Text = interiorTemperature;
            _squareTemperatureBar.Fraction = _snapshot.HasInteriorTemperature
                ? _snapshot.InteriorTemperatureNormalized
                : 0f;
            _squareTemperatureBar.FillColor = TemperatureColor(_snapshot.InteriorTemperatureLevel);
        }

        void ConfigureResponsiveText(RectangleF bounds)
        {
            float configScale = Config?.Scale ?? 1f;
            float timeScale = MathHelper.Clamp(configScale *
                                               Math.Max(0.55f, Math.Min(bounds.Width / 512f, bounds.Height / 256f)),
                0.45f, 2.5f);
            float scale = MathHelper.Clamp(configScale *
                                           Math.Max(0.55f, Math.Min(bounds.Width, bounds.Height) / 512f),
                0.45f, 2.5f);

            _squareWeatherTemperatureText.FontScale = 1.1f * scale;
            _squareDateText.FontScale = 1.0f * scale;
            _squareTimeText.MinFontScale = 1.0f * timeScale;
            _squareTimeText.MaxFontScale = 3.2f * timeScale;
            _squareForecast.TitleFontScale = 0.8f * scale;
            _squareForecast.ArrivalFontScale = 0.7f * scale;

            bool tiny = _layoutMode == ClockDashboardLayoutMode.Tiny;
            float statusTextScale = tiny ? 0.78f : 1.0f;
            float trailingStatusTextScale = tiny ? 0.70f : 0.9f;
            float metricStatusIconRatio = tiny ? 0.96f : 0.78f;
            float trailingStatusIconRatio = tiny ? 0.96f : 0.76f;
            _squareMomentValue.FontScale = statusTextScale * scale;
            _squareWindValue.FontScale = statusTextScale * scale;
            _squareSunriseValue.TextScale = trailingStatusTextScale * scale;
            _squareSunsetValue.TextScale = trailingStatusTextScale * scale;
            _squareMomentValue.IconSizeRatio = metricStatusIconRatio;
            _squareWindValue.IconSizeRatio = metricStatusIconRatio;
            _squareSunriseValue.IconSizeRatio = trailingStatusIconRatio;
            _squareSunsetValue.IconSizeRatio = trailingStatusIconRatio;

            float environmentTextScale = tiny ? 0.78f : 1.0f;
            float environmentIconRatio = tiny ? 0.86f : 0.6f;
            _squareOxygenText.FontScale = environmentTextScale * scale;
            _squareTemperatureText.FontScale = environmentTextScale * scale;
            _squareOxygenIcon.SizeRatio = environmentIconRatio;
            _squareTemperatureIcon.SizeRatio = environmentIconRatio;

            float cardScale = MathHelper.Clamp(scale, 0.65f, 1.6f);
            _squareTopCard.CornerRadiusPixels = 10f * cardScale;
            _wideWeatherIconCard.CornerRadiusPixels = 10f * cardScale;
            _tinyForecastCard.CornerRadiusPixels = 10f * cardScale;
            _squareForecastCard.CornerRadiusPixels = 10f * cardScale;
            _squareBottomCard.CornerRadiusPixels = 10f * cardScale;
            _squareTopCard.StrokeThicknessPixels = 0f;
            _wideWeatherIconCard.StrokeThicknessPixels = 0f;
            _tinyForecastCard.StrokeThicknessPixels = 0f;
            _squareForecastCard.StrokeThicknessPixels = 0f;
            _squareBottomCard.StrokeThicknessPixels = 0f;
            _squareTopCard.ContentPaddingPixels = 7f * cardScale;
            _wideWeatherIconCard.ContentPaddingPixels = 7f * cardScale;
            _tinyForecastCard.ContentPaddingPixels = 7f * cardScale;
            _squareForecastCard.ContentPaddingPixels = 6f * cardScale;
            _squareBottomCard.ContentPaddingPixels = 7f * cardScale;
            _squareTopCard.OuterInsetPixels = new Vector4(
                3f * cardScale,
                3f * cardScale,
                3f * cardScale,
                5f * cardScale);
            _wideWeatherIconCard.OuterInsetPixels = new Vector4(
                3f * cardScale,
                3f * cardScale,
                3f * cardScale,
                5f * cardScale);
            _tinyForecastCard.OuterInsetPixels = new Vector4(
                3f * cardScale,
                3f * cardScale,
                3f * cardScale,
                5f * cardScale);
            _squareForecastCard.OuterInsetPixels = new Vector4(
                3f * cardScale,
                5f * cardScale,
                3f * cardScale,
                3f * cardScale);
            _squareBottomCard.OuterInsetPixels = new Vector4(
                3f * cardScale,
                3f * cardScale,
                3f * cardScale,
                3f * cardScale);
        }

        TextBlock CreateTextBlock(
            float fontScale,
            TextBlockVerticalAlignment verticalAlignment,
            TextAlignment textAlignment = TextAlignment.LEFT)
        {
            return new TextBlock(default(RectangleF))
            {
                FontScale = fontScale,
                Ellipsize = true,
                HorizontalAlignment = textAlignment,
                VerticalAlignment = verticalAlignment,
            };
        }

        ProgressBar CreateProgressBar()
        {
            var bar = new ProgressBar(default(RectangleF))
            {
                CornerRadius = 0f,
                ProgressBarStyle = ProgressBarStyle.PillBleed,
                Fraction = 0f
            };
            bar.SetStyleId(CLOCK_PROGRESS_BAR_STYLE_ID);
            bar.SetStyles(_clockProgressBarStyles);
            return bar;
        }

        static StyleTree BuildClockProgressBarStyles()
        {
            var styles = new StyleTree();
            styles.For<ProgressBar>()
                .Id(CLOCK_PROGRESS_BAR_STYLE_ID)
                .Set(ProgressBar.HeightRatioProperty, 0.36f)
                .Set(ProgressBar.MinHeightPixelsProperty, 3f)
                .Set(ProgressBar.MaxHeightPixelsProperty, 9f)
                .Set(ProgressBar.HorizontalInsetRatioProperty, 0.06f)
                .Set(ProgressBar.MaxHorizontalInsetPixelsProperty, 8f);
            return styles;
        }


        static string ResolveForecastIcon(ClockDashboardSnapshot snapshot)
        {
            if (snapshot == null ||
                !string.Equals(snapshot.IncomingWeatherSubtype, "Clear", StringComparison.OrdinalIgnoreCase))
            {
                return ClockDashboardSpriteMap.ResolveWeatherIcon(
                    snapshot?.IncomingWeatherSubtype);
            }

            DateTime arrival = ClockDashboardFormatter.BuildIncomingArrivalDateTime(snapshot);
            if (arrival == DateTime.MinValue)
                return "WeatherSun";

            if (snapshot.ClockMode == DashboardClockMode.LocalSolar &&
                snapshot.HasLocalSolarTime)
            {
                double arrivalHour = arrival.TimeOfDay.TotalHours;

                if (snapshot.HasTerrainSunrise && snapshot.HasTerrainSunset)
                {
                    return IsSolarHourBetween(
                        arrivalHour,
                        snapshot.TerrainSunsetHour,
                        snapshot.TerrainSunriseHour)
                        ? "WeatherMoon"
                        : "WeatherSun";
                }

                return ClockDashboardSolarTime.ClassifyLocalSolarHour(arrivalHour) == DayMoment.Night
                    ? "WeatherMoon"
                    : "WeatherSun";
            }

            return snapshot.DayMoment == DayMoment.Night
                ? "WeatherMoon"
                : "WeatherSun";
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
            string display = snapshot.WeatherDisplayName;
            return string.IsNullOrWhiteSpace(subtype) ||
                   string.Equals(subtype, "Clear", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       display,
                       ClockDashboardLocalization.ClearWeather,
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
