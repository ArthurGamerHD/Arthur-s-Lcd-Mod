using System;
using Generated;
using LcdMod.Common.Audio;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    [NetworkPayload(12)]
    public sealed partial class PacketSyncBroadcastAudio
    {
        [ProtoMember(1)] public long PlaybackId;
        [ProtoMember(2)] public ulong RequestedBySteamId;
        [ProtoMember(3)] public AudioBroadcastMetadata Metadata;
        [ProtoMember(4)] public byte[] RuntimeWaveBytes;

        public PacketSyncBroadcastAudio()
        {
            Metadata = new AudioBroadcastMetadata();
            RuntimeWaveBytes = Array.Empty<byte>();
        }
    }
}
