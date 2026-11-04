using System;
using Acrux.Styling;
using Adk.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Thrust
{
    // Layout and localized labels are refreshed explicitly. Telemetry keeps the
    // same models and only notifies bindings whose values actually changed.
    [PreviewWith("Up.Value", "2.88 MN")]
    [PreviewWith("Up.Label", "Up:")]
    [PreviewWith("StopTime.Value", "5.6 s")]
    [PreviewWith("StopForce.Value", "1.9 g")]
    [PreviewWith("StopDistance.Value", "118 m")]
    [PreviewWith("Speed.Value", "42")]
    [PreviewWith("Right.Value", "1.44 MN")]
    [PreviewWith("Right.Label", "Right:")]
    [PreviewWith("Left.Value", "1.44 MN")]
    [PreviewWith("Left.Label", "Left:")]
    [PreviewWith("Forward.Value", "4.32 MN")]
    [PreviewWith("Forward.Label", "Forward:")]
    [PreviewWith("Down.Value", "1.44 MN")]
    [PreviewWith("Down.Label", "Down:")]
    [PreviewWith("Backward.Value", "1.44 MN")]
    [PreviewWith("Backward.Label", "Back:")]
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

        [PreviewWith(1f)]
        public float Scale => _scale;
        [PreviewWith(56f)]
        public float LegendHeight => 56f * _layoutScale;
        [PreviewWith(122f)]
        public float PanelWidth => Math.Min(122f * _layoutScale, _viewport.X * 0.25f);
        [PreviewWith("8 0 8 0")]
        public Vector4 PanelMargin => new Vector4(8f * _scale, 0f, 8f * _scale, 0f);
        [PreviewWith("130 0 130 0")]
        public Vector4 AxesMargin => new Vector4(PanelWidth + PanelMargin.X, 0f, PanelWidth + PanelMargin.Z, 0f);
        [PreviewWith(52f)]
        public float CellHeight => Math.Min(52f * _layoutScale,
            Math.Max(30f * _scale, (Math.Max(0f, _viewport.Y - LegendHeight) - PanelGap) * 0.36f));
        [PreviewWith(8f)]
        public float PanelGap => 8f * _layoutScale;
        [PreviewWith(112f)]
        public float PanelHeight => CellHeight * 2f + PanelGap;
        [PreviewWith(1f)]
        public float TelemetryOpacity => _telemetryOpacity;
        [PreviewWith("0.2 0.55 0.7")]
        public Vector3 PositiveFill => _positiveFill;
        [PreviewWith("0 0 0.1")]
        public Vector3 NegativeFill => _negativeFill;
        [PreviewWith("0 -0.8 0.2")]
        public Vector3 Gravity => _gravity;
        [PreviewWith("#C5EDFF")]
        public Color GravityColor => _gravityColor;
        public string Warning => _warning;
        [PreviewWith("#C5EDFF")]
        public Color WarningColor => _warningColor;
        [PreviewWith(0.7f)]
        public float WarningFontSize => 0.7f * _layoutScale;
        [PreviewWith(1f)]
        public float FontScale => _layoutScale;
        [PreviewWith("White")]
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
        [PreviewWith("White")]
        public string FontFamily { get; private set; }
        [PreviewWith("#C5EDFF")]
        public Color Foreground { get; private set; }
        [PreviewWith(0.46f)]
        public float FontSize { get; private set; }
        [PreviewWith(8f)]
        public float MarkerSize { get; private set; }
        [PreviewWith("6 14 0 0")]
        public Vector4 MarkerMargin { get; private set; }
        [PreviewWith("18 10.78 0 0")]
        public Vector4 LabelMargin { get; private set; }
        [PreviewWith("0 10.78 6 0")]
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
        [PreviewWith(89f)]
        public float Width { get; private set; }
        [PreviewWith(32f)]
        public float Height { get; private set; }
        [PreviewWith(1f)]
        public float FontSize { get; private set; }
        [PreviewWith("White")]
        public string FontFamily { get; private set; }
        [PreviewWith("#C5EDFF")]
        public Color Foreground { get; private set; }
        public BackgroundBrush Background { get; private set; }
        [PreviewWith("4 0 5 0")]
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
