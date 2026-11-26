using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Defense;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using Sandbox.Game.Entities;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRageMath;
using IMyFunctionalBlock = Sandbox.ModAPI.IMyFunctionalBlock;
using IMyGunObject = VRage.Game.ModAPI.IMyGunObject<VRage.Game.ModAPI.MyDeviceBase>;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using IMyUserControllableGun = Sandbox.ModAPI.IMyUserControllableGun;

namespace LcdMod.Client.Apps
{
    [LcdApp(30)]
    [MultiFunction(SurfaceScripts.DefenseDashboardSurfaceScript.TITLE, "ShieldIcon")]
    internal sealed partial class DefenseDashboardApp : App, DefenseDashboard.IDefenseDashboardHost
    {
        readonly Dictionary<string, WeaponAggregate> _weaponAggregates =
            new Dictionary<string, WeaponAggregate>(StringComparer.OrdinalIgnoreCase);
        readonly List<WeaponAggregate> _sortedWeapons = new List<WeaponAggregate>();
        readonly DefenseDashboard.DefenseDashboardViewModel _viewModel;

        DefenseDataLease _lease;
        bool _hasShields;
        bool _hasWeapons;

        public DefenseDashboardApp(IAppHost host) : base(host)
        {
            _viewModel = new DefenseDashboard.DefenseDashboardViewModel(this);
            SetContent(new DefenseDashboard.DefenseDashboardView { DataContext = _viewModel });
            NeedGridData(GridCapability.Blocks);
            CaptureLease();
        }

        public bool HasData => _hasShields || _hasWeapons;
        internal IReadOnlyList<ShieldInfo> NativeShields =>
            _lease != null && _lease.Service != null && _lease.Service.Latest.Shields != null
                ? (IReadOnlyList<ShieldInfo>)_lease.Service.Latest.Shields
                : Array.Empty<ShieldInfo>();
        internal IReadOnlyList<WeaponAggregate> NativeWeapons => _sortedWeapons;
        internal Color NativeCardColor => ThemeColor(ThemeResources.HeaderColor);
        internal Color NativeAccentColor => ThemeColor(ThemeResources.AccentColor);
        internal Color NativeWarningColor => ColorComponent.ResolveWarningColor();
        internal Color NativeErrorColor => ColorComponent.ResolveErrorColor();
        internal Color NativeForegroundColor => ThemeColor(ThemeResources.FontColor);
        internal Color NativeMutedColor => ThemeColor(ThemeResources.MutedTextColor);
        internal Color NativeSuccessColor => ThemeColor(ThemeResources.SuccessColor);
        internal Color NativeTrackColor => ThemeColor(ThemeResources.SurfaceContainerHighestColor);
        internal string NativeTextFont => TextFont;
        internal override float WindowRenderScaling => CardScale;
        IReadOnlyList<ShieldInfo> DefenseDashboard.IDefenseDashboardHost.NativeShields => NativeShields;
        IReadOnlyList<WeaponAggregate> DefenseDashboard.IDefenseDashboardHost.NativeWeapons => NativeWeapons;
        Color DefenseDashboard.IDefenseDashboardHost.NativeCardColor => NativeCardColor;
        Color DefenseDashboard.IDefenseDashboardHost.NativeAccentColor => NativeAccentColor;
        Color DefenseDashboard.IDefenseDashboardHost.NativeWarningColor => NativeWarningColor;
        Color DefenseDashboard.IDefenseDashboardHost.NativeErrorColor => NativeErrorColor;
        Color DefenseDashboard.IDefenseDashboardHost.NativeForegroundColor => NativeForegroundColor;
        Color DefenseDashboard.IDefenseDashboardHost.NativeMutedColor => NativeMutedColor;
        Color DefenseDashboard.IDefenseDashboardHost.NativeSuccessColor => NativeSuccessColor;
        Color DefenseDashboard.IDefenseDashboardHost.NativeTrackColor => NativeTrackColor;
        string DefenseDashboard.IDefenseDashboardHost.NativeTextFont => NativeTextFont;

