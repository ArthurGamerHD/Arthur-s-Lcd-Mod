using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.Apps.Radar;
using LcdMod.Client.Helpers;
using LcdMod.Client.SurfaceScripts.Abstract;
using LcdMod.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;
using SliderRadarRange = LcdMod.Client.Terminal.Controls.Generic.SliderRadarRange;
using static LcdMod.Common.Helpers.Constants;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    internal class ContactRecord
    {
        public long EntityId;
        public string Name;
        public MyRelationsBetweenPlayerAndBlock Relationship;
        public string IconTexture;
        public Vector3D WorldPosition;
        public bool IsTargeted;
        public float TargetLockPercent;
        public bool IsLockCandidate;
        public int MissedFrames;
    }

    [LcdApp(12)]

    [MultiFunction(SurfaceScripts.RadarSurfaceScript.TITLE, "GpsArrow")]

    [ConfigComponent(APP, typeof(RadarConfigComponent), PropertyName = "RadarComponent")]

    public partial class RadarApp : App, IApp, IRadarCamera
    {
        public const string ID = MOD_PREFIX + "Radar";
        public const string TITLE = MOD_PREFIX + "Radar";

        public event Action CameraMoved;
        public event Action<int> RangeScrolled;

        // Fixed ring distances (meters) drawn regardless of dynamic range
        const float RING_1_M = 800f;
        const float RING_2_M = 1400f;
        const float RING_3_M = 2000f;
        const float DEFAULT_RANGE = 3000f;
        const string LONG_RANGE_WARNING_KEY = MOD_PREFIX + "Radar_AntennaRangeWarning";

        // Timing intervals (Run() calls; each call ~= 166 ms at Update10)
        const int CONTACT_TIMEOUT = 36;

        const int MAX_CONTACTS = 32;
        const int MAX_MISSILES = 64;
        // A missile's trail, in updates (about 2.5 s), and how far ahead its line reaches.
        const int MISSILE_TRAIL_POINTS = 15;
        const float MISSILE_AHEAD_SECONDS = 5f;

        const float CAMERA_PAN_CONFIG_EPSILON_METERS = 0.001f;
        const float CAMERA_ZOOM_CONFIG_EPSILON = 0.0001f;
        const float CAMERA_MIN_ZOOM_SCALE = 0.1f;
        const float CAMERA_MAX_ZOOM_SCALE = 10f;
        const float CAMERA_DEFAULT_ZOOM_SCALE = 1f;
        const float CAMERA_MAX_PAN_RANGE_RATIO = 1f;
        const int FOOTER_SCROLL_STEP_SECONDS = 2;
        const int FOOTER_MAX_CONTACT_ROWS = 4;
        const float CAMERA_ZOOM_STEP = 1.1f;

        readonly Dictionary<long, ContactRecord> _contacts = new Dictionary<long, ContactRecord>();
        readonly HashSet<long> _seenThisFrame = new HashSet<long>();
        readonly HashSet<long> _processedGroupGridIds = new HashSet<long>();
        readonly Dictionary<long, string> _factionIconCache = new Dictionary<long, string>();
        readonly List<long> _toRemove = new List<long>();
        readonly List<ContactRecord> _sortedContacts = new List<ContactRecord>();
        readonly List<IMyCubeGrid> _tempGroupGrids = new List<IMyCubeGrid>();
        readonly List<IMyEntity> _tempEntities = new List<IMyEntity>();
        readonly RadarSignalBlockCache _signalBlocks = new RadarSignalBlockCache();
        readonly RadarViewModel _viewModel = new RadarViewModel();

        sealed class MissileTrack
        {
            public readonly List<Vector3D> Trail = new List<Vector3D>();
            public Vector3D Position;
            public Vector3D Velocity;
            // How much farther it flies before it runs out of range; infinite when it has no range.
            public double RemainingMeters;
            public Color Color;
            public bool Seen;
        }

        readonly Dictionary<long, MissileTrack> _missileTracks = new Dictionary<long, MissileTrack>();
        readonly List<IMyMissile> _missileBuffer = new List<IMyMissile>();
        readonly List<long> _missileGone = new List<long>();
        readonly List<RadarMissile> _missilePool = new List<RadarMissile>();

        float _maxRange = DEFAULT_RANGE;
        Vector2 _cameraPanMeters;
        float _cameraZoomScale = CAMERA_DEFAULT_ZOOM_SCALE;
        float _cameraYaw;
        float _cameraPitch = RadarScene.DEFAULT_PITCH;
        bool _syncConfigNextRun;
        long _debugLockedTargetEntityId;
        float _debugLockedTargetPercent;
        string _debugLockedTargetName = string.Empty;
        readonly TargetLockReader _targetLock = new TargetLockReader();
        long _lockCandidateId;

        readonly IAppHost _host;
        IMyCubeBlock Block => _host.Block;
        float FontScale => _host.Surface.FontSize;
        Color ForegroundColor => _host.ForegroundColor;
        Color BackgroundColor => _host.BackgroundColor;
        internal override float WindowRenderScaling => CardScale;

        public RadarApp(IAppHost host)
            : base(host)
        {
            _host = host;
            _viewModel.Scene.Camera = this;
            _viewModel.Scene.RingDistances = new[] { RING_1_M, RING_2_M, RING_3_M };
            _viewModel.ContactClicked += OnContactClicked;
            SetBackgroundContent(new RadarSceneView { DataContext = _viewModel });
            SetContent(new RadarView { DataContext = _viewModel });
        }

        public override void Update()
        {
            _maxRange = GetConfiguredRange();
            ApplyRadarConfig();
            SyncConfigIfNeeded();
            _targetLock.Update();
            CollectContacts();
            CollectMissiles();
            PurgeStaleContacts();
            BuildSortedContacts();
            UpdateScene();
            UpdateFooter();
        }

        public override List<MySprite> GetSprites()
        {
            return new List<MySprite>();
        }

        public override void Close()
        {
            _signalBlocks.Clear();
            base.Close();
        }

        void CollectContacts()
        {
            _seenThisFrame.Clear();
            _maxRange = GetConfiguredRange();

            CollectEntityContacts();

            foreach (var kv in _contacts)
            {
                if (_seenThisFrame.Contains(kv.Key))
                    kv.Value.MissedFrames = 0;
                else
                    kv.Value.MissedFrames++;
            }
        }

        void CollectEntityContacts()
        {
            try
            {
                var session = MyAPIGateway.Session;
                var shipPos = Block.WorldMatrix.Translation;
                float range = _maxRange;
                var ownGrid = Block.CubeGrid;
                if (ownGrid == null) return;

                long ownGridId = ownGrid.EntityId;
                long ownGridOwnerId = GetPrimaryGridOwner(ownGrid);
                long lockedTargetEntityId;
                float lockedTargetPercent;
                long lockedTargetGridId = GetLockedTargetGridId(out lockedTargetEntityId, out lockedTargetPercent);
                var normalizedLockPercent = NormalizeLockPercent(lockedTargetPercent);
                _debugLockedTargetEntityId = lockedTargetEntityId;
                _debugLockedTargetPercent = normalizedLockPercent;
                _debugLockedTargetName = ResolveGridName(lockedTargetGridId);
                IMyCubeGrid candidate = lockedTargetGridId == 0 ? _targetLock.GetLockCandidate() : null;
                _lockCandidateId = candidate == null ? 0 : candidate.EntityId;

                _tempEntities.Clear();
                var sphere = new BoundingSphereD(shipPos, range);
                // Top-most only: GetEntitiesInSphere also returns every fat block of every grid in range.
                var entitiesInSphere = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);
                if (entitiesInSphere != null)
                {
                    _tempEntities.AddRange(entitiesInSphere);
                    entitiesInSphere.Clear();
                }

                _processedGroupGridIds.Clear();
                _signalBlocks.BeginScan();
                foreach (var entity in _tempEntities)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null || grid.Closed)
                        continue;
                    if (grid.EntityId == ownGridId)
                        continue;

                    foreach (var signalBlock in _signalBlocks.Get(grid))
                    {
                        if (!signalBlock.Closed)
                            TryCollectSignalProxyContact(signalBlock, shipPos, ownGrid, ownGridOwnerId,
                                lockedTargetGridId, lockedTargetEntityId, normalizedLockPercent);
                    }
                }

                foreach (var entity in _tempEntities)
                {
                    var grid = entity as IMyCubeGrid;
                    if (grid == null || grid.Physics == null)
                        continue;
                    if (grid.EntityId == ownGridId)
                        continue;

                    if (_processedGroupGridIds.Contains(grid.EntityId))
                        continue;

                    _tempGroupGrids.Clear();
                    try
                    {
                        MyAPIGateway.GridGroups.GetGroup(grid, GridLinkTypeEnum.Logical, _tempGroupGrids);
                    }
                    catch (Exception e)
                    {
                        ErrorHandlerHelper.LogError(e, this);
                    }

                    if (_tempGroupGrids.Count == 0)
                        _tempGroupGrids.Add(grid);

                    IMyCubeGrid selectedGrid = null;
                    double selectedScore = double.MinValue;
                    bool ownGroup = false;
                    bool groupHasLockedTarget = false;
                    bool groupHasCandidate = false;

                    foreach (var groupGrid in _tempGroupGrids)
                    {
                        _processedGroupGridIds.Add(groupGrid.EntityId);
                        if (lockedTargetGridId != 0 && groupGrid.EntityId == lockedTargetGridId)
                            groupHasLockedTarget = true;
                        if (_lockCandidateId != 0 && groupGrid.EntityId == _lockCandidateId)
                            groupHasCandidate = true;

                        if (groupGrid.EntityId == ownGridId)
                        {
                            ownGroup = true;
                            continue;
                        }

                        if (groupGrid.Physics == null) continue;

                        var groupPos = groupGrid.WorldMatrix.Translation;
                        if (Vector3D.Distance(groupPos, shipPos) > range) continue;

                        var score = GetGridVolumeScore(groupGrid);
                        if (score > selectedScore)
                        {
                            selectedScore = score;
                            selectedGrid = groupGrid;
                        }
                    }

                    if (ownGroup || selectedGrid == null)
                        continue;

                    var pos = selectedGrid.WorldMatrix.Translation;
                    if (Vector3D.Distance(pos, shipPos) > DEFAULT_RANGE &&
                        !GridGroupHasLongRangeSignal(_tempGroupGrids, shipPos, ownGrid))
                        continue;

                    long entityId = selectedGrid.EntityId;
                    if (!_seenThisFrame.Add(entityId)) continue;

                    ContactRecord rec;
                    if (!_contacts.TryGetValue(entityId, out rec))
                    {
                        rec = new ContactRecord { EntityId = entityId };
                        _contacts[entityId] = rec;
                    }

                    rec.Name = selectedGrid.DisplayName ?? string.Empty;
                    rec.Relationship = GetGridRelationship(selectedGrid, ownGridOwnerId);
                    rec.IconTexture = GetFactionIconForOwner(GetPrimaryGridOwner(selectedGrid));
                    rec.WorldPosition = pos;
                    rec.IsTargeted = groupHasLockedTarget;
                    rec.TargetLockPercent = groupHasLockedTarget ? normalizedLockPercent : 0f;
                    rec.IsLockCandidate = groupHasCandidate;
                    rec.MissedFrames = 0;
                }

                _signalBlocks.EndScan();
            }
            catch (Exception ex)
            {
                ErrorHandlerHelper.LogError(ex, this);
            }
        }

        bool TryCollectSignalProxyContact(
            IMyEntity entity,
            Vector3D receiverPosition,
            IMyCubeGrid receiverGrid,
            long receiverOwnerId,
            long lockedTargetGridId,
            long lockedTargetEntityId,
            float normalizedLockPercent)
        {
            if (entity == null || receiverGrid == null)
                return false;

            var block = entity as IMyCubeBlock;
            var signalGrid = block?.CubeGrid;
            if (signalGrid != null && signalGrid.EntityId == receiverGrid.EntityId)
                return false;

            if (!IsLongRangeSignalEntityInRange(entity, receiverPosition, receiverGrid))
                return false;

            long entityId = signalGrid?.EntityId ?? entity.EntityId;
            if (_seenThisFrame.Contains(entityId))
                return true;

            var pos = entity.WorldMatrix.Translation;
            if (Vector3D.Distance(pos, receiverPosition) > _maxRange)
                return true;

            _seenThisFrame.Add(entityId);

            long ownerId = signalGrid != null
                ? GetPrimaryGridOwner(signalGrid)
                : GetPrimaryBlockOwner(block as IMyTerminalBlock);

            ContactRecord rec;
            if (!_contacts.TryGetValue(entityId, out rec))
            {
                rec = new ContactRecord { EntityId = entityId };
                _contacts[entityId] = rec;
            }

            rec.Name = GetSignalProxyName(entity, block, signalGrid);
            rec.Relationship = GetOwnerRelationship(ownerId, receiverOwnerId);
            rec.IconTexture = GetFactionIconForOwner(ownerId);
            rec.WorldPosition = pos;
            rec.IsTargeted = entity.EntityId == lockedTargetEntityId ||
                             (signalGrid != null && signalGrid.EntityId == lockedTargetGridId);
            rec.TargetLockPercent = rec.IsTargeted ? normalizedLockPercent : 0f;
            rec.IsLockCandidate = signalGrid != null && signalGrid.EntityId == _lockCandidateId;
            rec.MissedFrames = 0;
            return true;
        }

        static string GetSignalProxyName(IMyEntity entity, IMyCubeBlock block, IMyCubeGrid signalGrid)
        {
            var radio = entity as IMyRadioAntenna;
            if (radio != null && !string.IsNullOrWhiteSpace(radio.HudText))
                return radio.HudText;

            var beacon = entity as IMyBeacon;
            if (beacon != null && !string.IsNullOrWhiteSpace(beacon.HudText))
                return beacon.HudText;

            var terminalBlock = block as IMyTerminalBlock;
            if (terminalBlock != null && !string.IsNullOrWhiteSpace(terminalBlock.CustomName))
                return terminalBlock.CustomName;

            if (signalGrid != null && !string.IsNullOrWhiteSpace(signalGrid.DisplayName))
                return signalGrid.DisplayName;

            return entity.DisplayName ?? string.Empty;
        }

        static long GetPrimaryBlockOwner(IMyTerminalBlock block)
        {
            return block?.OwnerId ?? 0L;
        }

        static bool IsLongRangeSignalEntityInRange(
            IMyEntity entity,
            Vector3D receiverPosition,
            IMyCubeGrid receiverGrid)
        {
            var functional = entity as IMyFunctionalBlock;
            if (functional == null || !functional.IsFunctional || !functional.Enabled)
                return false;

            var radio = entity as IMyRadioAntenna;
            if (radio != null)
                return radio.IsBroadcasting && BroadcastRangeReaches(radio.WorldMatrix.Translation, radio.Radius,
                    receiverPosition);

            var beacon = entity as IMyBeacon;
            if (beacon != null)
                return BroadcastRangeReaches(beacon.WorldMatrix.Translation, beacon.Radius, receiverPosition);

            var laser = entity as IMyLaserAntenna;
            if (laser == null || laser.Other == null || receiverGrid == null)
                return false;

            return laser.Other.CubeGrid != null &&
                   laser.Other.CubeGrid.EntityId == receiverGrid.EntityId &&
                   laser.IsInRange(laser.Other);
        }

        void SyncConfigIfNeeded()
        {
            if (!_syncConfigNextRun)
                return;

            _syncConfigNextRun = false;
            if (Block != null && _host.ProviderConfig != null)
                ConfigManager.Sync(Block, _host.ProviderConfig);
        }

        void ApplyRadarConfig()
        {
            _cameraPanMeters = ClampCameraPan(GetConfigCameraPan(RadarComponent));
            _cameraZoomScale = ClampCameraZoomScale(RadarComponent.CameraZoomScale);
            _cameraYaw = NormalizeYaw(RadarComponent.CameraYawRadians);
            _cameraPitch = ClampPitch(RadarComponent.CameraPitchRadians);
        }

        static float NormalizeYaw(float yaw)
        {
            if (!IsFinite(yaw))
                return 0f;
            yaw %= MathHelper.TwoPi;
            return yaw < 0f ? yaw + MathHelper.TwoPi : yaw;
        }

        static float ClampPitch(float pitch)
        {
            return IsFinite(pitch)
                ? MathHelper.Clamp(pitch, RadarCamera.MINIMUM_PITCH, RadarCamera.MAXIMUM_PITCH)
                : RadarScene.DEFAULT_PITCH;
        }

        void PersistRadarCameraConfig()
        {
            RadarConfigComponent config = RadarComponent;
            bool changed = false;

            if (!NearlyEqual(GetConfigCameraPan(config), _cameraPanMeters))
            {
                config.CameraPanX = _cameraPanMeters.X;
                config.CameraPanY = _cameraPanMeters.Y;
                changed = true;
            }

            float cameraZoomScale = ClampCameraZoomScale(_cameraZoomScale);
            if (!NearlyEqual(ClampCameraZoomScale(config.CameraZoomScale), cameraZoomScale))
            {
                config.CameraZoomScale = cameraZoomScale;
                changed = true;
            }

            if (!NearlyEqual(config.CameraYawRadians, _cameraYaw) || !NearlyEqual(config.CameraPitchRadians, _cameraPitch))
            {
                config.CameraYawRadians = _cameraYaw;
                config.CameraPitchRadians = _cameraPitch;
                changed = true;
            }

            if (!changed)
                return;

            _syncConfigNextRun = true;
        }

        static Vector2 GetConfigCameraPan(RadarConfigComponent config)
        {
            return new Vector2(
                (float)config.CameraPanX,
                (float)config.CameraPanY);
        }

        static float ClampCameraZoomScale(float zoomScale)
        {
            if (!IsFinite(zoomScale) || zoomScale <= 0f)
                zoomScale = CAMERA_DEFAULT_ZOOM_SCALE;

            return MathHelper.Clamp(zoomScale, CAMERA_MIN_ZOOM_SCALE, CAMERA_MAX_ZOOM_SCALE);
        }

        static bool IsRangeScrollModifierPressed()
        {
            var input = MyAPIGateway.Input;
            return input.IsAnyCtrlKeyPressed();
        }

        float GetConfiguredRange()
        {
            return SliderRadarRange.GetRangeMeters(RadarComponent.RangeScale);
        }

        float GetWorldRange()
        {
            return IsFinite(_maxRange) && _maxRange > 0f ? _maxRange : DEFAULT_RANGE;
        }

        bool GridGroupHasLongRangeSignal(List<IMyCubeGrid> grids, Vector3D receiverPosition, IMyCubeGrid receiverGrid) => 
            grids.Any(t => GridHasLongRangeSignal(t, receiverPosition, receiverGrid));

        bool GridHasLongRangeSignal(IMyCubeGrid grid, Vector3D receiverPosition, IMyCubeGrid receiverGrid)
        {
            if (grid == null)
                return false;

            try
            {
                foreach (var block in _signalBlocks.Get(grid))
                {
                    if (!block.Closed && IsLongRangeSignalEntityInRange(block, receiverPosition, receiverGrid))
                        return true;
                }

                return false;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                return false;
            }
        }

        static bool BroadcastRangeReaches(Vector3D broadcastPosition, float radius, Vector3D receiverPosition)
        {
            if (radius <= 0f)
                return false;

            double radiusSq = radius;
            radiusSq *= radiusSq;
            return Vector3D.DistanceSquared(broadcastPosition, receiverPosition) <= radiusSq;
        }

        double GetGridVolumeScore(IMyCubeGrid grid)
        {
            var aabb = grid.WorldAABB;
            var size = aabb.Max - aabb.Min;
            return size.X * size.Y * size.Z;
        }

        long GetPrimaryGridOwner(IMyCubeGrid grid)
        {
            var owners = grid.BigOwners;
            return owners != null && owners.Count > 0 ? owners[0] : 0;
        }

        long GetLockedTargetGridId(out long targetEntityId, out float targetLockPercent)
        {
            return _targetLock.GetLockedGridId(Block, out targetEntityId, out targetLockPercent);
        }

        static float NormalizeLockPercent(float rawPercent)
        {
            if (rawPercent > 1f)
                rawPercent *= 0.01f;
            return MathHelper.Clamp(rawPercent, 0f, 1f);
        }

        static string ResolveGridName(long gridEntityId)
        {
            if (gridEntityId == 0)
                return string.Empty;

            IMyEntity entity;
            if (!MyAPIGateway.Entities.TryGetEntityById(gridEntityId, out entity))
                return string.Empty;

            var grid = entity as IMyCubeGrid;
            return grid?.DisplayName ?? string.Empty;
        }

        MyRelationsBetweenPlayerAndBlock GetGridRelationship(IMyCubeGrid grid, long ownGridOwnerId)
        {
            long ownerId = GetPrimaryGridOwner(grid);
            return GetOwnerRelationship(ownerId, ownGridOwnerId);
        }

        MyRelationsBetweenPlayerAndBlock GetOwnerRelationship(long ownerId, long ownGridOwnerId)
        {
            if (ownerId == 0)
                return MyRelationsBetweenPlayerAndBlock.Neutral;

            if (ownGridOwnerId != 0 && ownerId == ownGridOwnerId)
                return MyRelationsBetweenPlayerAndBlock.FactionShare; // Allied

            if (ownGridOwnerId == 0)
                return MyRelationsBetweenPlayerAndBlock.Neutral;

            var relation = Sandbox.Game.Entities.MyIDModule.GetRelationPlayerPlayer(ownGridOwnerId, ownerId);
            var relationName = relation.ToString();
            if (relationName == "Enemies")
                return MyRelationsBetweenPlayerAndBlock.Enemies;
            if (relationName == "Allies" || relationName == "Friends")
                return MyRelationsBetweenPlayerAndBlock.FactionShare;

            return MyRelationsBetweenPlayerAndBlock.Neutral;
        }

        string GetFactionIconForOwner(long ownerId)
        {
            if (ownerId == 0)
                return null;

            var faction = MyAPIGateway.Session.Factions?.TryGetPlayerFaction(ownerId);
            if (faction == null)
                return null;

            var icon = faction.FactionIcon?.ToString();
            if (string.IsNullOrEmpty(icon))
                return null;

            string cachedIcon;
            if (!_factionIconCache.TryGetValue(faction.FactionId, out cachedIcon) ||
                !string.Equals(cachedIcon, icon, StringComparison.Ordinal))
            {
                _factionIconCache[faction.FactionId] = icon;
                cachedIcon = icon;
            }

            return cachedIcon;
        }


        void PurgeStaleContacts()
        {
            _toRemove.Clear();
            foreach (var kv in _contacts)
                if (kv.Value.MissedFrames > CONTACT_TIMEOUT)
                    _toRemove.Add(kv.Key);

            foreach (var t in _toRemove)
                _contacts.Remove(t);
        }

        void UpdateScene()
        {
            RadarScene scene = _viewModel.Scene;
            scene.WorldRange = GetWorldRange();
            scene.ZoomScale = ClampCameraZoomScale(_cameraZoomScale);
            scene.PanMeters = _cameraPanMeters;
            scene.Yaw = _cameraYaw;
            scene.Pitch = _cameraPitch;
            scene.LineColor = ForegroundColor;
            BuildTargets(scene, ColorComponent.ResolveErrorColor(), ColorComponent.ResolveWarningColor(), GetHeaderColor());

            if (_debugLockedTargetEntityId != 0)
                _viewModel.SetDebug(
                    "TARGET: " + (string.IsNullOrWhiteSpace(_debugLockedTargetName) ? "Unknown" : _debugLockedTargetName) +
                    " (" + (_debugLockedTargetPercent * 100f).ToString("F0") + "%)",
                    new Color(_debugLockedTargetPercent >= 0.99f ? ColorComponent.ResolveErrorColor() : ColorComponent.ResolveWarningColor(), .85f));
            else
                _viewModel.SetDebug(null, Color.Transparent);
            _viewModel.InvalidateScene();
        }

        void BuildTargets(RadarScene scene, Color errColor, Color warnColor, Color allyColor)
        {
            scene.Targets.Clear();

            MatrixD gridMatrix;
            if (!TryGetReferenceWorldMatrix(out gridMatrix))
                gridMatrix = Block.WorldMatrix;
            int shown = Math.Min(_sortedContacts.Count, MAX_CONTACTS);
            bool hasLockedTarget = false;
            for (int i = 0; i < shown; i++)
            {
                RadarTarget info;
                if (!TryBuildTargetInfo(_sortedContacts[i], gridMatrix, errColor, warnColor, allyColor, scene.WorldRange, out info))
                    continue;

                if (info.TargetLock)
                    hasLockedTarget = true;

                scene.Targets.Add(info);
            }

            if (hasLockedTarget)
                Dim(scene.Targets, 1 - 0.5f * _debugLockedTargetPercent);
            BuildMissiles(scene, gridMatrix);
        }

        // A click on a contact locks on it, as the lock key would on its candidate (the candidate itself when it is
        // part of that ship); refused out of lock range or outside the seat's lock cone.
        void OnContactClicked(long entityId)
        {
            IMyEntity entity = MyAPIGateway.Entities.GetEntityById(entityId);
            IMyCubeGrid grid = entity as IMyCubeGrid ?? (entity as IMyCubeBlock)?.CubeGrid;
            if (grid == null)
                return;
            IMyCubeGrid candidate = _targetLock.GetLockCandidate();
            if (candidate != null && (candidate == grid ||
                                      MyAPIGateway.GridGroups.HasConnection(candidate, grid, GridLinkTypeEnum.Logical)))
                grid = candidate;
            if (!_targetLock.RequestLock(grid))
                return;
            var interactive = InteractiveSurface;
            if (interactive != null)
                interactive.PlaySounds(AudioHelper.HudClick, true);
        }

        // Missiles inside the radar's own (antenna-less) range, each with a short trail of where it was.
        void CollectMissiles()
        {
            foreach (var pair in _missileTracks)
                pair.Value.Seen = false;
            IMyCubeGrid ownGrid = Block == null ? null : Block.CubeGrid;
            if (ownGrid != null)
            {
                Vector3D shipPos = Block.WorldMatrix.Translation;
                MissileScanner.Collect(new BoundingSphereD(shipPos, Math.Min(_maxRange, DEFAULT_RANGE)), _missileBuffer);
                for (int i = 0; i < _missileBuffer.Count && i < MAX_MISSILES; i++)
                {
                    IMyMissile missile = _missileBuffer[i];
                    var entity = (IMyEntity)missile;
                    MissileTrack track;
                    if (!_missileTracks.TryGetValue(entity.EntityId, out track))
                    {
                        track = new MissileTrack { Color = MissileColor(missile) };
                        _missileTracks[entity.EntityId] = track;
                    }
                    else
                    {
                        track.Trail.Add(track.Position);
                        if (track.Trail.Count > MISSILE_TRAIL_POINTS)
                            track.Trail.RemoveAt(0);
                    }
                    track.Position = entity.GetPosition();
                    track.Velocity = missile.LinearVelocity;
                    track.RemainingMeters = RemainingRange(missile, track.Position);
                    track.Seen = true;
                }
                _missileBuffer.Clear();
            }
            _missileGone.Clear();
            foreach (var pair in _missileTracks)
                if (!pair.Value.Seen)
                    _missileGone.Add(pair.Key);
            for (int i = 0; i < _missileGone.Count; i++)
                _missileTracks.Remove(_missileGone[i]);
        }

        // As every view colors missiles, by who launched them: friendly ones all in the one allied color.
        static Color MissileColor(IMyMissile missile)
        {
            var launcher = MyAPIGateway.Entities.GetEntityById(missile.LauncherId) as IMyCubeBlock;
            return Acrux.Render3D.RelationPalette.ForMissile(launcher == null ? 0 : launcher.OwnerId);
        }

        static double RemainingRange(IMyMissile missile, Vector3D position)
        {
            if (missile.MaxTrajectory <= 0f)
                return double.PositiveInfinity;
            return Math.Max(0d, missile.MaxTrajectory - Vector3D.Distance(position, missile.Origin));
        }

        void BuildMissiles(RadarScene scene, MatrixD gridMatrix)
        {
            scene.Missiles.Clear();
            float range = scene.WorldRange;
            if (range <= 0f)
                return;
            MatrixD toRadar = MatrixD.Transpose(gridMatrix);
            foreach (var pair in _missileTracks)
            {
                MissileTrack track = pair.Value;
                Vector3 position = ToRadar(track.Position - gridMatrix.Translation, toRadar) / range;
                if (position.X * position.X + position.Y * position.Y > 1f)
                    continue;
                if (_missilePool.Count <= scene.Missiles.Count)
                    _missilePool.Add(new RadarMissile());
                RadarMissile missile = _missilePool[scene.Missiles.Count];
                Vector3 velocity = ToRadar(track.Velocity, toRadar);
                float speed = velocity.Length();
                missile.Position = position;
                missile.Heading = speed > 1e-6f ? velocity / speed : new Vector3(0f, -1f, 0f);
                float aheadMeters = (float)Math.Min(speed * MISSILE_AHEAD_SECONDS, track.RemainingMeters);
                missile.Ahead = ClipToRange(position, position + missile.Heading * (aheadMeters / range));
                missile.Trail.Clear();
                for (int i = 0; i < track.Trail.Count; i++)
                    missile.Trail.Add(ToRadar(track.Trail[i] - gridMatrix.Translation, toRadar) / range);
                missile.Color = track.Color;
                scene.Missiles.Add(missile);
            }
        }

        // Radar space: X right, Y backward, Z up, from the reference's axes.
        static Vector3 ToRadar(Vector3D offset, MatrixD toRadar)
        {
            Vector3D local = Vector3D.TransformNormal(offset, toRadar);
            return new Vector3((float)local.X, (float)local.Z, (float)local.Y);
        }

        // The point where a line leaving from inside the radar disc crosses its edge, or the end when it stays inside.
        static Vector3 ClipToRange(Vector3 from, Vector3 to)
        {
            Vector2 a = new Vector2(from.X, from.Y);
            Vector2 d = new Vector2(to.X - from.X, to.Y - from.Y);
            float qa = d.LengthSquared();
            if (qa <= 1e-12f || (to.X * to.X + to.Y * to.Y) <= 1f)
                return to;
            float qb = 2f * Vector2.Dot(a, d);
            float qc = a.LengthSquared() - 1f;
            float t = (-qb + (float)Math.Sqrt(Math.Max(0f, qb * qb - 4f * qa * qc))) / (2f * qa);
            return Vector3.Lerp(from, to, MathHelper.Clamp(t, 0f, 1f));
        }

        static void Dim(List<RadarTarget> targets, float multiplier)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                RadarTarget info = targets[i];
                if (info.TargetLock)
                    continue;
                info.IconColor = info.IconColor.MulSaturation(multiplier).MulValue(multiplier);
                info.ElevationColor = info.ElevationColor.MulSaturation(multiplier).MulValue(multiplier);
                targets[i] = info;
            }
        }

        void UpdateFooter()
        {
            bool showWarning = ShouldShowLongRangeWarning();
            bool hasContacts = _sortedContacts.Count > 0;
            _viewModel.SetFooter(showWarning || hasContacts, new Color(BackgroundColor.MulValue(0.8f), 0.5f));
            _viewModel.SetFontScale(FontScale);
            _viewModel.SetWarning(
                showWarning ? LocHelper.GetLoc(LONG_RANGE_WARNING_KEY) : null,
                new Color(ColorComponent.ResolveWarningColor(), 0.9f));
            _viewModel.SetHeader(
                LocHelper.GetLoc(MOD_PREFIX + "Radar_DetectedEntities"),
                GetFooterRangeText(),
                new Color(ForegroundColor, 0.75f),
                ForegroundColor);

            int visibleEntries = GetVisibleFooterEntryCount(_sortedContacts.Count);
            int startIndex = _sortedContacts.Count > visibleEntries
                ? GetScrollStep(FOOTER_SCROLL_STEP_SECONDS) % _sortedContacts.Count
                : 0;
            var shipPos = Block.WorldMatrix.Translation;
            var errColor = ColorComponent.ResolveErrorColor();
            var warnColor = ColorComponent.ResolveWarningColor();
            var allyColor = GetHeaderColor();
            for (int i = 0; i < visibleEntries; i++)
            {
                ContactRecord contact = _sortedContacts[(startIndex + i) % _sortedContacts.Count];
                _viewModel.ContactRow(i).Update(
                    FooterIconTexture(contact),
                    GetFooterContactText(contact, shipPos),
                    ContactColor(contact, errColor, warnColor, allyColor),
                    0.5f * FontScale);
            }
            _viewModel.SetContactCount(visibleEntries);
        }

        bool IRadarCamera.Scroll(int delta)
        {
            if (delta == 0)
                return false;
            if (IsRangeScrollModifierPressed())
                return ScrollRangeByWheelDelta(delta);

            float next = ClampCameraZoomScale(_cameraZoomScale * (delta > 0 ? CAMERA_ZOOM_STEP : 1f / CAMERA_ZOOM_STEP));
            if (NearlyEqual(_cameraZoomScale, next))
                return false;

            _cameraZoomScale = next;
            _cameraPanMeters = ClampCameraPan(_cameraPanMeters);
            CameraChanged();
            return true;
        }

        bool IRadarCamera.Orbit(Vector2 deltaRadians)
        {
            float yaw = NormalizeYaw(_cameraYaw + deltaRadians.X);
            float pitch = ClampPitch(_cameraPitch + deltaRadians.Y);
            if (NearlyEqual(yaw, _cameraYaw) && NearlyEqual(pitch, _cameraPitch))
                return false;

            _cameraYaw = yaw;
            _cameraPitch = pitch;
            CameraChanged();
            return true;
        }

        void IRadarCamera.ResetView()
        {
            _cameraYaw = 0f;
            _cameraPitch = RadarScene.DEFAULT_PITCH;
            _cameraPanMeters = Vector2.Zero;
            _cameraZoomScale = CAMERA_DEFAULT_ZOOM_SCALE;
            CameraChanged();
        }

        bool IRadarCamera.Pan(Vector2 deltaMeters)
        {
            Vector2 next = ClampCameraPan(_cameraPanMeters + deltaMeters);
            if (NearlyEqual(_cameraPanMeters, next))
                return false;

            _cameraPanMeters = next;
            CameraChanged();
            return true;
        }

        void CameraChanged()
        {
            PersistRadarCameraConfig();
            UpdateScene();
            Host.RenderSprites();
            CameraMoved?.Invoke();
        }

        static RadarMarker ContactMarker(ContactRecord contact)
        {
            switch (contact.Relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    return RadarMarker.Square;
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return RadarMarker.Circle;
                default:
                    return RadarMarker.Triangle;
            }
        }

        bool TryGetReferenceWorldMatrix(out MatrixD world)
        {
            return _host.TryGetReferenceWorldMatrix(InteractionComponent.ReferenceMode, out world, true);
        }

        bool TryBuildTargetInfo(ContactRecord contact, MatrixD gridMatrix, Color errColor, Color warnColor,
            Color allyColor,
            float worldRange,
            out RadarTarget targetInfo)
        {
            if (worldRange <= 0f)
            {
                targetInfo = default(RadarTarget);
                return false;
            }

            var transformedDirection =
                Vector3D.TransformNormal(contact.WorldPosition - gridMatrix.Translation, MatrixD.Transpose(gridMatrix));
            var position = new Vector3((float)transformedDirection.X, (float)transformedDirection.Z,
                (float)transformedDirection.Y);

            bool inRange = position.X * position.X + position.Y * position.Y < worldRange * worldRange;
            RadarMarker marker;

            if (inRange)
            {
                position /= worldRange;
                marker = ContactMarker(contact);
            }
            else
            {
                Vector3 directionFlat = position;
                directionFlat.Z = 0f;
                if (directionFlat.LengthSquared() < 1e-6f)
                {
                    targetInfo = default(RadarTarget);
                    return false;
                }

                position = Vector3.Normalize(directionFlat);
                marker = RadarMarker.OutOfRange;
            }

            Color iconColor = ContactColor(contact, errColor, warnColor, allyColor);
            targetInfo = new RadarTarget
            {
                EntityId = contact.EntityId,
                LockCandidate = contact.IsLockCandidate,
                Position = position,
                IconColor = iconColor,
                IconTexture = contact.IconTexture,
                ElevationColor = new Color(iconColor, 0.7f),
                TargetLock = contact.IsTargeted,
                TargetLockPercent = contact.TargetLockPercent,
                Marker = marker
            };
            return true;
        }

        bool ShouldShowLongRangeWarning()
        {
            return _maxRange > DEFAULT_RANGE + 0.5f;
        }

        bool ScrollRangeByWheelDelta(int delta)
        {
            float currentScale = SliderRadarRange.ClampRangeScale(RadarComponent.RangeScale);
            float nextScale = SliderRadarRange.ApplyScrollStep(currentScale, delta);
            if (Math.Abs(currentScale - nextScale) <= 0.001f)
                return false;

            RadarComponent.RangeScale = nextScale;
            _maxRange = SliderRadarRange.GetRangeMeters(nextScale);
            _cameraPanMeters = ClampCameraPan(_cameraPanMeters);
            PersistRadarCameraConfig();
            _syncConfigNextRun = true;
            UpdateScene();
            UpdateFooter();
            Host.RenderSprites();
            RangeScrolled?.Invoke(delta);
            return true;
        }

        Vector2 ClampCameraPan(Vector2 pan)
        {
            if (!IsFinite(pan))
                return Vector2.Zero;

            float maxPan = Math.Max(0f, _maxRange) * CAMERA_MAX_PAN_RANGE_RATIO;
            if (maxPan <= 0f)
                return Vector2.Zero;

            float lengthSq = pan.LengthSquared();
            float maxPanSq = maxPan * maxPan;
            if (lengthSq <= maxPanSq)
                return pan;

            return pan * (maxPan / (float)Math.Sqrt(lengthSq));
        }

        static bool NearlyEqual(Vector2 left, Vector2 right)
        {
            return Vector2.DistanceSquared(left, right) <=
                   CAMERA_PAN_CONFIG_EPSILON_METERS *
                   CAMERA_PAN_CONFIG_EPSILON_METERS;
        }

        static bool NearlyEqual(float left, float right)
        {
            return Math.Abs(left - right) <=
                   Math.Max(CAMERA_ZOOM_CONFIG_EPSILON, Math.Abs(left) * CAMERA_ZOOM_CONFIG_EPSILON);
        }

        static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.X) &&
                   !float.IsNaN(value.Y) &&
                   !float.IsInfinity(value.X) &&
                   !float.IsInfinity(value.Y);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static int GetVisibleFooterEntryCount(int entries)
        {
            return Math.Min(Math.Min(MAX_CONTACTS, Math.Max(0, entries)), FOOTER_MAX_CONTACT_ROWS);
        }

        string GetFooterRangeText()
        {
            return string.Format(
                       FormatingHelper.Culture,
                       LocHelper.GetLoc(MOD_PREFIX + "Common_Label_WithColon"),
                       MyTexts.GetString("BlockPropertyTitle_OreDetectorRange")) +
                   " " +
                   FormatingHelper.DistanceToString(_maxRange);
        }

        static string GetFooterContactText(ContactRecord contact, Vector3D shipPos)
        {
            string distance = FormatingHelper.DistanceToString(
                (float)Vector3D.Distance(contact.WorldPosition, shipPos));
            string name = string.IsNullOrWhiteSpace(contact.Name) ? "Unknown" : contact.Name;
            if (contact.IsTargeted)
                name = "[L] " + name;

            return distance + "  " + name;
        }

        void BuildSortedContacts()
        {
            _sortedContacts.Clear();
            var shipPos = Block.WorldMatrix.Translation;

            foreach (var kv in _contacts)
            {
                var c = kv.Value;
                if ((float)Vector3D.Distance(c.WorldPosition, shipPos) > _maxRange)
                    continue;
                _sortedContacts.Add(c);
            }

            // Targeted contacts first, then closest-first
            _sortedContacts.Sort((a, b) =>
            {
                if (a.IsTargeted != b.IsTargeted)
                    return a.IsTargeted ? -1 : 1;
                var pos = Block.WorldMatrix.Translation;
                float da = (float)Vector3D.Distance(a.WorldPosition, pos);
                float db = (float)Vector3D.Distance(b.WorldPosition, pos);
                return da.CompareTo(db);
            });
        }

        static Color ContactColor(ContactRecord c, Color errColor, Color warnColor, Color frdColor)
        {
            return RelationColor(c.Relationship, errColor, warnColor, frdColor);
        }

        static Color RelationColor(MyRelationsBetweenPlayerAndBlock relationship, Color errColor, Color warnColor,
            Color frdColor)
        {
            switch (relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return errColor;
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    return frdColor;
                default:
                    return warnColor;
            }
        }

        static string FooterIconTexture(ContactRecord contact)
        {
            if (!string.IsNullOrEmpty(contact.IconTexture))
                return contact.IconTexture;

            switch (contact.Relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    return "SquareSimple";
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return "Circle";
                default:
                    return "Triangle";
            }
        }

        static int GetScrollStep(int secondsPerStep)
        {
            try
            {
                var sess = MyAPIGateway.Session;
                int ticksPerStep = Math.Max(1, secondsPerStep * 60);
                return sess.GameplayFrameCounter / ticksPerStep;
            }
            catch
            {
                return 0;
            }
        }
    }
}
