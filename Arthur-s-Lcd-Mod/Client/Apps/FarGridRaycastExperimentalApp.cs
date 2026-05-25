using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.Tooltip;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using LcdMod.Common.Config.Models.Apps;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Game.Models;
using VRage.ModAPI;
using VRageMath;
using SliderFov = LcdMod.Client.Terminal.Controls.Generic.SliderFov;

namespace LcdMod.Client.Apps
{
    public class FarGridRaycastExperimentalApp : AppBase, IAppInteractive
    {
        public const string ID = "LcdMod_FarGridRaycastExperimental";
        public const string TITLE = "Far Grid Raycast Experimental";

        const float REFERENCE_FONT_SCALE = 0.1f;
        const float REFERENCE_CHARACTERS = 178f;
        const float REFERENCE_PIXELS = 512f;
        const float PIXELS_PER_CHARACTER = REFERENCE_PIXELS / REFERENCE_CHARACTERS;
        const float CHARACTERS_PER_PIXEL = REFERENCE_CHARACTERS / REFERENCE_PIXELS;

        const int INTERACTIVE_HITBOX_DOWNSCALE = 4;
        const float BASE_HORIZONTAL_FOV_DEGREES = 70f;
        const float MIN_HORIZONTAL_FOV_DEGREES = 0.1f;
        const float MAX_HORIZONTAL_FOV_DEGREES = 120f;
        const double FOV_PADDING_MULTIPLIER = 1.15d;
        const double TARGET_SEARCH_DISTANCE = 3600d;

        const long MAGNIFICATION_HUD_VISIBLE_FRAMES = 300L;
        const string FONT = "LcdMod_Monospace";
        const string MISS_GLYPH = "";
        const int COLOR_GLYPH_BASE = 0xE100;

        new ScreenConfigRaycast AppConfig => (ScreenConfigRaycast)base.AppConfig;
        float RayDensityMultiplier => AppConfig.RenderScale;
        int RaysPerTick => Math.Max(1, AppConfig.RaysPerTick);

        readonly IAppHost _host;
        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly List<ControlBase> _interactiveList = new List<ControlBase>();
        readonly List<IMyEntity> _entities = new List<IMyEntity>();
        readonly List<VisibleTarget> _visibleTargets = new List<VisibleTarget>();
        readonly List<IMyCubeGrid> _tempGroupGrids = new List<IMyCubeGrid>();
        readonly HashSet<long> _excludedGridIds = new HashSet<long>();
        readonly object _sampleLock = new object();
        readonly List<MySprite> _cachedFrameSprites = new List<MySprite>();
        string[] _cachedVoxelFrameRows = Array.Empty<string>();
        string[] _cachedColorFrameRows = Array.Empty<string>();
        string[] _cachedRelationFrameRows = Array.Empty<string>();
        bool[] _dirtyFrameRows = Array.Empty<bool>();
        bool[] _renderDirtyFrameRows = Array.Empty<bool>();
        readonly List<DetectedGridHitbox> _detectedGridHitboxes = new List<DetectedGridHitbox>();

        readonly Dictionary<long, DetectedGridHitbox> _detectedGridHitboxByEntityId =
            new Dictionary<long, DetectedGridHitbox>();

        readonly Dictionary<long, DetectedGridInfo> _detectedGridInfoByEntityId =
            new Dictionary<long, DetectedGridInfo>();

        Dictionary<BlockColorCacheKey, Color> _blockColorCache = new Dictionary<BlockColorCacheKey, Color>();

        RaySample[] _samples = Array.Empty<RaySample>();
        RaySample[] _renderSamples = Array.Empty<RaySample>();
        int _columns;
        int _rows;
        int _scanGeneration;
        int _scanIndex;
        int _nextScanStartIndex = -1;
        int _sampleWriteVersion;
        int _scanRaysPerSlice;
        bool _scanPending;
        ScanJob _scanJob;
        float _horizontalFovDegrees = BASE_HORIZONTAL_FOV_DEGREES;
        long _lastFovChangedFrame = long.MinValue;
        bool _frameCacheValid;
        int _cachedFrameGeneration = int.MinValue;
        int _cachedFrameWriteVersion = int.MinValue;
        int _cachedFrameColumns = -1;
        int _cachedFrameRows = -1;
        int _cachedFrameSampleLength = -1;
        int _cachedFrameRelationOverlay = -1;
        float _cachedFrameViewX;
        float _cachedFrameViewY;
        float _cachedFrameViewWidth;
        float _cachedFrameViewHeight;
        float _cachedFrameFontScale;
        bool _frameBuildPending;
        bool _frameBuildWorkerRunning;
        bool _frameBuildQueued;

        IMyCubeBlock Block => _host.Block;
        Sandbox.ModAPI.Ingame.IMyTextSurface Surface => _host.Surface;
        RectangleF ViewBox => _host.ViewBox;
        float FontScale => _host.Surface.FontSize;
        Color ForegroundColor => _host.ForegroundColor;
        public List<ControlBase> InteractiveList => _interactiveList;

        public FarGridRaycastExperimentalApp(ScreenConfigRaycast config, IAppHost host)
            : base(config, host)
        {
            _host = host;
        }

        public bool HasVisibleItems()
        {
            return true;
        }

        public override void Update()
        {
            if (IsScanPending())
            {
                return;
            }

            Vector3D origin;
            Vector3D forward;
            Vector3D right;
            Vector3D up;
            if (!TryGetViewFrame(out origin, out forward, out right, out up))
            {
                ClearSamples();
                return;
            }

            int displayColumns;
            int displayRows;
            GetFixedCanvasSize(out displayColumns, out displayRows);

            int columns;
            int rows;
            GetRayCanvasSize(displayColumns, displayRows, out columns, out rows);
            double horizontalFovRadians = GetHorizontalFovRadians();
            double horizontalTan = Math.Tan(horizontalFovRadians * 0.5d) * FOV_PADDING_MULTIPLIER;
            double verticalTan = Math.Tan(GetVerticalFovRadians() * 0.5d) * FOV_PADDING_MULTIPLIER;

            BuildVisibleGridList(origin, forward, right, up, horizontalTan, verticalTan);
            if (_visibleTargets.Count == 0)
            {
                ClearSamples();
                return;
            }

            BeginScan(origin, forward, right, up, columns, rows, horizontalTan, verticalTan);
        }

        bool TryGetViewFrame(out Vector3D origin, out Vector3D forward, out Vector3D right, out Vector3D up)
        {
            origin = Vector3D.Zero;
            forward = Vector3D.Zero;
            right = Vector3D.Zero;
            up = Vector3D.Zero;

            if (Block == null)
                return false;

            MatrixD referenceWorld;
            if (TryGetReferenceWorldMatrix(out referenceWorld))
            {
                origin = referenceWorld.Translation;
                forward = referenceWorld.Forward;
                right = referenceWorld.Right;
                up = referenceWorld.Up;
                return Normalize(ref forward) && Normalize(ref right) && Normalize(ref up);
            }

            origin = Block.GetPosition();
            var world = Block.WorldMatrix;
            forward = world.Forward;
            if (!Normalize(ref forward))
                return false;

            right = world.Right;
            right -= forward * Vector3D.Dot(right, forward);
            if (!Normalize(ref right))
            {
                GetViewBasis(forward, out right, out up);
                return true;
            }

            up = Vector3D.Cross(right, forward);
            return Normalize(ref up);
        }

        bool TryGetReferenceWorldMatrix(out MatrixD world)
        {
            return _host.TryGetReferenceWorldMatrix(AppConfig?.ReferenceMode ?? (int)ReferenceMode.Auto, out world);
        }

        public void OnMouseScroll(int delta, ref bool handled)
        {
            if (delta == 0 || handled)
                return;

            float magnification = SliderFov.FovToMagnification(_horizontalFovDegrees);
            float step = delta > 0 ? 1.1f : 1f / 1.1f;
            float nextMagnification = magnification * step;
            float nextFov = MathHelper.Clamp(SliderFov.MagnificationToFov(nextMagnification),
                MIN_HORIZONTAL_FOV_DEGREES, MAX_HORIZONTAL_FOV_DEGREES);

            if (Math.Abs(_horizontalFovDegrees - nextFov) <= 0.001f)
                return;

            _horizontalFovDegrees = nextFov;
            _lastFovChangedFrame = GetCurrentGameFrame();
            RestartScanPreservingCursor();
            handled = true;
        }

