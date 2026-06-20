using ProtoBuf;

namespace LcdMod.Common.Networking
{
    public enum GridRoomEnvironmentStatus : byte
    {
        Unavailable = 0,
        Processing = 1,
        Available = 2
    }

    [ProtoContract]
    public sealed class PacketSyncGridRoomEnvironment : NetworkPackage
    {
        public override PackageCode Code => PackageCode.SyncGridRoomEnvironment;

        [ProtoMember(1)]
        public long BlockEntityId;

        [ProtoMember(2)]
        public uint RequestId;

        [ProtoMember(3)]
        public GridRoomEnvironmentStatus Status;

        [ProtoMember(4)]
        public bool IsSealed;

        [ProtoMember(5)]
        public float OxygenRatio;
    }
}
