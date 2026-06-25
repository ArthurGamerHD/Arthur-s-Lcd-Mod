using System;
using LcdMod.Common.Helpers;

namespace LcdMod.Common.Config.Components
{
    public static class CargoActionsConfigComponentExtensions
    {
        public static void CopyActionSettingsFrom(this CargoActionsConfigComponent target, CargoActionsConfigComponent source)
        {
            if (target == null || source == null || ReferenceEquals(target, source))
                return;

            target.SortMode = source.SortMode;
            target.UraniumLargeGridSmallReactor = source.UraniumLargeGridSmallReactor;
            target.UraniumLargeGridLargeReactor = source.UraniumLargeGridLargeReactor;
            target.UraniumSmallGridSmallReactor = source.UraniumSmallGridSmallReactor;
            target.UraniumSmallGridLargeReactor = source.UraniumSmallGridLargeReactor;
            target.AmmoDefaultPerWeapon = source.AmmoDefaultPerWeapon;
            target.WeaponOverrideKeys = source.WeaponOverrideKeys == null
                ? Array.Empty<string>()
                : (string[])source.WeaponOverrideKeys.Clone();
            target.WeaponOverrideCounts = source.WeaponOverrideCounts == null
                ? Array.Empty<int>()
                : (int[])source.WeaponOverrideCounts.Clone();
            target.SettingsRevision = source.SettingsRevision;
        }

        public static FillSettings ToFillSettings(this CargoActionsConfigComponent config)
        {
            return new FillSettings
            {
                UraniumLargeGridSmallReactor = config.UraniumLargeGridSmallReactor,
                UraniumLargeGridLargeReactor = config.UraniumLargeGridLargeReactor,
                UraniumSmallGridSmallReactor = config.UraniumSmallGridSmallReactor,
                UraniumSmallGridLargeReactor = config.UraniumSmallGridLargeReactor,
                AmmoDefaultPerWeapon = config.AmmoDefaultPerWeapon,
                WeaponOverrideKeys = config.WeaponOverrideKeys ?? Array.Empty<string>(),
                WeaponOverrideCounts = config.WeaponOverrideCounts ?? Array.Empty<int>()
            };
        }
    }
}
