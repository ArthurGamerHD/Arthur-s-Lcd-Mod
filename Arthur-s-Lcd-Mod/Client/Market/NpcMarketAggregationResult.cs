using System;
using System.Collections.Generic;

namespace LcdMod.Client.Market
{
    internal sealed class NpcMarketAggregationResult
    {
        public readonly List<NpcMarketRow> Rows = new List<NpcMarketRow>();
        public readonly Dictionary<string, NpcMarketItemGroup> GroupsByItemKey =
            new Dictionary<string, NpcMarketItemGroup>(StringComparer.Ordinal);
    }
}
