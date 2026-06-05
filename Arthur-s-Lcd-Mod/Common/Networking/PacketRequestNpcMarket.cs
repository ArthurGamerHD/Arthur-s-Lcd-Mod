using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    public sealed class PacketRequestNpcMarket : NetworkPackage
    {
        public override PackageCode Code => PackageCode.RequestNpcMarket;

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
