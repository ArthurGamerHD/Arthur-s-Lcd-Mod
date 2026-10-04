using System;
using System.Xml.Serialization;
using ProtoBuf;

namespace LcdMod.Common.Config.Components
{
    /// <summary>One media player visualizer instance; settings missing here fall back to the instance's defaults.</summary>
    [ProtoContract]
    public sealed class MediaVisualizerInstanceSettings
    {
        [ProtoMember(1)] [XmlAttribute] public string Type { get; set; } = string.Empty;
        [ProtoMember(2)] [XmlAttribute] public bool Enabled { get; set; }
        [ProtoMember(3)] [XmlElement("Setting")] public MediaVisualizerSetting[] Settings { get; set; } =
            Array.Empty<MediaVisualizerSetting>();

        public MediaVisualizerInstanceSettings Clone()
        {
            var settings = Settings ?? Array.Empty<MediaVisualizerSetting>();
            var copy = new MediaVisualizerSetting[settings.Length];
            for (int i = 0; i < settings.Length; i++)
                copy[i] = settings[i] == null ? null : settings[i].Clone();
            return new MediaVisualizerInstanceSettings { Type = Type, Enabled = Enabled, Settings = copy };
        }

        public static MediaVisualizerInstanceSettings[] Copy(MediaVisualizerInstanceSettings[] values)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<MediaVisualizerInstanceSettings>();
            var copy = new MediaVisualizerInstanceSettings[values.Length];
            for (int i = 0; i < values.Length; i++)
                copy[i] = values[i] == null ? null : values[i].Clone();
            return copy;
        }
    }

    [ProtoContract]
    public sealed class MediaVisualizerSetting
    {
        [ProtoMember(1)] [XmlAttribute] public string Key { get; set; } = string.Empty;
        [ProtoMember(2)] [XmlAttribute] public float Value { get; set; }

        public MediaVisualizerSetting Clone()
        {
            return new MediaVisualizerSetting { Key = Key, Value = Value };
        }
    }

    /// <summary>The XML document exchanged through export/import.</summary>
    [XmlRoot("MediaVisualizer")]
    public sealed class MediaVisualizerSettingsDocument
    {
        [XmlElement("Instance")]
        public MediaVisualizerInstanceSettings[] Instances { get; set; } = Array.Empty<MediaVisualizerInstanceSettings>();
    }
}
