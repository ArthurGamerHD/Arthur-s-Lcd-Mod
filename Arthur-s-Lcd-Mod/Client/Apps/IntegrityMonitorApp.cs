using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using ColorExtensions = LcdMod.Client.Extensions.ColorExtensions;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;

using LcdMod.Common.Config.Generation;

namespace LcdMod.Client.Apps
{
    [LcdApp(10)]
    [ConfigComponent(APP, typeof(DiagnosticConfigComponent), PropertyName = "DiagnosticComponent")]
    [ConfigComponent(PROJECTOR_REFERENCE, typeof(BlockReferenceConfigComponent), PropertyName = "ProjectorReferenceComponent")]
    internal sealed partial class IntegrityMonitorApp : App
    {
        public static readonly List<MyTerminalControlComboBoxItem> IntegrityAxes = new List<MyTerminalControlComboBoxItem>
        {
            new MyTerminalControlComboBoxItem
            {
                Key = 0,
                Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_X_Positive")
            },
            new MyTerminalControlComboBoxItem
            {
                Key = 1,
                Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_X_Negative")
            },
            new MyTerminalControlComboBoxItem
            {
                Key = 2,
                Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_Y_Positive")
            },
            new MyTerminalControlComboBoxItem
            {
                Key = 3,
                Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_Y_Negative")
            },
            new MyTerminalControlComboBoxItem
            {
                Key = 4,
                Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_Z_Positive")
            },
            new MyTerminalControlComboBoxItem
            {
                Key = 5,
                Value = MyStringId.GetOrCompute(MOD_PREFIX + "Axis_Z_Negative")
            }
        };
        
        const int MAP_BUILD_BATCH_SIZE = 256;

        static readonly Dictionary<GridPairKey, CachedGridMapEntry> GridMapCache =
            new Dictionary<GridPairKey, CachedGridMapEntry>();

        public const string TITLE = "BroadcastStatus_IsPreviewGrid";

        readonly Dictionary<CellKind, string> _legendCategoryLocKeys = new Dictionary<CellKind, string>
        {
            { CellKind.None, "DisplayName_Category_ArmorBlocks" },
            { CellKind.Thruster, "ControlMenuItemLabel_Thrusts" },
            { CellKind.Power, "RadialMenuGroupTitle_Power" },
            { CellKind.Gyro, "DisplayName_BlockGroup_Gyroscopes" },
            { CellKind.Weapons, "DisplayName_BlueprintClass_Weapons" },
            { CellKind.Gravity, "DisplayName_TSS_Gravity" },
            { CellKind.Production, "RadialMenuGroupTitle_Production" }
        };

        const string MISSING_BLOCK_LOCKEY = "MissingBlock";
        const string DAMAGED_BLOCK_LOCKEY = "UnfinishedBlock";

        readonly Color _damagedColor = new Color(Color.Red, .75f);
        readonly Color _missingColor = new Color(Color.DarkRed, .95f);

        readonly List<MySprite> _sprites = new List<MySprite>();
        readonly IntegrityMonitor.IntegrityMonitorViewModel _viewModel = new IntegrityMonitor.IntegrityMonitorViewModel();
        string _customTitle;
        DepthMap2D? _depthCache;
        IMyProjector _projector;

        IMyCubeBlock Block => Host.Block;

        public override string Title => _customTitle ?? LocHelper.GetLoc(TITLE);
        internal override float WindowRenderScaling => CardScale;

        public IntegrityMonitorApp(IAppHost host) : base(host)
        {
            SetContent(new IntegrityMonitor.IntegrityMonitorView { DataContext = _viewModel });
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            CachedVersion = new DateTime();
        }

