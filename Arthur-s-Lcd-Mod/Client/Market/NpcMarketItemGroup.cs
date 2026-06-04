using System.Collections.Generic;

namespace LcdMod.Client.Market
{
    internal sealed class NpcMarketItemGroup
    {
        public string ItemKey;
        public NpcMarketMode Mode;
        public string DisplayName;
        public string SpriteName;
        public NpcMarketRow Summary;
        public readonly List<NpcMarketStationQuote> Quotes = new List<NpcMarketStationQuote>();
    }
}
