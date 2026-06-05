using System;
using System.Collections.Generic;
using ProtoBuf;

namespace LcdMod.Common.Helpers
{
    /// <summary>
    /// User-configured targets for the cargo "fill" buttons: how much uranium each reactor category
    /// gets, and how many ammo magazines each weapon gets (a global default plus optional per-weapon-
    /// type overrides, keyed by the weapon block SubtypeId). Travels in <c>PacketFillBlocks</c> so the
    /// authoritative server runs the fill with the same numbers the player picked on the screen.
    /// </summary>
    [ProtoContract]
    public class FillSettings
    {
        [ProtoMember(1)] public int UraniumLargeGridSmallReactor { get; set; }
        [ProtoMember(2)] public int UraniumLargeGridLargeReactor { get; set; }
        [ProtoMember(3)] public int UraniumSmallGridSmallReactor { get; set; }
        [ProtoMember(4)] public int UraniumSmallGridLargeReactor { get; set; }
        [ProtoMember(5)] public int AmmoDefaultPerWeapon { get; set; }
        [ProtoMember(6)] public string[] WeaponOverrideKeys { get; set; }
        [ProtoMember(7)] public int[] WeaponOverrideCounts { get; set; }
        [ProtoIgnore] Dictionary<string, int> _weaponCache;

        public FillSettings()
        {
            WeaponOverrideKeys = Array.Empty<string>();
            WeaponOverrideCounts = Array.Empty<int>();
        }

        public static FillSettings Defaults()
        {
            return new FillSettings
            {
                UraniumLargeGridSmallReactor = 4,
                UraniumLargeGridLargeReactor = 10,
                UraniumSmallGridSmallReactor = 1,
                UraniumSmallGridLargeReactor = 5,
                AmmoDefaultPerWeapon = 10
            };
        }

        public double GetUraniumTarget(bool gridLarge, bool reactorSmall)
        {
            if (gridLarge)
                return reactorSmall ? UraniumLargeGridSmallReactor : UraniumLargeGridLargeReactor;
            return reactorSmall ? UraniumSmallGridSmallReactor : UraniumSmallGridLargeReactor;
        }

        public int GetWeaponTarget(string weaponSubtype)
        {
            if (!string.IsNullOrEmpty(weaponSubtype))
            {
                EnsureWeaponCache();
                int count;
                if (_weaponCache.TryGetValue(weaponSubtype, out count))
                    return count;
            }

            return AmmoDefaultPerWeapon;
        }

        void EnsureWeaponCache()
        {
            if (_weaponCache != null)
                return;

            _weaponCache = new Dictionary<string, int>();
            var keys = WeaponOverrideKeys;
            var counts = WeaponOverrideCounts;
            if (keys == null || counts == null)
                return;

            int n = Math.Min(keys.Length, counts.Length);
            for (int i = 0; i < n; i++)
                if (!string.IsNullOrEmpty(keys[i]))
                    _weaponCache[keys[i]] = counts[i];
        }
    }
}