        public override void Update()
        {
            if (ProjectorReferenceComponent.EntityId == 0)
            {
                ShowWithoutMap(LocHelper.GetLoc("ScreenBlueprintsRew_NoBlueprints"), "Warning", ColorComponent.ResolveWarningColor());
                return;
            }

            FindProjector(Block?.CubeGrid, ref _projector);

            if ((_projector == null || !_projector.IsFunctional || _projector.Closed) && _depthCache != null)
            {
                if (GetTimeStep(0.5f) % 2 == 1)
                    _viewModel.Status.Show(
                        string.Format(
                            LocHelper.GetLoc(_projector != null && !_projector.IsFunctional
                                ? "SignalConnectivity_State_NotOperational"
                                : "SignalConnectivity_State_NoLaserLink"),
                            LocHelper.GetLoc("DisplayName_Block_Projector")),
                        "Warning",
                        ColorExtensions.MulValue(ColorComponent.ResolveErrorColor(), 2).MulSaturation(2));
                else
                    _viewModel.Status.Hide();
                return;
            }

            SetTitle(_projector?.CustomName);

            if (_projector != null && !_projector.Enabled)
            {
                ShowWithoutMap((_projector.CustomName ?? string.Empty) + " " + LocHelper.GetLoc("AssemblerState_Disabled"),
                    "GridPower", ColorComponent.ResolveWarningColor());
                return;
            }

            if (_projector?.ProjectedGrid == null || _projector.Closed)
            {
                ShowWithoutMap(LocHelper.Empty, "Warning", ColorComponent.ResolveWarningColor());
                return;
            }

            try
            {
                SetTitle(_projector.ProjectedGrid.CustomName);
                var map3D = GetOrUpdate3DMap(_projector);
                if (map3D.LastUpdate.Ticks == 0)
                {
                    if (_viewModel.HasMap)
                        _viewModel.Status.Hide();
                    else
                        _viewModel.Status.ShowLoading(LocHelper.GetLoc("LoadingPleaseWait"), Host.Surface.ScriptForegroundColor);
                    return;
                }

                _viewModel.Status.Hide();
                if (map3D.LastUpdate == CachedVersion)
                    return;

                CachedVersion = map3D.LastUpdate;
                var depth = BuildDepthMap(map3D.Cells, map3D.DamagedCells, map3D.MissingCells, map3D.CellTypes,
                    (View)GeneralComponent.DisplayMode);
                _depthCache = depth;
                PresentDepthMap(depth, (View)GeneralComponent.DisplayMode,
                    MathHelper.ToRadians(DiagnosticComponent.Rotation), GeneralComponent.GetScale());
            }
            catch (Exception e)
            {
                ShowWithoutMap(LocHelper.GetLoc("ScreenDebugOfficial_ErrorLogCaption") + "\n" + e.Message, "Warning",
                    ColorComponent.ResolveErrorColor());
            }
        }

        void ShowWithoutMap(string message, string icon, Color color)
        {
            _viewModel.ClearMap();
            _viewModel.Status.Show(message, icon, color);
        }

        void SetTitle(string title)
        {
            if (string.Equals(_customTitle, title, StringComparison.Ordinal))
                return;
            _customTitle = title;
            RaisePropertyChanged(nameof(Title));
        }

        public override List<MySprite> GetSprites()
        {
            return _sprites;
        }

        public DateTime CachedVersion { get; set; }

        static int GetTimeStep(float secondsPerStep)
        {
            var session = MyAPIGateway.Session;
            if (session == null)
                return 0;
            if (secondsPerStep <= 0f)
                secondsPerStep = 1f / 60f;
            var ticksPerStep = Math.Max(1, (int)Math.Round(secondsPerStep * 60f));
            return session.GameplayFrameCounter / ticksPerStep;
        }

        static CachedGridMap GetOrUpdate3DMap(IMyProjector projector)
        {
            if (projector == null || projector.Closed || projector.ProjectedGrid == null || projector.CubeGrid == null)
                return CachedGridMap.Empty;

            var key = new GridPairKey(projector.ProjectedGrid.EntityId, projector.CubeGrid.EntityId);
            CachedGridMapEntry entry;
            if (!GridMapCache.TryGetValue(key, out entry))
            {
                entry = new CachedGridMapEntry();
                GridMapCache[key] = entry;
            }

            if (entry.Updater == null)
                entry.Updater = Generate3DMapCoroutine(projector, entry).GetEnumerator();

            // Advance one chunk per Run() so map building happens progressively.
            bool hasMore;
            try
            {
                hasMore = entry.Updater.MoveNext();
            }
            catch
            {
                hasMore = false;
            }

            if (!hasMore)
            {
                entry.Updater.Dispose();
                entry.Updater = null;
            }


            return entry.Current;
        }

