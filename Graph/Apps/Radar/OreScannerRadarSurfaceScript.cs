using System;
using System.Collections.Generic;
using System.Text;
using Generated;
using Graph.Apps.Abstract;
using Graph.Helpers;
using Graph.System.Config.Models.Apps;
using Graph.System.TerminalControls.Generic;
using Sandbox.Definitions;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Voxels;
using VRageMath;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid = VRage.Game.ModAPI.IMyCubeGrid;

namespace Graph.Apps.Radar
{
    /// <summary>
    ///     Flat (top-down) 2D radar that adds a directional ore scanner.
    ///     Independent from <see cref="RadarSurfaceScript" />; both can be selected per surface.
    /// </summary>
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class OreScannerRadarSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControl<SliderOreScannerConeAngle>,
        IUsesTerminalControl<ListboxOreScannerReference>
    {
        protected override ConfigKind ConfigKind => ConfigKind.OreScanner;
        public const string ID = "LCDMod_OreScannerRadar";
        public const string TITLE = "LCDMod_OreScannerRadar";

        // ------------------------------------------------------------------
        // CONFIG (single source of truth for tuning)
        // ------------------------------------------------------------------
        // Box-shaped scan volume: a constant-cross-section beam projected from the
        // detector's emitter face. Replaces the earlier cone, which spread too wide
        // at long range and missed on-axis veins. Total cross-section is
        // 2*BOX_HALF_EXTENT_M on each side (so 70m × 70m by default), depth is BOX_LENGTH_M.
        private const float BOX_HALF_EXTENT_M = 55f;
        private const float BOX_LENGTH_M = 2000f;
        private const float BOX_AXIS_STEP_M = 30f; // along-axis spacing between sample slices
        private const float BOX_INNER_RING_M = 15f; // lateral radius of the inner ring of cross-section samples

        private const float
            BOX_OUTER_RING_M = 32f; // lateral radius of the outer ring (kept slightly inside the half-extent)

        // Each SamplePoint reads a (2*NEIGHBOURHOOD_RADIUS+1)³ voxel neighborhood in
        // one ReadRange call, so a single API read covers a (2*R+1)m cube instead of 1 m.
        private const int NEIGHBOURHOOD_RADIUS = 4; // 9×9×9 = 729 voxels per read (≈9 m sample window)
        private const float STATIONARY_HOLD_SEC = 10f;
        private const double VEL_LIN_THRESH_SQ = 0.04; // (~0.2 m/s)^2
        private const double VEL_ANG_THRESH_SQ = 0.0004; // (~0.02 rad/s)^2
        private const int MAX_VOXEL_READS_PER_TICK = 32; // throttle voxel reads inside Run()
        private const float SIGNAL_FULL_AT_HITS = 6f; // weighted hit count that maps to signal=1.0

        private const int MAX_HITS_EARLY_EXIT = 12; // stop scanning once we are clearly above the cap

        // Per-hit weighting: ore at the box origin weighs PROXIMITY_BONUS, ore at the
        // far end weighs PROXIMITY_FLOOR. With BONUS=3 / FULL=6 a single point-blank hit
        // already gives ~50 % signal, matching a "hot/cold" detector feel.
        private const float PROXIMITY_BONUS = 3f;
        private const float PROXIMITY_FLOOR = 0.4f;

        // Cross-section sample count at every axis slice: 1 axis + 4 inner ring + 4 outer ring = 9.
        private const int RING_SAMPLES = 4;

        // Layout
        private const float MARGIN_PX = 14f;

        // Look up the ore detector again every N gameplay frames (~2 s at 60 Hz).
        private const int DETECTOR_RESCAN_FRAMES = 120;
        private readonly List<MySprite> _backgroundSprites = new List<MySprite>();
        private readonly List<IMySlimBlock> _detectorScratch = new List<IMySlimBlock>();
        private readonly List<MySprite> _foregroundSprites = new List<MySprite>();
        private readonly StringBuilder _statusBuffer = new StringBuilder(48);

        // Reused buffers — never re-allocated per tick
        private readonly MyStorageData _voxelBuffer = new MyStorageData();
        private readonly Func<IMyVoxelBase, bool> _voxelFilter;
        private readonly List<IMyVoxelBase> _voxelMaps = new List<IMyVoxelBase>();

        // Cached for one Run() call (status bar reuses scan range without re-resolving planet)
        private float _cachedScanRange;

        // Detector (the grid block whose orientation drives the cone)
        private IMyOreDetector _detector;
        private bool _detectorAmbiguous; // true when the grid has more than one ore detector

        // Tracks the slider's last observed value so we can restart the scan
        // whenever the operator drags the slider in the terminal.
        private float _lastBiasSeen = float.NaN;
        private long _lastDetectorScanFrame = -DETECTOR_RESCAN_FRAMES;
        private long _lastReferenceSeen = -1L;
        private int _scanAxisIndex;
        private int _scanAxisSteps;
        private Vector3D _scanAxisWorld;
        private float _scanCoveredDistance; // running sum of adaptive steps; drives termination

        // Hoisted scan filter — avoids per-scan closure allocation. Reads _scanFilterSphere field.
        private BoundingSphereD _scanFilterSphere;

        // Scan job (incremental; preserved across Run() calls until done)
        private bool _scanJobActive;
        private Vector3D _scanOriginWorld;
        private float _scanRangeM;
        private Vector3D _scanRightWorld;
        private Vector3D _scanUpWorld;
        private float _scanWeightedHits;
        private float _signalStrength;

        // ------------------------------------------------------------------
        // STATE
        // ------------------------------------------------------------------
        // Scanner state
        private ScannerState _state = ScannerState.Idle;
        private long _stationaryStartFrame = -1;

        public OreScannerRadarSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
            _voxelFilter = VoxelFilterImpl;
        }

