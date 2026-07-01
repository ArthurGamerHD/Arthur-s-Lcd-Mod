using System;
using System.Collections.Generic;
using MyItemType = VRage.Game.ModAPI.Ingame.MyItemType;

namespace LcdMod.Client.GridData
{
    public sealed class ItemSnapshot
    {
        public ItemSnapshot(
            SearchQueryToken searchToken,
            DateTime revision,
            Dictionary<MyItemType, double> items)
        {
            SearchToken = searchToken;
            Revision = revision;
            Items = items ?? new Dictionary<MyItemType, double>();
        }

        public SearchQueryToken SearchToken { get; private set; }
        public DateTime Revision { get; private set; }
        public Dictionary<MyItemType, double> Items { get; private set; }

        public bool Matches(ItemSnapshot other)
        {
            return other != null &&
                   SearchToken.Equals(other.SearchToken) &&
                   Revision == other.Revision;
        }

        public static bool ContentEquals(
            IDictionary<MyItemType, double> left,
            IDictionary<MyItemType, double> right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left == null || right == null || left.Count != right.Count)
                return false;

            foreach (var entry in left)
            {
                double value;
                if (!right.TryGetValue(entry.Key, out value) || !entry.Value.Equals(value))
                    return false;
            }

            return true;
        }
    }
}