        public override void Update()
        {
            CaptureLease();
            UpdateWeaponStatuses(Host.GridLogic);
            _viewModel.Refresh();
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            MarkDirty();
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        public override void Close()
        {
            ReleaseLease();
            base.Close();
        }

        void CaptureLease()
        {
            var defenseData = LcdModSessionComponent.Client?.DefenseData;
            var requester = Host.GridLogic;
            const GridLinkTypeEnum linkType = GridLinkTypeEnum.Logical;
            if (defenseData == null || requester == null)
                return;

            if (_lease != null && _lease.Service != null &&
                ReferenceEquals(_lease.Service.Requester, requester) && _lease.Service.LinkType == linkType)
                return;

            ReleaseLease();
            _lease = defenseData.Capture(requester, linkType);
            if (_lease.Service != null)
            {
                _lease.Service.ShieldsChanged += OnShieldsChanged;
                OnShieldsChanged(_lease.Service);
            }
        }

        void ReleaseLease()
        {
            if (_lease == null)
                return;

            if (_lease.Service != null)
                _lease.Service.ShieldsChanged -= OnShieldsChanged;
            _lease.Dispose();
            _lease = null;
        }

        void OnShieldsChanged(DefenseDataService service)
        {
            if (_lease == null || !ReferenceEquals(_lease.Service, service))
                return;

            List<ShieldInfo> shields = service.Latest.Shields;
            _hasShields = false;
            for (int i = 0; shields != null && i < shields.Count && !_hasShields; i++)
                _hasShields = shields[i] != null;
            _viewModel.Refresh();
            MarkDirty();
        }

        void UpdateWeaponStatuses(GridLogic gridLogic)
        {
            _weaponAggregates.Clear();
            var weaponBlocks = gridLogic != null ? gridLogic.Blocks.UserControllableGuns : null;
            int blockCount = weaponBlocks != null ? weaponBlocks.Count : 0;
            for (int i = 0; i < blockCount; i++)
            {
                IMyTerminalBlock block = weaponBlocks[i];
                if (block == null || block.Closed || block.MarkedForClose)
                    continue;

                string subtype = block.BlockDefinition.SubtypeName;
                if (string.IsNullOrEmpty(subtype))
                    subtype = block.BlockDefinition.ToString();

                WeaponAggregate aggregate;
                if (!_weaponAggregates.TryGetValue(subtype, out aggregate))
                {
                    string displayName = block.DefinitionDisplayNameText;
                    aggregate = new WeaponAggregate
                    {
                        SubtypeId = subtype,
                        DisplayName = string.IsNullOrEmpty(displayName) ? subtype : displayName,
                        SpriteName = ResolveWeaponSprite(block)
                    };
                    _weaponAggregates[subtype] = aggregate;
                }

                aggregate.Total++;
                switch (GetWeaponState(block))
                {
                    case WeaponState.Shooting:
                        aggregate.Shooting++;
                        break;
                    case WeaponState.Warning:
                        aggregate.Warning++;
                        break;
                    case WeaponState.Unavailable:
                        aggregate.Unavailable++;
                        break;
                    default:
                        aggregate.Ready++;
                        break;
                }
            }

            SortWeapons();
        }

        void SortWeapons()
        {
            _sortedWeapons.Clear();
            foreach (var pair in _weaponAggregates)
                _sortedWeapons.Add(pair.Value);
            _sortedWeapons.Sort(delegate(WeaponAggregate left, WeaponAggregate right)
            {
                int byName = string.Compare(left.DisplayName, right.DisplayName,
                    StringComparison.OrdinalIgnoreCase);
                return byName != 0
                    ? byName
                    : string.Compare(left.SubtypeId, right.SubtypeId, StringComparison.OrdinalIgnoreCase);
            });

            _hasWeapons = _sortedWeapons.Count > 0;
        }

        static WeaponState GetWeaponState(IMyTerminalBlock block)
        {
            var functional = block as IMyFunctionalBlock;
            if (functional != null && (!functional.IsFunctional || !functional.Enabled))
                return WeaponState.Unavailable;

            var gun = block as IMyUserControllableGun;
            if (gun != null && gun.IsShooting)
                return WeaponState.Shooting;

            var weapon = block as IMyGunObject;
            if (weapon != null)
            {
                
                try
                {
                    if (weapon.GetAmmunitionAmount() == 0 && gun != null && gun.HasInventory && (float)gun.GetInventory(0).CurrentVolume == 0f)
                        return WeaponState.Warning;

                    MyGunStatusEnum ignoredStatus;
                    return weapon.CanShoot(MyShootActionEnum.PrimaryAction, block.OwnerId, out ignoredStatus)
                        ? WeaponState.Ready
                        : WeaponState.Warning;
                }
                catch
                {
                    // A modded gun can reject the shared API path. Fall through to the
                    // public functional state rather than depending on an internal type.
                }
            }

            return functional == null || functional.IsWorking
                ? WeaponState.Ready
                : WeaponState.Warning;
        }

        static string ResolveWeaponSprite(IMyTerminalBlock block)
        {
            try
            {
                var cubeBlock = block as MyCubeBlock;
                if (cubeBlock?.BlockDefinition != null)
                    return TextureHelper.GetOrAddTextureForBlock(cubeBlock.BlockDefinition);
            }
            catch
            {
                // Use the explicit missing-icon asset when a modded definition cannot be registered.
            }

            return "MissingIcon";
        }

        internal sealed class WeaponAggregate
        {
            public string SubtypeId;
            public string DisplayName;
            public string SpriteName;
            public int Total;
            public int Ready;
            public int Shooting;
            public int Warning;
            public int Unavailable;
        }

        enum WeaponState
        {
            Ready,
            Shooting,
            Warning,
            Unavailable
        }
    }
}