        protected override string DefaultTitle => TITLE;

        private bool VoxelFilterImpl(IMyVoxelBase v)
        {
            return v != null && v.Storage != null && v.PositionComp.WorldAABB.Intersects(_scanFilterSphere);
        }

        // ==================================================================
        // MAIN UPDATE LOOP
        // ==================================================================
        public override void Run()
        {
            base.Run();
            if (AppConfig == null)
                return;

            try
            {
                UpdateScannerState();
                AdvanceScanJob();
                if (_cachedScanRange <= 0f)
                    _cachedScanRange = BOX_LENGTH_M;
            }
            catch (Exception ex)
            {
                ErrorHandlerHelper.LogError(ex, this);
            }

            using (var frame = Surface.DrawFrame())
            {
                try
                {
                    AddBackground(_backgroundSprites);
                    DrawTitle(_foregroundSprites);
                    RenderCentered(_foregroundSprites);

                    frame.AddRange(_backgroundSprites);
                    frame.AddRange(_foregroundSprites);
                }
                finally
                {
                    _backgroundSprites.Clear();
                    _foregroundSprites.Clear();
                }
            }
        }

        // ==================================================================
        // SCANNER STATE MACHINE
        // ==================================================================
        private void UpdateScannerState()
        {
            var grid = Block?.CubeGrid as IMyCubeGrid;
            if (grid == null)
            {
                _state = ScannerState.NoDetector;
                _signalStrength = 0f;
                AbortScanJob();
                return;
            }

            // Slider OR reference-cockpit change → invalidate the cached signal
            // and restart the 5-second charging window with the new direction.
            var currentBias = AppConfig?.OreScannerConeBias ?? 0f;
            var currentRef = AppConfig?.OreScannerReferenceId ?? 0L;
            var sliderChanged = !float.IsNaN(_lastBiasSeen) && Math.Abs(currentBias - _lastBiasSeen) > 0.5f;
            var refChanged = _lastReferenceSeen != -1L && currentRef != _lastReferenceSeen;
            if (sliderChanged || refChanged)
            {
                AbortScanJob();
                _signalStrength = 0f;
                _stationaryStartFrame = -1;
                _cachedScanRange = 0f;
                _state = ScannerState.Charging;
            }

            _lastBiasSeen = currentBias;
            _lastReferenceSeen = currentRef;

            RefreshDetectorIfStale(grid);
            if (_detector == null)
            {
                if (_state != ScannerState.NoDetector)
                {
                    AbortScanJob();
                    _signalStrength = 0f;
                    _cachedScanRange = 0f;
                }

                _state = ScannerState.NoDetector;
                _stationaryStartFrame = -1;
                return;
            }

            if (_detectorAmbiguous)
            {
                if (_state != ScannerState.MultipleDetectors)
                {
                    AbortScanJob();
                    _signalStrength = 0f;
                    _cachedScanRange = 0f;
                }

                _state = ScannerState.MultipleDetectors;
                _stationaryStartFrame = -1;
                return;
            }

            var moving = IsGridMoving(grid);
            var currentFrame = GameplayFrame();

            if (moving)
            {
                if (_state != ScannerState.Idle)
                {
                    AbortScanJob();
                    _signalStrength = 0f;
                    _cachedScanRange = 0f; // re-resolve range after we settle again
                }

                _state = ScannerState.Idle;
                _stationaryStartFrame = -1;
                return;
            }

            // Grid is currently stationary
            if (_stationaryStartFrame < 0)
                _stationaryStartFrame = currentFrame;

            var secondsStill = (currentFrame - _stationaryStartFrame) / 60f;

            switch (_state)
            {
                case ScannerState.Idle:
                    _state = ScannerState.Charging;
                    break;
                case ScannerState.Charging:
                    if (secondsStill >= STATIONARY_HOLD_SEC)
                        StartScanJob();
                    break;
                case ScannerState.Scanning:
                    // AdvanceScanJob() drives the transition to Completed*
                    break;
                case ScannerState.CompletedNoSignal:
                case ScannerState.CompletedWithSignal:
                    // Persist cached state until movement resumes.
                    break;
            }
        }

