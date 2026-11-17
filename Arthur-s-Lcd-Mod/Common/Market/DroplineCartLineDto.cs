using ProtoBuf;

namespace LcdMod.Common.Market
{
    [ProtoContract]
    public sealed class DroplineCartLineDto
    {
        [ProtoMember(1)]
        public long StationId;

        [ProtoMember(2)]
        public string StationName;

        [ProtoMember(3)]
        public string TypeId;

        [ProtoMember(4)]
        public string SubtypeId;

        [ProtoMember(5)]
        public string Name;

        [ProtoMember(6)]
        public int Quantity;

        // -1 when the station no longer lists the offer.
        [ProtoMember(7)]
        public long UnitPrice;
    }
}
