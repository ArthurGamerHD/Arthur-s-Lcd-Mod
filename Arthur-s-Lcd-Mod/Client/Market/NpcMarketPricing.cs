using Sandbox.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;

namespace LcdMod.Client.Market
{
    internal static class NpcMarketPricing
    {
        public static int ApplyViewerPrice(long npcFactionId, int rawPrice, StoreItemTypes listingType)
        {
            var session = MyAPIGateway.Session;
            var player = session != null ? session.Player : null;
            var factions = session != null ? session.Factions : null;
            if (player == null || factions == null)
                return rawPrice;

            var reputation = factions.GetReputationBetweenPlayerAndFaction(player.IdentityId, npcFactionId);
            if (reputation <= 0)
                return rawPrice;

            // Public mod API exposes reputation, not the economy component's internal
            // offer-bonus method. Keep the same conservative curve used by this app.
            var normalized = reputation >= 1500 ? 1f : reputation / 1500f;
            var multiplier = listingType == StoreItemTypes.Order
                ? 1f + 0.05f * normalized
                : 1f - 0.1f * normalized;
            return System.Math.Max(1, (int)(rawPrice * multiplier));
        }
    }
}