        // Refresh the cached ore-detector reference periodically (or on first access,
        // or when the previous one was destroyed/removed). Searches only the LCD's own
        // grid — connected sub-grids are intentionally excluded for now.
        private void RefreshDetectorIfStale(IMyCubeGrid grid)
        {
            var now = GameplayFrame();

            // If the cached detector is still alive and physical, reuse it.
            if (_detector != null)
            {
                var ent = _detector as IMyEntity;
                if (ent != null && !ent.MarkedForClose && _detector.IsFunctional && _detector.CubeGrid == grid)
                {
                    if (now - _lastDetectorScanFrame < DETECTOR_RESCAN_FRAMES)
                        return;
                }
                else
                {
                    _detector = null;
                }
            }

            if (now - _lastDetectorScanFrame < DETECTOR_RESCAN_FRAMES && _detector != null)
                return;

            _lastDetectorScanFrame = now;

            try
            {
                _detectorScratch.Clear();
                grid.GetBlocks(_detectorScratch, b => b != null && b.FatBlock is IMyOreDetector);

                var functionalCount = 0;
                IMyOreDetector found = null;
                for (var i = 0; i < _detectorScratch.Count; i++)
                {
                    var det = _detectorScratch[i].FatBlock as IMyOreDetector;
                    if (det == null) continue;
                    if (!det.IsFunctional) continue;
                    functionalCount++;
                    found = det;
                }

                _detectorAmbiguous = functionalCount > 1;
                // When ambiguous we still cache the reference so re-checks are cheap, but
                // UpdateScannerState() refuses to scan and shows the multi-detector warning.
                _detector = functionalCount > 0 ? found : null;
            }
            catch (Exception ex)
            {
                ErrorHandlerHelper.LogError(ex, this);
                _detector = null;
                _detectorAmbiguous = false;
            }
            finally
            {
                _detectorScratch.Clear();
            }
        }

        private bool IsGridMoving(IMyCubeGrid grid)
        {
            var phys = grid.Physics;
            if (phys == null || !phys.Enabled || phys.IsStatic)
                return false; // stations / unphysical grids: treat as still

            double linSq = phys.LinearVelocity.LengthSquared();
            if (linSq > VEL_LIN_THRESH_SQ)
                return true;

            double angSq = phys.AngularVelocity.LengthSquared();
            return angSq > VEL_ANG_THRESH_SQ;
        }

