using System;
using LcdMod.Common.Audio;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    public sealed class PacketSyncBroadcastAudio : NetworkPackage
    {
        [ProtoMember(1)] public long PlaybackId;
        [ProtoMember(2)] public ulong RequestedBySteamId;
        [ProtoMember(3)] public AudioBroadcastMetadata Metadata;
        [ProtoMember(4)] public byte[] RuntimeWaveBytes;

        public override PackageCode Code => PackageCode.SyncBroadcastAudio;

        public PacketSyncBroadcastAudio()
        {
            Metadata = new AudioBroadcastMetadata();
            RuntimeWaveBytes = Array.Empty<byte>();
        }
    }
}