        static IEnumerable<bool> Generate3DMapCoroutine(IMyProjector projector, CachedGridMapEntry entry)
        {
            if (projector == null || projector.Closed || projector.ProjectedGrid == null || projector.CubeGrid == null)
                yield break;

            var cells = new List<Vector3I>();
            var missingCells = new HashSet<Vector3I>();
            var damagedCells = new HashSet<Vector3I>();
            var cellTypes = new Dictionary<Vector3I, CellKind>();

            Vector3I a = projector.ProjectedGrid.Min;
            Vector3I b = projector.ProjectedGrid.Max;

            int startX = Math.Min(a.X, b.X);
            int startY = Math.Min(a.Y, b.Y);
            int startZ = Math.Min(a.Z, b.Z);

            int endX = Math.Max(a.X, b.X);
            int endY = Math.Max(a.Y, b.Y);
            int endZ = Math.Max(a.Z, b.Z);

            int processed = 0;

            for (int x = startX; x <= endX; x++)
            {
                for (int y = startY; y <= endY; y++)
                {
                    for (int z = startZ; z <= endZ; z++)
                    {
                        var projectedPos = new Vector3I(x, y, z);

                        bool projectedExists = projector.ProjectedGrid.CubeExists(projectedPos);
                        if (projectedExists)
                        {
                            bool isBroken;
                            Vector3I realPos =
                                projector.CubeGrid.WorldToGridInteger(
                                    projector.ProjectedGrid.GridIntegerToWorld(projectedPos));
                            bool realExists = projector.CubeGrid.CubeExists(realPos);

                            if (!realExists)
                            {
                                isBroken = true;
                            }
                            else
                            {
                                var reference = projector.ProjectedGrid.GetCubeBlock(projectedPos);
                                var real = projector.CubeGrid.GetCubeBlock(realPos);

                                var targetIntegrity = reference.Integrity;
                                var realIntegrity = real.Integrity;

                                if (targetIntegrity > realIntegrity)
                                    damagedCells.Add(realPos);

                                isBroken = reference.GetType() != real.GetType();

                                if (reference.FatBlock != null)
                                    cellTypes[realPos] = GetCellKind(reference.FatBlock.GetType());
                            }

                            cells.Add(realPos);
                            if (isBroken)
                                missingCells.Add(realPos);
                        }

                        processed++;
                        if (processed >= MAP_BUILD_BATCH_SIZE)
                        {
                            processed = 0;
                            yield return true;
                        }
                    }
                }
            }

            entry.Current = new CachedGridMap(cells, missingCells, damagedCells, cellTypes, DateTime.Now);
        }

        void PresentDepthMap(DepthMap2D depthMap, View view, float rotation, float scale)
        {
            if (depthMap.Width <= 0 || depthMap.Height <= 0)
            {
                ShowWithoutMap(LocHelper.Empty, "Warning", ColorComponent.ResolveWarningColor());
                _viewModel.SetLegend(new List<IntegrityMonitor.IntegrityLegendEntry>(), Color.Transparent);
                return;
            }

            var cellColors = new Color?[depthMap.Width, depthMap.Height];
            var usedKinds = new HashSet<CellKind>();
            bool hasDamaged = false;
            bool hasMissing = false;
            for (int x = 0; x < depthMap.Width; x++)
            for (int y = 0; y < depthMap.Height; y++)
            {
                var percent = depthMap.Percent[x, y];
                if (!percent.HasValue)
                    continue;
                float shade = MathHelper.Clamp(percent.Value / 100f, 0f, 1f);
                float value = (255 - (60f + shade * 195f)) / 255;
                bool isDamaged = depthMap.Damaged[x, y];
                bool isMissing = depthMap.Missing[x, y];
                if (isMissing)
                    hasMissing = true;
                else if (isDamaged)
                    hasDamaged = true;
                else
                    usedKinds.Add(depthMap.CellType[x, y]);
                cellColors[x, y] = BlendWithAlpha(Color.Gray.MulValue(value),
                    isMissing ? _missingColor : isDamaged ? _damagedColor : GetColorForType(depthMap.CellType[x, y]));
            }

            float snappedRotation = SnapAngle(IsPositiveView(view) ? -rotation : rotation, MathHelper.ToRadians(2f));
            float renderRotation = snappedRotation;
            if (IsRightAngleMultiple(snappedRotation))
            {
                cellColors = RotateColorGridNearestSquare(cellColors, snappedRotation);
                renderRotation = 0f;
            }
            _viewModel.SetMap(cellColors, Math.Max(depthMap.Width, depthMap.Height), renderRotation, Math.Max(.05f, scale));
            _viewModel.SetLegend(BuildLegend(usedKinds, hasMissing, hasDamaged),
                new Color(Host.BackgroundColor.MulValue(0.8f), 0.5f));
        }

