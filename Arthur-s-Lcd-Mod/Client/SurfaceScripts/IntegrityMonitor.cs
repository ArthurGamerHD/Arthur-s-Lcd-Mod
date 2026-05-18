using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Generated;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Helpers;
using LcdMod.Client.Terminal.Controls;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using ColorExtensions = LcdMod.Client.Extensions.ColorExtensions;
using IMyCubeBlock = VRage.Game.ModAPI.IMyCubeBlock;
using ListboxProjectorSelection = LcdMod.Client.Terminal.Controls.Blueprint.ListboxProjectorSelection;
using SliderRotation = LcdMod.Client.Terminal.Controls.Generic.SliderRotation;

namespace LcdMod.Client.SurfaceScripts
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class IntegrityMonitorSurfaceScript : SurfaceScriptBase,
        IUsesTerminalControl<SliderRotation>,
        IUsesTerminalControl<ListboxProjectorSelection>,
        IMultiDisplayMode
    {
        protected override ConfigKind ConfigKind => ConfigKind.Diagnostic;
        readonly List<MyTerminalControlComboBoxItem> _integrityAxes = new List<MyTerminalControlComboBoxItem>
        {
            new MyTerminalControlComboBoxItem
            {
                Key = 0,
                Value = MyStringId.GetOrCompute("LcdMod_Axis_X_Positive")
            },
            new MyTerminalControlComboBoxItem
            {
                Key = 1,
                Value = MyStringId.GetOrCompute("LcdMod_Axis_X_Negative")
            },
            new MyTerminalControlComboBoxItem
            {
                Key = 2,
                Value = MyStringId.GetOrCompute("LcdMod_Axis_Y_Positive")
            },
            new MyTerminalControlComboBoxItem
            {
                Key = 3,
                Value = MyStringId.GetOrCompute("LcdMod_Axis_Y_Negative")
            },
            new MyTerminalControlComboBoxItem
            {
                Key = 4,
                Value = MyStringId.GetOrCompute("LcdMod_Axis_Z_Positive")
            },
            new MyTerminalControlComboBoxItem
            {
                Key = 5,
                Value = MyStringId.GetOrCompute("LcdMod_Axis_Z_Negative")
            }
        };
        
        const int MAP_BUILD_BATCH_SIZE = 256;

        static readonly Dictionary<GridPairKey, CachedGridMapEntry> GridMapCache =
            new Dictionary<GridPairKey, CachedGridMapEntry>();

        public const string ID = "PreviewCharts";
        public const string TITLE = "BroadcastStatus_IsPreviewGrid";

        protected override string DefaultTitle => _customTitle ?? TITLE;

        public List<MyTerminalControlComboBoxItem> GetDisplayModes() => _integrityAxes;

        string _customTitle;
        readonly HashSet<CellKind> _legendUsedKinds = new HashSet<CellKind>();
        bool _legendHasMissing, _legendHasDamaged;

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

        List<MySprite> _frameBuffer = new List<MySprite>();
        DepthMap2D? _depthCache;
        View _view;

        IMyProjector _projector;

        public IntegrityMonitorSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size) : base(surface,
            block, size)
        {
        }

        protected override void LayoutChanged()
        {
            base.LayoutChanged();
            _frameBuffer.Clear();
            CachedVersion = new DateTime();
            _customTitle = LocHelper.GetLoc("BroadcastStatus_IsPreviewGrid");
        }

        public override void SafeRun()
        {
            FooterHeight = 0f;
            _legendUsedKinds.Clear();
            _legendHasMissing = false;
            _legendHasDamaged = false;

            if (AppConfig == null)
                return;

            if (AppConfig.ReferenceBlock == 0)
            {
                EmptyWithFilters();
                return;
            }


            IMyCubeGrid grid = Block?.CubeGrid;
            FindProjector(grid, ref _projector);

            if ((_projector == null || !_projector.IsFunctional || _projector.Closed)
                && AppConfig.ReferenceBlock != 0
                && _depthCache != null)
            {
                int blinkStep = GetTimeStep(0.5f);
                bool showWarning = (blinkStep % 2) == 1;

                using (var frame = Surface.DrawFrame())
                {
                    var sprites = new List<MySprite>();
                    AddBackground(sprites);
                    if (!_frameBuffer.Any())
                    {
                        DrawDepthMap(_frameBuffer, (DepthMap2D)_depthCache, _view,
                            MathHelper.ToRadians(AppConfig.Rotation), AppConfig.Scale);
                        DrawFooter(_frameBuffer);
                    }

                    sprites.AddRange(_frameBuffer);
                    DrawTitle(sprites);

                    if (showWarning)
                        DrawMessage(sprites,
                            string.Format(
                                LocHelper.GetLoc(_projector != null && !_projector.IsFunctional
                                    ? "SignalConnectivity_State_NotOperational"
                                    : "SignalConnectivity_State_NoLaserLink"),
                                LocHelper.GetLoc("DisplayName_Block_Projector")), "Warning",
                            ColorExtensions.MulValue(AppConfig.ErrorColor, 2).MulSaturation(2),
                            AppConfig.Scale);

                    frame.AddRange(sprites);
                }

                return;
            }

            _customTitle = _projector?.CustomName ?? LocHelper.GetLoc("BroadcastStatus_IsPreviewGrid");

            if (_projector != null && !_projector.Enabled)
            {
                using (var frame = Surface.DrawFrame())
                {
                    var sprites = new List<MySprite>();
                    AddBackground(sprites);
                    DrawTitle(sprites);
                    DrawMessage(sprites,
                        (_projector.CustomName ?? string.Empty) + " " + LocHelper.GetLoc("AssemblerState_Disabled"),
                        "GridPower", AppConfig.WarningColor, AppConfig.Scale);
                    DrawFooter(sprites);
                    frame.AddRange(sprites);
                }

                return;
            }


            if (_projector?.ProjectedGrid == null || _projector.Closed)
            {
                Empty();
                return;
            }

            try
            {
                _customTitle = _projector?.ProjectedGrid?.CustomName;

                var map3D = GetOrUpdate3DMap(_projector);

                if (map3D.LastUpdate.Ticks == 0)
                {
                    using (var frame = Surface.DrawFrame())
                    {
                        var sprites = new List<MySprite>();
                        AddBackground(sprites);
                        DrawTitle(sprites);
                        DrawLoadingFrame(sprites, AppConfig.Scale);
                        DrawFooter(sprites);
                        frame.AddRange(sprites);
                    }

                    return;
                }

                if (map3D.LastUpdate == CachedVersion)
                    return;

                CachedVersion = map3D.LastUpdate;

                _view = (View)AppConfig.DisplayMode;
                var depth = BuildDepthMap(map3D.Cells, map3D.DamagedCells, map3D.MissingCells, map3D.CellTypes, _view);
                _depthCache = depth;

                _frameBuffer.Clear();
                DrawDepthMap(_frameBuffer, depth, _view, MathHelper.ToRadians(AppConfig.Rotation), AppConfig.Scale);
                DrawFooter(_frameBuffer);

                using (var frame = Surface.DrawFrame())
                {
                    var sprites = new List<MySprite>();
                    AddBackground(sprites);
                    sprites.AddRange(_frameBuffer);
                    DrawTitle(sprites);
                    frame.AddRange(sprites);
                }
            }
            catch (Exception e)
            {
                using (var frame = Surface.DrawFrame())
                {
                    var sprites = new List<MySprite>();

                    AddBackground(sprites);
                    DrawTitle(sprites);
                    DrawMessage(sprites, LocHelper.GetLoc("ScreenDebugOfficial_ErrorLogCaption") + "\n" + e.Message,
                        "Warning", AppConfig.ErrorColor, AppConfig.Scale);
                    DrawFooter(sprites);
                    frame.AddRange(sprites);
                }
            }
        }

        public DateTime CachedVersion { get; set; }

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

        void DrawDepthMap(List<MySprite> sprites, DepthMap2D depthMap, View view, float rotation = 0f, float scale = 1f)
        {
            if (depthMap.Width <= 0 || depthMap.Height <= 0)
            {
                var center = new Vector2(ViewBox.Center.X, (CaretY + ViewBox.Bottom - FooterHeight) * 0.5f);
                sprites.Add(MakeText((IMyTextSurface)Surface, LocHelper.Empty, center, Scale, TextAlignment.CENTER));
                return;
            }

            float margin = 0f;
            float contentStart = ViewBox.X + margin;
            float contentEnd = ViewBox.X + ViewBox.Width - margin;
            float contentTop = CaretY;
            float contentBottom = ViewBox.Bottom - FooterHeight;

            float contentWidth = Math.Max(0f, contentEnd - contentStart);
            float contentHeight = Math.Max(0f, contentBottom - contentTop);
            if (contentWidth <= 0f || contentHeight <= 0f)
                return;

            float mapScale = Math.Max(0.05f, scale);
            var cellColors = new Color?[depthMap.Width, depthMap.Height];
            var usedKinds = new HashSet<CellKind>();
            bool hasDamaged = false;
            bool hasMissing = false;

            for (int x = 0; x < depthMap.Width; x++)
            {
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

                    var color = BlendWithAlpha(Color.Gray.MulValue(value),
                        (isMissing ? _missingColor :
                            isDamaged ? _damagedColor : GetColorForType(depthMap.CellType[x, y])));
                    cellColors[x, y] = color;
                }
            }

            float adjustedRotation = IsPositiveView(view) ? -rotation : rotation;
            float snappedRotation = SnapAngle(adjustedRotation, MathHelper.ToRadians(2f));
            float renderRotation = snappedRotation;
            float renderScale = 1.05f;
            if (IsRightAngleMultiple(snappedRotation))
            {
                float squareRotation = snappedRotation;
                cellColors = RotateColorGridNearestSquare(cellColors, squareRotation);
                renderRotation = 0f;
                renderScale = 1;
            }

            int renderWidth = cellColors.GetLength(0);
            int renderHeight = cellColors.GetLength(1);
            int squareFitSide = Math.Max(depthMap.Width, depthMap.Height);
            squareFitSide = Math.Max(1, squareFitSide);
            float cellSize = Math.Min(contentWidth / squareFitSide, contentHeight / squareFitSide) * mapScale;
            float mapWidth = renderWidth * cellSize;
            float mapHeight = renderHeight * cellSize;
            float mapX = contentStart + (contentWidth - mapWidth) * 0.5f;
            float mapY = contentTop + (contentHeight - mapHeight) * 0.5f;
            var mapCenter = new Vector2(mapX + mapWidth * 0.5f, mapY + mapHeight * 0.5f);
            float cellWidth = cellSize;
            float cellHeight = cellSize;

            var root = BuildRenderTree(cellColors, 0, 0, renderWidth, renderHeight);
            EmitRenderTree(sprites, root, mapX, mapY, cellWidth, cellHeight, renderHeight, mapCenter, renderRotation,
                renderScale);
            _legendUsedKinds.Clear();
            foreach (var kind in usedKinds)
                _legendUsedKinds.Add(kind);
            _legendHasMissing = hasMissing;
            _legendHasDamaged = hasDamaged;
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

        protected override void DrawFooter(List<MySprite> sprites)
        {
            if (!_legendHasMissing && _legendUsedKinds.Count == 0)
                return;

            float margin = 0f;
            float contentStart = ViewBox.X + margin;
            float contentEnd = ViewBox.X + ViewBox.Width - margin;
            float contentWidth = Math.Max(1f, contentEnd - contentStart);

            float legendTextScale = 0.75f * Scale * FontScale;
            float legendScale = legendTextScale / 0.75f;
            float squareSize = 12f * legendScale;
            float rowHeight = Math.Max(18f * Scale, squareSize + 6f * legendScale);
            float colWidth = 150f * Scale;
            float pad = 6f * legendScale;

            int totalEntries = (_legendHasMissing ? 1 : 0) +
                               (_legendUsedKinds.Contains(CellKind.None) ? 1 : 0) +
                               _colors.Count(pair => _legendUsedKinds.Contains(pair.Key));
            int cols = Math.Max(1, (int)Math.Floor(contentWidth / Math.Max(1f, colWidth)));
            int rows = (int)Math.Ceiling(totalEntries / (float)cols);
            float legendHeight = rows * rowHeight + pad * 2f;
            float legendTop = ViewBox.Bottom - legendHeight;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(contentStart + contentWidth * 0.5f, legendTop + legendHeight * 0.5f),
                Size = new Vector2(contentWidth, legendHeight),
                Color = new Color(BackgroundColor.MulValue(0.8f), 0.5f),
                Alignment = TextAlignment.CENTER
            });

            float x = contentStart + pad;
            float y = ViewBox.Bottom - legendHeight + pad;
            int idx = 0;

            if (_legendUsedKinds.Contains(CellKind.None))
                DrawLegendEntryAt(sprites, idx++, cols, x, y, colWidth, rowHeight, squareSize, Color.Gray,
                    GetLegendCaption(CellKind.None));


            if (_legendHasMissing)
                DrawLegendEntryAt(sprites, idx++, cols, x, y, colWidth, rowHeight, squareSize, _missingColor,
                    LocHelper.GetLoc(MISSING_BLOCK_LOCKEY));

            if (_legendHasDamaged)
                DrawLegendEntryAt(sprites, idx++, cols, x, y, colWidth, rowHeight, squareSize, _damagedColor,
                    LocHelper.GetLoc(DAMAGED_BLOCK_LOCKEY));


            foreach (var pair in _colors)
            {
                if (!_legendUsedKinds.Contains(pair.Key))
                    continue;
                DrawLegendEntryAt(sprites, idx++, cols, x, y, colWidth, rowHeight, squareSize, pair.Value,
                    GetLegendCaption(pair.Key));
            }
        }

        string GetLegendCaption(CellKind kind)
        {
            string key;
            if (_legendCategoryLocKeys.TryGetValue(kind, out key))
                return LocHelper.GetLoc(key);

            return kind.ToString();
        }

        void DrawLegendEntryAt(List<MySprite> sprites, int idx, int cols, float startX, float startY, float colWidth,
            float rowHeight, float squareSize, Color color, string caption)
        {
            int col = idx % cols;
            int row = idx / cols;
            float x = startX + col * colWidth;
            float y = startY + row * rowHeight;
            AddLegendRow(sprites, x, y, colWidth, squareSize, color, caption);
        }

        void AddLegendRow(List<MySprite> sprites, float x, float y, float colWidth, float squareSize, Color color,
            string caption)
        {
            float textScale = 0.75f * Scale * FontScale;
            float layoutScale = textScale / 0.75f;
            float labelX = x + squareSize + 6f * layoutScale;
            float availableTextWidth = Math.Max(0f, colWidth - (labelX - x) - 4f * layoutScale);
            var captionSb = new StringBuilder(caption ?? string.Empty);
            TrimText(ref captionSb, availableTextWidth, 0.75f);
            string trimmedCaption = captionSb.ToString();
            float textHeight = FormatingHelper.GetSizeInPixel(trimmedCaption, "White", textScale, Surface).Y;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(x + squareSize * 0.5f, y + squareSize * 0.5f),
                Size = new Vector2(squareSize, squareSize),
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = trimmedCaption,
                Position = new Vector2(labelX, y + squareSize * 0.5f - textHeight * 0.5f),
                Color = Surface.ScriptForegroundColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White",
                RotationOrScale = textScale
            });
        }

        static Vector2 RotateAround(Vector2 point, Vector2 origin, float rotation)
        {
            if (Math.Abs(rotation) < 0.0001f)
                return point;

            float sin = (float)Math.Sin(rotation);
            float cos = (float)Math.Cos(rotation);
            var delta = point - origin;

            return new Vector2(
                delta.X * cos - delta.Y * sin,
                delta.X * sin + delta.Y * cos
            ) + origin;
        }

        static RenderQuadNode BuildRenderTree(Color?[,] colors, int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0)
                return null;

            Color? first = colors[x, y];
            bool allSame = true;

            for (int ix = x; ix < x + width && allSame; ix++)
            {
                for (int iy = y; iy < y + height; iy++)
                {
                    if (colors[ix, iy] != first)
                    {
                        allSame = false;
                        break;
                    }
                }
            }

            if (allSame)
            {
                if (!first.HasValue)
                    return null;

                return new RenderQuadNode
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    Color = first,
                    Children = null
                };
            }

            if (width == 1 && height == 1)
            {
                if (!first.HasValue)
                    return null;

                return new RenderQuadNode
                {
                    X = x,
                    Y = y,
                    Width = 1,
                    Height = 1,
                    Color = first,
                    Children = null
                };
            }

            int leftWidth = width / 2;
            int rightWidth = width - leftWidth;
            int bottomHeight = height / 2;
            int topHeight = height - bottomHeight;

            var children = new List<RenderQuadNode>(4);
            AddChild(children, BuildRenderTree(colors, x, y, leftWidth, bottomHeight));
            AddChild(children, BuildRenderTree(colors, x + leftWidth, y, rightWidth, bottomHeight));
            AddChild(children, BuildRenderTree(colors, x, y + bottomHeight, leftWidth, topHeight));
            AddChild(children, BuildRenderTree(colors, x + leftWidth, y + bottomHeight, rightWidth, topHeight));

            if (children.Count == 0)
                return null;

            if (children.Count == 1)
                return children[0];

            return new RenderQuadNode
            {
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Children = children.ToArray(),
                Color = null
            };
        }

        static void AddChild(List<RenderQuadNode> children, RenderQuadNode node)
        {
            if (node != null)
                children.Add(node);
        }

        static int EmitRenderTree(
            List<MySprite> sprites,
            RenderQuadNode node,
            float mapX,
            float mapY,
            float cellWidth,
            float cellHeight,
            int totalHeight,
            Vector2 mapCenter,
            float rotation,
            float scale = 1f)
        {
            if (node == null)
                return 0;

            if (node.IsLeaf)
            {
                float drawX = mapX + node.X * cellWidth;
                float drawY = mapY + (totalHeight - (node.Y + node.Height)) * cellHeight;
                float rectWidth = node.Width * cellWidth;
                float rectHeight = node.Height * cellHeight;
                var rectCenter = new Vector2(drawX + rectWidth * 0.5f, drawY + rectHeight * 0.5f);

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = RotateAround(rectCenter, mapCenter, rotation),
                    Size = new Vector2(rectWidth * scale, rectHeight * scale),
                    Color = node.Color,
                    Alignment = TextAlignment.CENTER,
                    RotationOrScale = rotation
                });
                return 1;
            }

            int count = 0;
            for (int i = 0; i < node.Children.Length; i++)
                count += EmitRenderTree(sprites, node.Children[i], mapX, mapY, cellWidth, cellHeight, totalHeight,
                    mapCenter, rotation, scale);

            return count;
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
            if (AppConfig.ReferenceBlock == 0)
            {
                projector = null;
                return;
            }

            if (projector != null && projector.EntityId == AppConfig.ReferenceBlock)
                return;

            var entity = MyAPIGateway.Entities.GetEntityById(AppConfig.ReferenceBlock) as IMyProjector;
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

    class RenderQuadNode
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public Color? Color;
        public RenderQuadNode[] Children;

        public bool IsLeaf => Children == null || Children.Length == 0;
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
