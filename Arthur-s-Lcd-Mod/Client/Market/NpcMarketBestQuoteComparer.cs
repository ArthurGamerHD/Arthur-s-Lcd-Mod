using System;
using System.Collections.Generic;

namespace LcdMod.Client.Market
{
    internal sealed class NpcMarketBestQuoteComparer : IComparer<NpcMarketStationQuote>
    {
        readonly NpcMarketMode _mode;

        public NpcMarketBestQuoteComparer(NpcMarketMode mode)
        {
            _mode = mode;
        }

        public int Compare(NpcMarketStationQuote a, NpcMarketStationQuote b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            var price = a.PersonalizedCurrentPricePerUnit.CompareTo(b.PersonalizedCurrentPricePerUnit);
            if (_mode == NpcMarketMode.Sell)
                price = -price;
            if (price != 0)
                return price;

            var distance = a.DistanceMeters.CompareTo(b.DistanceMeters);
            if (distance != 0)
                return distance;

            var seller = string.Compare(a.SellerFactionTag, b.SellerFactionTag, StringComparison.OrdinalIgnoreCase);
            if (seller != 0)
                return seller;

            var station = string.Compare(a.StationName, b.StationName, StringComparison.OrdinalIgnoreCase);
            return station != 0 ? station : a.StationId.CompareTo(b.StationId);
        }
    }
}
