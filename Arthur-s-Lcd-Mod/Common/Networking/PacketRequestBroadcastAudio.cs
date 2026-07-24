using System;
using LcdMod.Common.Audio;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    public sealed class PacketRequestBroadcastAudio : NetworkPackage
    {
        [ProtoMember(1)] public AudioBroadcastMetadata Metadata;
        [ProtoMember(2)] public byte[] RuntimeWaveBytes;

        public override PackageCode Code => PackageCode.RequestBroadcastAudio;

        public PacketRequestBroadcastAudio()
        {
            Metadata = new AudioBroadcastMetadata();
            RuntimeWaveBytes = Array.Empty<byte>();
        }

        public PacketRequestBroadcastAudio(AudioBroadcastMetadata metadata, byte[] runtimeWaveBytes)
        {
            Metadata = metadata ?? new AudioBroadcastMetadata();
            RuntimeWaveBytes = runtimeWaveBytes ?? Array.Empty<byte>();
        }
    }
}