        void BuildVisibleGridList(Vector3D origin, Vector3D forward, Vector3D right, Vector3D up,
            double horizontalTan, double verticalTan)
        {
            var ownGrid = Block?.CubeGrid;
            long ownGridOwnerId = GetPrimaryGridOwner(ownGrid);
            BuildExcludedGridIds(ownGrid);
            var searchSphere = new BoundingSphereD(origin + forward * (TARGET_SEARCH_DISTANCE * 0.5d),
                TARGET_SEARCH_DISTANCE * 0.5d);

            _entities.Clear();
            var entitiesInSphere = MyAPIGateway.Entities.GetEntitiesInSphere(ref searchSphere);
            if (entitiesInSphere != null)
            {
                foreach (var t in entitiesInSphere)
                    _entities.Add(t);

                entitiesInSphere.Clear();
            }

            _visibleTargets.Clear();
            _detectedGridInfoByEntityId.Clear();

            foreach (var t in _entities)
            {
                var grid = t as IMyCubeGrid;
                if (grid != null)
                {
                    if (grid.MarkedForClose)
                        continue;

                    if (_excludedGridIds.Contains(grid.EntityId))
                        continue;

                    var gridBounds = grid.WorldAABB;
                    VisibleTarget visibleGrid;
                    if (!TryBuildVisibleGrid(grid, gridBounds, origin, forward, right, up, horizontalTan, verticalTan,
                            ownGridOwnerId, out visibleGrid))
                        continue;

                    _visibleTargets.Add(visibleGrid);
                    _detectedGridInfoByEntityId[visibleGrid.EntityId] = visibleGrid.Info;
                    continue;
                }

                var voxel = t as MyVoxelMap;
                if (voxel == null || voxel.MarkedForClose)
                    continue;

                var voxelBounds = ((IMyEntity)voxel).WorldAABB;
                VisibleTarget visibleVoxel;
                if (!TryBuildVisibleVoxel(voxel, voxelBounds, origin, forward, right, up, horizontalTan, verticalTan,
                        out visibleVoxel))
                    continue;

                _visibleTargets.Add(visibleVoxel);
                _detectedGridInfoByEntityId[visibleVoxel.EntityId] = visibleVoxel.Info;
            }

            _visibleTargets.Sort((a, b) => a.SortDistance.CompareTo(b.SortDistance));
        }

        bool TryBuildVisibleGrid(IMyCubeGrid grid, BoundingBoxD bounds, Vector3D origin, Vector3D forward,
            Vector3D right, Vector3D up, double horizontalTan, double verticalTan, long ownGridOwnerId,
            out VisibleTarget visible)
        {
            visible = new VisibleTarget();

            double centerDistance;
            double sortDistance;
            if (!TryGetVisibleBoundsDistances(bounds, origin, forward, right, up, horizontalTan, verticalTan,
                    out centerDistance, out sortDistance))
                return false;

            visible.EntityId = grid.EntityId;
            visible.Grid = grid;
            visible.Bounds = bounds;
            visible.CenterDistance = centerDistance;
            visible.SortDistance = sortDistance;
            visible.Info = BuildDetectedGridInfo(grid, bounds, centerDistance, sortDistance, ownGridOwnerId);
            return true;
        }

        bool TryBuildVisibleVoxel(MyVoxelMap voxel, BoundingBoxD bounds, Vector3D origin, Vector3D forward,
            Vector3D right, Vector3D up, double horizontalTan, double verticalTan, out VisibleTarget visible)
        {
            visible = new VisibleTarget();

            double centerDistance;
            double sortDistance;
            if (!TryGetVisibleBoundsDistances(bounds, origin, forward, right, up, horizontalTan, verticalTan,
                    out centerDistance, out sortDistance))
                return false;

            visible.EntityId = voxel.EntityId;
            visible.Voxel = voxel;
            visible.Bounds = bounds;
            visible.CenterDistance = centerDistance;
            visible.SortDistance = sortDistance;
            visible.Info = BuildDetectedVoxelInfo(voxel, bounds, centerDistance, sortDistance);
            return true;
        }

        static DetectedGridInfo BuildDetectedGridInfo(IMyCubeGrid grid, BoundingBoxD bounds,
            double centerDistance, double sortDistance, long ownGridOwnerId)
        {
            var info = new DetectedGridInfo
            {
                EntityId = grid?.EntityId ?? 0L,
                Name = GetGridDisplayName(grid),
                EntityKind = "Grid",
                GridSize = grid != null ? grid.GridSizeEnum.ToString() : "Unknown",
                GridSizeMeters = grid?.GridSize ?? 0f,
                OwnerId = GetPrimaryGridOwner(grid),
                Relationship = grid != null ? GetGridRelationship(grid, ownGridOwnerId).ToString() : "Unknown",
                Center = bounds.Center,
                Size = bounds.Size,
                CenterDistance = Math.Max(0d, centerDistance),
                SortDistance = Math.Max(0d, sortDistance)
            };

            return info;
        }

        static DetectedGridInfo BuildDetectedVoxelInfo(MyVoxelMap voxel, BoundingBoxD bounds,
            double centerDistance, double sortDistance)
        {
            return new DetectedGridInfo
            {
                EntityId = voxel?.EntityId ?? 0L,
                Name = GetVoxelDisplayName(voxel),
                EntityKind = "Asteroid",
                GridSize = "Voxel map",
                GridSizeMeters = 0f,
                OwnerId = 0L,
                Relationship = "Natural",
                Center = bounds.Center,
                Size = bounds.Size,
                CenterDistance = Math.Max(0d, centerDistance),
                SortDistance = Math.Max(0d, sortDistance)
            };
        }

        static bool TryGetVisibleBoundsDistances(BoundingBoxD bounds, Vector3D origin, Vector3D forward,
            Vector3D right, Vector3D up, double horizontalTan, double verticalTan, out double centerDistance,
            out double sortDistance)
        {
            centerDistance = 0d;
            sortDistance = 0d;

            var center = bounds.Center;
            var halfExtents = bounds.HalfExtents;
            double radius = Math.Max(1d, halfExtents.Length());
            var toCenter = center - origin;
            double forwardDistance = Vector3D.Dot(toCenter, forward);
            if (forwardDistance < -radius || forwardDistance - radius > TARGET_SEARCH_DISTANCE)
                return false;

            double x = Vector3D.Dot(toCenter, right);
            double y = Vector3D.Dot(toCenter, up);
            double positiveDepth = Math.Max(0d, forwardDistance);
            double halfWidth = positiveDepth * horizontalTan;
            double halfHeight = positiveDepth * verticalTan;
            if (Math.Abs(x) > halfWidth + radius || Math.Abs(y) > halfHeight + radius)
                return false;

            centerDistance = Math.Max(0d, Vector3D.Distance(origin, center));
            sortDistance = Math.Max(0d, forwardDistance - radius);
            return true;
        }

        static string GetGridDisplayName(IMyCubeGrid grid)
        {
            if (grid == null)
                return "Unknown grid";

            var name = grid.CustomName;
            if (string.IsNullOrWhiteSpace(name))
                name = grid.DisplayName;
            if (string.IsNullOrWhiteSpace(name))
                name = "Grid " + grid.EntityId;

            return name;
        }

        static string GetVoxelDisplayName(MyVoxelMap voxel)
        {
            if (voxel == null)
                return "Asteroid";

            var name = voxel.StorageName;
            if (string.IsNullOrWhiteSpace(name))
                name = voxel.AsteroidName;
            if (string.IsNullOrWhiteSpace(name))
                name = voxel.DisplayName;
            if (string.IsNullOrWhiteSpace(name))
                name = "Asteroid " + voxel.EntityId;

            return name;
        }

        void BuildExcludedGridIds(IMyCubeGrid ownGrid)
        {
            _excludedGridIds.Clear();
            if (ownGrid == null)
                return;

            _excludedGridIds.Add(ownGrid.EntityId);
            AddGridGroupToExclusion(ownGrid, GridLinkTypeEnum.Logical);
            AddGridGroupToExclusion(ownGrid, GridLinkTypeEnum.Mechanical);
        }

        void AddGridGroupToExclusion(IMyCubeGrid ownGrid, GridLinkTypeEnum linkType)
        {
            _tempGroupGrids.Clear();
            try
            {
                MyAPIGateway.GridGroups.GetGroup(ownGrid, linkType, _tempGroupGrids);
            }
            catch
            {
                _tempGroupGrids.Clear();
            }

            foreach (var grid in _tempGroupGrids)
            {
                if (grid != null)
                    _excludedGridIds.Add(grid.EntityId);
            }
        }

        long[] CopyExcludedGridIds()
        {
            var result = new long[_excludedGridIds.Count];
            int index = 0;
            foreach (var gridId in _excludedGridIds)
                result[index++] = gridId;
            return result;
        }

        static bool ContainsGridId(long[] gridIds, long gridId)
        {
            if (gridIds == null)
                return false;

            foreach (var t in gridIds)
            {
                if (t == gridId)
                    return true;
            }

            return false;
        }

        void BeginScan(Vector3D origin, Vector3D forward, Vector3D right, Vector3D up, int columns, int rows,
            double horizontalTan, double verticalTan)
        {
            int rayCount = Math.Max(1, columns * rows);
            int generation = BeginSampleBatch(columns, rows, rayCount);

            _scanJob = new ScanJob
            {
                Generation = generation,
                Origin = origin,
                Forward = forward,
                Right = right,
                Up = up,
                Columns = columns,
                Rows = rows,
                RayCount = rayCount,
                HorizontalTan = horizontalTan,
                VerticalTan = verticalTan,
                OwnGridOwnerId = GetPrimaryGridOwner(Block?.CubeGrid),
                ExcludedGridIds = CopyExcludedGridIds(),
                VisibleTargets = _visibleTargets.ToArray()
            };

            ScheduleScanSlice(generation);
        }

