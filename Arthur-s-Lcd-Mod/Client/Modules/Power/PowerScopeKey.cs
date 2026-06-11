using System;
using VRage.Game.ModAPI;

namespace LcdMod.Client.Modules.Power
{
    public struct PowerScopeKey : IEquatable<PowerScopeKey>
    {
        public readonly GridLinkTypeEnum LinkType;
        public readonly long[] GridEntityIds;

        public PowerScopeKey(GridLinkTypeEnum linkType, long[] gridEntityIds)
        {
            LinkType = linkType;
            GridEntityIds = gridEntityIds ?? new long[0];
        }

        public bool Equals(PowerScopeKey other)
        {
            if (LinkType != other.LinkType)
                return false;

            var a = GridEntityIds ?? new long[0];
            var b = other.GridEntityIds ?? new long[0];
            if (a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is PowerScopeKey && Equals((PowerScopeKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)LinkType;
                var ids = GridEntityIds;
                if (ids != null)
                {
                    for (int i = 0; i < ids.Length; i++)
                        hash = (hash * 397) ^ ids[i].GetHashCode();
                }

                return hash;
            }
        }
    }
}
