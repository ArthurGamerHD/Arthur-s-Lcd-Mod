using System.Collections.Generic;
using ProtoBuf;

namespace LcdMod.Common.Market
{
    [ProtoContract]
    public sealed class NpcMarketSellerFactionDto
    {
        [ProtoMember(1)]
        public long FactionId;

        [ProtoMember(2)]
        public string Tag;

        [ProtoMember(3)]
        public string Name;

        [ProtoMember(4)]
        public List<NpcMarketStationDto> Stations;
    }
}
