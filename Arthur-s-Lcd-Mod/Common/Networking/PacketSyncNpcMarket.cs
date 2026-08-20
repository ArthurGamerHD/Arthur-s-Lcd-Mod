using System.Collections.Generic;
using Generated;
using LcdMod.Common.Market;
using ProtoBuf;

namespace LcdMod.Common.Networking
{
    [ProtoContract]
    [NetworkPayload(10)]
    public sealed partial class PacketSyncNpcMarket
    {
        [ProtoMember(1)]
        public uint RequestId;

        [ProtoMember(2)]
        public int Version;

        [ProtoMember(3)]
        public bool WasServedFromCache;

        [ProtoMember(4)]
        public long CapturedWorldElapsedTicks;

        [ProtoMember(5)]
        public long CacheBuiltAtWorldElapsedTicks;

        [ProtoMember(6)]
        public long NextEconomyTickWorldElapsedTicks;

        [ProtoMember(7)]
        public long NextNoCacheAllowedAtWorldElapsedTicks;

        [ProtoMember(8)]
        public int EconomyTickSeconds;

        [ProtoMember(9)]
        public NpcMarketScopeDto Scope;

        [ProtoMember(10)]
        public List<NpcMarketSellerFactionDto> Sellers;
    }
}
