using System.Collections.Generic;

namespace LcdMod.Common.Market
{
    public sealed class NpcMarketSnapshotDto
    {
        public int Version;
        public long CapturedWorldElapsedTicks;
        public long CacheBuiltAtWorldElapsedTicks;
        public long NextEconomyTickWorldElapsedTicks;
        public long NextNoCacheAllowedAtWorldElapsedTicks;
        public int EconomyTickSeconds;
        public List<NpcMarketOfferDto> Offers = new List<NpcMarketOfferDto>();
    }
}