        int BeginSampleBatch(int columns, int rows, int rayCount)
        {
            lock (_sampleLock)
            {
                _scanGeneration++;
                _columns = columns;
                _rows = rows;
                _scanIndex = _nextScanStartIndex >= 0 && _nextScanStartIndex < rayCount
                    ? _nextScanStartIndex
                    : 0;
                _nextScanStartIndex = -1;
                _scanRaysPerSlice = Math.Min(rayCount, RaysPerTick);
                _scanPending = true;
                EnsureSampleBufferSize(rayCount);
                EnsureDirtyFrameRowsSize(rows);
                MarkAllFrameRowsDirty();
                return _scanGeneration;
            }
        }

        void ScheduleScanSlice(int generation)
        {
            LcdModClientComponent.RunNextFrame.Add(delegate { RunScanSlice(generation); });
        }

        void RunScanSlice(int generation)
        {
            if (Block == null || Block.MarkedForClose || Surface == null)
            {
                lock (_sampleLock)
                {
                    if (generation == _scanGeneration)
                        _scanPending = false;
                }

                return;
            }

            ScanJob job;
            int startIndex;
            int endIndex;
            lock (_sampleLock)
            {
                if (!_scanPending || generation != _scanGeneration)
                    return;

                job = _scanJob;
                startIndex = _scanIndex;
                endIndex = Math.Min(job.RayCount, startIndex + _scanRaysPerSlice);
                _scanIndex = endIndex;
            }

            for (int index = startIndex; index < endIndex; index++)
                CastScanRay(job, index);

            lock (_sampleLock)
            {
                if (!_scanPending || generation != _scanGeneration)
                    return;

                if (_scanIndex < job.RayCount)
                    ScheduleScanSlice(generation);
                else
                    _scanPending = false;
            }

            RequestFrameCacheRebuild();
        }

        void CastScanRay(ScanJob job, int index)
        {
            int row = index / job.Columns;
            int col = index - row * job.Columns;
            double x = ((col + 0.5d) / job.Columns - 0.5d) * 2d * job.HorizontalTan;
            double y = (0.5d - (row + 0.5d) / job.Rows) * 2d * job.VerticalTan;
            var direction = job.Forward + job.Right * x + job.Up * y;
            if (!Normalize(ref direction))
            {
                SetSample(job, index, new RaySample());
                return;
            }

            double firstEnter;
            double lastExit;
            if (!TryGetRayTargetSpan(job, direction, out firstEnter, out lastExit))
            {
                SetSample(job, index, new RaySample());
                return;
            }

            var start = job.Origin + direction * firstEnter;
            var end = job.Origin + direction * lastExit;
            var closestSample = new RaySample();
            AddClosestVoxelSample(job, start, end, ref closestSample);

            var physics = MyAPIGateway.Physics;
            if (physics != null)
            {
                var hits = new List<IHitInfo>();
                try
                {
                    physics.CastRay(start, end, hits);
                    AddClosestPhysicsSampleFromHits(job, hits, ref closestSample);
                }
                catch
                {
                    IHitInfo hit;
                    if (physics.CastRay(start, end, out hit))
                    {
                        var sample = BuildSample(job.Origin, hit, job.OwnGridOwnerId,
                            job.ExcludedGridIds);
                        if (IsCloserSample(sample, closestSample))
                            closestSample = sample;
                    }
                }
            }

            SetSample(job, index, closestSample);
        }

        void AddClosestVoxelSample(ScanJob job, Vector3D start, Vector3D end, ref RaySample closestSample)
        {
            if (job.VisibleTargets == null)
                return;

            foreach (var visible in job.VisibleTargets)
            {
                if (visible.Voxel == null)
                    continue;

                var voxelSample = CastVisibleVoxelRay(job, visible.Voxel, start, end);
                if (IsCloserSample(voxelSample, closestSample))
                    closestSample = voxelSample;
            }
        }

        bool TryGetRayTargetSpan(ScanJob job, Vector3D direction, out double firstEnter, out double lastExit)
        {
            firstEnter = double.MaxValue;
            lastExit = double.MinValue;

            if (job.VisibleTargets == null)
                return false;

            for (int i = 0; i < job.VisibleTargets.Length; i++)
            {
                double enter;
                double exit;
                if (!TryRayBoxSegment(job.Origin, direction, job.VisibleTargets[i].Bounds, out enter, out exit))
                    continue;

                if (enter < firstEnter)
                    firstEnter = enter;
                if (exit > lastExit)
                    lastExit = exit;
            }

            return firstEnter < double.MaxValue && lastExit > double.MinValue && firstEnter <= lastExit;
        }

        void AddClosestPhysicsSampleFromHits(ScanJob job, List<IHitInfo> rayHits,
            ref RaySample closestSample)
        {
            if (rayHits == null)
                return;

            foreach (var t in rayHits)
            {
                var sample = BuildSample(job.Origin, t, job.OwnGridOwnerId,
                    job.ExcludedGridIds);
                if (IsCloserSample(sample, closestSample))
                    closestSample = sample;
            }
        }

        RaySample CastVisibleVoxelRay(ScanJob job, MyVoxelMap voxel, Vector3D rayStart, Vector3D rayEnd)
        {
            if (voxel == null)
                return new RaySample();

            var line = new LineD(rayStart, rayEnd);
            MyIntersectionResultLineTriangleEx? triangle;
            if (!voxel.GetIntersectionWithLine(ref line, out triangle) || !triangle.HasValue)
                return new RaySample();

            var hit = triangle.Value;
            return BuildVoxelSample(job.Origin, voxel, hit.IntersectionPointInWorldSpace, hit.NormalInWorldSpace);
        }

        void SetSample(ScanJob job, int index, RaySample sample)
        {
            lock (_sampleLock)
            {
                if (!_scanPending || job.Generation != _scanGeneration)
                    return;

                bool wasHit = index < _samples.Length && _samples[index].Hit;
                _samples[index] = sample;
                MarkSampleRowDirty(job, index);
                _sampleWriteVersion++;
                if (!wasHit && sample.Hit)
                {
                }
                else if (wasHit && !sample.Hit)
                {
                }
            }
        }

        RaySample BuildSample(Vector3D origin, IHitInfo hit,
            long ownGridOwnerId, long[] excludedGridIds)
        {
            if (hit == null)
                return new RaySample();

            var hitGrid = ResolveHitGrid(hit.HitEntity);
            if (hitGrid == null)
            {
                var hitVoxel = hit.HitEntity as MyVoxelMap;
                return BuildVoxelSample(origin, hitVoxel, hit.Position, hit.Normal);
            }

            if (ContainsGridId(excludedGridIds, hitGrid.EntityId))
                return new RaySample();

            double hitDistance = Vector3D.Distance(origin, hit.Position);
            int normalLuminance = GetNormalLuminance(origin, hit.Position, hit.Normal);
            Vector3I gridPosition;
            bool hasGridPosition = TryGetHitGridPosition(hitGrid, hit.Position, hit.Normal, out gridPosition);
            return new RaySample
            {
                HitKind = RayHitKind.Ship,
                EntityId = hitGrid.EntityId,
                Grid = hitGrid,
                HasGridPosition = hasGridPosition,
                GridPosition = gridPosition,
                NormalLuminance = normalLuminance,
                Distance = hitDistance,
                Glyph = BuildGlyph(5, GetGridRelationship(hitGrid, ownGridOwnerId))
            };
        }

        RaySample BuildVoxelSample(Vector3D origin, MyVoxelMap voxel, Vector3D hitPosition, Vector3 normal)
        {
            if (voxel == null)
                return new RaySample();

            double hitDistance = Vector3D.Distance(origin, hitPosition);
            int normalLuminance = GetNormalLuminance(origin, hitPosition, normal);
            return new RaySample
            {
                HitKind = RayHitKind.Voxel,
                EntityId = voxel.EntityId,
                Distance = hitDistance,
                NormalLuminance = normalLuminance,
                Glyph = BuildGlyph(5, MyRelationsBetweenPlayerAndBlock.Neutral),
                ColorGlyph = BuildColorGlyph(Color.White, normalLuminance)
            };
        }

        static bool IsCloserSample(RaySample candidate, RaySample current)
        {
            if (!candidate.Hit)
                return false;
            if (!current.Hit)
                return true;

            return candidate.Distance < current.Distance;
        }

        bool IsScanPending()
        {
            lock (_sampleLock)
            {
                return _scanPending;
            }
        }

        IMyCubeGrid ResolveHitGrid(IMyEntity entity)
        {
            var grid = entity as IMyCubeGrid;
            if (grid != null)
                return grid;

            var cubeBlock = entity as IMyCubeBlock;
            if (cubeBlock != null)
                return cubeBlock.CubeGrid;

            return null;
        }

