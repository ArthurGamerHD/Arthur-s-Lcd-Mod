using ProtoBuf;

namespace LcdMod.Common.Market
{
    public enum NpcMarketScopeMode : byte
    {
        None = 0,
        OwnerFactionUnion = 1,
        OwnerOnly = 2,
        UnownedHostBlock = 3,
        InvalidHostBlock = 4,
        AccessDenied = 5
    }

    [ProtoContract]
    public sealed class NpcMarketScopeDto
    {
        [ProtoMember(1)]
        public NpcMarketScopeMode Mode;

        [ProtoMember(2)]
        public long HostBlockEntityId;

        [ProtoMember(3)]
        public long HostOwnerIdentityId;

        [ProtoMember(4)]
        public long HostFactionId;

        [ProtoMember(5)]
        public string HostFactionTag;

        [ProtoMember(6)]
        public string HostFactionName;

        [ProtoMember(7)]
        public int ContributingMemberCount;

        [ProtoMember(8)]
        public int KnownStationCount;

        [ProtoMember(9)]
        public int HostSurfaceIndex;
    }
}
