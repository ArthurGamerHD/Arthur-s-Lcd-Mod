using Generated;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    [NetworkPayload(9)]
    public sealed partial class PacketRequestNpcMarket
    {
        [ProtoMember(1)]
        public uint RequestId;

        [ProtoMember(2)]
        public bool NoCache;

        [ProtoMember(3)]
        public long HostBlockEntityId;

        [ProtoMember(4)]
        public int HostSurfaceIndex;
    }
}
