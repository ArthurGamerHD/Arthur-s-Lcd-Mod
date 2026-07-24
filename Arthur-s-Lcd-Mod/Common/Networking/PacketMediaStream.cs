using ProtoBuf;

namespace LcdMod.Common.Networking
{
    public enum MediaStreamControlIntent
    {
        Request = 1,
        Invite = 2,
        Refused = 3,
        Accepted = 4,
        Clients = 5,
        Refresh = 6,
        Close = 7
    }

    [ProtoContract]
    public sealed class PacketMediaStreamControl : NetworkPackage
    {
        [ProtoMember(1)] public MediaStreamControlIntent Intent;
        [ProtoMember(2)] public long ClientStreamId;
        [ProtoMember(3)] public long ServerStreamId;
        [ProtoMember(4)] public ulong RequestedBySteamId;
        [ProtoMember(5)] public long BlockEntityId;
        [ProtoMember(6)] public int SurfaceIndex;
        [ProtoMember(7)] public int AppTypeId;
        [ProtoMember(8)] public string Title;
        [ProtoMember(9)] public long TotalDurationTicks;
        [ProtoMember(10)] public long ServerFrame;
        [ProtoMember(11)] public bool StopPlayback;
        [ProtoMember(12)] public ulong[] ListenerSteamIds;

        public override PackageCode Code => PackageCode.MediaStreamControl;
    }

    [ProtoContract]
    public sealed class PacketRequestMediaStreamChunk : NetworkPackage
    {
        [ProtoMember(1)] public long ClientStreamId;
        [ProtoMember(2)] public int ChunkIndex;
        [ProtoMember(3)] public byte[] PcmBytes;
        [ProtoMember(4)] public long DurationTicks;
        [ProtoMember(5)] public bool IsFinal;

        public override PackageCode Code => PackageCode.RequestMediaStreamChunk;
    }

    [ProtoContract]
    public sealed class PacketSyncMediaStreamChunk : NetworkPackage
    {
        [ProtoMember(1)] public long ServerStreamId;
        [ProtoMember(2)] public int ChunkIndex;
        [ProtoMember(3)] public byte[] PcmBytes;
        [ProtoMember(4)] public long DurationTicks;
        [ProtoMember(5)] public bool IsFinal;
        [ProtoMember(6)] public long ServerFrame;

        public override PackageCode Code => PackageCode.SyncMediaStreamChunk;
    }
}
