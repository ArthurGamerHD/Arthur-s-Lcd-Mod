using ProtoBuf;
using VRage.Game.ObjectBuilders.Definitions;

namespace LcdMod.Common.Market
{
    [ProtoContract]
    public sealed class NpcMarketOfferDto
    {
        [ProtoMember(1)]
        public string TypeId;

        [ProtoMember(2)]
        public string SubtypeId;

        [ProtoMember(3)]
        public int RawPricePerUnit;

        [ProtoMember(4)]
        public int PreviousRawPricePerUnit;

        [ProtoMember(5)]
        public int Amount;

        [ProtoMember(6)]
        public ItemTypes ItemType;

        [ProtoMember(7)]
        public string PrefabName;

        [ProtoMember(8)]
        public int PrefabTotalPcu;

        [ProtoMember(9)]
        public StoreItemTypes StoreItemType;
    }
}
