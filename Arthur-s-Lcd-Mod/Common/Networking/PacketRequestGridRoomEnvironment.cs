using Generated;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    [NetworkPayload(13)]
    public sealed partial class PacketRequestGridRoomEnvironment
    {
        public const long REQUEST_INTERVAL_TICKS = 100L;

        [ProtoMember(1)]
        public long BlockEntityId;

        [ProtoMember(2)]
        public uint RequestId;
    }
}
