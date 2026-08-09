using System;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Defense
{
    public struct DefenseScopeKey : IEquatable<DefenseScopeKey>
    {
        public readonly GridLinkTypeEnum LinkType;
        public readonly long[] GridEntityIds;

        public DefenseScopeKey(GridLinkTypeEnum linkType, long[] gridEntityIds)
        {
            LinkType = linkType;
            GridEntityIds = gridEntityIds ?? Array.Empty<long>();
        }

        public bool Equals(DefenseScopeKey other)
        {
            if (LinkType != other.LinkType)
                return false;

            var left = GridEntityIds ?? Array.Empty<long>();
            var right = other.GridEntityIds ?? Array.Empty<long>();
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is DefenseScopeKey && Equals((DefenseScopeKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)LinkType;
                var ids = GridEntityIds;
                if (ids != null)
                    for (int i = 0; i < ids.Length; i++)
                        hash = (hash * 397) ^ ids[i].GetHashCode();
                return hash;
            }
        }
    }
}
