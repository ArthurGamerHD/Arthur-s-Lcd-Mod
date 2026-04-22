using System;
using System.Collections.Generic;
using System.Text;
using Generated;
using Graph.Apps.Abstract;
using Graph.Extensions;
using Graph.Helpers;
using Graph.System.TerminalControls.Color;
using Graph.System.TerminalControls.Generic;
using Graph.System.TerminalControls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage;
using VRage.Utils;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyCockpit = Sandbox.ModAPI.IMyCockpit;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;

namespace Graph.Apps.Radar
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

    [MyTextSurfaceScript(ID, TITLE)]
    public partial class RadarSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControlGroup<ColorsTerminalControlGroup>,
        IUsesTerminalControl<SwitchToggleHeader>,
        IUsesTerminalControl<SliderScale>
    {
        public const string ID = "LCDMod_Radar";
        public const string TITLE = "LCDMod_Radar";

        protected override string DefaultTitle => TITLE;

        // Fixed ring distances (meters) drawn regardless of dynamic range
        const float RING_1_M = 800f;
        const float RING_2_M = 1400f;
        const float RING_3_M = 2000f;
        const float DEFAULT_RANGE = 3000f;

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
        const float RADAR_SIZE_SCALE = 0.95f;
        const float QUADRANT_LINE_COVERAGE_PER_SIDE = .8f;
        const float PROJECTION_ANGLE_DEG = 55f;
        const int FOOTER_MAX_ROWS = 4;
        const float FOOTER_ROW_HEIGHT_PX = 18f;
        const float FOOTER_HEADER_HEIGHT_PX = 14f;
        const float FOOTER_COL_WIDTH_PX = 230f;
        const float FOOTER_RADAR_CLEARANCE_FACTOR = 0.35f;
        const int FOOTER_SCROLL_STEP_SECONDS = 2;


        readonly Dictionary<long, ContactRecord> _contacts = new Dictionary<long, ContactRecord>();
        readonly HashSet<long> _seenThisFrame = new HashSet<long>();
        readonly HashSet<long> _processedGroupGridIds = new HashSet<long>();
        readonly Dictionary<long, string> _factionIconCache = new Dictionary<long, string>();
        readonly List<long> _toRemove = new List<long>();
        readonly List<ContactRecord> _sortedContacts = new List<ContactRecord>();
        readonly List<IMyCubeGrid> _tempGroupGrids = new List<IMyCubeGrid>();

        float _maxRange = DEFAULT_RANGE;
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
        long _cachedCharacterId;
        Sandbox.Game.EntityComponents.MyTargetLockingComponent _cachedCharacterTargetLocking;
        float _radarProjectionCos;
        float _radarProjectionSin;

        struct TargetInfo
        {
            public Vector3 Position;
            public Color IconColor;
            public string IconTexture;
            public Color ElevationColor;
            public bool TargetLock;
            public float TargetLockPercent;
            public bool AbovePlane;
            public float Rotation;
            public Action<List<MySprite>, Vector2, Color, float, float> DrawFunction;
        }


        public RadarSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }


        public override void Run()
        {
            base.Run();
            if (Config == null)
                return;

            CollectContacts();
            PurgeStaleContacts();
            UpdateFooterHeight();

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                DrawTitle(sprites); // sets CaretY; respects Config.TitleVisible
                RenderRadar(sprites);
                DrawFooter(sprites);
                frame.AddRange(sprites);
            }
        }


        void CollectContacts()
        {
            _seenThisFrame.Clear();
            float detectedRange = 0f;

            CollectEntityContacts();

            _maxRange = detectedRange > 1f ? detectedRange : DEFAULT_RANGE;

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

                var shipPos = ((IMyEntity)Block).WorldMatrix.Translation;
                float range = _maxRange;
                var ownGrid = Block.CubeGrid as IMyCubeGrid;
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
                    catch
                    {
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

                    long entityId = selectedGrid.EntityId;
                    if (_seenThisFrame.Contains(entityId)) continue;
                    _seenThisFrame.Add(entityId);

                    var pos = selectedGrid.WorldMatrix.Translation;
                    float dist = (float)Vector3D.Distance(pos, shipPos);

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
            long lockedGridId = 0;

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
            var characterEntity = player?.Character as VRage.ModAPI.IMyEntity;
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
            var warnColor = Config.WarningColor;
            var errColor = Config.ErrorColor;
            var allyColor = Config.HeaderColor;
            var planeColor = new Color(ForegroundColor, 0.12f);

            UpdateProjectionAngle();

            float minScale = Math.Min(Scale, 1f);
            float margin = RADAR_MARGIN_PX * minScale;
            float areaTop = CaretY + margin;
            float radarFooterClearance = FooterHeight * FOOTER_RADAR_CLEARANCE_FACTOR;
            float areaBottom = ViewBox.Bottom - radarFooterClearance - margin;
            float areaHeight = areaBottom - areaTop;
            float areaWidth = ViewBox.Width - margin * 2f;
            if (areaWidth <= 0f || areaHeight <= 0f) return;

            Vector2 viewportCropped = new Vector2(
                areaWidth,
                areaHeight - (RANGE_TEXT_SIZE * SIZE_TO_PX + _borderPadding.Y) * minScale);
            if (viewportCropped.X <= 0f || viewportCropped.Y <= 0f) return;

            float sideLength;
            if (viewportCropped.X * _radarProjectionCos < viewportCropped.Y)
                sideLength = viewportCropped.X;
            else
                sideLength = viewportCropped.Y / _radarProjectionCos;
            sideLength *= RADAR_SIZE_SCALE;

            Vector2 radarCenterPos = new Vector2(ViewBox.Center.X, areaTop + viewportCropped.Y * 0.5f);
            var radarPlaneSize = new Vector2(sideLength, sideLength * _radarProjectionCos);

            DrawRadarPlaneBackground(sprites, radarCenterPos, radarPlaneSize, minScale, lineColor, backColor,
                planeColor);
            DrawRadarPlane(sprites, radarCenterPos, radarPlaneSize, minScale, lineColor);

            BuildSortedContacts();
            BuildTargetLayers(errColor, warnColor, allyColor, _debugLockedTargetPercent);
            for (int i = 0; i < _targetsBelowPlane.Count; i++)
                DrawTargetIcon(sprites, radarCenterPos, radarPlaneSize, _targetsBelowPlane[i], minScale, backColor);

            for (int i = 0; i < _targetsAbovePlane.Count; i++)
                DrawTargetIcon(sprites, radarCenterPos, radarPlaneSize, _targetsAbovePlane[i], minScale, backColor);

            if (_debugLockedTargetEntityId != 0)
            {
                var debugText = "TARGET: " + (string.IsNullOrWhiteSpace(_debugLockedTargetName)
                                               ? "Unknown"
                                               : _debugLockedTargetName)
                                           + " (" + (_debugLockedTargetPercent * 100f).ToString("F0") + "%)";
                float debugScale = 0.5f * minScale;
                float debugOffsetY =
                    Surface.MeasureStringInPixels(new StringBuilder(debugText), "White", debugScale).Y * 0.5f;
                var debugColor = _debugLockedTargetPercent >= 0.99f ? Config.ErrorColor : Config.WarningColor;
                sprites.Add(new MySprite(
                    SpriteType.TEXT,
                    debugText,
                    radarCenterPos + new Vector2(0f, radarPlaneSize.Y * 0.5f + 12f * minScale + debugOffsetY),
                    null,
                    new Color(debugColor, 0.85f),
                    "White",
                    TextAlignment.CENTER,
                    debugScale));
            }
        }

        void UpdateProjectionAngle()
        {
            float rads = MathHelper.ToRadians(PROJECTION_ANGLE_DEG);
            _radarProjectionCos = (float)Math.Cos(rads);
            _radarProjectionSin = (float)Math.Sin(rads);
        }

        void BuildTargetLayers(Color errColor, Color warnColor, Color allyColor, float debugLockedTargetPercent)
        {
            _targetsBelowPlane.Clear();
            _targetsAbovePlane.Clear();

            var gridMatrix = ((IMyEntity)Block).WorldMatrix;
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
                        info.ElevationColor = info.ElevationColor.MulSaturation(colorMultiplier).MulValue(colorMultiplier);
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

        bool TryBuildTargetInfo(ContactRecord contact, MatrixD gridMatrix, Color errColor, Color warnColor,
            Color allyColor,
            out TargetInfo targetInfo)
        {
            var transformedDirection =
                Vector3D.TransformNormal(contact.WorldPosition - gridMatrix.Translation, MatrixD.Transpose(gridMatrix));
            var position = new Vector3((float)transformedDirection.X, (float)transformedDirection.Z,
                (float)transformedDirection.Y);

            bool inRange = position.X * position.X + position.Y * position.Y < _maxRange * _maxRange;
            bool isGps = contact.EntityId < 0;
            bool above = position.Z >= 0f;
            float angle = 0f;
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

                float angleOffset = position.Z > 0f ? MathHelper.Pi : 0f;
                position = Vector3.Normalize(directionFlat);
                angle = angleOffset + MathHelper.PiOver2;
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
                Rotation = angle,
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

            float angleTextSize = QUADRANT_LABEL_TEXT_SIZE * scale;
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
                "White",
                TextAlignment.CENTER,
                angleTextSize));
            sprites.Add(new MySprite(
                SpriteType.TEXT,
                "90º",
                radarScreenCenter + halfHorizontal + new Vector2(labelMargin - sideLabelInwardOffset, 0f) +
                angleLabelOffset,
                null,
                angleColor,
                "White",
                TextAlignment.LEFT,
                angleTextSize));
            sprites.Add(new MySprite(
                SpriteType.TEXT,
                "180º",
                radarScreenCenter + halfVertical + new Vector2(0f, labelMargin) + angleLabelOffset,
                null,
                angleColor,
                "White",
                TextAlignment.CENTER,
                angleTextSize));
            sprites.Add(new MySprite(
                SpriteType.TEXT,
                "270º",
                radarScreenCenter - halfHorizontal - new Vector2(labelMargin - sideLabelInwardOffset, 0f) +
                angleLabelOffset,
                null,
                angleColor,
                "White",
                TextAlignment.RIGHT,
                angleTextSize));
        }

        void DrawTargetIcon(List<MySprite> sprites, Vector2 screenCenter, Vector2 radarPlaneSize, TargetInfo targetInfo, float scale, Color backColor)
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

        protected override void DrawFooter(List<MySprite> sprites)
        {
            if (FooterHeight <= 0f)
                return;

            BuildSortedContacts();
            if (_sortedContacts.Count == 0)
                return;

            float margin = ViewBox.Width * Margin;
            float left = ViewBox.X + margin;
            float right = ViewBox.Right - margin;
            float width = Math.Max(1f, right - left);
            float top = ViewBox.Bottom - FooterHeight;
            float pad = 6f * Scale;
            float rowHeight = FOOTER_ROW_HEIGHT_PX * Scale;
            float headerHeight = FOOTER_HEADER_HEIGHT_PX * Scale;
            float colWidth = FOOTER_COL_WIDTH_PX * Scale;
            int cols = Math.Max(1, (int)Math.Floor(width / Math.Max(1f, colWidth)));
            int visibleEntries = FOOTER_MAX_ROWS * cols;
            int maxEntries = Math.Min(_sortedContacts.Count, visibleEntries);
            int startIndex = 0;
            if (_sortedContacts.Count > visibleEntries)
                startIndex = GetScrollStep(FOOTER_SCROLL_STEP_SECONDS) % _sortedContacts.Count;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(left + width * 0.5f, top + FooterHeight * 0.5f),
                Size = new Vector2(width, FooterHeight),
                Color = new Color(BackgroundColor, 0.78f),
                Alignment = TextAlignment.CENTER
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = "DETECTED ENTITIES",
                Position = new Vector2(left + pad, top + 1f * Scale),
                Color = new Color(ForegroundColor, 0.75f),
                Alignment = TextAlignment.LEFT,
                FontId = "White",
                RotationOrScale = 0.55f * Scale
            });
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = MyTexts.GetString("BlockPropertyTitle_OreDetectorRange") + ": " + FormatingHelper.DistanceToString(_maxRange),
                Position = new Vector2(left + width - pad, top + 1f * Scale),
                Color = new Color(ForegroundColor, 0.75f),
                Alignment = TextAlignment.RIGHT,
                FontId = "White",
                RotationOrScale = 0.55f * Scale
            });

            var shipPos = ((IMyEntity)Block).WorldMatrix.Translation;
            var errColor = Config.ErrorColor;
            var warnColor = Config.WarningColor;
            var allyColor = Config.HeaderColor;
            float contentTop = top + headerHeight + pad;
            float drawColWidth = width / cols;

            for (int i = 0; i < maxEntries; i++)
            {
                ContactRecord contact = _sortedContacts[(startIndex + i) % _sortedContacts.Count];
                int col = i / FOOTER_MAX_ROWS;
                int row = i % FOOTER_MAX_ROWS;

                float x = left + col * drawColWidth + pad;
                float y = contentTop + row * rowHeight;
                Color iconColor = ContactColor(contact, errColor, warnColor, allyColor);
                float iconSize = 9f * Scale;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = FooterIconTexture(contact),
                    Position = new Vector2(x + iconSize * 0.5f, y + iconSize * 0.5f),
                    Size = new Vector2(iconSize, iconSize),
                    Color = iconColor,
                    Alignment = TextAlignment.CENTER
                });

                string dist = FormatingHelper.DistanceToString((float)Vector3D.Distance(contact.WorldPosition, shipPos));
                float distanceScale = 0.48f * Scale;
                float distanceWidth = Surface.MeasureStringInPixels(new StringBuilder(dist), "White", distanceScale).X;
                float colRight = left + (col + 1) * drawColWidth - pad;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = dist,
                    Position = new Vector2(colRight, y),
                    Color = new Color(ForegroundColor, 0.75f),
                    Alignment = TextAlignment.RIGHT,
                    FontId = "White",
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
                    FontId = "White",
                    RotationOrScale = 0.5f * Scale
                });
            }
        }

        void UpdateFooterHeight()
        {
            int entries = Math.Min(MAX_CONTACTS, _contacts.Count);
            if (entries <= 0)
            {
                FooterHeight = 0f;
                return;
            }

            float margin = ViewBox.Width * Margin;
            float width = Math.Max(1f, ViewBox.Width - margin * 2f);
            float colWidth = FOOTER_COL_WIDTH_PX * Scale;
            int cols = Math.Max(1, (int)Math.Floor(width / Math.Max(1f, colWidth)));
            int rows = (int)Math.Ceiling(Math.Min(entries, FOOTER_MAX_ROWS * cols) / (float)cols);
            FooterHeight = (FOOTER_HEADER_HEIGHT_PX + FOOTER_ROW_HEIGHT_PX * rows + 12f) * Scale;
        }

        void BuildSortedContacts()
        {
            _sortedContacts.Clear();
            var shipPos = ((IMyEntity)Block).WorldMatrix.Translation;

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
                var pos = ((IMyEntity)Block).WorldMatrix.Translation;
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
    }
}