        public static Color BlendWithAlpha(Color bottom, Color top)
        {
            float topA = top.A / 255f;
            float bottomA = bottom.A / 255f;

            float outA = topA + bottomA * (1f - topA);

            if (outA <= 0f)
                return new Color(0, 0, 0, 0);

            byte r = (byte)Math.Round((top.R * topA + bottom.R * bottomA * (1f - topA)) / outA);
            byte g = (byte)Math.Round((top.G * topA + bottom.G * bottomA * (1f - topA)) / outA);
            byte b = (byte)Math.Round((top.B * topA + bottom.B * bottomA * (1f - topA)) / outA);
            byte a = (byte)Math.Round(outA * 255f);

            return new Color(r, g, b, a);
        }

        List<IntegrityMonitor.IntegrityLegendEntry> BuildLegend(HashSet<CellKind> usedKinds, bool hasMissing, bool hasDamaged)
        {
            var entries = new List<IntegrityMonitor.IntegrityLegendEntry>();
            if (!hasMissing && usedKinds.Count == 0)
                return entries;
            if (usedKinds.Contains(CellKind.None))
                entries.Add(new IntegrityMonitor.IntegrityLegendEntry(Color.Gray, GetLegendCaption(CellKind.None)));
            if (hasMissing)
                entries.Add(new IntegrityMonitor.IntegrityLegendEntry(_missingColor, LocHelper.GetLoc(MISSING_BLOCK_LOCKEY)));
            if (hasDamaged)
                entries.Add(new IntegrityMonitor.IntegrityLegendEntry(_damagedColor, LocHelper.GetLoc(DAMAGED_BLOCK_LOCKEY)));
            foreach (var pair in _colors)
                if (usedKinds.Contains(pair.Key))
                    entries.Add(new IntegrityMonitor.IntegrityLegendEntry(pair.Value, GetLegendCaption(pair.Key)));
            return entries;
        }

        string GetLegendCaption(CellKind kind)
        {
            string key;
            return _legendCategoryLocKeys.TryGetValue(kind, out key) ? LocHelper.GetLoc(key) : kind.ToString();
        }

        static Color?[,] RotateColorGridNearestSquare(Color?[,] source, float rotation)
        {
            int width = source.GetLength(0);
            int height = source.GetLength(1);
            if (width <= 0 || height <= 0)
                return new Color?[0, 0];

            int side = Math.Max(width, height);
            var square = new Color?[side, side];
            int offX = (side - width) / 2;
            int offY = (side - height) / 2;
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                square[x + offX, y + offY] = source[x, y];

            float sin = (float)Math.Sin(rotation);
            float cos = (float)Math.Cos(rotation);
            var rotated = new Color?[side, side];
            float srcCx = (side - 1) * 0.5f;
            float srcCy = (side - 1) * 0.5f;
            float dstCx = srcCx;
            float dstCy = srcCy;

            for (int x = 0; x < side; x++)
            {
                for (int y = 0; y < side; y++)
                {
                    float dx = x - dstCx;
                    float dy = y - dstCy;

                    // Screen/grid space here is Y-down, so inverse sampling uses this sign convention.
                    float srcXf = dx * cos - dy * sin + srcCx;
                    float srcYf = dx * sin + dy * cos + srcCy;

                    int sx = (int)Math.Round(srcXf);
                    int sy = (int)Math.Round(srcYf);

                    if (sx < 0 || sx >= side || sy < 0 || sy >= side)
                        continue;

                    rotated[x, y] = square[sx, sy];
                }
            }

            return rotated;
        }

        static bool IsRightAngleMultiple(float rotation)
        {
            double quarterTurns = rotation / (Math.PI * 0.5);
            return Math.Abs(quarterTurns - Math.Round(quarterTurns)) < 0.001;
        }

        static bool IsPositiveView(View view)
        {
            return view == View.Xpos || view == View.Ypos || view == View.Zpos;
        }

        static float SnapAngle(float rotation, float step)
        {
            if (step <= 0f)
                return rotation;

            return (float)(Math.Round(rotation / step) * step);
        }

        private static CellKind GetCellKind(Type topType)
        {
            if (topType == null)
                return CellKind.None;

            if (MyAPIGateway.Reflection.IsAssignableFrom(typeof(IMyThrust), topType))
                return CellKind.Thruster;

            if (MyAPIGateway.Reflection.IsAssignableFrom(typeof(IMyPowerProducer), topType))
                return CellKind.Power;

            if (MyAPIGateway.Reflection.IsAssignableFrom(typeof(IMyGyro), topType))
                return CellKind.Gyro;

            if (MyAPIGateway.Reflection.IsAssignableFrom(typeof(IMyWarhead), topType) ||
                MyAPIGateway.Reflection.IsAssignableFrom(typeof(IMyUserControllableGun), topType))
                return CellKind.Weapons;

            if (MyAPIGateway.Reflection.IsAssignableFrom(typeof(IMyGravityGenerator), topType) ||
                MyAPIGateway.Reflection.IsAssignableFrom(typeof(IMyArtificialMassBlock), topType))
                return CellKind.Gravity;

            if (MyAPIGateway.Reflection.IsAssignableFrom(typeof(IMyProductionBlock), topType))
                return CellKind.Production;

            return CellKind.None;
        }

