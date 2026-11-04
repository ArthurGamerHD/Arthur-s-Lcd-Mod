using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Apps.ExternalSensors;
using LcdMod.Client.Apps.StarMap;
using LcdMod.Client.Config;
using Acrux.Core.Input;
using LcdMod.Client.Helpers;
using Acrux.Cartography;
using Acrux.Render3D;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Config.Components;
using LcdMod.Common.Config.Generation;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;

namespace LcdMod.Client.Apps
{
    // Matches the Star Map's old display modes, so a screen moved over from its HUD stays in first person.
    public enum ExternalSensorsDisplayMode
    {
        ThirdPerson = 0,
        FirstPerson = 1
    }

    /// <summary>
    /// The world around the ship as its sensors see it, in the star map's 3D scene: following the ship from outside,
    /// where other grids, asteroids and missiles show only while some camera or turret has them in view (the ship's
    /// own, or an allied ship's whose antenna reaches it; the ship's own missiles always show), or through the
    /// selected camera (or the screen's reference without one), with the flight instruments of the Star Map's old HUD.
    /// </summary>
    [LcdApp(33)]
    [ConfigComponent(APP, typeof(StarMapConfigComponent), PropertyName = "StarMapComponent")]
    [ConfigComponent(CAMERA_REFERENCE, typeof(BlockReferenceConfigComponent), PropertyName = "CameraReferenceComponent")]
    public partial class ExternalSensorsApp : App, IApp, IExternalSensorsHost
    {
        public const string TITLE = MOD_PREFIX + "ExternalSensors";
        // A camera sees this much across, like the game's own camera view.
        const double CAMERA_HALF_ANGLE_RADIANS = 35d * Math.PI / 180d;
        // A turret sees a 190 degree dome around the way it is mounted (its block's up), a little below its base.
        const double TURRET_HALF_ANGLE_RADIANS = 95d * Math.PI / 180d;
        const int SCAN_TICKS = 6;
        // The ship's cameras are looked for again this often, in scans.
        const int CAMERA_REFRESH_SCANS = 20;
        // Without readable session settings: the game's default sync distance.
        const double DEFAULT_SENSOR_RANGE = 3000d;
        // Allied ships are looked for this far out: the largest antenna's broadcast range.
        const double MAXIMUM_ANTENNA_RANGE = 50000d;

