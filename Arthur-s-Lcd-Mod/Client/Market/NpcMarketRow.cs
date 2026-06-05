using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace LcdMod.Client.Market
{
    internal sealed class NpcMarketRow
    {
        public string ItemKey;
        public NpcMarketStationQuote BestQuote;
        public ItemTypes ItemType;
        public string TypeId;
        public string SubtypeId;
        public string PrefabName;
        public int PrefabTotalPcu;
        public string DisplayName;
        public string SpriteName;
        public StoreItemTypes StoreItemType;
        public int RawPreviousPricePerUnit;
        public int RawCurrentPricePerUnit;
        public int PersonalizedPreviousPricePerUnit;
        public int PersonalizedCurrentPricePerUnit;
        public float PersonalizedTrendPercent;
        public float RelationBenefitPercent;
        public float EffectiveViewerChangePercent;
        public int PricePerUnit;
        public int PreviousPricePerUnit;
        public int Amount;
        public float DeltaPercent;
        public long BestStationId;
        public string BestStationName;
        public Vector3D BestStationPosition;
        public long BestSellerFactionId;
        public string BestSellerFactionTag;

        public string GetSecondaryLabel()
        {
            switch (ItemType)
            {
                case ItemTypes.Grid:
                    return PrefabTotalPcu > 0 ? PrefabTotalPcu + " PCU" : string.Empty;
                case ItemTypes.Oxygen:
                case ItemTypes.Hydrogen:
                case ItemTypes.Gas:
                    return Amount > 0 ? Amount + " L" : string.Empty;
                default:
                    return string.Empty;
            }
        }
    }
}