        Color GetColorForType(CellKind topType)
        {
            Color color;
            if (_colors.TryGetValue(topType, out color))
                return color;

            return Color.Transparent;
        }

        Dictionary<CellKind, Color> _colors = new Dictionary<CellKind, Color>
        {
            { CellKind.Thruster, new Color(0, 64, 255, 100) }, // blue
            { CellKind.Power, new Color(0, 255, 0, 100) }, // green
            { CellKind.Gyro, new Color(255, 255, 0, 100) }, // yellow
            { CellKind.Weapons, new Color(255, 64, 0, 100) }, // orange
            { CellKind.Gravity, new Color(108, 0, 128, 100) }, // violet 
            { CellKind.Production, new Color(0, 170, 110, 100) } // cyan
        };


        private static DepthMap2D BuildDepthMap(
            IEnumerable<Vector3I> cells,
            HashSet<Vector3I> damagedSet,
            HashSet<Vector3I> missingSet,
            IReadOnlyDictionary<Vector3I, CellKind> cellKinds,
            View view)
        {
            var axes = GetAxes(view);

            var points = cells
                .Select(p =>
                {
                    CellKind kind;
                    if (!cellKinds.TryGetValue(p, out kind))
                        kind = CellKind.None;

                    return new
                    {
                        Pos = p,
                        U = axes.U(p),
                        V = axes.V(p),
                        D = axes.D(p),
                        Damaged = damagedSet.Contains(p),
                        Missing = missingSet.Contains(p),
                        Kind = kind
                    };
                })
                .ToList();

            if (points.Count == 0)
            {
                return new DepthMap2D
                {
                    Percent = new float?[0, 0],
                    Damaged = new bool[0, 0],
                    Missing = new bool[0, 0],
                    CellType = new CellKind[0, 0],
                    Width = 0,
                    Height = 0,
                    UMin = 0,
                    VMin = 0
                };
            }

            int uMin = points.Min(p => p.U);
            int uMax = points.Max(p => p.U);
            int vMin = points.Min(p => p.V);
            int vMax = points.Max(p => p.V);
            int dMin = points.Min(p => p.D);
            int dMax = points.Max(p => p.D);

            int width = uMax - uMin + 1;
            int height = vMax - vMin + 1;

            float?[,] percent = new float?[width, height];
            bool[,] damaged = new bool[width, height];
            bool[,] missing = new bool[width, height];
            CellKind[,] topKind = new CellKind[width, height];

            int[,] bestDepth = new int[width, height];
            bool[,] filled = new bool[width, height];

            int[,] bestKindDepth = new int[width, height];
            bool[,] kindFilled = new bool[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    bestDepth[x, y] = int.MaxValue;
                    bestKindDepth[x, y] = int.MaxValue;
                    topKind[x, y] = CellKind.None;
                }
            }

            foreach (var p in points)
            {
                int x = p.U - uMin;
                int y = p.V - vMin;

                // Damaged scans the whole column
                if (p.Damaged)
                    damaged[x, y] = true;

                if (p.Missing)
                    missing[x, y] = true;

                // Front-most cell overall for Percent
                if (!filled[x, y] || p.D < bestDepth[x, y])
                {
                    bestDepth[x, y] = p.D;
                    filled[x, y] = true;
                }

                // First front-most cell that actually has a kind
                if (p.Kind != CellKind.None &&
                    (!kindFilled[x, y] || p.D < bestKindDepth[x, y]))
                {
                    bestKindDepth[x, y] = p.D;
                    kindFilled[x, y] = true;
                    topKind[x, y] = p.Kind;
                }
            }

            int depthRange = dMax - dMin;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (!filled[x, y])
                        continue;

