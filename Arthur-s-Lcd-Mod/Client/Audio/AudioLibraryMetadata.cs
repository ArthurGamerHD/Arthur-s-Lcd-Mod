using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace LcdMod.Client.Audio
{
    [Serializable]
    [XmlRoot("AudioLibrary")]
    public sealed class AudioLibraryMetadata
    {
        [XmlAttribute("version")]
        public int Version = 2;

        [XmlArray("Assets")]
        [XmlArrayItem("Asset")]
        public List<AudioAssetMetadata> Assets = new List<AudioAssetMetadata>();
    }

    [Serializable]
    public sealed class AudioAssetMetadata
    {
        [XmlAttribute("id")]
        public string Id;

        [XmlAttribute("ownerSteamId")]
        public ulong OwnerSteamId;

        [XmlAttribute("sourcePath")]
        public string SourcePath;

        [XmlAttribute("sourceSha256")]
        public string SourceSha256;

        [XmlAttribute("sourceArchivePath")]
        public string SourceArchivePath;

        [XmlAttribute("sourceByteLength")]
        public long SourceByteLength;

        [XmlAttribute("runtimePath")]
        public string RuntimePath;

        [XmlAttribute("runtimeSha256")]
        public string RuntimeSha256;

        [XmlAttribute("runtimeByteLength")]
        public long RuntimeByteLength;

        [XmlAttribute("pcmByteLength")]
        public long PcmByteLength;

        [XmlAttribute("durationTicks")]
        public long DurationTicks;

        [XmlAttribute("sampleRate")]
        public int SampleRate;

        [XmlAttribute("channels")]
        public int Channels;

        [XmlAttribute("bitsPerSample")]
        public int BitsPerSample;

        [XmlAttribute("sourceSampleRate")]
        public int SourceSampleRate;

        [XmlAttribute("sourceChannels")]
        public int SourceChannels;

        [XmlAttribute("sourceBitsPerSample")]
        public int SourceBitsPerSample;

        [XmlAttribute("sourceEncoding")]
        public string SourceEncodingName;

        [XmlAttribute("wasNormalized")]
        public bool WasNormalized;

        [XmlAttribute("importedUtcTicks")]
        public long ImportedUtcTicks;
    }
}
