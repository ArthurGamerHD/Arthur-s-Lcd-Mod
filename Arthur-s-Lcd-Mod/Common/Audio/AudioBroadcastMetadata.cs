#if EXPERIMENTAL
using ProtoBuf;

namespace LcdMod.Common.Audio
{
    [ProtoContract]
    public sealed class AudioBroadcastMetadata
    {
        [ProtoMember(1)] public string AssetId;
        [ProtoMember(2)] public ulong OwnerSteamId;
        [ProtoMember(3)] public string RuntimePath;
        [ProtoMember(4)] public long RuntimeByteLength;
        [ProtoMember(5)] public long PcmByteLength;
        [ProtoMember(6)] public long DurationTicks;
        [ProtoMember(7)] public string RuntimeSha256;
    }
}
#endif
