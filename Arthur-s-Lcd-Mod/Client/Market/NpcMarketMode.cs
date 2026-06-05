using VRage.Game.ObjectBuilders.Definitions;

namespace LcdMod.Client.Market
{
    internal enum NpcMarketMode : byte
    {
        Buy = 0,
        Sell = 1
    }

    internal static class NpcMarketModeExtensions
    {
        public static StoreItemTypes ToStoreItemType(this NpcMarketMode mode)
        {
            return mode == NpcMarketMode.Buy ? StoreItemTypes.Offer : StoreItemTypes.Order;
        }
    }
}