        // ==================================================================
        // SCAN JOB (incremental cone walk)
        // ==================================================================
        private void StartScanJob()
        {
            var detectorEntity = _detector as IMyEntity;
            if (detectorEntity == null) return;

            var matrix = detectorEntity.WorldMatrix;
            _scanOriginWorld = matrix.Translation;

            // Build the box axis from the slider value [-100, +100].
            //   slider 0   → axis aims at the detector's local Up axis (the dish/emitter
            //                face on a vanilla SE ore detector points along its Up).
            //   slider >0  → axis tilts toward an absolute SKY direction
            //   slider <0  → axis tilts toward an absolute GROUND direction
            // Where SKY/GROUND come from (in priority order):
            //   1. The selected cockpit / control seat (its Up = sky, -Up = ground)
            //   2. Planet gravity at the detector position (anti-gravity = sky)
            //   3. None (positive/negative still tilts toward detector.Forward
            //      so the slider remains useful in deep space without a cockpit).
            var defaultAxis = matrix.Up;
            var bias = MathHelper.Clamp(AppConfig?.OreScannerConeBias ?? 0f, -100f, 100f);

            if (Math.Abs(bias) < 0.5f)
            {
                _scanAxisWorld = defaultAxis;
            }
            else
            {
                Vector3D sky;
                var haveAbsoluteSky = TryResolveSkyDirection(_scanOriginWorld, out sky);
                Vector3D anchor;
                if (haveAbsoluteSky)
                    // Absolute reference: positive bias points to sky, negative to ground.
                    anchor = bias > 0f ? sky : -sky;
                else
                    // No reference frame: tilt around the detector's local Forward axis.
                    anchor = bias > 0f ? matrix.Forward : -matrix.Forward;
                var t = Math.Abs(bias) / 100f;
                _scanAxisWorld = Vector3D.Normalize(Vector3D.Lerp(defaultAxis, anchor, t));
            }

            // Build orthonormal lateral basis for the box cross-section
            var right = matrix.Right;
            if (Math.Abs(Vector3D.Dot(right, _scanAxisWorld)) > 0.95)
                right = matrix.Forward;
            _scanRightWorld = Vector3D.Normalize(Vector3D.Cross(_scanAxisWorld, right));
            _scanUpWorld = Vector3D.Normalize(Vector3D.Cross(_scanRightWorld, _scanAxisWorld));

            _scanRangeM = BOX_LENGTH_M;
            _cachedScanRange = _scanRangeM;
            _scanAxisIndex = 0;
            _scanCoveredDistance = 0f;
            _scanAxisSteps = Math.Max(1, (int)Math.Ceiling(_scanRangeM / BOX_AXIS_STEP_M));
            _scanWeightedHits = 0f;

            // Pre-fetch nearby voxel maps once for the whole scan (intersect with box bounding sphere)
            _voxelMaps.Clear();
            try
            {
                var centre = _scanOriginWorld + _scanAxisWorld * (_scanRangeM * 0.5);
                var halfDiag = Math.Sqrt(BOX_HALF_EXTENT_M * BOX_HALF_EXTENT_M * 2.0
                                         + _scanRangeM * 0.5 * (_scanRangeM * 0.5));
                _scanFilterSphere = new BoundingSphereD(centre, halfDiag + 64.0);
                if (MyAPIGateway.Session != null && MyAPIGateway.Session.VoxelMaps != null)
                    MyAPIGateway.Session.VoxelMaps.GetInstances(_voxelMaps, _voxelFilter);
                _scanJobActive = true;
                _state = ScannerState.Scanning;
            }
            catch (Exception ex)
            {
                ErrorHandlerHelper.LogError(ex, this);
                AbortScanJob();
            }
        }

        private void AbortScanJob()
        {
            _scanJobActive = false;
            _scanAxisIndex = 0;
            _scanCoveredDistance = 0f;
            _scanWeightedHits = 0f;
            _voxelMaps.Clear();
        }

        private void AdvanceScanJob()
        {
            if (!_scanJobActive)
                return;

            var reads = 0;

            while (_scanAxisIndex < _scanAxisSteps && reads < MAX_VOXEL_READS_PER_TICK)
            {
                _scanCoveredDistance += BOX_AXIS_STEP_M;
                var distance = Math.Min(_scanCoveredDistance, _scanRangeM);
                var axisPoint = _scanOriginWorld + _scanAxisWorld * distance;

                // Axis sample
                if (SamplePoint(axisPoint, distance))
                    reads++;

                // Inner ring (closer to axis — catches near-axis veins)
                SampleRing(axisPoint, distance, BOX_INNER_RING_M, ref reads);
                // Outer ring (just inside the box wall — catches off-axis veins out to the edge)
                SampleRing(axisPoint, distance, BOX_OUTER_RING_M, ref reads);

                _scanAxisIndex++;

                if (_scanWeightedHits >= MAX_HITS_EARLY_EXIT)
                    break; // strong signal already; no need to keep scanning
            }

            if (_scanAxisIndex >= _scanAxisSteps || _scanWeightedHits >= MAX_HITS_EARLY_EXIT)
                FinaliseScanJob();
        }

        private void SampleRing(Vector3D axisPoint, float distance, float lateralRadius, ref int reads)
        {
            for (var i = 0; i < RING_SAMPLES && reads < MAX_VOXEL_READS_PER_TICK; i++)
            {
                var angle = i / (double)RING_SAMPLES * MathHelper.TwoPi;
                var offset = (_scanRightWorld * Math.Cos(angle) + _scanUpWorld * Math.Sin(angle)) * lateralRadius;
                if (SamplePoint(axisPoint + offset, distance))
                    reads++;
            }
        }

