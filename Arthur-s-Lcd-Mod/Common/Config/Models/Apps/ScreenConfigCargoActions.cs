using System;
using System.ComponentModel;
using LcdMod.Common.Config.Interfaces;
using LcdMod.Common.Helpers;
using ProtoBuf;
using GridLinkTypeEnum = VRage.Game.ModAPI.GridLinkTypeEnum;

namespace LcdMod.Common.Config.Models.Apps
{
    [ProtoContract]
    public partial class ScreenConfigCargoActions : ScreenConfigInteractive, IGridGroupReference
    {
        public override int Id => 20;

        public GridLinkTypeEnum GridLinkType => (GridLinkTypeEnum)GridLinkTypeInternal;

        [ProtoMember(28)] public int SortMode { get; set; }

        [ProtoMember(29)] [DefaultValue(4)] public int UraniumLargeGridSmallReactor { get; set; } = 4;
        [ProtoMember(30)] [DefaultValue(10)] public int UraniumLargeGridLargeReactor { get; set; } = 10;
        [ProtoMember(31)] [DefaultValue(1)] public int UraniumSmallGridSmallReactor { get; set; } = 1;
        [ProtoMember(32)] [DefaultValue(5)] public int UraniumSmallGridLargeReactor { get; set; } = 5;
        [ProtoMember(33)] [DefaultValue(10)] public int AmmoDefaultPerWeapon { get; set; } = 10;

        [ProtoMember(34)] public string[] WeaponOverrideKeys { get; set; } = Array.Empty<string>();
        [ProtoMember(35)] public int[] WeaponOverrideCounts { get; set; } = Array.Empty<int>();

        [ProtoMember(36)] public int SettingsRevision { get; set; }

        [ProtoMember(37)] [DefaultValue(true)] public bool ShowConfigButton { get; set; } = true;

        // Grid-link scope of the actions (per-screen, like ShowConfigButton). Physical also picks
        // containers on docked/connected subgrids, matching the CargoFilled default.
        [ProtoMember(38)] [DefaultValue((int)GridLinkTypeEnum.Physical)]
        public int GridLinkTypeInternal { get; set; } = (int)GridLinkTypeEnum.Physical;

        /// <summary>
        ///     Copies every user-tunable setting edited by the settings dialog (sort mode, fill
        ///     targets) from <paramref name="source" />. Used to mirror the settings across all screens of
        ///     the construct that run the Cargo Actions app, so an edit made on one screen also applies to
        ///     every other screen with the same app.
        /// </summary>
        public void CopyActionSettingsFrom(ScreenConfigCargoActions source)
        {
            if (source == null || ReferenceEquals(source, this))
                return;

            SortMode = source.SortMode;
            UraniumLargeGridSmallReactor = source.UraniumLargeGridSmallReactor;
            UraniumLargeGridLargeReactor = source.UraniumLargeGridLargeReactor;
            UraniumSmallGridSmallReactor = source.UraniumSmallGridSmallReactor;
            UraniumSmallGridLargeReactor = source.UraniumSmallGridLargeReactor;
            AmmoDefaultPerWeapon = source.AmmoDefaultPerWeapon;
            WeaponOverrideKeys = CopyArray(source.WeaponOverrideKeys);
            WeaponOverrideCounts = CopyArray(source.WeaponOverrideCounts);
            SettingsRevision = source.SettingsRevision;
        }

        private static string[] CopyArray(string[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<string>();

            var copy = new string[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static int[] CopyArray(int[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<int>();

            var copy = new int[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        public FillSettings ToFillSettings()
        {
            return new FillSettings
            {
                UraniumLargeGridSmallReactor = UraniumLargeGridSmallReactor,
                UraniumLargeGridLargeReactor = UraniumLargeGridLargeReactor,
                UraniumSmallGridSmallReactor = UraniumSmallGridSmallReactor,
                UraniumSmallGridLargeReactor = UraniumSmallGridLargeReactor,
                AmmoDefaultPerWeapon = AmmoDefaultPerWeapon,
                WeaponOverrideKeys = WeaponOverrideKeys ?? Array.Empty<string>(),
                WeaponOverrideCounts = WeaponOverrideCounts ?? Array.Empty<int>()
            };
        }
    }
}