        static long GetPrimaryGridOwner(IMyCubeGrid grid)
        {
            var owners = grid?.BigOwners;
            return owners != null && owners.Count > 0 ? owners[0] : 0L;
        }

        static MyRelationsBetweenPlayerAndBlock GetGridRelationship(IMyCubeGrid grid, long ownGridOwnerId)
        {
            long ownerId = GetPrimaryGridOwner(grid);
            if (ownerId == 0L)
                return MyRelationsBetweenPlayerAndBlock.Neutral;

            if (ownGridOwnerId != 0L && ownerId == ownGridOwnerId)
                return MyRelationsBetweenPlayerAndBlock.Owner;

            if (ownGridOwnerId == 0L)
                return MyRelationsBetweenPlayerAndBlock.Neutral;

            var relation = MyIDModule.GetRelationPlayerPlayer(ownGridOwnerId, ownerId);
            var relationName = relation.ToString();
            if (relationName == "Enemies")
                return MyRelationsBetweenPlayerAndBlock.Enemies;
            if (relationName == "Allies" || relationName == "Friends")
                return MyRelationsBetweenPlayerAndBlock.FactionShare;

            return MyRelationsBetweenPlayerAndBlock.Neutral;
        }

        static int GetNormalLuminance(Vector3D origin, Vector3D hitPosition, Vector3 normal)
        {
            Vector3D normalDirection = normal;
            if (!Normalize(ref normalDirection))
                return 3;

            Vector3D toSource = origin - hitPosition;
            if (!Normalize(ref toSource))
                return 5;

            double facing = Math.Abs(Vector3D.Dot(normalDirection, toSource));
            return MathHelper.Clamp(1 + (int)Math.Round(facing * 4d), 1, 5);
        }

        static bool TryGetHitGridPosition(IMyCubeGrid grid, Vector3D hitPosition, Vector3 normal,
            out Vector3I gridPosition)
        {
            gridPosition = Vector3I.Zero;
            if (grid == null)
                return false;

            Vector3D inside = hitPosition;
            Vector3D normalDirection = normal;
            if (Normalize(ref normalDirection))
                inside -= normalDirection * Math.Max(0.05d, grid.GridSize * 0.1d);

            try
            {
                // Convert to grid coordinates immediately while the ray hit is being processed.
                // Equivalent to: Round(Transform(coords, WorldMatrixNormalizedInv) * GridSizeR).
                gridPosition = grid.WorldToGridInteger(inside);
                return true;
            }
            catch
            {
                gridPosition = Vector3I.Zero;
                return false;
            }
        }

        string BuildGlyph(int luminance, MyRelationsBetweenPlayerAndBlock relationship)
        {
            return BuildColorGlyph(GetRelationshipColor(relationship), luminance);
        }

        Color GetRelationshipColor(MyRelationsBetweenPlayerAndBlock relationship)
        {
            switch (relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return AppConfig?.ErrorColor ?? Color.Red;
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    return AppConfig?.HeaderColor ?? Color.Blue;
                default:
                    return AppConfig?.WarningColor ?? Color.Yellow;
            }
        }

        static string BuildColorGlyph(Color color, int luminance)
        {
            float shade = MathHelper.Clamp(luminance / 5f, 0f, 1f);
            int r = QuantizeColorChannel(color.R, shade);
            int g = QuantizeColorChannel(color.G, shade);
            int b = QuantizeColorChannel(color.B, shade);

            if (r == g && g == b && b == 0)
            {
                r = 1;
                g = 1;
                b = 1;
            }

            return new string((char)(COLOR_GLYPH_BASE + (r << 6) + (g << 3) + b), 1);
        }

        void ClearSamples()
        {
            lock (_sampleLock)
            {
                _scanGeneration++;
                _scanIndex = 0;
                _nextScanStartIndex = -1;
                _scanRaysPerSlice = 0;
                _scanPending = false;
                _sampleWriteVersion++;
                _scanJob = new ScanJob();
            }
        }

        void RestartScanPreservingCursor()
        {
            lock (_sampleLock)
            {
                _scanGeneration++;
                _nextScanStartIndex = Math.Max(0, _scanIndex);
                _scanIndex = 0;
                _scanRaysPerSlice = 0;
                _scanPending = false;
                _scanJob = new ScanJob();
            }
        }

        public override List<MySprite> GetSprites()
        {
            _sprites.Clear();
            InteractiveList.Clear();

            DrawFrame(_sprites);
            if (ShouldDrawMagnificationHud())
                DrawMagnificationHud(_sprites);

            return _sprites;
        }

        void DrawFrame(List<MySprite> sprites)
        {
            if (ViewBox.Width <= 0f || ViewBox.Height <= 0f)
                return;

            int displayColumns;
            int displayRows;
            GetFixedCanvasSize(out displayColumns, out displayRows);

            int columns;
            int rows;
            int generation;
            int writeVersion;
            int sampleLength;
            SnapshotFrameState(out columns, out rows, out generation, out writeVersion, out sampleLength);

            if (columns <= 0 || rows <= 0)
                GetRayCanvasSize(displayColumns, displayRows, out columns, out rows);

            int relationOverlay = AppConfig.RelationOverlay;
            if (IsFrameCacheValid(columns, rows, generation, writeVersion, sampleLength, relationOverlay))
            {
                AddCachedFrameSprites(sprites);
                RegisterDetectedGridHitboxes();
                return;
            }

            RequestFrameCacheRebuild();
            AddCachedFrameSprites(sprites);
            RegisterDetectedGridHitboxes();
        }

        void RequestFrameCacheRebuild()
        {
            if (_frameBuildWorkerRunning)
            {
                _frameBuildQueued = true;
                return;
            }

            if (_frameBuildPending)
                return;

            _frameBuildPending = true;
            LcdModClientComponent.RunNextFrame.Add(BuildFrameCacheIfNeeded);
        }

        void BuildFrameCacheIfNeeded()
        {
            _frameBuildPending = false;

            if (AppConfig == null || ViewBox.Width <= 0f || ViewBox.Height <= 0f)
                return;

            int displayColumns;
            int displayRows;
            GetFixedCanvasSize(out displayColumns, out displayRows);

            int columns;
            int rows;
            int generation;
            int writeVersion;
            int sampleLength;
            SnapshotFrameState(out columns, out rows, out generation, out writeVersion, out sampleLength);

            if (columns <= 0 || rows <= 0)
                GetRayCanvasSize(displayColumns, displayRows, out columns, out rows);

            int relationOverlay = AppConfig.RelationOverlay;
            if (IsFrameCacheValid(columns, rows, generation, writeVersion, sampleLength, relationOverlay))
                return;

            SnapshotFrameSamples(out columns, out rows, out generation, out writeVersion, out sampleLength);
            if (columns <= 0 || rows <= 0)
                GetRayCanvasSize(displayColumns, displayRows, out columns, out rows);

            StartFrameCacheBuild(columns, rows, generation, writeVersion, sampleLength, relationOverlay);
        }

        void SnapshotFrameState(out int columns, out int rows, out int generation, out int writeVersion,
            out int sampleLength)
        {
            lock (_sampleLock)
            {
                columns = _columns;
                rows = _rows;
                generation = _scanGeneration;
                writeVersion = _sampleWriteVersion;
                sampleLength = _samples.Length;
            }
        }

        void SnapshotFrameSamples(out int columns, out int rows, out int generation, out int writeVersion,
            out int sampleLength)
        {
            lock (_sampleLock)
            {
                columns = _columns;
                rows = _rows;
                generation = _scanGeneration;
                writeVersion = _sampleWriteVersion;
                sampleLength = _samples.Length;

                if (_renderSamples.Length != sampleLength)
                    _renderSamples = new RaySample[sampleLength];

                if (sampleLength > 0)
                    Array.Copy(_samples, _renderSamples, sampleLength);

                if (_renderDirtyFrameRows.Length != rows)
                    _renderDirtyFrameRows = new bool[Math.Max(0, rows)];

                int dirtyRowCount = Math.Min(_dirtyFrameRows.Length, _renderDirtyFrameRows.Length);
                for (int i = 0; i < dirtyRowCount; i++)
                {
                    _renderDirtyFrameRows[i] = _dirtyFrameRows[i];
                    _dirtyFrameRows[i] = false;
                }

                for (int i = dirtyRowCount; i < _renderDirtyFrameRows.Length; i++)
                    _renderDirtyFrameRows[i] = true;
            }
        }

        bool IsFrameCacheValid(int columns, int rows, int generation, int writeVersion, int sampleLength,
            int relationOverlay)
        {
            return _frameCacheValid &&
                   _cachedFrameGeneration == generation &&
                   _cachedFrameWriteVersion == writeVersion &&
                   _cachedFrameColumns == columns &&
                   _cachedFrameRows == rows &&
                   _cachedFrameSampleLength == sampleLength &&
                   _cachedFrameRelationOverlay == relationOverlay &&
                   _cachedFrameViewX.Equals(ViewBox.X) &&
                   _cachedFrameViewY.Equals(ViewBox.Y) &&
                   _cachedFrameViewWidth.Equals(ViewBox.Width) &&
                   _cachedFrameViewHeight.Equals(ViewBox.Height) &&
                   _cachedFrameFontScale.Equals(FontScale);
        }