        /// <summary>
        ///     Reads a (2*NEIGHBOURHOOD_RADIUS+1)³ voxel cube around <paramref name="worldPos" /> in a single
        ///     ReadRange call. With 1m voxels this is a (2R+1) m sample window per step, large enough that an
        ///     ore vein crossing the box is reliably caught even when BOX_AXIS_STEP_M leaves a gap larger
        ///     than the vein's diameter.
        /// </summary>
        /// <returns>true when a voxel read happened (whether or not any ore was found)</returns>
        private bool SamplePoint(Vector3D worldPos, float distance)
        {
            for (var v = 0; v < _voxelMaps.Count; v++)
            {
                var vb = _voxelMaps[v];
                if (vb == null || vb.Storage == null) continue;

                var aabb = vb.PositionComp.WorldAABB;
                if (aabb.Contains(worldPos) == ContainmentType.Disjoint) continue;

                var local = worldPos - vb.PositionLeftBottomCorner;
                var center = new Vector3I(
                    (int)Math.Floor(local.X),
                    (int)Math.Floor(local.Y),
                    (int)Math.Floor(local.Z));

                var size = vb.Storage.Size;
                var min = Vector3I.Max(center - NEIGHBOURHOOD_RADIUS, Vector3I.Zero);
                var max = Vector3I.Min(center + NEIGHBOURHOOD_RADIUS, size - Vector3I.One);
                if (min.X > max.X || min.Y > max.Y || min.Z > max.Z) continue;

                var bufferSize = max - min + Vector3I.One;
                var totalVoxels = bufferSize.X * bufferSize.Y * bufferSize.Z;

                try
                {
                    _voxelBuffer.Resize(bufferSize);
                    vb.Storage.ReadRange(_voxelBuffer, MyStorageDataTypeFlags.Material, 0, min, max);
                }
                catch (Exception ex)
                {
                    ErrorHandlerHelper.LogError(ex, this);
                    return true;
                }

                var hitsInBlock = 0;
                for (var i = 0; i < totalVoxels; i++)
                    if (IsValidOre(_voxelBuffer.Material(i)))
                        hitsInBlock++;

                if (hitsInBlock > 0)
                {
                    // Closer ore weighs more (linear falloff PROXIMITY_BONUS → PROXIMITY_FLOOR).
                    // The fraction of ore voxels in the read block scales the weight so a bigger
                    // vein produces a stronger signal than a single voxel grazed by the sample.
                    var fillFraction = hitsInBlock / (float)totalVoxels;
                    var t = MathHelper.Clamp(distance / Math.Max(1f, _scanRangeM), 0f, 1f);
                    var distanceWeight = PROXIMITY_BONUS - (PROXIMITY_BONUS - PROXIMITY_FLOOR) * t;
                    _scanWeightedHits += distanceWeight * Math.Min(1f, fillFraction * 4f);
                }

                return true; // counted one voxel read
            }

            return false; // no voxel base contained this point — no read happened
        }

