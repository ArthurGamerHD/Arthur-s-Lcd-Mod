using System;
using System.Collections.Generic;
using System.Linq;
using Acrux.Render3D;
using LcdMod.Client.Extensions;
using Sandbox.ModAPI;
using VRageMath;

namespace LcdMod.Client.Apps.IntegrityMonitor
{
    // One finished 2D map: its colored cells, already rotated when the rotation is a right angle.
    internal sealed class IntegrityMapResult
    {
        public Color?[,] Cells;
        public int FitSide;
        public float Rotation;
        public HashSet<CellKind> UsedKinds;
        public bool HasMissing;
        public bool HasDamaged;
        public bool IsEmpty;
    }

    // The 2D integrity map of the 3D scene's comparison (GridSceneRenderer.GetMapCells): the reference's blocks in
    // the grid's cells, each missing, damaged or whole. The depth map and its colors are built on a background
    // worker and published whole.
    internal sealed class IntegrityMapBuilder
    {
        internal static readonly Color DamagedColor = new Color(Color.Red, .75f);
        internal static readonly Color MissingColor = new Color(Color.DarkRed, .95f);
        internal static readonly Dictionary<CellKind, Color> KindColors = new Dictionary<CellKind, Color>
        {
            { CellKind.Thruster, new Color(0, 64, 255, 100) },
            { CellKind.Power, new Color(0, 255, 0, 100) },
            { CellKind.Gyro, new Color(255, 255, 0, 100) },
            { CellKind.Weapons, new Color(255, 64, 0, 100) },
            { CellKind.Gravity, new Color(108, 0, 128, 100) },
            { CellKind.Production, new Color(0, 170, 110, 100) },
            { CellKind.Cargo, new Color(220, 160, 40, 100) },
            { CellKind.Conveyor, new Color(230, 60, 180, 100) }
        };

        sealed class Pass
        {
            public int[] Cells;
            public View View;
            public float Rotation;
            // GridSceneState.XRayCategories bits (CellKind and GridBlockCategory share values): zero colors every kind.
            public int XRay;
            public IntegrityMapResult Result;
            public Exception Failure;
            // Set last by the worker (volatile: its writes above are visible once this reads true).
            public volatile bool Done;
        }

        int[] _cells;
        View _view;
        float _rotation = float.NaN;
        int _xRay = -1;
        Pass _pass;

        public IntegrityMapResult Result { get; private set; }
        public Exception LastFailure { get; private set; }

        public void Reset()
        {
            _cells = null;
            _pass = null;
            Result = null;
        }

        // Game thread, with the scene's latest cells (shared, never changed). True when a pass finished with a new map.
        public bool Update(int[] cells, View view, float rotation, int xRay)
        {
            if (_pass != null)
                return _pass.Done && Publish();
            if (cells == null)
                return false;
            if (ReferenceEquals(cells, _cells) && view == _view && rotation == _rotation && xRay == _xRay)
                return false;
            _cells = cells;
            _view = view;
            _rotation = rotation;
            _xRay = xRay;
            var pass = new Pass { Cells = cells, View = view, Rotation = rotation, XRay = xRay };
            _pass = pass;
            if (MyAPIGateway.Parallel == null)
            {
                Compute(pass);
                return Publish();
            }
            try
            {
                MyAPIGateway.Parallel.StartBackground(() => Compute(pass));
            }
            catch (Exception)
            {
                Compute(pass);
            }
            return false;
        }

        bool Publish()
        {
            Pass pass = _pass;
            _pass = null;
            LastFailure = pass.Failure;
            if (pass.Failure != null)
                return false;
            Result = pass.Result;
            return true;
        }

        static void Compute(Pass pass)
        {
            try
            {
                int[] data = pass.Cells;
                const int stride = GridSceneRenderer.MAP_CELL_STRIDE;
                var cells = new List<Vector3I>();
                var missing = new HashSet<Vector3I>();
                var damaged = new HashSet<Vector3I>();
                var kinds = new Dictionary<Vector3I, CellKind>();
                for (int i = 0; i + stride <= data.Length; i += stride)
                {
                    int flags = data[i + 6];
                    var kind = (CellKind)data[i + 7];
                    for (int x = data[i]; x <= data[i + 3]; x++)
                        for (int y = data[i + 1]; y <= data[i + 4]; y++)
                            for (int z = data[i + 2]; z <= data[i + 5]; z++)
                            {
                                var cell = new Vector3I(x, y, z);
                                if ((flags & GridSceneRenderer.MAP_CELL_MISSING) != 0)
                                    missing.Add(cell);
                                else
                                {
                                    if ((flags & GridSceneRenderer.MAP_CELL_DAMAGED) != 0)
                                        damaged.Add(cell);
                                    if (kind != CellKind.None)
                                        kinds[cell] = kind;
                                }
                                cells.Add(cell);
                            }
                }
                pass.Result = Present(BuildDepthMap(cells, damaged, missing, kinds, pass.View), pass.Rotation, pass.XRay);
            }
            catch (Exception exception)
            {
                // Caught on the worker: the engine may treat uncaught background exceptions as fatal.
                pass.Failure = exception;
            }
            pass.Done = true;
        }

        static IntegrityMapResult Present(DepthMap2D depthMap, float rotation, int xRay)
        {
            var result = new IntegrityMapResult { UsedKinds = new HashSet<CellKind>() };
            if (depthMap.Width <= 0 || depthMap.Height <= 0)
            {
                result.IsEmpty = true;
                return result;
            }

            var cellColors = new Color?[depthMap.Width, depthMap.Height];
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
                CellKind kind = depthMap.CellType[x, y];
                if (xRay != 0 && (xRay & (1 << (int)kind)) == 0)
                    kind = CellKind.None;
                if (isMissing)
                    result.HasMissing = true;
                else if (isDamaged)
                    result.HasDamaged = true;
                else
                    result.UsedKinds.Add(kind);
                cellColors[x, y] = BlendWithAlpha(Color.Gray.MulValue(value),
                    isMissing ? MissingColor : isDamaged ? DamagedColor : ColorOf(kind));
            }

            float snappedRotation = SnapAngle(rotation, MathHelper.ToRadians(2f));
            float renderRotation = snappedRotation;
            if (IsRightAngleMultiple(snappedRotation))
            {
                cellColors = RotateColorGridNearestSquare(cellColors, snappedRotation);
                renderRotation = 0f;
            }
            result.Cells = cellColors;
            result.FitSide = Math.Max(depthMap.Width, depthMap.Height);
            result.Rotation = renderRotation;
            return result;
        }

        static Color ColorOf(CellKind kind)
        {
            Color color;
            return KindColors.TryGetValue(kind, out color) ? color : Color.Transparent;
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


        static float SnapAngle(float rotation, float step)
        {
            if (step <= 0f)
                return rotation;

            return (float)(Math.Round(rotation / step) * step);
        }


        static DepthMap2D BuildDepthMap(
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


        // The same screen axes as the 3D view's camera from that side (GridSceneRenderer), so switching between
        // the two keeps the picture's orientation.
        static ViewAxes GetAxes(View view)
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
                        U = p => p.X,
                        V = p => -p.Z,
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
                        U = p => p.X,
                        V = p => p.Y,
                        D = p => -p.Z
                    };

                case View.Zneg: // looking from -Z toward +Z
                    return new ViewAxes
                    {
                        U = p => -p.X,
                        V = p => p.Y,
                        D = p => p.Z
                    };

                default:
                    throw new Exception("Invalid view");
            }
        }
    }
}
