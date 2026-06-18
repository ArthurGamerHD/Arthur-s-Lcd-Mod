using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Config;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Helpers;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.ModAPI;
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
        public int MissedFrames;
    }

    public class RadarApp : App, IApp
    {
        public const string ID = MOD_PREFIX + "Radar";
        public const string TITLE = MOD_PREFIX + "Radar";

        // Fixed ring distances (meters) drawn regardless of dynamic range
        const float RING_1_M = 800f;
        const float RING_2_M = 1400f;
        const float RING_3_M = 2000f;
        const float DEFAULT_RANGE = 3000f;
        const string LONG_RANGE_WARNING_KEY = MOD_PREFIX + "Radar_AntennaRangeWarning";

        const float LOCK_ANIM_DISTANCE = 20f;

        // Timing intervals (Run() calls; each call ≈ 166 ms at Update10)
        const int CONTACT_TIMEOUT = 36;

        const int MAX_CONTACTS = 32;

        // Visual sizes in logical pixels scaled by Scale at render time
        const float RADAR_MARGIN_PX = 14f;

        const float RANGE_TEXT_SIZE = 1.2f;
        const float DOT_SIZE = 16f;
        const float QUADRANT_LABEL_TEXT_SIZE = 0.6f;
        const float QUADRANT_LABEL_MARGIN_PX = 10f;
        const float SIZE_TO_PX = 28.8f;
        const float TGT_ELEVATION_LINE_WIDTH = 4f;
        const float RADAR_RANGE_LINE_WIDTH = 8f;
        const float QUADRANT_LINE_WIDTH = 4f;
        const float QUADRANT_LINE_COVERAGE_PER_SIDE = .8f;
        const float PROJECTION_ANGLE_DEG = 55f;
        const int FOOTER_MAX_ROWS = 4;
        const float FOOTER_ROW_HEIGHT_PX = 18f;
        const float FOOTER_HEADER_HEIGHT_PX = 14f;
        const float FOOTER_COL_WIDTH_PX = 230f;
        const int FOOTER_SCROLL_STEP_SECONDS = 2;


        readonly Dictionary<long, ContactRecord> _contacts = new Dictionary<long, ContactRecord>();
        readonly HashSet<long> _seenThisFrame = new HashSet<long>();
        readonly HashSet<long> _processedGroupGridIds = new HashSet<long>();
        readonly Dictionary<long, string> _factionIconCache = new Dictionary<long, string>();
        readonly List<long> _toRemove = new List<long>();
        readonly List<ContactRecord> _sortedContacts = new List<ContactRecord>();
        readonly List<IMyCubeGrid> _tempGroupGrids = new List<IMyCubeGrid>();
        readonly List<IMySlimBlock> _tempSlimBlocks = new List<IMySlimBlock>();

        float _maxRange = DEFAULT_RANGE;
        bool _syncConfigNextRun;
        long _debugLockedTargetEntityId;
        float _debugLockedTargetPercent;
        string _debugLockedTargetName = string.Empty;

        readonly List<IMyEntity> _tempEntities = new List<IMyEntity>();
        readonly Vector2 _tgtIconSize = new Vector2(20f, 20f);
        readonly Vector2 _shipIconSize = new Vector2(32f, 16f);
        readonly Vector2 _borderPadding = new Vector2(16f, 64f);
        readonly List<TargetInfo> _targetsBelowPlane = new List<TargetInfo>();
        readonly List<TargetInfo> _targetsAbovePlane = new List<TargetInfo>();
        readonly StringBuilder _footerTextBuilder = new StringBuilder();
        readonly List<MySprite> _backgroundSprites = new List<MySprite>();
        readonly List<MySprite> _foregroundSprites = new List<MySprite>();
        readonly List<Control> _children = new List<Control>();
        long _cachedCharacterId;
        Sandbox.Game.EntityComponents.MyTargetLockingComponent _cachedCharacterTargetLocking;
        float _radarProjectionCos;
        float _radarProjectionSin;
        float _radarFooterClampHeight;
        float _footerHeight;

        readonly IAppHost _host;
        new ScreenConfigRadar AppConfig => (ScreenConfigRadar)base.AppConfig;
        IMyCubeBlock Block => _host.Block;
        Sandbox.ModAPI.Ingame.IMyTextSurface Surface => _host.Surface;
        RectangleF ViewBox => _host.ViewBox;
        float Scale => _host.Config.Scale;
        float FontScale => _host.Surface.FontSize;
        float LayoutScale => Scale * FontScale;
        Color ForegroundColor => _host.ForegroundColor;
        Color BackgroundColor => _host.BackgroundColor;
        public override IReadOnlyList<Control> Children => _children;

        struct TargetInfo
        {
            public Vector3 Position;
            public Color IconColor;
            public string IconTexture;
            public Color ElevationColor;
            public bool TargetLock;
            public float TargetLockPercent;
            public bool AbovePlane;
            public Action<List<MySprite>, Vector2, Color, float, float> DrawFunction;
        }


        public RadarApp(ScreenConfigRadar config, IAppHost host)
            : base(config, host)
        {
            _host = host;
        }

        public override void Update()
        {
            if (AppConfig == null)
                return;

            SyncConfigIfNeeded();
            CollectContacts();
            PurgeStaleContacts();
            BuildSortedContacts();
            UpdateFooterHeights();
        }

        public override List<MySprite> GetSprites()
        {
            _backgroundSprites.Clear();
            _foregroundSprites.Clear();

            DrawFooter(_foregroundSprites);
            RenderRadar(_backgroundSprites);

            _backgroundSprites.AddRange(_foregroundSprites);
            _foregroundSprites.Clear();
            return _backgroundSprites;
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
                if (session == null) return;

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

                _tempEntities.Clear();
                var sphere = new BoundingSphereD(shipPos, range);
                var entitiesInSphere = MyAPIGateway.Entities.GetEntitiesInSphere(ref sphere);
                if (entitiesInSphere != null)
                {
                    for (int i = 0; i < entitiesInSphere.Count; i++)
                        _tempEntities.Add(entitiesInSphere[i]);
                    entitiesInSphere.Clear();
                }

                _processedGroupGridIds.Clear();
                foreach (var entity in _tempEntities)
                {
                    if (TryCollectSignalProxyContact(entity, shipPos, ownGrid, ownGridOwnerId, lockedTargetGridId,
                            lockedTargetEntityId, normalizedLockPercent))
                        continue;

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

                    for (int i = 0; i < _tempGroupGrids.Count; i++)
                    {
                        var groupGrid = _tempGroupGrids[i];
                        _processedGroupGridIds.Add(groupGrid.EntityId);
                        if (lockedTargetGridId != 0 && groupGrid.EntityId == lockedTargetGridId)
                            groupHasLockedTarget = true;

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
                    if (_seenThisFrame.Contains(entityId)) continue;
                    _seenThisFrame.Add(entityId);

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
                    rec.MissedFrames = 0;
                }
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

        float GetConfiguredRange()
        {
            return SliderRadarRange.GetRangeMeters(AppConfig?.RangeScale ?? SliderRadarRange.DEFAULT_SCALE);
        }

        bool GridGroupHasLongRangeSignal(List<IMyCubeGrid> grids, Vector3D receiverPosition, IMyCubeGrid receiverGrid)
        {
            for (int i = 0; i < grids.Count; i++)
                if (GridHasLongRangeSignal(grids[i], receiverPosition, receiverGrid))
                    return true;

            return false;
        }

        bool GridHasLongRangeSignal(IMyCubeGrid grid, Vector3D receiverPosition, IMyCubeGrid receiverGrid)
        {
            if (grid == null)
                return false;

            try
            {
                _tempSlimBlocks.Clear();
                grid.GetBlocks(_tempSlimBlocks,
                    slimBlock => IsLongRangeSignalBlockInRange(slimBlock, receiverPosition, receiverGrid));
                return _tempSlimBlocks.Count > 0;
            }
            catch (Exception e)
            {
                ErrorHandlerHelper.LogError(e, this);
                return false;
            }
            finally
            {
                _tempSlimBlocks.Clear();
            }
        }

        static bool IsLongRangeSignalBlockInRange(
            IMySlimBlock slimBlock,
            Vector3D receiverPosition,
            IMyCubeGrid receiverGrid)
        {
            var block = slimBlock?.FatBlock;
            var functional = block as IMyFunctionalBlock;
            if (functional == null || !functional.IsFunctional || !functional.Enabled)
                return false;

            var radio = block as IMyRadioAntenna;
            if (radio != null)
                return radio.IsBroadcasting && BroadcastRangeReaches(radio.WorldMatrix.Translation, radio.Radius,
                    receiverPosition);

            var beacon = block as IMyBeacon;
            if (beacon != null)
                return BroadcastRangeReaches(beacon.WorldMatrix.Translation, beacon.Radius, receiverPosition);

            var laser = block as IMyLaserAntenna;
            if (laser == null || laser.Other == null || receiverGrid == null)
                return false;

            return laser.Other.CubeGrid != null &&
                   laser.Other.CubeGrid.EntityId == receiverGrid.EntityId &&
                   laser.IsInRange(laser.Other);
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
            targetEntityId = 0;
            targetLockPercent = 0f;

            long lockedEntityId;
            float lockedPercent;
            long lockedGridId;

            var player = MyAPIGateway.Session?.LocalHumanPlayer;
            RefreshCachedCharacterTargetLocking(player);

            lockedGridId = GetLockedTargetGridIdFromTargetLockingComponent(_cachedCharacterTargetLocking,
                out lockedEntityId,
                out lockedPercent);
            if (lockedEntityId != 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
                return lockedGridId;

            var playerCharacter = player?.Character;
            lockedGridId = GetLockedTargetGridIdFromEntity(playerCharacter, out lockedEntityId, out lockedPercent);
            if (lockedEntityId != 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
                return lockedGridId;

            var controlledEntity = player?.Controller?.ControlledEntity?.Entity;
            lockedGridId = GetLockedTargetGridIdFromEntity(controlledEntity, out lockedEntityId, out lockedPercent);
            if (lockedEntityId != 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
                return lockedGridId;

            var cameraEntity = MyAPIGateway.Session?.CameraController?.Entity;
            lockedGridId = GetLockedTargetGridIdFromEntity(cameraEntity, out lockedEntityId, out lockedPercent);
            if (lockedEntityId != 0 && targetEntityId == 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
                return lockedGridId;

            var cockpit = Block as IMyCockpit;
            lockedGridId = GetLockedTargetGridIdFromEntity(cockpit, out lockedEntityId, out lockedPercent);
            if (lockedEntityId != 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
                return lockedGridId;

            var myGrid = Block.CubeGrid as Sandbox.Game.Entities.MyCubeGrid;
            var mainCockpit = myGrid?.MainCockpit as IMyCockpit;
            lockedGridId = GetLockedTargetGridIdFromEntity(mainCockpit, out lockedEntityId, out lockedPercent);
            if (lockedEntityId != 0 && targetEntityId == 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            if (lockedGridId != 0)
                return lockedGridId;

            lockedGridId = GetLockedTargetGridIdFromEntity(myGrid, out lockedEntityId, out lockedPercent);
            if (lockedEntityId != 0 && targetEntityId == 0)
            {
                targetEntityId = lockedEntityId;
                targetLockPercent = lockedPercent;
            }

            return lockedGridId;
        }

        void RefreshCachedCharacterTargetLocking(IMyPlayer player)
        {
            var characterEntity = player?.Character as IMyEntity;
            var currentCharacterId = characterEntity?.EntityId ?? 0;
            if (currentCharacterId == _cachedCharacterId)
                return;

            _cachedCharacterId = currentCharacterId;
            _cachedCharacterTargetLocking = characterEntity?.Components?
                .Get<Sandbox.Game.EntityComponents.MyTargetLockingComponent>();
        }

        static long GetLockedTargetGridIdFromEntity(IMyEntity entity, out long targetEntityId,
            out float targetLockPercent)
        {
            targetEntityId = 0;
            targetLockPercent = 0f;
            if (entity == null || entity.Components == null)
                return 0;

            var targetLockingBlock =
                entity.Components.Get<Sandbox.Game.EntityComponents.MyTargetLockingBlockComponent>();
            if (targetLockingBlock != null)
            {
                var blockTargetEntity = targetLockingBlock.TargetEntity;
                if (blockTargetEntity != null)
                {
                    targetEntityId = blockTargetEntity.EntityId;
                    targetLockPercent = targetLockingBlock.LockingProgressPercent;
                    var blockTargetGrid = blockTargetEntity as IMyCubeGrid;
                    if (blockTargetGrid != null)
                        return blockTargetGrid.EntityId;
                }
            }

            var targetLocking = entity.Components.Get<Sandbox.Game.EntityComponents.MyTargetLockingComponent>();
            if (targetLocking == null)
                return 0;


            var targetEntity = targetLocking.TargetEntity;
            if (targetEntity == null)
                return 0;

            targetEntityId = targetEntity.EntityId;
            targetLockPercent = targetLocking.LockingProgressPercent;
            var targetGrid = targetEntity as IMyCubeGrid;
            return targetGrid?.EntityId ?? 0;
        }

        static long GetLockedTargetGridIdFromTargetLockingComponent(
            Sandbox.Game.EntityComponents.MyTargetLockingComponent targetLocking,
            out long targetEntityId,
            out float targetLockPercent)
        {
            targetEntityId = 0;
            targetLockPercent = 0f;
            if (targetLocking == null)
                return 0;

            var targetEntity = targetLocking.TargetEntity;
            if (targetEntity == null)
                return 0;

            targetEntityId = targetEntity.EntityId;
            targetLockPercent = targetLocking.LockingProgressPercent;
            var targetGrid = targetEntity as IMyCubeGrid;
            return targetGrid?.EntityId ?? 0;
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

            var faction = MyAPIGateway.Session?.Factions?.TryGetPlayerFaction(ownerId);
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

            for (int i = 0; i < _toRemove.Count; i++)
                _contacts.Remove(_toRemove[i]);
        }


        void RenderRadar(List<MySprite> sprites)
        {
            var lineColor = ForegroundColor;
            var backColor = BackgroundColor;
            var warnColor = AppConfig.WarningColor;
            var errColor = AppConfig.ErrorColor;
            var allyColor = AppConfig.HeaderColor;
            var planeColor = new Color(ForegroundColor, 0.12f);

            UpdateProjectionAngle();

            float radarScale = Scale;
            float cappedScale;
            float cappedLayoutScale;
            GetRadarCappedScales(out cappedScale, out cappedLayoutScale);
            float margin = RADAR_MARGIN_PX * cappedScale;
            float titleTopPadding = 0f;
            float titleClamp = _host.TitleVisible ? 40f * cappedLayoutScale : 0f;
            float footerClamp = _radarFooterClampHeight;
            float areaTop = ViewBox.Y + titleTopPadding + titleClamp + margin;
            float areaBottom = ViewBox.Bottom - footerClamp - margin;
            float areaHeight = areaBottom - areaTop;
            float areaWidth = ViewBox.Width - margin * 2f;
            if (areaWidth <= 0f || areaHeight <= 0f) return;

            Vector2 viewportCropped = new Vector2(
                areaWidth,
                areaHeight - (RANGE_TEXT_SIZE * SIZE_TO_PX + _borderPadding.Y) * cappedScale);
            if (viewportCropped.X <= 0f || viewportCropped.Y <= 0f) return;

            float sideLength;
            if (viewportCropped.X * _radarProjectionCos < viewportCropped.Y)
                sideLength = viewportCropped.X;
            else
                sideLength = viewportCropped.Y / _radarProjectionCos;

            Vector2 radarCenterPos = new Vector2(ViewBox.Center.X, areaTop + viewportCropped.Y * 0.5f);
            var radarPlaneSize = new Vector2(sideLength, sideLength * _radarProjectionCos) * AppConfig.Scale;

            DrawLongRangeWarning(sprites, areaBottom, cappedScale);
            DrawRadarPlaneBackground(sprites, radarCenterPos, radarPlaneSize, radarScale, lineColor, backColor,
                planeColor);
            DrawRadarPlane(sprites, radarCenterPos, radarPlaneSize, radarScale, lineColor);

            BuildTargetLayers(errColor, warnColor, allyColor);
            for (int i = 0; i < _targetsBelowPlane.Count; i++)
                DrawTargetIcon(sprites, radarCenterPos, radarPlaneSize, _targetsBelowPlane[i], radarScale);

            for (int i = 0; i < _targetsAbovePlane.Count; i++)
                DrawTargetIcon(sprites, radarCenterPos, radarPlaneSize, _targetsAbovePlane[i], radarScale);

            if (_debugLockedTargetEntityId != 0)
            {
                var debugText = "TARGET: " + (string.IsNullOrWhiteSpace(_debugLockedTargetName)
                                               ? "Unknown"
                                               : _debugLockedTargetName)
                                           + " (" + (_debugLockedTargetPercent * 100f).ToString("F0") + "%)";
                float debugScale = 0.5f * radarScale * FontScale;
                float debugOffsetY =
                    Surface.MeasureStringInPixels(new StringBuilder(debugText), TextFont, debugScale).Y * 0.5f;
                var debugColor = _debugLockedTargetPercent >= 0.99f ? AppConfig.ErrorColor : AppConfig.WarningColor;
                sprites.Add(new MySprite(
                    SpriteType.TEXT,
                    debugText,
                    radarCenterPos + new Vector2(0f, radarPlaneSize.Y * 0.5f + 12f * radarScale + debugOffsetY),
                    null,
                    new Color(debugColor, 0.85f),
                    TextFont,
                    TextAlignment.CENTER,
                    debugScale));
            }
        }

        void DrawLongRangeWarning(List<MySprite> sprites, float areaBottom, float scale)
        {
            if (_maxRange <= DEFAULT_RANGE + 0.5f)
                return;

            float textScale = 0.42f * scale * FontScale;
            float textHeight = Surface.MeasureStringInPixels(
                new StringBuilder(LocHelper.GetLoc(LONG_RANGE_WARNING_KEY)),
                TextFont,
                textScale).Y;
            float y = areaBottom - textHeight * 0.5f;
            sprites.Add(new MySprite(
                SpriteType.TEXT,
                LocHelper.GetLoc(LONG_RANGE_WARNING_KEY),
                new Vector2(ViewBox.Center.X, y),
                null,
                new Color(AppConfig.WarningColor, 0.9f),
                TextFont,
                TextAlignment.CENTER,
                textScale));
        }

        public override void OnMouseScroll(int delta, ref bool handled)
        {
            var config = base.AppConfig as ScreenConfigRadar;
            if (config == null || delta == 0 || handled)
                return;

            float currentScale = SliderRadarRange.ClampRangeScale(config.RangeScale);
            float nextScale = SliderRadarRange.ApplyScrollStep(currentScale, delta);
            if (Math.Abs(currentScale - nextScale) <= 0.001f)
                return;

            config.RangeScale = nextScale;
            _maxRange = SliderRadarRange.GetRangeMeters(nextScale);
            _syncConfigNextRun = true;
            handled = true;
        }

        void UpdateProjectionAngle()
        {
            float rads = MathHelper.ToRadians(PROJECTION_ANGLE_DEG);
            _radarProjectionCos = (float)Math.Cos(rads);
            _radarProjectionSin = (float)Math.Sin(rads);
        }

        void BuildTargetLayers(Color errColor, Color warnColor, Color allyColor)
        {
            _targetsBelowPlane.Clear();
            _targetsAbovePlane.Clear();

            MatrixD gridMatrix;
            if (!TryGetReferenceWorldMatrix(out gridMatrix))
                gridMatrix = Block.WorldMatrix;
            int shown = Math.Min(_sortedContacts.Count, MAX_CONTACTS);
            bool hasLockedTarget = false;
            for (int i = 0; i < shown; i++)
            {
                TargetInfo info;
                if (!TryBuildTargetInfo(_sortedContacts[i], gridMatrix, errColor, warnColor, allyColor, out info))
                    continue;

                if (info.TargetLock)
                    hasLockedTarget = true;

                if (info.AbovePlane) _targetsAbovePlane.Add(info);
                else _targetsBelowPlane.Add(info);
            }

            var colorMultiplier = 1 - 0.5f * _debugLockedTargetPercent;

            if (hasLockedTarget)
            {
                for (int i = 0; i < _targetsBelowPlane.Count; i++)
                {
                    var info = _targetsBelowPlane[i];
                    if (!info.TargetLock)
                    {
                        info.IconColor = info.IconColor.MulSaturation(colorMultiplier).MulValue(colorMultiplier);
                        info.ElevationColor =
                            info.ElevationColor.MulSaturation(colorMultiplier).MulValue(colorMultiplier);
                        _targetsBelowPlane[i] = info;
                    }
                }

                for (int i = 0; i < _targetsAbovePlane.Count; i++)
                {
                    var info = _targetsAbovePlane[i];
                    if (!info.TargetLock)
                    {
                        info.IconColor = info.IconColor.MulSaturation(0.5f).MulValue(0.5f);
                        info.ElevationColor = info.ElevationColor.MulSaturation(0.5f).MulValue(0.5f);
                        _targetsAbovePlane[i] = info;
                    }
                }
            }

            _targetsBelowPlane.Sort((a, b) => a.Position.Y.CompareTo(b.Position.Y));
            _targetsAbovePlane.Sort((a, b) => a.Position.Y.CompareTo(b.Position.Y));
        }

        bool TryGetReferenceWorldMatrix(out MatrixD world)
        {
            return _host.TryGetReferenceWorldMatrix(AppConfig?.ReferenceMode ?? (int)ReferenceMode.Auto, out world, true);
        }

        bool TryBuildTargetInfo(ContactRecord contact, MatrixD gridMatrix, Color errColor, Color warnColor,
            Color allyColor,
            out TargetInfo targetInfo)
        {
            var transformedDirection =
                Vector3D.TransformNormal(contact.WorldPosition - gridMatrix.Translation, MatrixD.Transpose(gridMatrix));
            var position = new Vector3((float)transformedDirection.X, (float)transformedDirection.Z,
                (float)transformedDirection.Y);

            bool inRange = position.X * position.X + position.Y * position.Y < _maxRange * _maxRange;
            bool above = position.Z >= 0f;
            Action<List<MySprite>, Vector2, Color, float, float> drawFunction;

            if (inRange)
            {
                position /= _maxRange;
                drawFunction = ContactDrawFunction(contact);
            }
            else
            {
                Vector3 directionFlat = position;
                directionFlat.Z = 0f;
                if (directionFlat.LengthSquared() < 1e-6f)
                {
                    targetInfo = default(TargetInfo);
                    return false;
                }

                position = Vector3.Normalize(directionFlat);
                drawFunction = DrawOutOfRangeIcon;
            }

            Color iconColor = ContactColor(contact, errColor, warnColor, allyColor);
            targetInfo = new TargetInfo
            {
                Position = position,
                IconColor = iconColor,
                IconTexture = contact.IconTexture,
                ElevationColor = new Color(iconColor, 0.7f),
                TargetLock = contact.IsTargeted,
                TargetLockPercent = contact.TargetLockPercent,
                AbovePlane = above,
                DrawFunction = drawFunction
            };
            return true;
        }

        void DrawRadarPlaneBackground(List<MySprite> sprites, Vector2 centerPos, Vector2 radarPlaneSize, float scale,
            Color lineColor, Color backColor, Color planeColor)
        {
            float lineWidth = RADAR_RANGE_LINE_WIDTH * scale;
            AddTexture(sprites, "Circle", centerPos, radarPlaneSize, lineColor);
            AddTexture(sprites, "Circle", centerPos, radarPlaneSize - lineWidth * Vector2.One, backColor);

            DrawRangeRing(sprites, centerPos, radarPlaneSize, RING_3_M, lineWidth, lineColor, backColor);
            DrawRangeRing(sprites, centerPos, radarPlaneSize, RING_2_M, lineWidth, lineColor, backColor);
            DrawRangeRing(sprites, centerPos, radarPlaneSize, RING_1_M, lineWidth, lineColor, backColor);

            AddTexture(sprites, "Circle", centerPos, radarPlaneSize, planeColor);
        }

        void DrawRangeRing(List<MySprite> sprites, Vector2 centerPos, Vector2 radarPlaneSize, float ringDistance,
            float lineWidth, Color lineColor, Color backColor)
        {
            if (_maxRange <= 0f || ringDistance <= 0f)
                return;

            float ratio = ringDistance / _maxRange;
            if (ratio <= 0f || ratio >= 1f)
                return;

            Vector2 ringSize = radarPlaneSize * ratio;
            if (ringSize.X <= lineWidth || ringSize.Y <= lineWidth)
                return;

            AddTexture(sprites, "Circle", centerPos, ringSize, new Color(lineColor, 0.65f));
            AddTexture(sprites, "Circle", centerPos, ringSize - lineWidth * Vector2.One, backColor);
        }

        void DrawRadarPlane(List<MySprite> sprites, Vector2 radarScreenCenter, Vector2 radarPlaneSize, float scale,
            Color lineColor)
        {
            var iconSize = _shipIconSize * scale;
            AddTexture(sprites, "Triangle", radarScreenCenter + new Vector2(0f, -0.2f * iconSize.Y), iconSize,
                lineColor);

            float lineWidth = QUADRANT_LINE_WIDTH * scale;
            Color quadrantLineColor = new Color(lineColor, 0.5f);
            Vector2 halfHorizontal = new Vector2(radarPlaneSize.X * 0.5f, 0f);
            Vector2 halfVertical = new Vector2(0f, radarPlaneSize.Y * 0.5f);
            Vector2 horizontalInner = halfHorizontal * (1f - QUADRANT_LINE_COVERAGE_PER_SIDE);
            Vector2 verticalInner = halfVertical * (1f - QUADRANT_LINE_COVERAGE_PER_SIDE);
            DrawLine(sprites, radarScreenCenter - halfHorizontal, radarScreenCenter - horizontalInner, lineWidth,
                quadrantLineColor);
            DrawLine(sprites, radarScreenCenter + horizontalInner, radarScreenCenter + halfHorizontal, lineWidth,
                quadrantLineColor);
            DrawLine(sprites, radarScreenCenter - halfVertical, radarScreenCenter - verticalInner, lineWidth,
                quadrantLineColor);
            DrawLine(sprites, radarScreenCenter + verticalInner, radarScreenCenter + halfVertical, lineWidth,
                quadrantLineColor);

            float angleTextSize = QUADRANT_LABEL_TEXT_SIZE * scale * FontScale;
            float labelMargin = QUADRANT_LABEL_MARGIN_PX * scale;
            Color angleColor = new Color(ForegroundColor, 0.72f);
            float angleLabelHalfHeight =
                Surface.MeasureStringInPixels(new StringBuilder("180"), "Debug", angleTextSize).Y * 0.5f;
            Vector2 angleLabelOffset = new Vector2(0f, -angleLabelHalfHeight);
            float sideLabelInwardOffset = labelMargin * 0.5f;
            sprites.Add(new MySprite(
                SpriteType.TEXT,
                "0º",
                radarScreenCenter - halfVertical - new Vector2(0f, labelMargin) + angleLabelOffset,
                null,
                angleColor,
                TextFont,
                TextAlignment.CENTER,
                angleTextSize));
            sprites.Add(new MySprite(
                SpriteType.TEXT,
                "90º",
                radarScreenCenter + halfHorizontal + new Vector2(labelMargin - sideLabelInwardOffset, 0f) +
                angleLabelOffset,
                null,
                angleColor,
                TextFont,
                TextAlignment.LEFT,
                angleTextSize));
            sprites.Add(new MySprite(
                SpriteType.TEXT,
                "180º",
                radarScreenCenter + halfVertical + new Vector2(0f, labelMargin) + angleLabelOffset,
                null,
                angleColor,
                TextFont,
                TextAlignment.CENTER,
                angleTextSize));
            sprites.Add(new MySprite(
                SpriteType.TEXT,
                "270º",
                radarScreenCenter - halfHorizontal - new Vector2(labelMargin - sideLabelInwardOffset, 0f) +
                angleLabelOffset,
                null,
                angleColor,
                TextFont,
                TextAlignment.RIGHT,
                angleTextSize));
        }

        void DrawTargetIcon(List<MySprite> sprites, Vector2 screenCenter, Vector2 radarPlaneSize, TargetInfo targetInfo,
            float scale)
        {
            Vector3 targetPosPixels = targetInfo.Position * new Vector3(1f, _radarProjectionCos, _radarProjectionSin) *
                                      radarPlaneSize.X * 0.5f;
            var targetPosPlane = new Vector2(targetPosPixels.X, targetPosPixels.Y);
            Vector2 iconPos = targetPosPlane - targetPosPixels.Z * Vector2.UnitY;

            RoundVector2(ref iconPos);
            RoundVector2(ref targetPosPlane);

            float elevationLineWidth = Math.Max(1f, TGT_ELEVATION_LINE_WIDTH * scale);
            var elevationSprite = new MySprite(SpriteType.TEXTURE, "SquareSimple",
                screenCenter + (iconPos + targetPosPlane) * 0.5f,
                new Vector2(elevationLineWidth, Math.Abs(targetPosPixels.Z)),
                ScaleColorAlpha(targetInfo.ElevationColor, 1f));
            RoundVector2(ref elevationSprite.Position);
            RoundVector2(ref elevationSprite.Size);

            Vector2 iconDrawPos = screenCenter + iconPos;
            RoundVector2(ref iconDrawPos);

            float shadowThickness = 2f * (float)Math.Max(1f, Math.Round(scale * 4f));
            float shadowScale = scale * (_tgtIconSize.X + shadowThickness) / _tgtIconSize.X;

            Vector2 iconSize = _tgtIconSize * scale;
            iconSize.Y *= _radarProjectionCos;
            var projectedIconSprite = new MySprite(SpriteType.TEXTURE, "Circle",
                screenCenter + targetPosPlane, iconSize, ScaleColorAlpha(targetInfo.ElevationColor, 1f));
            RoundVector2(ref projectedIconSprite.Position);

            bool showProjectedElevation = Math.Abs(iconPos.Y - targetPosPlane.Y) > iconSize.Y;
            if (targetInfo.AbovePlane)
            {
                if (showProjectedElevation)
                {
                    sprites.Add(projectedIconSprite);
                    sprites.Add(elevationSprite);
                }

                DrawContactIcon(sprites, iconDrawPos, targetInfo, shadowScale, 0f);
            }
            else
            {
                if (showProjectedElevation) sprites.Add(elevationSprite);
                DrawContactIcon(sprites, iconDrawPos, targetInfo, shadowScale, 0f);
                if (showProjectedElevation) sprites.Add(projectedIconSprite);
            }

            if (targetInfo.TargetLock)
            {
                float lockOffsetPx = (1f - MathHelper.Clamp(targetInfo.TargetLockPercent, 0f, 1f)) *
                                     LOCK_ANIM_DISTANCE * scale;
                Vector2 targetBoxSize = (_tgtIconSize + 20f) * scale + new Vector2(lockOffsetPx * 2f);
                if (targetInfo.TargetLockPercent >= 0.999f)
                    DrawRotatedSquareOutline(sprites, iconDrawPos, (_tgtIconSize.X + 8f) * scale, 2f * scale,
                        targetInfo.IconColor, MathHelper.PiOver4);
                DrawBoxCorners(sprites, targetBoxSize, iconDrawPos, 12f * scale, 4f * scale, targetInfo.IconColor);
            }
        }


        static void DrawLine(List<MySprite> sprites, Vector2 point1, Vector2 point2, float width, Color color)
        {
            Vector2 position = 0.5f * (point1 + point2);
            Vector2 diff = point1 - point2;
            float length = diff.Length();
            if (length > 0f) diff /= length;
            var size = new Vector2(length, width);
            float angle = (float)Math.Acos(Vector2.Dot(diff, Vector2.UnitX));
            angle *= Math.Sign(Vector2.Dot(diff, Vector2.UnitY));
            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", position, size, color, null,
                TextAlignment.CENTER, angle));
        }

        static void DrawRotatedSquareOutline(List<MySprite> sprites, Vector2 center, float size, float thickness,
            Color color, float rotation)
        {
            float half = size * 0.5f;
            var p1 = RotateVector(new Vector2(-half, -half), rotation) + center;
            var p2 = RotateVector(new Vector2(half, -half), rotation) + center;
            var p3 = RotateVector(new Vector2(half, half), rotation) + center;
            var p4 = RotateVector(new Vector2(-half, half), rotation) + center;

            DrawLine(sprites, p1, p2, thickness, color);
            DrawLine(sprites, p2, p3, thickness, color);
            DrawLine(sprites, p3, p4, thickness, color);
            DrawLine(sprites, p4, p1, thickness, color);
        }

        static Vector2 RotateVector(Vector2 v, float radians)
        {
            float c = (float)Math.Cos(radians);
            float s = (float)Math.Sin(radians);
            return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
        }

        static void DrawBoxCorners(List<MySprite> sprites, Vector2 boxSize, Vector2 centerPos, float lineLength,
            float lineWidth, Color color)
        {
            var horizontalSize = new Vector2(lineLength, lineWidth);
            var verticalSize = new Vector2(lineWidth, lineLength);
            Vector2 horizontalOffset = 0.5f * horizontalSize;
            Vector2 verticalOffset = 0.5f * verticalSize;
            Vector2 boxHalfSize = 0.5f * boxSize;
            Vector2 boxTopLeft = centerPos - boxHalfSize;
            Vector2 boxBottomRight = centerPos + boxHalfSize;
            Vector2 boxTopRight = centerPos + new Vector2(boxHalfSize.X, -boxHalfSize.Y);
            Vector2 boxBottomLeft = centerPos + new Vector2(-boxHalfSize.X, boxHalfSize.Y);

            AddTexture(sprites, "SquareSimple", boxTopLeft + horizontalOffset, horizontalSize, color);
            AddTexture(sprites, "SquareSimple", boxTopLeft + verticalOffset, verticalSize, color);
            AddTexture(sprites, "SquareSimple", boxTopRight + new Vector2(-horizontalOffset.X, horizontalOffset.Y),
                horizontalSize, color);
            AddTexture(sprites, "SquareSimple", boxTopRight + new Vector2(-verticalOffset.X, verticalOffset.Y),
                verticalSize, color);
            AddTexture(sprites, "SquareSimple", boxBottomLeft + new Vector2(horizontalOffset.X, -horizontalOffset.Y),
                horizontalSize, color);
            AddTexture(sprites, "SquareSimple", boxBottomLeft + new Vector2(verticalOffset.X, -verticalOffset.Y),
                verticalSize, color);
            AddTexture(sprites, "SquareSimple", boxBottomRight - horizontalOffset, horizontalSize, color);
            AddTexture(sprites, "SquareSimple", boxBottomRight - verticalOffset, verticalSize, color);
        }

        static void AddTexture(List<MySprite> sprites, string texture, Vector2 position, Vector2 size, Color color,
            float rotation = 0f)
        {
            sprites.Add(new MySprite(SpriteType.TEXTURE, texture, position, size, color, null, TextAlignment.CENTER,
                rotation));
        }

        static void AddSpriteWithShadow(List<MySprite> sprites, MySprite sprite, float offset)
        {
            sprites.Add(sprite.Shadow(offset));
            sprites.Add(sprite);
        }

        void DrawContactIcon(List<MySprite> sprites, Vector2 position, TargetInfo targetInfo, float scale,
            float rotation)
        {
            if (!string.IsNullOrEmpty(targetInfo.IconTexture))
            {
                AddSpriteWithShadow(sprites, new MySprite(
                    SpriteType.TEXTURE,
                    targetInfo.IconTexture,
                    position,
                    new Vector2(DOT_SIZE * 1.5f) * scale,
                    targetInfo.IconColor,
                    null,
                    TextAlignment.CENTER,
                    rotation), scale);
                return;
            }

            targetInfo.DrawFunction(sprites, position, targetInfo.IconColor, scale, rotation);
        }

        static Color ScaleColorAlpha(Color color, float scale)
        {
            if (scale > 0.999f) return color;
            color.A = (byte)Math.Round(color.A * scale);
            return color;
        }

        static void DrawSquare(List<MySprite> frame, Vector2 centerPos, Color color, float scale,
            float rotation)
        {
            AddSpriteWithShadow(frame,
                new MySprite(SpriteType.TEXTURE, "SquareSimple", centerPos, new Vector2(DOT_SIZE) * scale, color, null,
                    TextAlignment.CENTER, rotation), scale);
        }

        static void DrawCircle(List<MySprite> frame, Vector2 centerPos, Color color, float scale,
            float rotation)
        {
            AddSpriteWithShadow(frame,
                new MySprite(SpriteType.TEXTURE, "Circle", centerPos, new Vector2(DOT_SIZE) * scale, color, null,
                    TextAlignment.CENTER, rotation), scale);
        }

        static void DrawOutOfRangeIcon(List<MySprite> frame, Vector2 centerPos, Color color,
            float scale,
            float rotation)
        {
            float sin = (float)Math.Sin(rotation);
            float cos = (float)Math.Cos(rotation);
            scale *= 0.075f;

            AddTexture(frame, "SquareSimple",
                new Vector2(cos * 61f - sin * -12f, sin * 61f + cos * -12f) * scale + centerPos,
                new Vector2(180f, 350f) * scale, color.MulValue(0.2f), -0.7854f + rotation);
            AddTexture(frame, "SquareSimple",
                new Vector2(cos * -61f - sin * -12f, sin * -61f + cos * -12f) * scale + centerPos,
                new Vector2(180f, 350f) * scale, color.MulValue(0.2f), 0.7854f + rotation);
            AddTexture(frame, "SquareSimple",
                new Vector2(cos * 61f - sin * -12f, sin * 61f + cos * -12f) * scale + centerPos,
                new Vector2(80f, 250f) * scale, color, -0.7854f + rotation);
            AddTexture(frame, "SquareSimple",
                new Vector2(cos * -61f - sin * -12f, sin * -61f + cos * -12f) * scale + centerPos,
                new Vector2(80f, 250f) * scale, color, 0.7854f + rotation);
        }

        static void DrawTriangle(List<MySprite> frame, Vector2 centerPos, Color color, float scale,
            float rotation)
        {
            Vector2 iconSize = new Vector2(DOT_SIZE) * scale;
            Vector2 shadowSize = iconSize * 1.2f;

            AddTexture(frame, "Triangle", centerPos, shadowSize, color.MulValue(0.2f), rotation);
            AddTexture(frame, "Triangle", centerPos + new Vector2(0, iconSize.Y * .05f), iconSize, color, rotation);
        }

        static void RoundVector2(ref Vector2 vec)
        {
            vec.X = (float)Math.Round(vec.X);
            vec.Y = (float)Math.Round(vec.Y);
        }

        static void RoundVector2(ref Vector2? vec)
        {
            if (!vec.HasValue) return;
            var value = vec.Value;
            value.X = (float)Math.Round(value.X);
            value.Y = (float)Math.Round(value.Y);
            vec = value;
        }

        void DrawFooter(List<MySprite> sprites)
        {
            if (_footerHeight <= 0f)
                return;
            if (_sortedContacts.Count == 0)
                return;

            float margin = 0f;
            float left = ViewBox.X + margin;
            float right = ViewBox.Right - margin;
            float width = Math.Max(1f, right - left);
            float top = ViewBox.Bottom - _footerHeight;
            float footerScale = LayoutScale;
            float pad = 6f * footerScale;
            float headerHeight = FOOTER_HEADER_HEIGHT_PX * footerScale;
            float colWidth = FOOTER_COL_WIDTH_PX * Scale;
            int cols = Math.Max(1, (int)Math.Floor(width / Math.Max(1f, colWidth)));
            int rowsPerCol = Math.Min(FOOTER_MAX_ROWS, Math.Max(1, (int)Math.Ceiling(_sortedContacts.Count / (float)cols)));
            int visibleEntries = rowsPerCol * cols;
            int maxEntries = Math.Min(_sortedContacts.Count, visibleEntries);
            int startIndex = 0;
            if (_sortedContacts.Count > visibleEntries)
                startIndex = GetScrollStep(FOOTER_SCROLL_STEP_SECONDS) % _sortedContacts.Count;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(left + width * 0.5f, top + _footerHeight * 0.5f),
                Size = new Vector2(width, _footerHeight),
                Color = new Color(BackgroundColor.MulValue(0.8f), 0.5f),
                Alignment = TextAlignment.CENTER
            });

            sprites.Add(MySprite.CreateClipRect(new Rectangle(
                (int)Math.Floor(left),
                (int)Math.Floor(top),
                (int)Math.Ceiling(width),
                (int)Math.Ceiling(_footerHeight))));

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = LocHelper.GetLoc(MOD_PREFIX + "Radar_DetectedEntities"),
                Position = new Vector2(left + pad, top + 1f * Scale),
                Color = new Color(ForegroundColor, 0.75f),
                Alignment = TextAlignment.LEFT,
                FontId = TextFont,
                RotationOrScale = 0.55f * Scale * FontScale
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "Common_Label_WithColon"),
                           MyTexts.GetString("BlockPropertyTitle_OreDetectorRange")) + " " +
                       FormatingHelper.DistanceToString(_maxRange),
                Position = new Vector2(left + width - pad, top + 1f * Scale),
                Color = new Color(ForegroundColor, 0.75f),
                Alignment = TextAlignment.RIGHT,
                FontId = TextFont,
                RotationOrScale = 0.55f * Scale * FontScale
            });

            var shipPos = Block.WorldMatrix.Translation;
            var errColor = AppConfig.ErrorColor;
            var warnColor = AppConfig.WarningColor;
            var allyColor = AppConfig.HeaderColor;
            float contentTop = top + headerHeight + pad;
            float contentBottom = top + _footerHeight - pad;
            float rowHeight = Math.Max(1f, (contentBottom - contentTop) / Math.Max(1, rowsPerCol));
            float drawColWidth = width / cols;

            for (int i = 0; i < maxEntries; i++)
            {
                ContactRecord contact = _sortedContacts[(startIndex + i) % _sortedContacts.Count];
                int col = i / rowsPerCol;
                int row = i % rowsPerCol;

                float x = left + col * drawColWidth + pad;
                float y = contentTop + row * rowHeight;
                Color iconColor = ContactColor(contact, errColor, warnColor, allyColor);
                float iconSize = 12f * footerScale;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = FooterIconTexture(contact),
                    Position = new Vector2(x + iconSize * 0.5f, y + iconSize * 0.75f),
                    Size = new Vector2(iconSize, iconSize),
                    Color = iconColor,
                    Alignment = TextAlignment.CENTER
                });

                string dist =
                    FormatingHelper.DistanceToString((float)Vector3D.Distance(contact.WorldPosition, shipPos));
                float distanceScale = 0.48f * Scale * FontScale;
                float distanceWidth = Surface.MeasureStringInPixels(new StringBuilder(dist), TextFont, distanceScale).X;
                float colRight = left + (col + 1) * drawColWidth - pad;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = dist,
                    Position = new Vector2(colRight, y),
                    Color = new Color(ForegroundColor, 0.75f),
                    Alignment = TextAlignment.RIGHT,
                    FontId = TextFont,
                    RotationOrScale = distanceScale
                });

                _footerTextBuilder.Clear();
                if (contact.IsTargeted)
                    _footerTextBuilder.Append("[L] ");
                _footerTextBuilder.Append(string.IsNullOrWhiteSpace(contact.Name) ? "Unknown" : contact.Name);
                float nameAvailable = Math.Max(0f, colRight - (x + iconSize + 6f * Scale) - distanceWidth - 8f * Scale);
                var labelText = _footerTextBuilder;
                TrimText(ref labelText, nameAvailable, 0.5f);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = labelText.ToString(),
                    Position = new Vector2(x + iconSize + 6f * Scale, y),
                    Color = ForegroundColor,
                    Alignment = TextAlignment.LEFT,
                    FontId = TextFont,
                    RotationOrScale = 0.5f * Scale * FontScale
                });
            }

            sprites.Add(MySprite.CreateClearClipRect());
        }

        void UpdateFooterHeights()
        {
            int entries = _sortedContacts.Count;
            _footerHeight = CalculateFooterHeight(entries, Scale, LayoutScale);
            float cappedScale;
            float cappedLayoutScale;
            GetRadarCappedScales(out cappedScale, out cappedLayoutScale);
            _radarFooterClampHeight = CalculateFooterHeight(entries, cappedScale, cappedLayoutScale);
        }

        float CalculateFooterHeight(int entries, float scale, float layoutScale)
        {
            entries = Math.Min(MAX_CONTACTS, entries);
            if (entries <= 0)
                return 0f;

            float margin = 0f;
            float width = Math.Max(1f, ViewBox.Width - margin * 2f);
            float colWidth = FOOTER_COL_WIDTH_PX * scale;
            int cols = Math.Max(1, (int)Math.Floor(width / Math.Max(1f, colWidth)));
            int rows = (int)Math.Ceiling(Math.Min(entries, FOOTER_MAX_ROWS * cols) / (float)cols);
            return (FOOTER_HEADER_HEIGHT_PX + FOOTER_ROW_HEIGHT_PX * rows + 12f) * layoutScale;
        }

        void GetRadarCappedScales(out float cappedScale, out float cappedLayoutScale)
        {
            float configScale = Math.Max(AppConfig.Scale, 0.0001f);
            float autoScale = Scale / configScale;
            float cappedUserScale = Math.Min(configScale, 1f);
            cappedScale = autoScale * cappedUserScale;
            cappedLayoutScale = cappedScale * FontScale;
        }

        void BuildSortedContacts()
        {
            _sortedContacts.Clear();
            var shipPos = Block.WorldMatrix.Translation;

            // Entity contacts: only those within detection range
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
            switch (c.Relationship)
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

        static Action<List<MySprite>, Vector2, Color, float, float> ContactDrawFunction(ContactRecord contact)
        {
            switch (contact.Relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    return DrawSquare;
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return DrawCircle;
                default:
                    return DrawTriangle;
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

        void TrimText(ref StringBuilder sb, float availableWidth, float fontSize = 1f)
        {
            Vector2 textSize = Surface.MeasureStringInPixels(sb, TextFont, fontSize * Scale * FontScale);
            if (textSize.X <= availableWidth)
                return;

            var source = sb.ToString();
            for (int i = source.Length - 1; i > 0; i--)
            {
                sb.Clear();
                sb.Append(FormatingHelper.TrimName(source, i));
                textSize = Surface.MeasureStringInPixels(sb, TextFont, fontSize * Scale * FontScale);
                if (textSize.X <= availableWidth)
                    break;
            }
        }

        static int GetScrollStep(int secondsPerStep)
        {
            try
            {
                var sess = MyAPIGateway.Session;
                if (sess == null)
                    return 0;
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