                    percent[x, y] = depthRange == 0
                        ? 0f
                        : 100f * (bestDepth[x, y] - dMin) / depthRange;
                }
            }

            return new DepthMap2D
            {
                Percent = percent,
                Damaged = damaged,
                Missing = missing,
                CellType = topKind,
                Width = width,
                Height = height,
                UMin = uMin,
                VMin = vMin
            };
        }

        void FindProjector(IMyCubeGrid grid, ref IMyProjector projector)
        {
            if (ProjectorReferenceComponent.EntityId == 0)
            {
                projector = null;
                return;
            }

            if (projector != null && projector.EntityId == ProjectorReferenceComponent.EntityId)
                return;

            var entity = MyAPIGateway.Entities.GetEntityById(ProjectorReferenceComponent.EntityId) as IMyProjector;
            projector = entity?.CubeGrid.IsInSameLogicalGroupAs(grid) ?? false ? entity : null;
        }


        private static ViewAxes GetAxes(View view)
        {
            switch (view)
            {
                // These choose a stable 2D plane.
                // Depth is signed so 0% = front-most, 100% = back-most.

                case View.Xpos: // looking from +X toward -X
                    return new ViewAxes
                    {
                        U = p => -p.Z,
                        V = p => p.Y,
                        D = p => -p.X
                    };

                case View.Xneg: // looking from -X toward +X
                    return new ViewAxes
                    {
                        U = p => p.Z,
                        V = p => p.Y,
                        D = p => p.X
                    };

                case View.Ypos: // looking from +Y toward -Y
                    return new ViewAxes
                    {
                        U = p => -p.X,
                        V = p => p.Z,
                        D = p => -p.Y
                    };

                case View.Yneg: // looking from -Y toward +Y
                    return new ViewAxes
                    {
                        U = p => p.X,
                        V = p => p.Z,
                        D = p => p.Y
                    };

                case View.Zpos: // looking from +Z toward -Z
                    return new ViewAxes
                    {
                        U = p => -p.X,
                        V = p => p.Y,
                        D = p => -p.Z
                    };

                case View.Zneg: // looking from -Z toward +Z
                    return new ViewAxes
                    {
                        U = p => p.X,
                        V = p => p.Y,
                        D = p => p.Z
                    };

                default:
                    throw new Exception("Invalid view");
            }
        }
    }

    public struct DepthMap2D
    {
        public float?[,] Percent;
        public bool[,] Damaged;
        public bool[,] Missing;
        public CellKind[,] CellType;
        public int Width;
        public int Height;
        public int UMin;
        public int VMin;
    }

    public struct ViewAxes
    {
        public Func<Vector3I, int> U; // 2D X
        public Func<Vector3I, int> V; // 2D Y
        public Func<Vector3I, int> D; // depth, normalized later
    }


    public enum CellKind
    {
        None,
        Thruster,
        Power,
        Gyro,
        Weapons,
        Gravity,
        Production
    }

    struct GridPairKey : IEquatable<GridPairKey>
    {
        public readonly long ProjectedGridId;
        public readonly long RealGridId;

        public GridPairKey(long projectedGridId, long realGridId)
        {
            ProjectedGridId = projectedGridId;
            RealGridId = realGridId;
        }

        public bool Equals(GridPairKey other)
        {
            return ProjectedGridId == other.ProjectedGridId && RealGridId == other.RealGridId;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is GridPairKey))
                return false;

            return Equals((GridPairKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (ProjectedGridId.GetHashCode() * 397) ^ RealGridId.GetHashCode();
            }
        }
    }

    struct CachedGridMap
    {
        public static readonly CachedGridMap Empty = new CachedGridMap(
            new List<Vector3I>(),
            new HashSet<Vector3I>(),
            new HashSet<Vector3I>(),
            new Dictionary<Vector3I, CellKind>(),
            new DateTime(0));

        public readonly List<Vector3I> Cells;
        public readonly HashSet<Vector3I> MissingCells;
        public readonly HashSet<Vector3I> DamagedCells;
        public readonly Dictionary<Vector3I, CellKind> CellTypes;
        public readonly DateTime LastUpdate;

        public CachedGridMap(List<Vector3I> cells,
            HashSet<Vector3I> missingCells,
            HashSet<Vector3I> damagedCells,
            Dictionary<Vector3I, CellKind> cellTypes, DateTime now)
        {
            Cells = cells;
            MissingCells = missingCells;
            DamagedCells = damagedCells;
            CellTypes = cellTypes;
            LastUpdate = now;
        }
    }

    class CachedGridMapEntry
    {
        public CachedGridMap Current = CachedGridMap.Empty;
        public IEnumerator<bool> Updater;
    }
    
    public enum View
    {
        Xpos = 0,
        Xneg = 1,
        Ypos = 2,
        Yneg = 3,
        Zpos = 4,
        Zneg = 5,
    }
}
