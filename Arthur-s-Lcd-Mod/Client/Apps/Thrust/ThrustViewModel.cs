using System;
using Adk.Gui.Styling;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Thrust
{
    // Layout and localized labels are refreshed explicitly. Telemetry keeps the
    // same models and only notifies bindings whose values actually changed.
    internal sealed class ThrustViewModel : ObservableObject
    {
        Vector2 _viewport;
        float _scale = 1f, _layoutScale = 1f, _telemetryOpacity;
        Vector3 _positiveFill, _negativeFill, _gravity;
        Color _gravityColor, _warningColor;
        string _warning;

        public ThrustLegendEntry Forward { get; } = new ThrustLegendEntry(Color.Blue);
        public ThrustLegendEntry Backward { get; } = new ThrustLegendEntry(Color.Blue);
        public ThrustLegendEntry Left { get; } = new ThrustLegendEntry(Color.Red);
        public ThrustLegendEntry Right { get; } = new ThrustLegendEntry(Color.Red);
        public ThrustLegendEntry Up { get; } = new ThrustLegendEntry(Color.Green);
        public ThrustLegendEntry Down { get; } = new ThrustLegendEntry(Color.Green);
        public ThrustTelemetryEntry Speed { get; } = new ThrustTelemetryEntry();
        public ThrustTelemetryEntry StopDistance { get; } = new ThrustTelemetryEntry();
        public ThrustTelemetryEntry StopTime { get; } = new ThrustTelemetryEntry();
        public ThrustTelemetryEntry StopForce { get; } = new ThrustTelemetryEntry();

        public float Scale => _scale;
        public float LegendHeight => 56f * _layoutScale;
        public float PanelWidth => Math.Min(122f * _layoutScale, _viewport.X * 0.25f);
        public Vector4 PanelMargin => new Vector4(8f * _scale, 0f, 8f * _scale, 0f);
        public Vector4 AxesMargin => new Vector4(PanelWidth + PanelMargin.X, 0f, PanelWidth + PanelMargin.Z, 0f);
        public float CellHeight => Math.Min(52f * _layoutScale,
            Math.Max(30f * _scale, (Math.Max(0f, _viewport.Y - LegendHeight) - PanelGap) * 0.36f));
        public float PanelGap => 8f * _layoutScale;
        public float PanelHeight => CellHeight * 2f + PanelGap;
        public float TelemetryOpacity => _telemetryOpacity;
        public Vector3 PositiveFill => _positiveFill;
        public Vector3 NegativeFill => _negativeFill;
        public Vector3 Gravity => _gravity;
        public Color GravityColor => _gravityColor;
        public string Warning => _warning;
        public Color WarningColor => _warningColor;
        public float WarningFontSize => 0.7f * _layoutScale;
        public float FontScale => _layoutScale;
        public string FontFamily { get; private set; }
        public BackgroundBrush LegendBackground { get; private set; }

        public void SetViewport(Vector2 size)
        {
            if (_viewport == size) return;
            _viewport = size;
            RaisePropertyChanged<object>(string.Empty);
        }

        public void RefreshLayout(float scale, float fontScale, string font, Color foreground,
            Color legendBackground, string[] labels)
        {
            _scale = scale;
            _layoutScale = scale * fontScale;
            FontFamily = font;
            LegendBackground = new SolidColorBrush(legendBackground);
            var legends = new[] { Forward, Backward, Left, Right, Up, Down };
            for (int i = 0; i < legends.Length; i++)
                legends[i].RefreshLayout(scale, _layoutScale, font, foreground, labels[i]);
            foreach (var entry in new[] { Speed, StopDistance, StopTime, StopForce })
                entry.RefreshLayout(_layoutScale, font, foreground);
            RaisePropertyChanged<object>(string.Empty);
        }

        public void UpdateAxes(Vector3 positive, Vector3 negative, Vector3 gravity, Color gravityColor)
        {
            RaiseAndSetIfChanged(ref _positiveFill, positive, nameof(PositiveFill));
            RaiseAndSetIfChanged(ref _negativeFill, negative, nameof(NegativeFill));
            RaiseAndSetIfChanged(ref _gravity, gravity, nameof(Gravity));
            RaiseAndSetIfChanged(ref _gravityColor, gravityColor, nameof(GravityColor));
        }

        public void UpdateWarning(string text, Color color)
        {
            RaiseAndSetIfChanged(ref _warning, text, nameof(Warning));
            RaiseAndSetIfChanged(ref _warningColor, color, nameof(WarningColor));
        }

        public void UpdateTelemetryOpacity(double speed)
        {
            RaiseAndSetIfChanged(ref _telemetryOpacity, (float)Math.Min(1d, Math.Max(0d, speed)), nameof(TelemetryOpacity));
            Speed.UpdateOpacity(_telemetryOpacity);
            StopDistance.UpdateOpacity(_telemetryOpacity);
            StopTime.UpdateOpacity(_telemetryOpacity);
            StopForce.UpdateOpacity(_telemetryOpacity);
        }
    }

    internal sealed class ThrustLegendEntry : ObservableObject
    {
        string _value;
        public ThrustLegendEntry(Color marker) { Marker = new SolidColorBrush(marker); }
        public BackgroundBrush Marker { get; }
        public string Label { get; private set; }
        public string Value => _value;
        public string FontFamily { get; private set; }
        public Color Foreground { get; private set; }
        public float FontSize { get; private set; }
        public float MarkerSize { get; private set; }
        public Vector4 MarkerMargin { get; private set; }
        public Vector4 LabelMargin { get; private set; }
        public Vector4 ValueMargin { get; private set; }

        public void RefreshLayout(float scale, float layoutScale, string font, Color foreground, string label)
        {
            Label = label;
            FontFamily = font;
            Foreground = foreground;
            FontSize = 0.46f * layoutScale;
            MarkerSize = 8f * layoutScale;
            float rowHeight = 28f * layoutScale;
            float textTop = (rowHeight - 14f * FontSize) * 0.5f;
            MarkerMargin = new Vector4(6f * scale, rowHeight * 0.5f, 0f, 0f);
            LabelMargin = new Vector4(6f * scale + MarkerSize + 4f * layoutScale, textTop, 0f, 0f);
            ValueMargin = new Vector4(0f, textTop, 6f * scale, 0f);
            RaisePropertyChanged<object>(string.Empty);
        }

        public void UpdateValue(string value) { RaiseAndSetIfChanged(ref _value, value, nameof(Value)); }
    }

    internal sealed class ThrustTelemetryEntry : ObservableObject
    {
        string _value;
        string _sourceValue;
        bool _layoutDirty = true;
        Color _baseForeground;
        float _opacity;
        public string Value => _value;
        public float Width { get; private set; }
        public float Height { get; private set; }
        public float FontSize { get; private set; }
        public string FontFamily { get; private set; }
        public Color Foreground { get; private set; }
        public BackgroundBrush Background { get; private set; }
        public Vector4 TextMargin { get; private set; }

        public void RefreshLayout(float scale, string font, Color foreground)
        {
            float hudScale = Math.Max(0.1f, scale);
            Width = 89f * hudScale;
            Height = 32f * hudScale;
            FontSize = hudScale;
            FontFamily = font;
            _baseForeground = foreground;
            RefreshColors();
            TextMargin = new Vector4(4f * hudScale, 0f, 5f * hudScale, 0f);
            _layoutDirty = true;
            RaisePropertyChanged<object>(string.Empty);
        }

        public void UpdateValue(string value) { RaiseAndSetIfChanged(ref _value, value, nameof(Value)); }

        public void UpdateMeasuredValue(string value, Func<string, float, float, string> trim)
        {
            if (!_layoutDirty && _sourceValue == value) return;
            _sourceValue = value;
            _layoutDirty = false;
            UpdateValue(trim(value, Width - TextMargin.X - TextMargin.Z, FontSize));
        }

        public void UpdateOpacity(float opacity)
        {
            if (_opacity == opacity) return;
            _opacity = opacity;
            RefreshColors();
            RaisePropertyChanged<Color>(nameof(Foreground));
            RaisePropertyChanged<BackgroundBrush>(nameof(Background));
        }

        void RefreshColors()
        {
            Foreground = _opacity >= 1f ? _baseForeground : new Color(_baseForeground, _opacity);
            Background = new TextureBrush("AH_TextBox", Color.Transparent, Foreground);
        }
    }
}
