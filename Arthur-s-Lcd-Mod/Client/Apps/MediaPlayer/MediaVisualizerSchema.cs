using System;
using System.Collections.Generic;
using LcdMod.Common.Config.Components;
using SeAudioVisualizer.Core;

namespace LcdMod.Client.Apps.MediaPlayer
{
    internal enum VisualizerFieldKind
    {
        Header,
        Float,
        Integer,
        Toggle,
        HiddenToggle,
        ColorMode,
        Color,
        PercentWidth,
        PercentHeight,
        Length
    }

    internal sealed class VisualizerField
    {
        const float REFERENCE_SIDE_PIXELS = 1080f;

        public readonly string Key;
        public readonly string Label;
        public readonly VisualizerFieldKind Kind;
        public readonly float Minimum;
        public readonly float Maximum;

        VisualizerField(string key, string label, VisualizerFieldKind kind, float minimum, float maximum)
        {
            Key = key;
            Label = label;
            Kind = kind;
            Minimum = minimum;
            Maximum = maximum;
        }

        public static VisualizerField Header(string label) =>
            new VisualizerField(null, label, VisualizerFieldKind.Header, 0f, 0f);
        public static VisualizerField Float(string key, string label, float minimum, float maximum) =>
            new VisualizerField(key, label, VisualizerFieldKind.Float, minimum, maximum);
        public static VisualizerField Integer(string key, string label, int minimum, int maximum) =>
            new VisualizerField(key, label, VisualizerFieldKind.Integer, minimum, maximum);
        public static VisualizerField Toggle(string key, string label) =>
            new VisualizerField(key, label, VisualizerFieldKind.Toggle, 0f, 1f);
        public static VisualizerField HiddenToggle(string key) =>
            new VisualizerField(key, null, VisualizerFieldKind.HiddenToggle, 0f, 1f);
        public static VisualizerField ColorMode() =>
            new VisualizerField(null, "Color Mode", VisualizerFieldKind.ColorMode, 0f, 0f);
        public static VisualizerField Width(string key, string label) =>
            new VisualizerField(key, label, VisualizerFieldKind.PercentWidth, 0f, 100f);
        public static VisualizerField Height(string key, string label) =>
            new VisualizerField(key, label, VisualizerFieldKind.PercentHeight, 0f, 100f);
        public static VisualizerField Color(string label) =>
            new VisualizerField("Color", label, VisualizerFieldKind.Color, 0f, 1f);

        /// <summary>A size in percent of the image's shorter side; the range is the desktop one at 1080 pixels.</summary>
        public static VisualizerField Length(string key, string label, float minimumPixels, float maximumPixels) =>
            new VisualizerField(key, label, VisualizerFieldKind.Length,
                minimumPixels / REFERENCE_SIDE_PIXELS * 100f, maximumPixels / REFERENCE_SIDE_PIXELS * 100f);
    }

    /// <summary>
    /// The configurable visualizer instances, with the ranges of the desktop visualizer's settings window. Positions
    /// are percentages of the image width or height and sizes percentages of its shorter side, so a layout keeps its
    /// proportions on any texture.
    /// </summary>
    internal sealed class VisualizerInstanceSchema
    {
        public readonly string Type;
        public readonly string Label;
        public readonly AudioVisualizerFlags Flag;
        public readonly VisualizerField[] Fields;

        VisualizerInstanceSchema(string type, string label, AudioVisualizerFlags flag, params VisualizerField[] fields)
        {
            Type = type;
            Label = label;
            Flag = flag;
            Fields = fields;
        }

        public VisualizerField Find(string key)
        {
            for (int i = 0; i < Fields.Length; i++)
                if (Fields[i].Key == key)
                    return Fields[i];
            return null;
        }

        static VisualizerField[] ColorFields(string colorLabel)
        {
            return new[]
            {
                VisualizerField.Header("Colors"),
                VisualizerField.ColorMode(),
                VisualizerField.HiddenToggle("UseTimeColor"),
                VisualizerField.HiddenToggle("UseRealTimeColor"),
                VisualizerField.Toggle("InvertColor", "Invert Color"),
                VisualizerField.Color(colorLabel),
                VisualizerField.Header("Audio Data Source"),
                VisualizerField.Toggle("UseFFT", "Use FFT Data")
            };
        }