        public static readonly List<MyTerminalControlComboBoxItem> DisplayModes =
            new List<MyTerminalControlComboBoxItem>
            {
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)ExternalSensorsDisplayMode.ThirdPerson,
                    Value = VRage.Utils.MyStringId.GetOrCompute(MOD_PREFIX + "ExternalSensors_ThirdPerson")
                },
                new MyTerminalControlComboBoxItem
                {
                    Key = (long)ExternalSensorsDisplayMode.FirstPerson,
                    Value = VRage.Utils.MyStringId.GetOrCompute(MOD_PREFIX + "ExternalSensors_FirstPerson")
                }
            };

        readonly ExternalSensorsViewModel _viewModel;
        readonly List<IMyCubeGrid> _group = new List<IMyCubeGrid>();
        readonly List<IMySlimBlock> _slims = new List<IMySlimBlock>();
        // Cameras and turrets: what the ship (and its allies) can see with.
        readonly List<IMyCubeBlock> _cameras = new List<IMyCubeBlock>();
        readonly List<IMyEntity> _entities = new List<IMyEntity>();
        readonly List<IMyMissile> _missiles = new List<IMyMissile>();
        readonly List<IMyCubeGrid> _allies = new List<IMyCubeGrid>();
        readonly List<IMyCubeGrid> _allyGroup = new List<IMyCubeGrid>();
        readonly List<long> _visible = new List<long>();
        readonly TargetLockReader _targetLock = new TargetLockReader();
        readonly List<IMyCubeGrid> _contacts = new List<IMyCubeGrid>();
        readonly List<IMyCubeGrid> _shipGroup = new List<IMyCubeGrid>();
        long[] _shown;
        // Starts due, so the first update scans.
        int _scanTick = SCAN_TICKS;
        // In third person: what the last scan let through besides missiles, and whether a sensor was working then.
        readonly List<long> _sensed = new List<long>();
        bool _anyCamera;
        int _cameraScans;
        bool _syncConfigNextRun;
        bool _closed;

        public ExternalSensorsApp(IAppHost host)
            : base(host)
        {
            _viewModel = new ExternalSensorsViewModel(this);
            SetBackgroundContent(new ExternalSensorsSceneView { DataContext = _viewModel });
            SetContent(new ExternalSensorsView { DataContext = _viewModel });
        }

        public bool IsFirstPerson => GeneralComponent.DisplayMode == (int)ExternalSensorsDisplayMode.FirstPerson;

        // For the FTUE tips: the view button was pressed.
        public event Action ViewToggled;

        // For the FTUE tips: the third-person camera was turned or zoomed.
        public event Action OrbitChanged
        {
            add { _viewModel.OrbitChanged += value; }
            remove { _viewModel.OrbitChanged -= value; }
        }
        public PointerCursor RequestedCursor => PointerCursor.Inherit;
        internal override float WindowRenderScaling => CardScale;

        public List<MyTerminalControlComboBoxItem> GetDisplayModes() => DisplayModes;

        public override void Update()
        {
            _targetLock.Update();
            UpdateSensors();
            _viewModel.Tick();
            if (!_syncConfigNextRun)
                return;
            _syncConfigNextRun = false;
            if (Host.Block != null && Host.ProviderConfig != null)
                ConfigManager.Sync(Host.Block, Host.ProviderConfig);
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        public override void Close()
        {
            _closed = true;
            _viewModel.Dispose();
            base.Close();
        }

        // The ship (its whole construct) always shows; in third person anything else only while a working sensor
        // on it has it in view. Looking through a camera, its own view does the same. Missiles are looked at every
        // update: they are gone before a scan or two passes.
        void UpdateSensors()
        {
            IMyCubeGrid grid = Host.Block == null ? null : Host.Block.CubeGrid;
            if (grid == null || MyAPIGateway.GridGroups == null)
                return;
            bool scan = ++_scanTick >= SCAN_TICKS;
            if (scan)
                _scanTick = 0;
            if (IsFirstPerson)
            {
                if (!scan)
                    return;
                _viewModel.Notice = SelectedCamera() == null && CameraReferenceComponent.EntityId != 0
                    ? LocHelper.GetLoc(MOD_PREFIX + "ExternalSensors_CameraLost")
                    : null;
                _shown = null;
                _viewModel.SetVisibleEntities(null);
                FindContactsInRange(grid);
                return;
            }

            if (scan)
                ScanThirdPerson(grid);
            _visible.Clear();
            _visible.AddRange(_sensed);
            AddMissiles(grid, _anyCamera);
            _visible.Sort();
            if (_shown != null && SameIds(_shown, _visible))
                return;
            _shown = _visible.ToArray();
            _viewModel.SetVisibleEntities(_shown);
        }

        void ScanThirdPerson(IMyCubeGrid grid)
        {
            _group.Clear();
            MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Logical, _group);
            if (!_group.Contains(grid))
                _group.Add(grid);
            if (_cameraScans-- <= 0)
            {
                _cameraScans = CAMERA_REFRESH_SCANS;
                FindCameras();
            }
            _visible.Clear();
            for (int i = 0; i < _group.Count; i++)
                _visible.Add(_group[i].EntityId);
            for (int i = 0; i < _allies.Count; i++)
                if (!_allies[i].MarkedForClose)
                    _visible.Add(_allies[i].EntityId);

            bool anyCamera = false;
            for (int c = 0; c < _cameras.Count; c++)
                anyCamera |= IsWorking(_cameras[c]);
            _viewModel.Notice = anyCamera ? null : LocHelper.GetLoc(MOD_PREFIX + "ExternalSensors_NoCameras");
            if (anyCamera)
                AddSeen(grid);
            // What the ship is locking on, it is aware of.
            IMyCubeGrid target;
            float percent;
            bool locked;
            if (TryGetTargetLock(out target, out percent, out locked) && !_group.Contains(target))
                _visible.Add(target.EntityId);
            _anyCamera = anyCamera;
            FindContacts(_visible);
            _sensed.Clear();
            _sensed.AddRange(_visible);
        }

        // The ship's cameras, and those of allied ships whose antenna broadcast reaches it while it has a working
        // antenna to hear them.
        // Looking through a camera, every other ship in range; its own view leaves out what it cannot see.
        void FindContactsInRange(IMyCubeGrid grid)
        {
            _group.Clear();
            MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Logical, _group);
            if (!_group.Contains(grid))
                _group.Add(grid);
            var sphere = new BoundingSphereD(grid.WorldVolume.Center, SensorRange());
            _entities.Clear();
            _entities.AddRange(MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere));
            _visible.Clear();
            for (int e = 0; e < _entities.Count; e++)
                if (_entities[e] is IMyCubeGrid)
                    _visible.Add(_entities[e].EntityId);
            _entities.Clear();
            FindContacts(_visible);
        }

        // The ships among these entities, not the screen's own: one grid per ship, its largest.
        void FindContacts(List<long> ids)
        {
            _contacts.Clear();
            for (int i = 0; i < ids.Count; i++)
            {
                var grid = MyAPIGateway.Entities.GetEntityById(ids[i]) as IMyCubeGrid;
                if (grid == null || grid.MarkedForClose || grid.Physics == null || _group.Contains(grid))
                    continue;
                _shipGroup.Clear();
                MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Mechanical, _shipGroup);
                IMyCubeGrid ship = grid;
                for (int g = 0; g < _shipGroup.Count; g++)
                    if (_shipGroup[g].WorldVolume.Radius > ship.WorldVolume.Radius)
                        ship = _shipGroup[g];
                if (!_contacts.Contains(ship) && !_group.Contains(ship))
                    _contacts.Add(ship);
            }
            _shipGroup.Clear();
        }

        List<IMyCubeGrid> IExternalSensorsHost.Contacts => _contacts;

        // Enemies always carry their outline, name and distance; any other ship only while the pointer is on it.
        bool IExternalSensorsHost.ShowsContact(IMyCubeGrid ship, bool underPointer)
        {
            return underPointer || RelationPalette.IsEnemy(ship);
        }

        IMyCubeGrid IExternalSensorsHost.LockCandidate
        {
            get
            {
                IMyCubeGrid target;
                float percent;
                bool locked;
                return TryGetTargetLock(out target, out percent, out locked) ? null : ContactOf(_targetLock.GetLockCandidate());
            }
        }

        // The grid itself when it is the game's candidate (or part of the same ship), as the lock key would ask for it.
        bool IExternalSensorsHost.RequestLock(IMyCubeGrid ship)
        {
            IMyCubeGrid candidate = _targetLock.GetLockCandidate();
            return _targetLock.RequestLock(candidate != null && ContactOf(candidate) == ship ? candidate : ship);
        }

        // The contact a grid belongs to: the contact itself, or a subgrid of its ship.
        IMyCubeGrid ContactOf(IMyCubeGrid grid)
        {
            if (grid == null)
                return null;
            for (int i = 0; i < _contacts.Count; i++)
                if (_contacts[i] == grid || MyAPIGateway.GridGroups.HasConnection(_contacts[i], grid, GridLinkTypeEnum.Mechanical))
                    return _contacts[i];
            return null;
        }

        void FindCameras()
        {
            _cameras.Clear();
            _allies.Clear();
            for (int i = 0; i < _group.Count; i++)
                AddCameras(_group[i], false);
            if (!HasWorkingAntenna(_group))
            {
                _slims.Clear();
                return;
            }
            IMyCubeGrid own = Host.Block.CubeGrid;
            Vector3D center = own.WorldVolume.Center;
            var sphere = new BoundingSphereD(center, MAXIMUM_ANTENNA_RANGE);
            _entities.Clear();
            _entities.AddRange(MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere));
            for (int e = 0; e < _entities.Count; e++)
            {
                var other = _entities[e] as IMyCubeGrid;
                if (other == null || other.MarkedForClose || _group.Contains(other) || _allies.Contains(other))
                    continue;
                _allyGroup.Clear();
                MyAPIGateway.GridGroups.GetGroup(other, GridLinkTypeEnum.Logical, _allyGroup);
                if (!_allyGroup.Contains(other))
                    _allyGroup.Add(other);
                if (!ReachesUs(_allyGroup, center))
                    continue;
                for (int g = 0; g < _allyGroup.Count; g++)
                {
                    if (_allies.Contains(_allyGroup[g]))
                        continue;
                    _allies.Add(_allyGroup[g]);
                    AddCameras(_allyGroup[g], true);
                }
            }
            _entities.Clear();
            _allyGroup.Clear();
            _slims.Clear();
        }

        void AddCameras(IMyCubeGrid grid, bool alliedOnly)
        {
            _slims.Clear();
            grid.GetBlocks(_slims, slim => slim.FatBlock is IMyCameraBlock || slim.FatBlock is IMyLargeTurretBase);
            for (int s = 0; s < _slims.Count; s++)
            {
                IMyCubeBlock sensor = _slims[s].FatBlock;
                if (!alliedOnly || IsAllied(sensor))
                    _cameras.Add(sensor);
            }
        }

        bool HasWorkingAntenna(List<IMyCubeGrid> grids)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                _slims.Clear();
                grids[i].GetBlocks(_slims, slim => slim.FatBlock is IMyRadioAntenna && slim.FatBlock.IsWorking);
                if (_slims.Count > 0)
                    return true;
            }
            return false;
        }

        // An allied construct with an antenna it owns broadcasting far enough to reach the ship.
        bool ReachesUs(List<IMyCubeGrid> grids, Vector3D center)
        {
            for (int i = 0; i < grids.Count; i++)
            {
                _slims.Clear();
                grids[i].GetBlocks(_slims, slim => slim.FatBlock is IMyRadioAntenna);
                for (int s = 0; s < _slims.Count; s++)
                {
                    var antenna = (IMyRadioAntenna)_slims[s].FatBlock;
                    if (antenna.IsWorking && antenna.EnableBroadcasting && IsAllied(antenna) &&
                        Vector3D.Distance(antenna.GetPosition(), center) <= antenna.Radius)
                        return true;
                }
            }
            return false;
        }

        static bool IsAllied(IMyCubeBlock block)
        {
            IMyPlayer player = MyAPIGateway.Session == null ? null : MyAPIGateway.Session.Player;
            if (player == null)
                return false;
            MyRelationsBetweenPlayerAndBlock relation = block.GetUserRelationToOwner(player.IdentityId);
            return relation == MyRelationsBetweenPlayerAndBlock.Owner ||
                   relation == MyRelationsBetweenPlayerAndBlock.FactionShare ||
                   relation == MyRelationsBetweenPlayerAndBlock.Friends;
        }

        // Missiles from the ship and from the allied ships it shares cameras with always; any other only while a
        // camera or turret sees it.
        void AddMissiles(IMyCubeGrid grid, bool anyCamera)
        {
            MissileScanner.Collect(new BoundingSphereD(grid.WorldVolume.Center, SensorRange()), _missiles);
            for (int i = 0; i < _missiles.Count; i++)
            {
                IMyMissile missile = _missiles[i];
                var entity = (IMyEntity)missile;
                var launcher = MyAPIGateway.Entities.GetEntityById(missile.LauncherId) as IMyCubeBlock;
                bool own = launcher != null && launcher.CubeGrid != null &&
                           (_group.Contains(launcher.CubeGrid) || _allies.Contains(launcher.CubeGrid));
                if (own || (anyCamera && IsSeen(new BoundingSphereD(entity.GetPosition(), 1d))))
                    _visible.Add(entity.EntityId);
            }
            _missiles.Clear();
        }

        static bool IsWorking(IMyCubeBlock sensor)
        {
            return sensor != null && !sensor.Closed && sensor.IsWorking;
        }

        // Other grids and asteroids (planets always draw) that some working camera or turret has inside its view.
        void AddSeen(IMyCubeGrid grid)
        {
            var sphere = new BoundingSphereD(grid.WorldVolume.Center, SensorRange());
            _entities.Clear();
            _entities.AddRange(MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere));
            for (int e = 0; e < _entities.Count; e++)
            {
                IMyEntity entity = _entities[e];
                if (entity == null || entity.MarkedForClose)
                    continue;
                var other = entity as IMyCubeGrid;
                if (other != null ? _group.Contains(other) : !(entity is IMyVoxelBase) || entity is MyPlanet)
                    continue;
                if (IsSeen(entity.WorldVolume))
                    _visible.Add(entity.EntityId);
            }
            _entities.Clear();
        }

        bool IsSeen(BoundingSphereD volume)
        {
            for (int c = 0; c < _cameras.Count; c++)
            {
                IMyCubeBlock sensor = _cameras[c];
                if (!IsWorking(sensor))
                    continue;
                bool turret = sensor is IMyLargeTurretBase;
                Vector3D toTarget = volume.Center - sensor.GetPosition();
                double distance = toTarget.Length();
                if (distance <= volume.Radius)
                    return true;
                Vector3D axis = turret ? sensor.WorldMatrix.Up : sensor.WorldMatrix.Forward;
                double angle = Math.Acos(MathHelper.Clamp(Vector3D.Dot(toTarget / distance, axis), -1d, 1d));
                if (angle - Math.Asin(Math.Min(1d, volume.Radius / distance)) <=
                    (turret ? TURRET_HALF_ANGLE_RADIANS : CAMERA_HALF_ANGLE_RADIANS))
                    return true;
            }
            return false;
        }

        // As far as the game draws grids (the view distance), never under the multiplayer sync distance.
        static double SensorRange()
        {
            var settings = MyAPIGateway.Session == null ? null : MyAPIGateway.Session.SessionSettings;
            return settings == null
                ? DEFAULT_SENSOR_RANGE
                : Math.Max(Math.Max(settings.ViewDistance, settings.SyncDistance), 1d);
        }

        static bool SameIds(long[] shown, List<long> ids)
        {
            if (shown.Length != ids.Count)
                return false;
            for (int i = 0; i < shown.Length; i++)
                if (shown[i] != ids[i])
                    return false;
            return true;
        }

        IMyCameraBlock SelectedCamera()
        {
            long id = CameraReferenceComponent.EntityId;
            if (id == 0 || Host.Block == null)
                return null;
            var camera = MyAPIGateway.Entities.GetEntityById(id) as IMyCameraBlock;
            return camera != null && IsWorking(camera) && camera.CubeGrid.IsInSameLogicalGroupAs(Host.Block.CubeGrid)
                ? camera
                : null;
        }

        MatrixD? CameraMatrix
        {
            get
            {
                if (Host.Block == null)
                    return null;
                if (IsFirstPerson)
                {
                    IMyCameraBlock camera = SelectedCamera();
                    return camera != null ? Panned(camera.WorldMatrix) : ReferenceMatrix;
                }
                return ThirdPersonMatrix();
            }
        }

        // The camera block's view nudged as the game pans it: pitch about its right, then yaw about its up.
        MatrixD Panned(MatrixD world)
        {
            float pitch = _viewModel.PanPitchDegrees;
            float yaw = _viewModel.PanYawDegrees;
            if (pitch == 0f && yaw == 0f)
                return world;
            Vector3D position = world.Translation;
            MatrixD view = MatrixD.CreateFromAxisAngle(Vector3D.Right, MathHelper.ToRadians(pitch)) *
                           MatrixD.CreateFromAxisAngle(Vector3D.Up, MathHelper.ToRadians(yaw)) * world;
            view.Translation = position;
            return view;
        }

        long IExternalSensorsHost.ViewCameraId
        {
            get
            {
                IMyCameraBlock camera = IsFirstPerson ? SelectedCamera() : null;
                return camera == null ? 0 : camera.EntityId;
            }
        }

        float IExternalSensorsHost.PanLimitDegrees
        {
            get
            {
                IMyCameraBlock camera = IsFirstPerson ? SelectedCamera() : null;
                return camera == null ? 0f : Math.Max(0f, camera.RaycastConeLimit);
            }
        }

        // Behind and above the ship by default, in the ship's own axes, looking at its centre.
        MatrixD ThirdPersonMatrix()
        {
            IMyCubeGrid grid = Host.Block.CubeGrid;
            MatrixD own = OwnOrientation();
            BoundingSphereD volume = grid.WorldVolume;
            double yaw = _viewModel.OrbitYaw;
            double pitch = _viewModel.OrbitPitch;
            Vector3D direction = own.Right * (Math.Sin(yaw) * Math.Cos(pitch)) + own.Up * Math.Sin(pitch) +
                                 own.Backward * (Math.Cos(yaw) * Math.Cos(pitch));
            Vector3D position = volume.Center + direction * (Math.Max(1d, volume.Radius) * _viewModel.OrbitDistanceRadii);
            return MatrixD.CreateWorld(position, -direction, own.Up);
        }

        // The cockpit the screen is in, the main cockpit, or the grid.
        MatrixD OwnOrientation()
        {
            var controller = Host.Block as IMyShipController;
            if (controller != null)
                return controller.WorldMatrix;
            var grid = Host.Block.CubeGrid as MyCubeGrid;
            var main = grid == null ? null : grid.MainCockpit as IMyShipController;
            return main != null ? main.WorldMatrix : Host.Block.CubeGrid.WorldMatrix;
        }

        MatrixD ReferenceMatrix
        {
            get
            {
                MatrixD world;
                if (!Host.TryGetReferenceWorldMatrix(InteractionComponent.ReferenceMode, out world))
                    return Host.Block.WorldMatrix;
                Vector3D forward = world.Forward;
                Vector3D right = world.Right;
                Vector3D up = world.Up;
                if (forward.Normalize() <= 1e-6 || right.Normalize() <= 1e-6 || up.Normalize() <= 1e-6)
                    return Host.Block.WorldMatrix;
                world.Forward = forward;
                world.Right = right;
                world.Up = up;
                return world;
            }
        }

        CartographyModule Maps.IMapHost.Cartography =>
            LcdModSessionComponent.Client != null ? LcdModSessionComponent.Client.Cartography : null;

        MatrixD? Maps.IMapHost.ReferenceWorldMatrix => CameraMatrix;

        void Maps.IMapHost.PlayClick()
        {
            Play(AudioHelper.HudClick);
        }

        void Maps.IMapHost.PlayGps()
        {
            Play(AudioHelper.HudGps3);
        }

        void Play(MySoundPair sound)
        {
            var interactive = Host as InteractiveSurfaceScript;
            if (interactive != null)
                interactive.PlaySounds(sound, true);
        }

        void Maps.IMapHost.RequestRender()
        {
            if (!_closed)
                Host.RenderSprites();
        }

        void Maps.IMapHost.RequestFrames()
        {
            if (!_closed)
                Host.RequestFrames();
        }

        StarMapConfigComponent IExternalSensorsHost.Config => StarMapComponent;
        IMyCubeBlock IExternalSensorsHost.Block => Host.Block;
        string IExternalSensorsHost.Font => TextFont;
        float IExternalSensorsHost.FontScale => Host.Surface.FontSize;
        Color IMarkerColors.HeaderColor => GetHeaderColor();
        Color IMarkerColors.WarningColor => ColorComponent.ResolveWarningColor();
        Color IMarkerColors.ErrorColor => ColorComponent.ResolveErrorColor();
        Color IExternalSensorsHost.ForegroundColor => Host.ForegroundColor;

        Vector3D IExternalSensorsHost.LinearVelocity =>
            Host.Block == null || Host.Block.CubeGrid == null ? Vector3D.Zero : (Vector3D)Host.Block.CubeGrid.LinearVelocity;

        void IExternalSensorsHost.SyncConfig()
        {
            _syncConfigNextRun = true;
        }

        void IExternalSensorsHost.ToggleView()
        {
            GeneralComponent.DisplayMode = IsFirstPerson
                ? (int)ExternalSensorsDisplayMode.ThirdPerson
                : (int)ExternalSensorsDisplayMode.FirstPerson;
            _shown = null;
            _scanTick = SCAN_TICKS;
            _syncConfigNextRun = true;
            Play(AudioHelper.HudClick);
            ViewToggled?.Invoke();
        }

        public bool TryGetTargetLock(out IMyCubeGrid target, out float percent, out bool locked)
        {
            target = null;
            percent = 0f;
            locked = false;
            if (Host.Block == null)
                return false;
            long entityId;
            long gridId = _targetLock.GetLockedGridId(Host.Block, out entityId, out percent, out locked);
            target = gridId == 0 ? null : MyAPIGateway.Entities.GetEntityById(gridId) as IMyCubeGrid;
            return target != null && !target.MarkedForClose;
        }
    }
}