        void AddCachedFrameSprites(List<MySprite> sprites)
        {
            for (int i = 0; i < _cachedFrameSprites.Count; i++)
                sprites.Add(_cachedFrameSprites[i]);
        }

        void StartFrameCacheBuild(int columns, int rows, int generation, int writeVersion, int sampleLength,
            int relationOverlay)
        {
            var baseSize = new Vector2(
                columns * PIXELS_PER_CHARACTER,
                rows * PIXELS_PER_CHARACTER);
            var scaleMultiplier = Math.Min(ViewBox.Width / baseSize.X, ViewBox.Height / baseSize.Y);
            var scale = REFERENCE_FONT_SCALE * scaleMultiplier;
            var size = baseSize * scaleMultiplier;
            var tableRect = new RectangleF(
                ViewBox.Center.X - size.X * 0.5f,
                ViewBox.Y + (ViewBox.Height - size.Y) * 0.5f,
                size.X,
                size.Y);

            bool rebuildAllRows = !IsFrameLayoutCacheValid(columns, rows, sampleLength, relationOverlay, tableRect);
            EnsureFrameRowCacheSize(rows, relationOverlay);

            var data = new FrameBuildData
            {
                Columns = columns,
                Rows = rows,
                Generation = generation,
                WriteVersion = writeVersion,
                SampleLength = sampleLength,
                RelationOverlay = relationOverlay,
                TableRect = tableRect,
                Scale = scale,
                RebuildAllRows = rebuildAllRows,
                Samples = new RaySample[sampleLength],
                DirtyRows = new bool[rows],
                VoxelRows = new string[rows],
                ColorRows = new string[rows],
                RelationRows = relationOverlay > 0 ? new string[rows] : null,
                BlockColorCache = SnapshotBlockColorCache()
            };
            if (sampleLength > 0)
                Array.Copy(_renderSamples, data.Samples, sampleLength);
            if (rows > 0)
                Array.Copy(_renderDirtyFrameRows, data.DirtyRows, Math.Min(rows, _renderDirtyFrameRows.Length));

            _frameBuildWorkerRunning = true;
            MyAPIGateway.Parallel.Start(
                () => BuildFrameRowsWorker(data),
                () => ApplyFrameRows(data));
        }

        void ApplyFrameRows(FrameBuildData data)
        {
            _frameBuildWorkerRunning = false;

            if (data == null || data.Exception != null)
            {
                if (_frameBuildQueued)
                {
                    _frameBuildQueued = false;
                    RequestFrameCacheRebuild();
                }

                return;
            }

            if (data.RebuildAllRows || _cachedColorFrameRows.Length != data.Rows ||
                _cachedVoxelFrameRows.Length != data.Rows)
                EnsureFrameRowCacheSize(data.Rows, data.RelationOverlay);

            for (int row = 0; row < data.Rows; row++)
            {
                if (data.VoxelRows[row] != null)
                    _cachedVoxelFrameRows[row] = data.VoxelRows[row];
                if (data.ColorRows[row] != null)
                    _cachedColorFrameRows[row] = data.ColorRows[row];
                if (data.RelationOverlay > 0 && data.RelationRows != null && data.RelationRows[row] != null)
                    _cachedRelationFrameRows[row] = data.RelationRows[row];
            }

            AdoptBlockColorCache(data.BlockColorCache);

            RebuildCachedFrameSprites(data.TableRect, data.Rows, data.RelationOverlay, data.Scale,
                data.RebuildAllRows);
            RebuildDetectedGridHitboxes(data.TableRect, data.Columns, data.Rows, data.SampleLength);

            _frameCacheValid = true;
            _cachedFrameGeneration = data.Generation;
            _cachedFrameWriteVersion = data.WriteVersion;
            _cachedFrameColumns = data.Columns;
            _cachedFrameRows = data.Rows;
            _cachedFrameSampleLength = data.SampleLength;
            _cachedFrameRelationOverlay = data.RelationOverlay;
            _cachedFrameViewX = ViewBox.X;
            _cachedFrameViewY = ViewBox.Y;
            _cachedFrameViewWidth = ViewBox.Width;
            _cachedFrameViewHeight = ViewBox.Height;
            _cachedFrameFontScale = FontScale;

            if (_frameBuildQueued)
            {
                _frameBuildQueued = false;
                RequestFrameCacheRebuild();
            }
        }

        Dictionary<BlockColorCacheKey, Color> SnapshotBlockColorCache()
        {
            if (_blockColorCache == null || _blockColorCache.Count == 0)
                return new Dictionary<BlockColorCacheKey, Color>();

            return new Dictionary<BlockColorCacheKey, Color>(_blockColorCache);
        }

        void AdoptBlockColorCache(Dictionary<BlockColorCacheKey, Color> cache)
        {
            if (cache == null)
                return;

            _blockColorCache = cache;
        }

        static void BuildFrameRowsWorker(FrameBuildData data)
        {
            try
            {
                var voxelBuilder = new StringBuilder(Math.Max(1, data.Columns * MISS_GLYPH.Length));
                var colorBuilder = new StringBuilder(Math.Max(1, data.Columns * MISS_GLYPH.Length));
                var relationBuilder = data.RelationOverlay > 0
                    ? new StringBuilder(Math.Max(1, data.Columns * MISS_GLYPH.Length))
                    : null;

                for (int row = 0; row < data.Rows; row++)
                {
                    bool dirty = data.RebuildAllRows || IsFrameBuildRowDirty(data, row);
                    if (!dirty)
                        continue;

                    data.VoxelRows[row] = BuildVoxelFrameRowWorker(data, row, voxelBuilder);
                    data.ColorRows[row] = BuildShipFrameRowWorker(data, row, colorBuilder);
                    if (data.RelationOverlay > 0)
                        data.RelationRows[row] = BuildRelationFrameRowWorker(data, row, relationBuilder);
                }
            }
            catch (Exception e)
            {
                data.Exception = e;
            }
        }

        static string BuildRelationFrameRowWorker(FrameBuildData data, int row, StringBuilder builder)
        {
            builder.Clear();
            for (int col = 0; col < data.Columns; col++)
            {
                int index = row * data.Columns + col;
                RaySample sample = index < data.SampleLength ? data.Samples[index] : new RaySample();

                if (data.RelationOverlay == 1)
                {
                    string outlineGlyph;
                    builder.Append(TryGetOutsideRelationshipOutlineGlyph(row, col, data.Columns, data.Rows,
                        data.Samples, sample, out outlineGlyph)
                        ? outlineGlyph
                        : MISS_GLYPH);
                }
                else
                {
                    builder.Append(sample.IsShip ? sample.Glyph : MISS_GLYPH);
                }
            }

            return builder.ToString();
        }

        static bool IsFrameBuildRowDirty(FrameBuildData data, int row)
        {
            if (data.DirtyRows == null || row < 0 || row >= data.DirtyRows.Length)
                return true;

            if (data.DirtyRows[row])
                return true;

            return data.RelationOverlay == 1 &&
                   ((row > 0 && row - 1 < data.DirtyRows.Length && data.DirtyRows[row - 1]) ||
                    (row + 1 < data.Rows && row + 1 < data.DirtyRows.Length && data.DirtyRows[row + 1]));
        }

        static string BuildVoxelFrameRowWorker(FrameBuildData data, int row, StringBuilder builder)
        {
            builder.Clear();
            for (int col = 0; col < data.Columns; col++)
            {
                int index = row * data.Columns + col;
                RaySample sample = index < data.SampleLength ? data.Samples[index] : new RaySample();
                builder.Append(sample.IsVoxel ? sample.ColorGlyph : MISS_GLYPH);
            }

            return builder.ToString();
        }

        static string BuildShipFrameRowWorker(FrameBuildData data, int row, StringBuilder builder)
        {
            builder.Clear();
            for (int col = 0; col < data.Columns; col++)
            {
                int index = row * data.Columns + col;
                RaySample sample = index < data.SampleLength ? data.Samples[index] : new RaySample();
                builder.Append(sample.IsShip ? ResolveShipColorGlyph(data.BlockColorCache, sample) : MISS_GLYPH);
            }

            return builder.ToString();
        }

        static string ResolveShipColorGlyph(Dictionary<BlockColorCacheKey, Color> cache, RaySample sample)
        {
            int luminance = MathHelper.Clamp(sample.NormalLuminance, 1, 5);
            if (!sample.IsShip || sample.Grid == null || !sample.HasGridPosition)
                return BuildColorGlyph(Color.White, luminance);

            var key = new BlockColorCacheKey(sample.EntityId, sample.GridPosition);
            Color cachedColor;
            if (cache != null && cache.TryGetValue(key, out cachedColor))
                return BuildColorGlyph(cachedColor, luminance);

            Color color = Color.White;
            try
            {
                var block = sample.Grid.GetCubeBlock(sample.GridPosition);
                if (block != null)
                    color = MyColorPickerConstants.HSVOffsetToHSV(block.ColorMaskHSV).HSVtoColor();
            }
            catch
            {
                color = Color.White;
            }

            if (cache != null)
                cache[key] = color;

            return BuildColorGlyph(color, luminance);
        }

