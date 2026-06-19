#if EXPERIMENTAL
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
            RuntimeWaveBytes = new byte[0];
        }

        public PacketRequestBroadcastAudio(AudioBroadcastMetadata metadata, byte[] runtimeWaveBytes)
        {
            Metadata = metadata ?? new AudioBroadcastMetadata();
            RuntimeWaveBytes = runtimeWaveBytes ?? new byte[0];
        }
    }
}
#endif
