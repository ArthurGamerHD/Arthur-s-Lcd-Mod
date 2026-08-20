using System;
using Generated;
using LcdMod.Common.Audio;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    [NetworkPayload(11)]
    public sealed partial class PacketRequestBroadcastAudio
    {
        [ProtoMember(1)] public AudioBroadcastMetadata Metadata;
        [ProtoMember(2)] public byte[] RuntimeWaveBytes;

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
