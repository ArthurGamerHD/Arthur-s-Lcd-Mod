using System;
using System.Collections.Generic;

namespace LcdMod.Client.Market
{
    internal sealed class NpcMarketStationDialogComparer : IComparer<NpcMarketStationQuote>
    {
        readonly NpcMarketStationSortColumn _column;
        readonly bool _descending;

        public NpcMarketStationDialogComparer(NpcMarketStationSortColumn column, bool descending)
        {
            _column = column;
            _descending = descending;
        }

        public int Compare(NpcMarketStationQuote a, NpcMarketStationQuote b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a == null)
                return 1;
            if (b == null)
                return -1;

            var left = _descending ? b : a;
            var right = _descending ? a : b;
            int result;
            switch (_column)
            {
                case NpcMarketStationSortColumn.Distance:
                    result = left.DistanceMeters.CompareTo(right.DistanceMeters);
                    break;
                case NpcMarketStationSortColumn.Station:
                    result = string.Compare(left.StationName, right.StationName, StringComparison.OrdinalIgnoreCase);
                    break;
                case NpcMarketStationSortColumn.Trend:
                    result = left.EffectiveViewerChangePercent.CompareTo(right.EffectiveViewerChangePercent);
                    break;
                default:
                    result = left.PersonalizedCurrentPricePerUnit.CompareTo(right.PersonalizedCurrentPricePerUnit);
                    break;
            }

            if (result != 0)
                return result;

            result = a.DistanceMeters.CompareTo(b.DistanceMeters);
            return result != 0 ? result : a.StationId.CompareTo(b.StationId);
        }
    }
}