        static readonly VisualizerField[] TrailFields =
        {
            VisualizerField.Header("Fade Trail"),
            VisualizerField.Toggle("EnableFadeTrail", "Enable Fade Trail"),
            VisualizerField.Float("FadeSpeed", "Fade Speed", .8f, .99f),
            VisualizerField.Integer("TrailLength", "Trail Length", 5, 50)
        };

        static VisualizerField[] Join(params VisualizerField[][] groups)
        {
            var fields = new List<VisualizerField>();
            for (int i = 0; i < groups.Length; i++)
                fields.AddRange(groups[i]);
            return fields.ToArray();
        }

        public static readonly VisualizerInstanceSchema[] All =
        {
            new VisualizerInstanceSchema("Spectrum", "Spectrum", AudioVisualizerFlags.Spectrum, Join(
                new[]
                {
                    VisualizerField.Header("Spectrum Settings"),
                    VisualizerField.Length("Amplitude", "Amplitude (%)", .1f, 2000f),
                    VisualizerField.Length("BarWidth", "Bar Width (%)", 1f, 20f),
                    VisualizerField.Length("BarSpacing", "Bar Spacing (%)", 0f, 20f),
                    VisualizerField.Integer("BarCount", "Bar Count", 3, 1024),
                    VisualizerField.Float("Size", "Size", -256f, 500f),
                    VisualizerField.Integer("BarAngle", "Bar Angle (deg)", 0, 360),
                    VisualizerField.Header("Peak Indicators"),
                    VisualizerField.Toggle("EnablePeakIndicators", "Enable Peak Indicators"),
                    VisualizerField.Length("PeakDropSpeed", "Peak Drop Speed (%)", .01f, 100f),
                    VisualizerField.Length("PeakLength", "Peak Length (%)", 1f, 50f),
                    VisualizerField.Length("PeakOffset", "Peak Offset (%)", 0f, 50f),
                    VisualizerField.Header("Position"),
                    VisualizerField.Width("PositionX", "Position X (%)"),
                    VisualizerField.Height("PositionY", "Position Y (%)")
                },
                ColorFields("Bar Color"),
                TrailFields)),
            new VisualizerInstanceSchema("Waveform", "Waveform", AudioVisualizerFlags.Waveform, Join(
                new[]
                {
                    VisualizerField.Header("Waveform Settings"),
                    VisualizerField.Float("Amplitude", "Amplitude", .1f, 500f),
                    VisualizerField.Length("LineThickness", "Line Thickness (%)", 1f, 10f),
                    VisualizerField.Toggle("MirrorH", "Mirrors Waveform Horizontally"),
                    VisualizerField.Toggle("MirrorV", "Mirrors Waveform Vertically"),
                    VisualizerField.Header("Position"),
                    VisualizerField.Height("PositionY", "Position Y (%)"),
                    VisualizerField.Width("StartX", "Start X (%)"),
                    VisualizerField.Width("EndX", "End X (%)")
                },
                ColorFields("Color"),
                TrailFields)),
            new VisualizerInstanceSchema("Triangle", "Triangle", AudioVisualizerFlags.Triangle, Join(
                new[]
                {
                    VisualizerField.Header("Triangle Settings"),
                    VisualizerField.Length("BaseSize", "Base Size (%)", 10f, 300f),
                    VisualizerField.Length("Amplitude", "Amplitude (%)", 100f, 2000f),
                    VisualizerField.Toggle("Filled", "Filled Triangle"),
                    VisualizerField.Length("LineThickness", "Line Thickness (%)", 1f, 10f),
                    VisualizerField.Float("Sensitivity", "Audio Sensitivity", .1f, 10f),
                    VisualizerField.Header("Position"),
                    VisualizerField.Width("PositionX", "Position X (%)"),
                    VisualizerField.Height("PositionY", "Position Y (%)"),
                    VisualizerField.Header("Rotation"),
                    VisualizerField.Integer("RotationSpeed", "Rotation Speed (deg/frame)", -90, 90),
                    VisualizerField.Integer("CurrentAngle", "Manual Angle", 0, 360)
                },
                ColorFields("Triangle Color"),
                TrailFields)),
            new VisualizerInstanceSchema("Circle", "Circle", AudioVisualizerFlags.Circle, Join(
                new[]
                {
                    VisualizerField.Header("Circle Settings"),
                    VisualizerField.Length("CircleSize", "Circle Size (%)", 5f, 150f),
                    VisualizerField.Integer("DotsMin", "Minimum Dots", 10, 1000),
                    VisualizerField.Integer("DotsMax", "Maximum Dots", 10, 1000),
                    VisualizerField.Length("DotSize", "Dot Size (%)", 1f, 20f),
                    VisualizerField.Float("Sensitivity", "Audio Sensitivity", .1f, 10f),
                    VisualizerField.Header("Position"),
                    VisualizerField.Width("PositionX", "Position X (%)"),
                    VisualizerField.Height("PositionY", "Position Y (%)")
                },
                ColorFields("Circle Color"),
                TrailFields)),
            new VisualizerInstanceSchema("CustomShader", "Shader", AudioVisualizerFlags.CustomShader, Join(
                new[]
                {
                    VisualizerField.Header("Shader Settings"),
                    VisualizerField.Float("Intensity", "Intensity", .1f, 20f),
                    VisualizerField.Float("Scale", "Scale", .1f, 2f),
                    VisualizerField.Float("Speed", "Speed", .1f, 4f),
                    VisualizerField.Float("AudioReactivity", "Audio Reactivity", 0f, 1.5f),
                    VisualizerField.Header("Position"),
                    VisualizerField.Width("PositionX", "Position X (%)"),
                    VisualizerField.Height("PositionY", "Position Y (%)")
                },
                ColorFields("Base Color")))
        };