        bool IsFrameLayoutCacheValid(int columns, int rows, int sampleLength, int relationOverlay,
            RectangleF tableRect)
        {
            return _frameCacheValid &&
                   _cachedFrameColumns == columns &&
                   _cachedFrameRows == rows &&
                   _cachedFrameSampleLength == sampleLength &&
                   _cachedFrameRelationOverlay == relationOverlay &&
                   _cachedFrameViewX.Equals(ViewBox.X) &&
                   _cachedFrameViewY.Equals(ViewBox.Y) &&
                   _cachedFrameViewWidth.Equals(ViewBox.Width) &&
                   _cachedFrameViewHeight.Equals(ViewBox.Height) &&
                   _cachedFrameFontScale.Equals(FontScale) &&
                   _cachedFrameSprites.Count == rows * (relationOverlay > 0 ? 3 : 2) &&
                   tableRect.Width > 0f &&
                   tableRect.Height > 0f;
        }

        void EnsureFrameRowCacheSize(int rows, int relationOverlay)
        {
            if (_cachedVoxelFrameRows.Length != rows)
                _cachedVoxelFrameRows = new string[Math.Max(0, rows)];

            if (_cachedColorFrameRows.Length != rows)
                _cachedColorFrameRows = new string[Math.Max(0, rows)];

            if (relationOverlay > 0)
            {
                if (_cachedRelationFrameRows.Length != rows)
                    _cachedRelationFrameRows = new string[Math.Max(0, rows)];
            }
            else if (_cachedRelationFrameRows.Length != 0)
            {
                _cachedRelationFrameRows = Array.Empty<string>();
            }
        }

        bool IsRenderRowDirty(int row, int rows, int relationOverlay)
        {
            if (_renderDirtyFrameRows == null || row < 0 || row >= _renderDirtyFrameRows.Length)
                return true;

            if (_renderDirtyFrameRows[row])
                return true;

            return relationOverlay == 1 &&
                   ((row > 0 && row - 1 < _renderDirtyFrameRows.Length && _renderDirtyFrameRows[row - 1]) ||
                    (row + 1 < rows && row + 1 < _renderDirtyFrameRows.Length && _renderDirtyFrameRows[row + 1]));
        }

        void RebuildCachedFrameSprites(RectangleF tableRect, int rows, int relationOverlay, float scale,
            bool rebuildAllRows)
        {
            if (!rebuildAllRows)
            {
                UpdateCachedFrameSpriteData(rows, relationOverlay);
                return;
            }

            _cachedFrameSprites.Clear();
            float lineHeight = rows > 0 ? tableRect.Height / rows : 0f;
            for (int row = 0; row < rows; row++)
            {
                _cachedFrameSprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = _cachedVoxelFrameRows[row] ?? string.Empty,
                    Position = new Vector2(tableRect.Center.X, tableRect.Y + row * lineHeight),
                    Color = Color.White,
                    FontId = FONT,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = scale
                });
            }

            for (int row = 0; row < rows; row++)
            {
                _cachedFrameSprites.Add(new MySprite
                {
                    Type = SpriteType.TEXT,
                    Data = _cachedColorFrameRows[row] ?? string.Empty,
                    Position = new Vector2(tableRect.Center.X, tableRect.Y + row * lineHeight),
                    Color = Color.White,
                    FontId = FONT,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = scale
                });
            }

