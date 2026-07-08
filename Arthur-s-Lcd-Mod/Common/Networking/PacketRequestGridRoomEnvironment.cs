using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    public sealed class PacketRequestGridRoomEnvironment : NetworkPackage
    {
        public const long REQUEST_INTERVAL_TICKS = 100L;

        public override PackageCode Code => PackageCode.RequestGridRoomEnvironment;

        [ProtoMember(1)]
        public long BlockEntityId;

        [ProtoMember(2)]
        public uint RequestId;
    }
}