        public static VisualizerInstanceSchema Get(string type)
        {
            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i].Type, type, StringComparison.OrdinalIgnoreCase))
                    return All[i];
            return null;
        }
    }

    /// <summary>
    /// The default settings, read once from the XML file shipped with the mod (the desktop visualizer's saved
    /// configuration, rescaled to percentages). The host sets <see cref="Loader"/>; without it there are no defaults.
    /// </summary>
    internal static class MediaVisualizerDefaults
    {
        public const string FILE = "Data/Visualizer/DefaultVisualizer.xml";

        static Func<MediaVisualizerInstanceSettings[]> _loader;
        static MediaVisualizerInstanceSettings[] _instances;

        public static Func<MediaVisualizerInstanceSettings[]> Loader
        {
            get { return _loader; }
            set
            {
                _loader = value;
                _instances = null;
            }
        }

        public static MediaVisualizerInstanceSettings[] Instances
        {
            get
            {
                if (_instances == null)
                    _instances = (_loader == null ? null : _loader()) ?? Array.Empty<MediaVisualizerInstanceSettings>();
                return _instances;
            }
        }
    }

    /// <summary>Reads stored instance settings against the schema and pushes them into the visualizer core.</summary>
    internal static class MediaVisualizerSettingsResolver
    {
        public static MediaVisualizerInstanceSettings NewDefault(VisualizerInstanceSchema schema)
        {
            var original = Find(MediaVisualizerDefaults.Instances, schema.Type);
            if (original != null)
            {
                var result = Expand(new[] { original })[0];
                result.Enabled = true;
                return result;
            }

            var instance = new MediaVisualizerInstanceSettings { Type = schema.Type, Enabled = true };
            if (schema.Type == "Circle")
            {
                // Circle is available in Linux but absent from the shipped layout.
                // Convert its core defaults from the 1080px reference side to percentages.
                var c = new CircleConfig();
                float percent = 100f / 1080f;
                instance.Settings = new[]
                {
                    new MediaVisualizerSetting { Key = "CircleSize", Value = c.CircleSize * percent },
                    new MediaVisualizerSetting { Key = "DotsMin", Value = c.DotsMin },
                    new MediaVisualizerSetting { Key = "DotsMax", Value = c.DotsMax },
                    new MediaVisualizerSetting { Key = "DotSize", Value = c.DotSize * percent },
                    new MediaVisualizerSetting { Key = "Sensitivity", Value = c.Sensitivity },
                    new MediaVisualizerSetting { Key = "PositionX", Value = 50f },
                    new MediaVisualizerSetting { Key = "PositionY", Value = 50f },
                    new MediaVisualizerSetting { Key = "ColorR", Value = c.Color.X },
                    new MediaVisualizerSetting { Key = "ColorG", Value = c.Color.Y },
                    new MediaVisualizerSetting { Key = "ColorB", Value = c.Color.Z },
                    new MediaVisualizerSetting { Key = "UseTimeColor", Value = c.UseTimeColor ? 1f : 0f },
                    new MediaVisualizerSetting { Key = "UseRealTimeColor", Value = c.UseRealTimeColor ? 1f : 0f },
                    new MediaVisualizerSetting { Key = "InvertColor", Value = c.InvertColor ? 1f : 0f },
                    new MediaVisualizerSetting { Key = "EnableFadeTrail", Value = c.EnableFadeTrail ? 1f : 0f },
                    new MediaVisualizerSetting { Key = "FadeSpeed", Value = c.FadeSpeed },
                    new MediaVisualizerSetting { Key = "TrailLength", Value = c.TrailLength },
                    new MediaVisualizerSetting { Key = "UseFFT", Value = c.UseFFT ? 1f : 0f }
                };
            }
            return Expand(new[] { instance })[0];
        }

        public static MediaVisualizerInstanceSettings Find(MediaVisualizerInstanceSettings[] instances, string type)
        {
            if (instances == null)
                return null;
            for (int i = 0; i < instances.Length; i++)
                if (instances[i] != null && string.Equals(instances[i].Type, type, StringComparison.OrdinalIgnoreCase))
                    return instances[i];
            return null;
        }

        public static bool IsEnabled(MediaVisualizerInstanceSettings[] instances, VisualizerInstanceSchema schema)
        {
            var instance = Find(instances ?? MediaVisualizerDefaults.Instances, schema.Type);
            return instance != null && instance.Enabled;
        }

        public static float Get(MediaVisualizerInstanceSettings[] instances, VisualizerInstanceSchema schema, string key)
        {
            return Get(Find(instances, schema.Type), schema, key);
        }

        public static float Get(MediaVisualizerInstanceSettings instance, VisualizerInstanceSchema schema, string key)
        {
            float value;
            if (TryGetStored(instance, key, out value) ||
                TryGetStored(Find(MediaVisualizerDefaults.Instances, schema.Type), key, out value))
                return Clamp(schema, key, value);
            return Fallback(schema, key);
        }

        static float Fallback(VisualizerInstanceSchema schema, string key)
        {
            if (key == "ColorR" || key == "ColorG" || key == "ColorB")
                return 1f;
            VisualizerField field = schema.Find(key);
            return field == null ? 0f : field.Minimum;
        }

        static float Clamp(VisualizerInstanceSchema schema, string key, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return Fallback(schema, key);
            if (key == "ColorR" || key == "ColorG" || key == "ColorB")
                return Math.Max(0f, Math.Min(1f, value));
            VisualizerField field = schema.Find(key);
            if (field == null)
                return value;
            value = Math.Max(field.Minimum, Math.Min(field.Maximum, value));
            if (field.Kind == VisualizerFieldKind.Integer)
                value = (float)Math.Round(value);
            else if (field.Kind == VisualizerFieldKind.Toggle || field.Kind == VisualizerFieldKind.HiddenToggle)
                value = value >= .5f ? 1f : 0f;
            return value;
        }

        static bool TryGetStored(MediaVisualizerInstanceSettings instance, string key, out float value)
        {
            value = 0f;
            if (instance == null || instance.Settings == null)
                return false;
            for (int i = 0; i < instance.Settings.Length; i++)
            {
                var setting = instance.Settings[i];
                if (setting != null && setting.Key == key)
                {
                    value = setting.Value;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Every instance with every value spelled out, as written by export and the settings window.</summary>
        public static MediaVisualizerInstanceSettings[] Expand(MediaVisualizerInstanceSettings[] instances)
        {
            var source = instances ?? MediaVisualizerDefaults.Instances;
            var result = new List<MediaVisualizerInstanceSettings>();
            for (int i = 0; i < source.Length; i++)
            {
                var instance = source[i];
                if (instance == null) continue;
                VisualizerInstanceSchema schema = VisualizerInstanceSchema.Get(instance.Type);
                if (schema == null)
                {
                    result.Add(instance.Clone());
                    continue;
                }
                var settings = new List<MediaVisualizerSetting>();
                foreach (string key in Keys(schema))
                    settings.Add(new MediaVisualizerSetting { Key = key, Value = Get(instance, schema, key) });
                result.Add(new MediaVisualizerInstanceSettings
                {
                    Type = instance.Type,
                    Enabled = instance.Enabled,
                    Settings = settings.ToArray()
                });
            }
            return result.ToArray();
        }

        public static IEnumerable<string> Keys(VisualizerInstanceSchema schema)
        {
            for (int i = 0; i < schema.Fields.Length; i++)
            {
                VisualizerField field = schema.Fields[i];
                if (field.Kind == VisualizerFieldKind.Header || field.Kind == VisualizerFieldKind.ColorMode)
                    continue;
                if (field.Kind == VisualizerFieldKind.Color)
                {
                    yield return "ColorR";
                    yield return "ColorG";
                    yield return "ColorB";
                }
                else
                    yield return field.Key;
            }
        }

        public static AudioVisualizerFlags EnabledFlags(MediaVisualizerInstanceSettings[] instances)
        {
            var flags = AudioVisualizerFlags.None;
            var source = instances ?? MediaVisualizerDefaults.Instances;
            for (int i = 0; i < source.Length; i++)
            {
                var instance = source[i];
                if (instance == null || !instance.Enabled) continue;
                var schema = VisualizerInstanceSchema.Get(instance.Type);
                if (schema != null) flags |= schema.Flag;
            }
            return flags;
        }

        public static void Apply(MediaVisualizerInstanceSettings[] instances, VisualizerManager manager, int width,
            int height)
        {
            var source = instances ?? MediaVisualizerDefaults.Instances;
            var visualizers = new List<IVisualizer>();
            for (int i = 0; i < source.Length; i++)
            {
                var instance = source[i];
                if (instance == null) continue;
                VisualizerInstanceSchema schema = VisualizerInstanceSchema.Get(instance.Type);
                if (schema == null) continue;
                IVisualizer visualizer;
                switch (schema.Type)
                {
                    case "Spectrum": visualizer = new SpectrumVisualizer(); break;
                    case "Waveform": visualizer = new WaveformVisualizer(); break;
                    case "Triangle": visualizer = new TriangleVisualizer(); break;
                    case "Circle": visualizer = new CircleVisualizer(); break;
                    case "CustomShader": visualizer = new CustomShaderVisualizer(); break;
                    default: continue;
                }
                visualizer.Initialize(width, height);
                visualizer.IsEnabled = instance.Enabled;
                visualizers.Add(visualizer);
                Func<string, float> get = key => Get(instance, schema, key);
                Func<string, bool> on = key => get(key) >= .5f;
                Func<string, int> x = key => (int)Math.Round(get(key) / 100f * width);
                Func<string, int> y = key => (int)Math.Round(get(key) / 100f * height);
                float side = Math.Min(width, height);
                Func<string, float> length = key => get(key) / 100f * side;
                var color = new Color3(get("ColorR"), get("ColorG"), get("ColorB"));
                switch (schema.Type)
                {
                    case "Spectrum":
                        var spectrum = (SpectrumVisualizer)visualizer;
                        SpectrumConfig s = spectrum.Config;
                        s.Amplitude = length("Amplitude");
                        s.BarWidth = length("BarWidth");
                        s.BarSpacing = length("BarSpacing");
                        s.BarCount = (int)get("BarCount");
                        s.Size = get("Size");
                        s.BarAngle = (int)get("BarAngle");
                        s.EnablePeakIndicators = on("EnablePeakIndicators");
                        s.PeakDropSpeed = length("PeakDropSpeed");
                        s.PeakLength = length("PeakLength");
                        s.PeakOffset = length("PeakOffset");
                        s.PositionX = x("PositionX");
                        s.PositionY = y("PositionY");
                        s.Color = color;
                        s.UseTimeColor = on("UseTimeColor");
                        s.UseRealTimeColor = on("UseRealTimeColor");
                        s.InvertColor = on("InvertColor");
                        s.UseFFT = on("UseFFT");
                        s.EnableFadeTrail = on("EnableFadeTrail");
                        s.FadeSpeed = get("FadeSpeed");
                        s.TrailLength = (int)get("TrailLength");
                        break;
                    case "Waveform":
                        var waveform = (WaveformVisualizer)visualizer;
                        WaveformConfig w = waveform.Config;
                        w.Amplitude = get("Amplitude");
                        w.LineThickness = length("LineThickness");
                        w.MirrorH = on("MirrorH");
                        w.MirrorV = on("MirrorV");
                        w.PositionY = y("PositionY");
                        w.StartX = x("StartX");
                        w.EndX = x("EndX");
                        w.Color = color;
                        w.UseTimeColor = on("UseTimeColor");
                        w.UseRealTimeColor = on("UseRealTimeColor");
                        w.InvertColor = on("InvertColor");
                        w.UseFFT = on("UseFFT");
                        w.EnableFadeTrail = on("EnableFadeTrail");
                        w.FadeSpeed = get("FadeSpeed");
                        w.TrailLength = (int)get("TrailLength");
                        break;
                    case "Triangle":
                        var triangle = (TriangleVisualizer)visualizer;
                        TriangleConfig t = triangle.Config;
                        t.BaseSize = length("BaseSize");
                        t.Amplitude = length("Amplitude");
                        t.Filled = on("Filled");
                        t.LineThickness = length("LineThickness");
                        t.Sensitivity = get("Sensitivity");
                        t.PositionX = x("PositionX");
                        t.PositionY = y("PositionY");
                        t.RotationSpeed = (int)get("RotationSpeed");
                        t.CurrentAngle = (int)get("CurrentAngle");
                        t.Color = color;
                        t.UseTimeColor = on("UseTimeColor");
                        t.UseRealTimeColor = on("UseRealTimeColor");
                        t.InvertColor = on("InvertColor");
                        t.UseFFT = on("UseFFT");
                        t.EnableFadeTrail = on("EnableFadeTrail");
                        t.FadeSpeed = get("FadeSpeed");
                        t.TrailLength = (int)get("TrailLength");
                        break;
                    case "Circle":
                        var circle = (CircleVisualizer)visualizer;
                        CircleConfig c = circle.Config;
                        c.CircleSize = length("CircleSize");
                        c.DotsMin = (int)get("DotsMin");
                        c.DotsMax = Math.Max(c.DotsMin, (int)get("DotsMax"));
                        c.DotSize = length("DotSize");
                        c.Sensitivity = get("Sensitivity");
                        c.PositionX = x("PositionX");
                        c.PositionY = y("PositionY");
                        c.Color = color;
                        c.UseTimeColor = on("UseTimeColor");
                        c.UseRealTimeColor = on("UseRealTimeColor");
                        c.InvertColor = on("InvertColor");
                        c.UseFFT = on("UseFFT");
                        c.EnableFadeTrail = on("EnableFadeTrail");
                        c.FadeSpeed = get("FadeSpeed");
                        c.TrailLength = (int)get("TrailLength");
                        break;
                    case "CustomShader":
                        var shader = (CustomShaderVisualizer)visualizer;
                        CustomShaderConfig h = shader.Config;
                        h.Intensity = get("Intensity");
                        h.Scale = get("Scale");
                        h.Speed = get("Speed");
                        h.AudioReactivity = get("AudioReactivity");
                        h.PositionX = x("PositionX");
                        h.PositionY = y("PositionY");
                        h.Color = color;
                        h.UseTimeColor = on("UseTimeColor");
                        h.UseRealTimeColor = on("UseRealTimeColor");
                        h.InvertColor = on("InvertColor");
                        h.UseFFT = on("UseFFT");
                        break;
                }
            }
            manager.SetInstances(visualizers);
        }
    }
}
