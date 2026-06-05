using LcdMod.Common.Market;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace LcdMod.Client.Market
{
    internal sealed class NpcMarketStationQuote
    {
        public string ItemKey;
        public ItemTypes ItemType;
        public StoreItemTypes StoreItemType;
        public string TypeId;
        public string SubtypeId;
        public string PrefabName;
        public int PrefabTotalPcu;
        public long StationId;
        public string StationName;
        public Vector3D StationPosition;
        public long SellerFactionId;
        public string SellerFactionTag;
        public string SellerFactionName;
        public NpcMarketStationKnowledgeFlags KnowledgeFlags;
        public int KnownByMemberCount;
        public int RawPreviousPricePerUnit;
        public int RawCurrentPricePerUnit;
        public int PersonalizedPreviousPricePerUnit;
        public int PersonalizedCurrentPricePerUnit;
        public float PersonalizedTrendPercent;
        public float RelationBenefitPercent;
        public float EffectiveViewerChangePercent;
        public int Amount;
        public double DistanceMeters;
    }
}
