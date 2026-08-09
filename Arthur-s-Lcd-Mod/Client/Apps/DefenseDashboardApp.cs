using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.GridData;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates.Custom;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Helpers;
using LcdMod.Client.Modules.Defense;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
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
    internal sealed partial class DefenseDashboardApp : App
    {
        const long WEAPON_REFRESH_FRAMES = 100L;
        const long INITIAL_WEAPON_RETRY_FRAMES = 5L;

        readonly Grid _rootGrid;
        readonly TilingPanel _shieldPanel;
        readonly TilingPanel _weaponPanel;
        readonly Dictionary<string, ShieldStatus> _shieldControls =
            new Dictionary<string, ShieldStatus>(StringComparer.Ordinal);
        readonly Dictionary<string, WeaponStatus> _weaponControls =
            new Dictionary<string, WeaponStatus>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _desiredShieldKeys = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _desiredWeaponKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, WeaponAggregate> _weaponAggregates =
            new Dictionary<string, WeaponAggregate>(StringComparer.OrdinalIgnoreCase);
        readonly List<WeaponAggregate> _sortedWeapons = new List<WeaponAggregate>();
        readonly List<IMyTerminalBlock> _weaponBlocks = new List<IMyTerminalBlock>();

        DefenseDataLease _lease;
        GridLogic _weaponGridLogic;
        Color _cardColor;
        Color _warningColor;
        Color _errorColor;
        long _lastWeaponRefreshFrame = long.MinValue;
        bool _hasShields;
        bool _hasWeapons;

        public DefenseDashboardApp(IAppHost host) : base(host)
        {
            _rootGrid = AddLogicalChild(new Grid(default(RectangleF), new[] { 1f }, new[] { 1f, 1f }));
            _shieldPanel = new TilingPanel
            {
                GapPixels = 6f,
                PaddingPixels = 3f,
                FillFromBottom = true
            };
            _weaponPanel = new TilingPanel { GapPixels = 6f, PaddingPixels = 3f };
            _rootGrid.Set(_shieldPanel, 0, 0);
            _rootGrid.Set(_weaponPanel, 0, 1);
            UpdatePresentationColors();
            CaptureLease();
        }

        public bool HasData => _hasShields || _hasWeapons;

        public override IReadOnlyList<Control> VisualChildren { get; } = new Control[] { };

        public override void Update()
        {
            CaptureLease();
            UpdatePresentationColors();

            long frame = MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
            GridLogic gridLogic = Host.GridLogic;
            if (!ReferenceEquals(_weaponGridLogic, gridLogic))
            {
                _weaponGridLogic = gridLogic;
                _lastWeaponRefreshFrame = long.MinValue;
            }

            if (_lastWeaponRefreshFrame == long.MinValue ||
                frame - _lastWeaponRefreshFrame >= WEAPON_REFRESH_FRAMES)
                RefreshWeaponBlocks(frame);

            UpdateWeaponStatuses();
        }

        public override void LayoutChanged()
        {
            _rootGrid.InvalidateLayout();
            MarkDirty();
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            float scale = Math.Max(0.25f, GeneralComponent.GetScale());
            float fontScale = Math.Max(0.25f, Host.Surface.FontSize);
            float titleOffset = Host.TitleVisible ? 40f * scale * fontScale : 0f;
            RectangleF view = Host.ViewBox;
            var content = new RectangleF(
                view.X,
                view.Y + titleOffset,
                view.Width,
                Math.Max(0f, view.Height - titleOffset));

            ConfigureSections();
            _rootGrid.Arrange(content);
            _rootGrid.Render(sprites);
            ClearDirtyAfterRender();
            return sprites;
        }

        public override void Close()
        {
            ReleaseLease();
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
            _desiredShieldKeys.Clear();
            int desiredIndex = 0;
            int count = shields?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                ShieldInfo shield = shields?[i];
                if (shield == null)
                    continue;

                string baseKey = string.IsNullOrEmpty(shield.ProviderName) ? "Shield" : shield.ProviderName;
                string key = baseKey;
                int suffix = 2;
                while (!_desiredShieldKeys.Add(key))
                    key = baseKey + "#" + suffix++;

                ShieldStatus control;
                if (!_shieldControls.TryGetValue(key, out control))
                {
                    control = new ShieldStatus(key, shield);
                    _shieldControls[key] = control;
                }
                else
                {
                    control.SetViewModel(shield);
                }

                control.SetPresentationColors(_cardColor, _warningColor, _errorColor);
                _shieldPanel.AddChild(control);
                _shieldPanel.MoveChild(control, desiredIndex++);
            }

            RemoveStaleShieldTiles();
            _hasShields = desiredIndex > 0;
            MarkDirty();
        }

        void UpdatePresentationColors()
        {
            Color cardColor = GetHeaderColor();
            Color warningColor = ColorComponent.ResolveWarningColor();
            Color errorColor = ColorComponent.ResolveErrorColor();
            bool changed = !_cardColor.Equals(cardColor) ||
                           !_warningColor.Equals(warningColor) ||
                           !_errorColor.Equals(errorColor);

            _cardColor = cardColor;
            _warningColor = warningColor;
            _errorColor = errorColor;
            if (!changed)
                return;

            foreach (var pair in _shieldControls)
                pair.Value.SetPresentationColors(_cardColor, _warningColor, _errorColor);
        }

        void RemoveStaleShieldTiles()
        {
            for (int i = _shieldPanel.VisualChildren.Count - 1; i >= 0; i--)
            {
                var control = _shieldPanel.VisualChildren[i] as ShieldStatus;
                if (control != null && !_desiredShieldKeys.Contains(control.ProviderKey))
                    _shieldPanel.RemoveChild(control);
            }
        }

        void RefreshWeaponBlocks(long frame)
        {
            List<IMyTerminalBlock> blocks = _weaponGridLogic != null
                ? _weaponGridLogic.GetTerminalBlocks<IMyTerminalBlock>()
                : null;

            _weaponBlocks.Clear();
            int blockCount = blocks != null ? blocks.Count : 0;
            for (int i = 0; i < blockCount; i++)
            {
                IMyTerminalBlock block = blocks[i];
                if (block == null || block.Closed || block.MarkedForClose || !(block is IMyUserControllableGun))
                    continue;

                _weaponBlocks.Add(block);
            }

            bool waitingForInitialGridScan = blockCount == 0 && _weaponGridLogic != null &&
                                             _weaponGridLogic.Blocks.IsRefreshRunning;
            _lastWeaponRefreshFrame = waitingForInitialGridScan
                ? frame - (WEAPON_REFRESH_FRAMES - INITIAL_WEAPON_RETRY_FRAMES)
                : frame;
        }

        void UpdateWeaponStatuses()
        {
            _weaponAggregates.Clear();
            for (int i = 0; i < _weaponBlocks.Count; i++)
            {
                IMyTerminalBlock block = _weaponBlocks[i];
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

            SyncWeaponControls();
        }

        void SyncWeaponControls()
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

            _desiredWeaponKeys.Clear();
            for (int i = 0; i < _sortedWeapons.Count; i++)
            {
                WeaponAggregate weapon = _sortedWeapons[i];
                _desiredWeaponKeys.Add(weapon.SubtypeId);

                WeaponStatus control;
                if (!_weaponControls.TryGetValue(weapon.SubtypeId, out control))
                {
                    control = new WeaponStatus(weapon.SubtypeId);
                    _weaponControls[weapon.SubtypeId] = control;
                }

                control.Bind(weapon.DisplayName, weapon.SpriteName, weapon.Total, weapon.Ready, weapon.Shooting,
                    weapon.Warning, weapon.Unavailable, _cardColor, _warningColor, _errorColor);
                _weaponPanel.AddChild(control);
                _weaponPanel.MoveChild(control, i);
            }

            RemoveStaleWeaponTiles();
            _hasWeapons = _sortedWeapons.Count > 0;
        }

        void RemoveStaleWeaponTiles()
        {
            for (int i = _weaponPanel.VisualChildren.Count - 1; i >= 0; i--)
            {
                var control = _weaponPanel.VisualChildren[i] as WeaponStatus;
                if (control != null && !_desiredWeaponKeys.Contains(control.SubtypeId))
                    _weaponPanel.RemoveChild(control);
            }
        }

        void ConfigureSections()
        {
            _shieldPanel.SetVisible(_hasShields);
            _weaponPanel.SetVisible(_hasWeapons);

            if (_hasShields && _hasWeapons)
            {
                _rootGrid.SetRows(1f, 1f);
                return;
            }

            if (_hasShields)
            {
                _rootGrid.SetRows(1f, 0f);
                return;
            }

            _rootGrid.SetRows(0f, 1f);
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

        sealed class WeaponAggregate
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