        private bool IsValidOre(byte materialIdx)
        {
            MyVoxelMaterialDefinition def;
            try
            {
                def = MyDefinitionManager.Static.GetVoxelMaterialDefinition(materialIdx);
            }
            catch (Exception ex)
            {
                ErrorHandlerHelper.LogError(ex, this);
                return false;
            }

            if (def == null) return false;
            var ore = def.MinedOre;
            if (string.IsNullOrEmpty(ore)) return false;
            if (string.Equals(ore, "Stone", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(ore, "Ice", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private void FinaliseScanJob()
        {
            _scanJobActive = false;
            _voxelMaps.Clear();

            if (_scanWeightedHits <= 0f)
            {
                _signalStrength = 0f;
                _state = ScannerState.CompletedNoSignal;
            }
            else
            {
                _signalStrength = MathHelper.Clamp(_scanWeightedHits / SIGNAL_FULL_AT_HITS, 0f, 1f);
                _state = ScannerState.CompletedWithSignal;
            }
        }

        // ==================================================================
        // ORIENTATION REFERENCE
        // ==================================================================
        // Resolves what "up" means in the world for the slider's tilt axis.
        // Priority: selected cockpit → planet gravity → none.
        private bool TryResolveSkyDirection(Vector3D worldPos, out Vector3D sky)
        {
            sky = Vector3D.Up;

            // 1. Cockpit / control seat reference, if selected and on the same grid.
            try
            {
                var id = AppConfig?.OreScannerReferenceId ?? 0L;
                if (id != 0L)
                {
                    IMyEntity ent;
                    if (MyAPIGateway.Entities.TryGetEntityById(id, out ent))
                    {
                        var ctl = ent as IMyShipController;
                        if (ctl != null && ctl.IsFunctional && ctl.CubeGrid == Block?.CubeGrid)
                        {
                            sky = Vector3D.Normalize(ctl.WorldMatrix.Up);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandlerHelper.LogError(ex, this);
            }

            // 2. Planet gravity at the detector position (covers cockpit-less ships
            //    sitting in a gravity well — most common in-game situation).
            try
            {
                var grid = Block?.CubeGrid as IMyCubeGrid;
                var phys = grid?.Physics;
                if (phys != null)
                {
                    var g = phys.Gravity;
                    if (g.LengthSquared() > 0.001f)
                    {
                        sky = -Vector3D.Normalize(g);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorHandlerHelper.LogError(ex, this);
            }

            return false;
        }

        // ==================================================================
        // RENDER — centered scanner panel
        //   Centre of screen → one of three visuals depending on state:
        //     A) WifiOff sprite       → no signal / moving / scan finished empty
        //     B) WifiSearching sprite → charging or scanning (alpha pulses)
        //     C) 4 stacked rings      → CompletedWithSignal: ring count and
        //        blink rate scale with _signalStrength, painted in title color
        //   Bottom of screen → status text + RNG line (footer)
        //   Right edge → tilt indicator (track + triangle)
        // ==================================================================
        private void RenderCentered(List<MySprite> sprites)
        {
            var titleClamp = TitleVisible ? TITLE_BAR_HEIGHT_BASE * LayoutScale : 0f;
            var margin = MARGIN_PX * Scale;
            var areaTop = ViewBox.Y + titleClamp + margin;
            var areaBottom = ViewBox.Bottom - margin;
            var areaWidth = ViewBox.Width - margin * 2f;
            var areaHeight = areaBottom - areaTop;
            if (areaWidth <= 0f || areaHeight <= 0f) return;

            // Reserve a footer band for the two text rows; the icon centres
            // inside the remaining area above it.
            var footerHeight = (0.6f + 0.85f) * Scale * FontScale * 32f + 12f * LayoutScale;
            var iconAreaBottom = areaBottom - footerHeight;
            var iconAreaHeight = Math.Max(1f, iconAreaBottom - areaTop);

            var iconCenter = new Vector2(ViewBox.Center.X, areaTop + iconAreaHeight * 0.5f);
            var baseUnit = Math.Min(areaWidth, iconAreaHeight);
            var iconSize = baseUnit * 0.55f;

            DrawStateVisual(sprites, iconCenter, iconSize);
            DrawFooter(sprites, areaBottom, areaWidth);
            DrawTiltIndicator(sprites, areaTop, areaBottom);
        }

        // Routes to the right visual based on state.
        private void DrawStateVisual(List<MySprite> sprites, Vector2 center, float size)
        {
            switch (_state)
            {
                case ScannerState.NoDetector:
                    DrawAnimatedSprite(sprites, "NoDetector_0", 4, 18, center, size, StatusTextColor());
                    return;

                case ScannerState.MultipleDetectors:
                    DrawAnimatedSprite(sprites, "MultipleDetectors_0", 4, 18, center, size, StatusTextColor());
                    return;

                case ScannerState.Idle:
                case ScannerState.CompletedNoSignal:
                    DrawSpriteCentered(sprites, "WifiOff", center, size,
                        StatusTextColor());
                    return;

                case ScannerState.Charging:
                case ScannerState.Scanning:
                    // 4-frame loop at ~300 ms/frame (18 game ticks @ 60 Hz).
                    // 1.2 s full cycle — natural "searching" rhythm.
                    // Used while the grid is settling AND while the voxel
                    // scan is running.
                    DrawAnimatedSprite(sprites, "WifiSearching_0", 4, 18, center, size, AppConfig.HeaderColor);
                    return;

                case ScannerState.CompletedWithSignal:
                    // Final result: 4 rings + clockwise sweep needle.
                    DrawScanningRings(sprites, center, size);
                    return;
            }
        }

        private static void DrawSpriteCentered(List<MySprite> sprites, string sprite, Vector2 center, float size,
            Color color)
        {
            sprites.Add(new MySprite(SpriteType.TEXTURE, sprite, center,
                new Vector2(size), color));
        }

        // Picks the current animation frame (1-based suffix appended to spritePrefix)
        // and draws it centered. framesPerStep counts game ticks per frame.
        private void DrawAnimatedSprite(List<MySprite> sprites, string spritePrefix, int frameCount,
            int framesPerStep, Vector2 center, float size, Color color)
        {
            var frame = (int)(GameplayFrame() / framesPerStep % frameCount);
            DrawSpriteCentered(sprites, spritePrefix + (frame + 1), center, size, color);
        }

        // 4 concentric rings (white by default; lit rings painted in the title
        // colour) with the 8-frame DetectorScan sweep animation overlaid on top.
        // Lit-ring count = ceil(signalStrength * 4).
        private void DrawScanningRings(List<MySprite> sprites, Vector2 center, float size)
        {
            var litRings = MathHelper.Clamp((int)Math.Ceiling(_signalStrength * 4f), 0, 4);

            var ringOn = AppConfig.HeaderColor; // painted as signal grows
            var ringOff = new Color(ForegroundColor, 0.20f); // ghost ring; doesn't compete with lit rings

            var thickness = Math.Max(2f, size * 0.05f);
            // Outer → inner, so largest ring is index 0
            for (var i = 0; i < 4; i++)
            {
                var diameter = size * (1f - i * 0.22f);
                var outer = i < litRings ? ringOn : ringOff;
                AddTexture(sprites, "Circle", center, new Vector2(diameter), outer);
                AddTexture(sprites, "Circle", center, new Vector2(Math.Max(2f, diameter - thickness * 2f)),
                    BackgroundColor);
            }

            // 8-frame sweep animation overlaid on the rings.
            // 125 ms/frame ≈ 7.5 game ticks @ 60 Hz → use 8 ticks (133 ms) to stay
            // on integer game frames; one full loop ≈ 1.07 s.
            const int FRAMES_PER_STEP = 8;
            var frame = (int)(GameplayFrame() / FRAMES_PER_STEP % 8);
            var sweepSprite = "DetectorScan_0" + (frame + 1);
            sprites.Add(new MySprite(SpriteType.TEXTURE, sweepSprite, center,
                new Vector2(size), AppConfig.HeaderColor));
        }

        // Footer router: text rows for in-progress states, icon row for the final result.
        private void DrawFooter(List<MySprite> sprites, float areaBottom, float areaWidth)
        {
            if (_state == ScannerState.CompletedWithSignal)
                DrawIconFooter(sprites, areaBottom, areaWidth);
            else
                DrawFooterText(sprites, areaBottom);
        }

        // Replaces the textual "SINAL X%" / "Range: X" with two icon+value cells
        // side by side at the bottom of the LCD.
        private void DrawIconFooter(List<MySprite> sprites, float areaBottom, float areaWidth)
        {
            var iconSize = Math.Max(20f, 32f * LayoutScale);
            var fontScale = 0.75f * Scale * FontScale;
            var labelGap = 6f * LayoutScale;

            var rowY = areaBottom - iconSize * 0.5f - 6f * LayoutScale;
            var halfGap = Math.Min(areaWidth * 0.22f, areaWidth * 0.5f - iconSize);

            // Each cell groups [icon][value] horizontally, then both cells are
            // mirrored around the screen centre.
            var leftCell = new Vector2(ViewBox.Center.X - halfGap, rowY);
            var rightCell = new Vector2(ViewBox.Center.X + halfGap, rowY);

            // Left: complete WiFi icon + signal %
            var leftIcon = leftCell + new Vector2(-iconSize * 0.5f, 0f);
            var leftValue = leftCell + new Vector2(+iconSize * 0.5f + labelGap, 0f);
            DrawSpriteCentered(sprites, "WifiSearching_04", leftIcon, iconSize, AppConfig.HeaderColor);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = (int)Math.Round(_signalStrength * 100f) + "%",
                Position = new Vector2(leftValue.X, leftValue.Y - 12f * LayoutScale),
                Color = AppConfig.HeaderColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White",
                RotationOrScale = fontScale
            });

            // Right: ruler icon + scan distance
            var rightIcon = rightCell + new Vector2(-iconSize * 0.5f, 0f);
            var rightValue = rightCell + new Vector2(+iconSize * 0.5f + labelGap, 0f);
            DrawSpriteCentered(sprites, "SignalDistance_Ruler", rightIcon, iconSize, AppConfig.HeaderColor);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = FormatingHelper.DistanceToString(_cachedScanRange),
                Position = new Vector2(rightValue.X, rightValue.Y - 12f * LayoutScale),
                Color = AppConfig.HeaderColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White",
                RotationOrScale = fontScale
            });
        }

        // Single status line centered near the bottom of the LCD.
        private void DrawFooterText(List<MySprite> sprites, float areaBottom)
        {
            var statusFontScale = 0.85f * Scale * FontScale;
            var statusY = areaBottom - 16f * LayoutScale;

            BuildStatusText(_statusBuffer);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = _statusBuffer.ToString(),
                Position = new Vector2(ViewBox.Center.X, statusY),
                Color = StatusTextColor(),
                Alignment = TextAlignment.CENTER,
                FontId = "White",
                RotationOrScale = statusFontScale
            });
        }

        // Right-edge vertical indicator for the cone-tilt slider:
        //   marker at middle  → slider 0   (cone points straight up)
        //   marker at top     → slider +100 (cone points up/forward)
        //   marker at bottom  → slider -100 (cone points down/backward)
        // Triangle apex flips with the sign so it always points where the cone points.
        private void DrawTiltIndicator(List<MySprite> sprites, float areaTop, float areaBottom)
        {
            var bias = MathHelper.Clamp(AppConfig?.OreScannerConeBias ?? 0f, -100f, 100f);

            var trackHeight = (areaBottom - areaTop) * 0.55f;
            var trackY = (areaTop + areaBottom) * 0.5f;
            var trackX = ViewBox.Right - 22f * Scale;
            var trackWidth = Math.Max(2f, 4f * Scale);

            // Track + endcaps + middle tick
            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(trackX, trackY), new Vector2(trackWidth, trackHeight),
                new Color(ForegroundColor, 0.30f)));
            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(trackX, trackY - trackHeight * 0.5f),
                new Vector2(trackWidth * 3f, Math.Max(2f, 2f * Scale)),
                new Color(ForegroundColor, 0.55f)));
            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(trackX, trackY + trackHeight * 0.5f),
                new Vector2(trackWidth * 3f, Math.Max(2f, 2f * Scale)),
                new Color(ForegroundColor, 0.55f)));
            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                new Vector2(trackX, trackY),
                new Vector2(trackWidth * 4f, Math.Max(2f, 2f * Scale)),
                new Color(ForegroundColor, 0.75f)));

            // Marker — Triangle that flips with the sign
            var markerY = trackY - bias / 100f * (trackHeight * 0.5f);
            var triSize = 14f * Scale;
            var markerColor =
                Math.Abs(bias) < 0.5f
                    ? new Color(ForegroundColor, 0.9f)
                    : _state == ScannerState.CompletedWithSignal
                        ? AppConfig.HeaderColor
                        : AppConfig.WarningColor;

            // SE Triangle sprite already points up; rotate 180° to point down for negative tilt.
            var rot = bias >= 0f ? 0f : MathHelper.Pi;
            sprites.Add(new MySprite(SpriteType.TEXTURE, "Triangle",
                new Vector2(trackX, markerY),
                new Vector2(triSize),
                markerColor, null, TextAlignment.CENTER, rot));
        }

        private void BuildStatusText(StringBuilder sb)
        {
            sb.Clear();
            switch (_state)
            {
                case ScannerState.NoDetector:
                    sb.Append(LocHelper.GetLoc("LCDMod_OreScanner_NoDetector"));
                    return;
                case ScannerState.MultipleDetectors:
                    sb.Append(LocHelper.GetLoc("LCDMod_OreScanner_MultipleDetectors"));
                    return;
                case ScannerState.Idle:
                    sb.Append(LocHelper.GetLoc("LCDMod_OreScanner_Moving"));
                    return;
                case ScannerState.Charging:
                {
                    var secondsStill = _stationaryStartFrame < 0
                        ? 0f
                        : (GameplayFrame() - _stationaryStartFrame) / 60f;
                    var left = Math.Max(0f, STATIONARY_HOLD_SEC - secondsStill);
                    sb.AppendFormat(LocHelper.GetLoc("LCDMod_OreScanner_Charging"), left.ToString("F1"));
                    return;
                }
                case ScannerState.Scanning:
                {
                    var pct = _scanAxisSteps > 0 ? _scanAxisIndex / (float)_scanAxisSteps : 0f;
                    sb.AppendFormat(LocHelper.GetLoc("LCDMod_OreScanner_Scanning"), (pct * 100f).ToString("F0"));
                    return;
                }
                case ScannerState.CompletedNoSignal:
                    sb.Append(LocHelper.GetLoc("LCDMod_OreScanner_Clear"));
                    return;
                case ScannerState.CompletedWithSignal:
                    sb.AppendFormat(LocHelper.GetLoc("LCDMod_OreScanner_Signal"),
                        (_signalStrength * 100f).ToString("F0"));
                    return;
            }
        }

        private Color StatusTextColor()
        {
            switch (_state)
            {
                case ScannerState.NoDetector:
                case ScannerState.MultipleDetectors:
                    return AppConfig.ErrorColor;
                case ScannerState.Idle:
                    return new Color(ForegroundColor, 0.55f);
                case ScannerState.Charging:
                case ScannerState.Scanning:
                    return AppConfig.WarningColor;
                case ScannerState.CompletedWithSignal:
                    return AppConfig.ErrorColor;
                default:
                    return new Color(ForegroundColor, 0.85f);
            }
        }

        // ==================================================================
        // PRIMITIVES (mirrors RadarSurfaceScript helpers — kept local for
        // independence; both classes use identical sprite vocabulary)
        // ==================================================================
        private static void AddTexture(List<MySprite> sprites, string texture, Vector2 position, Vector2 size,
            Color color, float rotation = 0f)
        {
            sprites.Add(new MySprite(SpriteType.TEXTURE, texture, position, size, color, null, TextAlignment.CENTER,
                rotation));
        }

        private static long GameplayFrame()
        {
            try
            {
                var s = MyAPIGateway.Session;
                return s != null ? s.GameplayFrameCounter : 0;
            }
            catch
            {
                return 0;
            }
        }

        private enum ScannerState
        {
            NoDetector, // grid has no working ore detector
            MultipleDetectors, // grid has more than one ore detector → ambiguous direction
            Idle, // moving
            Charging, // stationary, counting down to scan
            Scanning,
            CompletedNoSignal,
            CompletedWithSignal
        }
    }
}