            if (relationOverlay > 0)
            {
                for (int row = 0; row < rows; row++)
                {
                    _cachedFrameSprites.Add(new MySprite
                    {
                        Type = SpriteType.TEXT,
                        Data = _cachedRelationFrameRows[row] ?? string.Empty,
                        Position = new Vector2(tableRect.Center.X, tableRect.Y + row * lineHeight),
                        Color = relationOverlay == 1 ? Color.White : new Color(255, 255, 255,  16),
                        FontId = FONT,
                        Alignment = TextAlignment.CENTER,
                        RotationOrScale = scale
                    });
                }
            }
        }

        void UpdateCachedFrameSpriteData(int rows, int relationOverlay)
        {
            for (int row = 0; row < rows && row < _cachedFrameSprites.Count; row++)
            {
                if (_renderDirtyFrameRows != null && row < _renderDirtyFrameRows.Length && !_renderDirtyFrameRows[row])
                    continue;

                var sprite = _cachedFrameSprites[row];
                sprite.Data = _cachedVoxelFrameRows[row] ?? string.Empty;
                _cachedFrameSprites[row] = sprite;
            }

            int shipOffset = rows;
            for (int row = 0; row < rows && shipOffset + row < _cachedFrameSprites.Count; row++)
            {
                if (_renderDirtyFrameRows != null && row < _renderDirtyFrameRows.Length && !_renderDirtyFrameRows[row])
                    continue;

                var sprite = _cachedFrameSprites[shipOffset + row];
                sprite.Data = _cachedColorFrameRows[row] ?? string.Empty;
                _cachedFrameSprites[shipOffset + row] = sprite;
            }

            if (relationOverlay <= 0)
                return;

            int relationOffset = rows * 2;
            for (int row = 0; row < rows && relationOffset + row < _cachedFrameSprites.Count; row++)
            {
                if (!IsRenderRowDirty(row, rows, relationOverlay))
                    continue;

                var sprite = _cachedFrameSprites[relationOffset + row];
                sprite.Data = _cachedRelationFrameRows[row] ?? string.Empty;
                _cachedFrameSprites[relationOffset + row] = sprite;
            }
        }

        void RebuildDetectedGridHitboxes(RectangleF frameRect, int columns, int rows, int sampleLength)
        {
            for (int i = 0; i < _detectedGridHitboxes.Count; i++)
                _detectedGridHitboxes[i].BeginFrame(frameRect, 0, 0);

            if (frameRect.Width <= 0f || frameRect.Height <= 0f || columns <= 0 || rows <= 0)
            {
                RemoveInactiveDetectedGridHitboxes();
                return;
            }

            int downscale = Math.Max(1, INTERACTIVE_HITBOX_DOWNSCALE);
            int hitColumns = Math.Max(1, (int)Math.Ceiling(columns / (double)downscale));
            int hitRows = Math.Max(1, (int)Math.Ceiling(rows / (double)downscale));

            for (int i = 0; i < _detectedGridHitboxes.Count; i++)
                _detectedGridHitboxes[i].BeginFrame(frameRect, hitColumns, hitRows);

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    int sampleIndex = row * columns + col;
                    if (sampleIndex >= sampleLength)
                        continue;

                    RaySample sample = _renderSamples[sampleIndex];
                    if (!sample.IsShip || sample.EntityId == 0L)
                        continue;

                    int hitCol = Math.Min(hitColumns - 1, col * hitColumns / columns);
                    int hitRow = Math.Min(hitRows - 1, row * hitRows / rows);
                    var hitbox = GetOrCreateDetectedGridHitbox(sample.EntityId, frameRect, hitColumns, hitRows);
                    DetectedGridInfo info;
                    if (_detectedGridInfoByEntityId.TryGetValue(sample.EntityId, out info))
                        hitbox.SetInfo(info);
                    hitbox.MarkCell(hitRow, hitCol, sample.Distance);
                }
            }

            RemoveInactiveDetectedGridHitboxes();
            _detectedGridHitboxes.Sort(CompareDetectedGridHitboxesForRegistration);
        }

        DetectedGridHitbox GetOrCreateDetectedGridHitbox(long entityId, RectangleF frameRect, int hitColumns,
            int hitRows)
        {
            DetectedGridHitbox hitbox;
            if (_detectedGridHitboxByEntityId.TryGetValue(entityId, out hitbox))
                return hitbox;

            hitbox = new DetectedGridHitbox(entityId);
            hitbox.BeginFrame(frameRect, hitColumns, hitRows);
            hitbox.Entry = new InteractiveCustomEntry(
                () => hitbox.InteractiveBounds,
                hitbox.Hit,
                CursorType.Hand,
                hitbox /*,
                OnDetectedGridHitboxClick*/)
            {
                CustomRender = RenderDetectedGridHitbox
            };
            hitbox.Entry.SetTooltip(new InteractiveTooltip(
                () => hitbox.GetTooltipTitle(),
                () => BuildDetectedGridInfoLines(hitbox),
                () => hitbox.GetTooltipFooter(),
                null,
                TooltipActivationMode.Click,
                TooltipActivationMode.Click));

            _detectedGridHitboxByEntityId[entityId] = hitbox;
            _detectedGridHitboxes.Add(hitbox);
            return hitbox;
        }

        void RemoveInactiveDetectedGridHitboxes()
        {
            for (int i = _detectedGridHitboxes.Count - 1; i >= 0; i--)
            {
                var hitbox = _detectedGridHitboxes[i];
                if (hitbox.Active)
                    continue;

                _detectedGridHitboxByEntityId.Remove(hitbox.EntityId);
                _detectedGridHitboxes.RemoveAt(i);
            }
        }

        static int CompareDetectedGridHitboxesForRegistration(DetectedGridHitbox a, DetectedGridHitbox b)
        {
            return b.SortDistance.CompareTo(a.SortDistance);
        }

        void RegisterDetectedGridHitboxes()
        {
            for (int i = 0; i < _detectedGridHitboxes.Count; i++)
            {
                var hitbox = _detectedGridHitboxes[i];
                if (hitbox == null || !hitbox.Active || hitbox.Entry == null)
                    continue;

                hitbox.Entry.SetVisible(true);
                InteractiveList.Add(hitbox.Entry);
            }
        }

        void RenderDetectedGridHitbox(ControlBase entry, ControlRenderContext context, List<MySprite> sprites)
        {
            // Intentionally invisible. These entries only export per-grid hit tests to InteractiveEntries.
        }

        List<ITooltipLine> BuildDetectedGridInfoLines(DetectedGridHitbox hitbox)
        {
            var lines = new List<ITooltipLine>(9);
            if (hitbox == null)
                return lines;

            var info = hitbox.Info;
            lines.Add(new StaticTooltipLine("Entity: " + hitbox.EntityId));
            lines.Add(new StaticTooltipLine("Type: " + SafeTooltipText(info.EntityKind, "Unknown")));
            lines.Add(new StaticTooltipLine("Relationship: " + SafeTooltipText(info.Relationship, "Unknown")));
            lines.Add(new StaticTooltipLine("Grid: " + SafeTooltipText(info.GridSize, "Unknown")));
            if (info.GridSizeMeters > 0f)
                lines.Add(new StaticTooltipLine("Cell size: " + FormatingHelper.DistanceToString(info.GridSizeMeters)));
            lines.Add(new StaticTooltipLine("Closest: " +
                                            FormatingHelper.DistanceToString((float)hitbox.SortDistance)));
            if (info.CenterDistance > 0d)
                lines.Add(new StaticTooltipLine("Center: " +
                                                FormatingHelper.DistanceToString((float)info.CenterDistance)));
            lines.Add(new StaticTooltipLine("Bounds: " + FormatTooltipSize(info.Size)));
            lines.Add(new StaticTooltipLine("Position: " +
                                            FormatingHelper.FormatBearing(Matrix.Identity, info.Center)));
            lines.Add(new StaticTooltipLine("Samples: " + hitbox.SampleCount));

            return lines;
        }

        static string SafeTooltipText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        static string FormatTooltipSize(Vector3D size)
        {
            return string.Format(FormatingHelper.Culture, "{0:0.#} x {1:0.#} x {2:0.#} m", size.X, size.Y, size.Z);
        }

        static bool TryGetOutsideRelationshipOutlineGlyph(int row, int col, int columns, int rows,
            RaySample[] samples, RaySample sample, out string glyph)
        {
            glyph = MISS_GLYPH;

            // Draw relationship outlines only into cells that are outside the ship layer.
            // Voxel cells are allowed here because the outline is rendered as a separate layer.
            if (sample.IsShip)
                return false;

            return TryGetRelationshipGlyph(row - 1, col, columns, rows, samples, out glyph) ||
                   TryGetRelationshipGlyph(row + 1, col, columns, rows, samples, out glyph) ||
                   TryGetRelationshipGlyph(row, col - 1, columns, rows, samples, out glyph) ||
                   TryGetRelationshipGlyph(row, col + 1, columns, rows, samples, out glyph);
        }

        static bool TryGetRelationshipGlyph(int row, int col, int columns, int rows, RaySample[] samples,
            out string glyph)
        {
            glyph = MISS_GLYPH;

            if (row < 0 || col < 0 || row >= rows || col >= columns || samples == null)
                return false;

            int index = row * columns + col;
            RaySample neighbor = index < samples.Length ? samples[index] : new RaySample();

            if (!neighbor.IsShip)
                return false;

            glyph = neighbor.Glyph ?? MISS_GLYPH;
            return true;
        }

        bool ShouldDrawMagnificationHud()
        {
            long frame = GetCurrentGameFrame();
            return _lastFovChangedFrame != long.MinValue &&
                   frame >= _lastFovChangedFrame &&
                   frame - _lastFovChangedFrame <= MAGNIFICATION_HUD_VISIBLE_FRAMES;
        }

        static long GetCurrentGameFrame()
        {
            return MyAPIGateway.Session != null ? MyAPIGateway.Session.GameplayFrameCounter : 0L;
        }

        void DrawMagnificationHud(List<MySprite> sprites)
        {
            const float textScale = 0.55f;
            float magnification = SliderFov.FovToMagnification(_horizontalFovDegrees);
            string text = "MAG: " + magnification.ToString("0.##", FormatingHelper.Culture) + "x";
            var textSize = FormatingHelper.GetSizeInPixel(text, "White", textScale, Surface);
            const float margin = 8f;
            var pos = new Vector2(
                MathHelper.Clamp(ViewBox.Right - margin - textSize.X * 0.5f, ViewBox.X + textSize.X * 0.5f,
                    ViewBox.Right - textSize.X * 0.5f),
                MathHelper.Clamp(ViewBox.Bottom - margin - textSize.Y * 0.5f, ViewBox.Y + textSize.Y * 0.5f,
                    ViewBox.Bottom - textSize.Y * 0.5f));

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = text,
                Position = pos,
                Color = ForegroundColor,
                FontId = "White",
                Alignment = TextAlignment.CENTER,
                RotationOrScale = textScale
            });
        }

        void GetRayCanvasSize(int displayColumns, int displayRows, out int columns, out int rows)
        {
            double axisDensity = Math.Sqrt(Math.Max(0.001d, RayDensityMultiplier));
            columns = Math.Max(1, (int)Math.Round(displayColumns * axisDensity));
            rows = Math.Max(1, (int)Math.Round(displayRows * axisDensity));
        }

        void GetFixedCanvasSize(out int columns, out int rows)
        {
            if (ViewBox.Width <= 0f || ViewBox.Height <= 0f)
            {
                columns = 1;
                rows = 1;
                return;
            }

            float statusScale = 0.55f * FontScale;
            float statusHeight = Surface.MeasureStringInPixels(new StringBuilder("A"), FONT, statusScale).Y + 4f;
            float tableHeight = Math.Max(0f, ViewBox.Height - statusHeight);

            columns = Math.Max(1, (int)Math.Floor(ViewBox.Width * CHARACTERS_PER_PIXEL));
            rows = Math.Max(1, (int)Math.Floor(tableHeight * CHARACTERS_PER_PIXEL));
        }

        double GetVerticalFovRadians()
        {
            if (ViewBox.Width <= 0f || ViewBox.Height <= 0f)
                return GetHorizontalFovRadians();

            float statusScale = 0.55f * FontScale;
            float statusHeight = Surface.MeasureStringInPixels(new StringBuilder("A"), FONT, statusScale).Y + 4f;
            double tableHeight = Math.Max(1f, ViewBox.Height - statusHeight);
            double aspect = tableHeight / Math.Max(1f, ViewBox.Width);
            return 2d * Math.Atan(Math.Tan(GetHorizontalFovRadians() * 0.5d) * aspect);
        }

        double GetHorizontalFovRadians()
        {
            return MathHelper.ToRadians(MathHelper.Clamp(_horizontalFovDegrees, MIN_HORIZONTAL_FOV_DEGREES,
                MAX_HORIZONTAL_FOV_DEGREES));
        }

        void EnsureSampleBufferSize(int rayCount)
        {
            if (_samples.Length == rayCount)
                return;

            var oldSamples = _samples;
            _samples = new RaySample[rayCount];
            int copyCount = Math.Min(oldSamples.Length, _samples.Length);
            for (int i = 0; i < copyCount; i++)
                _samples[i] = oldSamples[i];
        }

        void EnsureDirtyFrameRowsSize(int rows)
        {
            if (_dirtyFrameRows.Length != rows)
                _dirtyFrameRows = new bool[Math.Max(0, rows)];
        }

        void MarkAllFrameRowsDirty()
        {
            for (int i = 0; i < _dirtyFrameRows.Length; i++)
                _dirtyFrameRows[i] = true;
        }

        void MarkSampleRowDirty(ScanJob job, int index)
        {
            if (job.Columns <= 0)
                return;

            int row = index / job.Columns;
            if (row < 0 || row >= _dirtyFrameRows.Length)
                return;

            _dirtyFrameRows[row] = true;
        }

        static void GetViewBasis(Vector3D forward, out Vector3D right, out Vector3D up)
        {
            right = Vector3D.Cross(forward, Vector3D.Up);
            if (right.LengthSquared() < 1e-6)
                right = Vector3D.Cross(forward, Vector3D.Right);
            right.Normalize();

            up = Vector3D.Cross(right, forward);
            up.Normalize();
        }


        static bool TryRayBoxSegment(Vector3D origin, Vector3D direction, BoundingBoxD bounds, out double enter,
            out double exit)
        {
            enter = 0d;
            exit = double.MaxValue;

            if (!ClipRayAxis(origin.X, direction.X, bounds.Min.X, bounds.Max.X, ref enter, ref exit))
                return false;
            if (!ClipRayAxis(origin.Y, direction.Y, bounds.Min.Y, bounds.Max.Y, ref enter, ref exit))
                return false;
            if (!ClipRayAxis(origin.Z, direction.Z, bounds.Min.Z, bounds.Max.Z, ref enter, ref exit))
                return false;

            if (exit < 0d || enter > TARGET_SEARCH_DISTANCE)
                return false;

            enter = Math.Max(0d, enter);
            exit = Math.Min(TARGET_SEARCH_DISTANCE, exit);

            return true;
        }

        static bool ClipRayAxis(double origin, double direction, double min, double max, ref double enter,
            ref double exit)
        {
            const double epsilon = 1e-9;
            if (Math.Abs(direction) < epsilon)
                return origin >= min && origin <= max;

            double t1 = (min - origin) / direction;
            double t2 = (max - origin) / direction;
            if (t1 > t2)
            {
                double temp = t1;
                t1 = t2;
                t2 = temp;
            }

            if (t1 > enter)
                enter = t1;
            if (t2 < exit)
                exit = t2;

            return enter <= exit;
        }

        static bool Normalize(ref Vector3D value)
        {
            if (value.LengthSquared() <= 1e-8)
                return false;

            value.Normalize();
            return true;
        }

        static int QuantizeColorChannel(byte value, float shade)
        {
            return MathHelper.Clamp((int)Math.Round(value / 255d * 7d * shade), 0, 7);
        }

        sealed class FrameBuildData
        {
            public int Columns;
            public int Rows;
            public int Generation;
            public int WriteVersion;
            public int SampleLength;
            public int RelationOverlay;
            public RectangleF TableRect;
            public float Scale;
            public bool RebuildAllRows;
            public RaySample[] Samples;
            public bool[] DirtyRows;
            public string[] VoxelRows;
            public string[] ColorRows;
            public string[] RelationRows;
            public Dictionary<BlockColorCacheKey, Color> BlockColorCache;
            public Exception Exception;
        }

        public struct DetectedGridInfo
        {
            public long EntityId;
            public string Name;
            public string EntityKind;
            public string GridSize;
            public float GridSizeMeters;
            public long OwnerId;
            public string Relationship;
            public Vector3D Center;
            public Vector3D Size;
            public double CenterDistance;
            public double SortDistance;
        }

        sealed class DetectedGridHitbox
        {
            bool[] _cells = Array.Empty<bool>();

            public DetectedGridHitbox(long entityId)
            {
                EntityId = entityId;
            }

            public long EntityId { get; private set; }
            public InteractiveCustomEntry Entry { get; set; }
            public RectangleF FrameRect { get; private set; }
            public RectangleF InteractiveBounds { get; private set; }
            public int Columns { get; private set; }
            public int Rows { get; private set; }
            int _minHitCol;
            int _minHitRow;
            int _maxHitCol;
            int _maxHitRow;
            public bool Active { get; private set; }
            public double SortDistance { get; private set; }
            public int SampleCount { get; private set; }
            public DetectedGridInfo Info { get; private set; }

            public void BeginFrame(RectangleF frameRect, int columns, int rows)
            {
                FrameRect = frameRect;
                InteractiveBounds = frameRect;
                Columns = Math.Max(0, columns);
                Rows = Math.Max(0, rows);
                Active = false;
                SortDistance = double.PositiveInfinity;
                SampleCount = 0;
                _minHitCol = Columns;
                _minHitRow = Rows;
                _maxHitCol = -1;
                _maxHitRow = -1;

                int cellCount = Columns * Rows;
                if (_cells.Length != cellCount)
                    _cells = new bool[cellCount];
                else if (cellCount > 0)
                    Array.Clear(_cells, 0, cellCount);
            }

            public void SetInfo(DetectedGridInfo info)
            {
                Info = info;
            }

            public void MarkCell(int row, int col, double distance)
            {
                if (row < 0 || col < 0 || row >= Rows || col >= Columns || _cells == null)
                    return;

                int index = row * Columns + col;
                if (index < 0 || index >= _cells.Length)
                    return;

                if (!_cells[index])
                    SampleCount++;

                _cells[index] = true;
                Active = true;
                if (col < _minHitCol)
                    _minHitCol = col;
                if (row < _minHitRow)
                    _minHitRow = row;
                if (col > _maxHitCol)
                    _maxHitCol = col;
                if (row > _maxHitRow)
                    _maxHitRow = row;
                UpdateInteractiveBounds();
                if (distance < SortDistance)
                    SortDistance = distance;
            }

            void UpdateInteractiveBounds()
            {
                if (!Active || Columns <= 0 || Rows <= 0 || _maxHitCol < _minHitCol || _maxHitRow < _minHitRow)
                {
                    InteractiveBounds = FrameRect;
                    return;
                }

                float cellWidth = FrameRect.Width / Math.Max(1, Columns);
                float cellHeight = FrameRect.Height / Math.Max(1, Rows);
                InteractiveBounds = new RectangleF(
                    FrameRect.X + _minHitCol * cellWidth,
                    FrameRect.Y + _minHitRow * cellHeight,
                    Math.Max(cellWidth, (_maxHitCol - _minHitCol + 1) * cellWidth),
                    Math.Max(cellHeight, (_maxHitRow - _minHitRow + 1) * cellHeight));
            }

            public bool Hit(Vector2 point)
            {
                if (!Active || Columns <= 0 || Rows <= 0 || _cells == null || !FrameRect.Contains(point))
                    return false;

                float normalizedX = (point.X - FrameRect.X) / Math.Max(1f, FrameRect.Width);
                float normalizedY = (point.Y - FrameRect.Y) / Math.Max(1f, FrameRect.Height);
                int col = MathHelper.Clamp((int)Math.Floor(normalizedX * Columns), 0, Columns - 1);
                int row = MathHelper.Clamp((int)Math.Floor(normalizedY * Rows), 0, Rows - 1);
                int index = row * Columns + col;

                return index >= 0 && index < _cells.Length && _cells[index];
            }

            public string GetTooltipTitle()
            {
                return string.IsNullOrWhiteSpace(Info.Name) ? "Grid " + EntityId : Info.Name;
            }

            public string GetTooltipFooter()
            {
                if (!double.IsInfinity(SortDistance) && SortDistance >= 0d)
                    return FormatingHelper.DistanceToString((float)SortDistance);

                if (Info.SortDistance >= 0d)
                    return FormatingHelper.DistanceToString((float)Info.SortDistance);

                return string.Empty;
            }

            public override string ToString()
            {
                return GetTooltipTitle();
            }
        }

        struct BlockColorCacheKey : IEquatable<BlockColorCacheKey>
        {
            readonly long _entityId;
            readonly Vector3I _position;

            public BlockColorCacheKey(long entityId, Vector3I position)
            {
                _entityId = entityId;
                _position = position;
            }

            public bool Equals(BlockColorCacheKey other)
            {
                return _entityId == other._entityId &&
                       _position == other._position;
            }

            public override bool Equals(object obj)
            {
                return obj is BlockColorCacheKey && Equals((BlockColorCacheKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _entityId.GetHashCode();
                    hash = (hash * 397) ^ _position.GetHashCode();
                    return hash;
                }
            }
        }

        enum RayHitKind
        {
            None = 0,
            Voxel = 1,
            Ship = 2
        }

        struct RaySample
        {
            public RayHitKind HitKind;
            public long EntityId;
            public IMyCubeGrid Grid;
            public bool HasGridPosition;
            public Vector3I GridPosition;
            public int NormalLuminance;
            public double Distance;
            public string Glyph;
            public string ColorGlyph;

            public bool Hit => HitKind != RayHitKind.None;

            public bool IsVoxel => HitKind == RayHitKind.Voxel;

            public bool IsShip => HitKind == RayHitKind.Ship;
        }

        struct ScanJob
        {
            public int Generation;
            public Vector3D Origin;
            public Vector3D Forward;
            public Vector3D Right;
            public Vector3D Up;
            public int Columns;
            public int Rows;
            public int RayCount;
            public double HorizontalTan;
            public double VerticalTan;
            public long OwnGridOwnerId;
            public long[] ExcludedGridIds;
            public VisibleTarget[] VisibleTargets;
        }


        public struct VisibleTarget
        {
            public long EntityId;
            public IMyCubeGrid Grid;
            public MyVoxelMap Voxel;
            public BoundingBoxD Bounds;
            public double CenterDistance;
            public double SortDistance;
            public DetectedGridInfo Info;
        }
    }
}
