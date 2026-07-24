using ProtoBuf;

namespace LcdMod.Common.Networking
{
    public enum MediaPlayerCommandKind
    {
        Play = 1,
        Pause = 2,
        Resume = 3,
        Stop = 4,
        Seek = 5
    }

    public enum MediaPlayerSourceKind
    {
        None = 0,
        SoundSubtype = 1,
        ContentPath = 2,
        LocalRuntimePath = 3
    }

    [ProtoContract]
    public sealed class PacketRequestMediaPlayerCommand : NetworkPackage
    {
        [ProtoMember(1)] public long BlockEntityId;
        [ProtoMember(2)] public int SurfaceIndex;
        [ProtoMember(3)] public int AppTypeId;
        [ProtoMember(4)] public MediaPlayerCommandKind Command;
        [ProtoMember(5)] public MediaPlayerSourceKind SourceKind;
        [ProtoMember(6)] public string SourceId;
        [ProtoMember(7)] public string DisplayName;
        [ProtoMember(8)] public double PositionSeconds;
        [ProtoMember(9)] public long ClientFrame;

        public override PackageCode Code => PackageCode.RequestMediaPlayerCommand;
    }

    [ProtoContract]
    public sealed class PacketSyncMediaPlayerCommand : NetworkPackage
    {
        [ProtoMember(1)] public long BlockEntityId;
        [ProtoMember(2)] public int SurfaceIndex;
        [ProtoMember(3)] public int AppTypeId;
        [ProtoMember(4)] public MediaPlayerCommandKind Command;
        [ProtoMember(5)] public MediaPlayerSourceKind SourceKind;
        [ProtoMember(6)] public string SourceId;
        [ProtoMember(7)] public string DisplayName;
        [ProtoMember(8)] public double PositionSeconds;
        [ProtoMember(9)] public long ClientFrame;
        [ProtoMember(10)] public ulong RequestedBySteamId;
        [ProtoMember(11)] public long ServerFrame;

        public override PackageCode Code => PackageCode.SyncMediaPlayerCommand;
    }
}
