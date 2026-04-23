using System;
using System.Collections.Generic;
using Generated;
using Graph.Apps.Abstract;
using Graph.Extensions;
using Graph.Helpers;
using Graph.System.TerminalControls.Generic;
using Graph.System.TerminalControls.Groups;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using IMyCockpit = Sandbox.ModAPI.IMyCockpit;

namespace Graph.Apps
{
    [MyTextSurfaceScript(ID, TITLE)]
    public partial class ThrustSurfaceScript : SurfaceScriptBase
    {
        public const string ID = "LCDMod_Thrust";
        public const string TITLE = "HelpScreen_JoystickThrust";
        const float AXIS_THICKNESS = 6f;
        const float ARROW_SIZE_MULTIPLIER = 3f;
        const float BASE_OPACITY = 0.25f;
        const float LEGEND_HEIGHT_BASE = 56f;
        const float GRAVITY_ARROW_LENGTH_FACTOR = 0.85f;
        const float GRAVITY_NORMALIZE_MPS2 = 10f;
        const float GRAVITY_LOAD_WARN_THRESHOLD = 0.80f;
        const float GRAVITY_LOAD_CRITICAL_THRESHOLD = 0.95f;
        const float GRAVITY_ERROR_FLASH_SECONDS = 0.5f;
        const int DIR_FORWARD = 0;
        const int DIR_BACKWARD = 1;
        const int DIR_LEFT = 2;
        const int DIR_RIGHT = 3;
        const int DIR_UP = 4;
        const int DIR_DOWN = 5;

        protected override string DefaultTitle => TITLE;

        public ThrustSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size)
        {
        }

        public override void Run()
        {
            base.Run();

            if (Config == null) return;

            var maxThrust = new double[6];
            var curThrust = new double[6];
            bool hasAny = false;

            try
            {
                var grid = Block?.CubeGrid as IMyCubeGrid;
                if (grid != null)
                {
                    var slims = new List<IMySlimBlock>();
                    grid.GetBlocks(slims, b => b.FatBlock is IMyThrust);

                    for (int i = 0; i < slims.Count; i++)
                    {
                        var thr = slims[i].FatBlock as IMyThrust;
                        if (thr == null) continue;
                        if (!thr.Enabled) continue; // turned off
                        if (!thr.IsFunctional) continue; // damaged below functional line
                        if (!thr.IsWorking) continue; // no fuel/power or otherwise unable to provide thrust

                        var pushDir = Base6Directions.GetOppositeDirection(thr.Orientation.Forward);
                        int idx = DirIndex(pushDir);
                        if (idx < 0) continue;

                        double max = 0d;
                        double cur = 0d;
                        max = thr.MaxEffectiveThrust;
                        cur = thr.CurrentThrust;

                        maxThrust[idx] += max;
                        curThrust[idx] += cur;
                        if (max > 0d) hasAny = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"[LCDMod] ThrustGraph error: {ex.Message}");
            }

            if (!hasAny)
            {
                Empty();
                return;
            }

            var fills = new float[6];
            for (int i = 0; i < fills.Length; i++)
            {
                if (maxThrust[i] <= 0d) continue;
                fills[i] = MathHelper.Clamp((float)(curThrust[i] / maxThrust[i]), 0f, 1f);
            }

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                AddBackground(sprites);
                DrawTitle(sprites);
                Vector3D gravityVector;
                float gravityLoad;
                bool hasGravity = TryGetNaturalGravityAndUtilization(maxThrust, out gravityVector, out gravityLoad);
                DrawIsometricAxes(sprites, fills, hasGravity ? (Vector3D?)gravityVector : null, gravityLoad);
                DrawBottomLegend(sprites, maxThrust);
                DrawGravityLoadWarning(sprites, hasGravity, gravityLoad);
                frame.AddRange(sprites);
            }
        }

        void DrawIsometricAxes(List<MySprite> sprites, float[] fills, Vector3D? gravityVector, float gravityLoad)
        {
            float contentTop = CaretY;
            float legendHeight = LEGEND_HEIGHT_BASE * LayoutScale;
            float contentBottom = ViewBox.Bottom - legendHeight;
            float contentHeight = contentBottom - contentTop;
            if (contentHeight <= 0f) return;

            var origin = new Vector2(ViewBox.Center.X, contentTop + (contentHeight * 0.5f));
            float axisLength = Math.Min(ViewBox.Width, contentHeight) * 0.35f;
            float thickness = AXIS_THICKNESS * Scale;
            float arrowSize = thickness * ARROW_SIZE_MULTIPLIER;
            float cos30 = 0.8660254f;
            float sin30 = 0.5f;
            var xDir = new Vector2(cos30, sin30);
            var zDir = new Vector2(cos30, -sin30);
            var blockForward = Block.Orientation.Forward;
            var blockBackward = Base6Directions.GetOppositeDirection(blockForward);
            var blockLeft = Block.Orientation.Left;
            var blockRight = Base6Directions.GetOppositeDirection(blockLeft);
            var blockUp = Block.Orientation.Up;
            var blockDown = Base6Directions.GetOppositeDirection(blockUp);

            DrawAxesPass(sprites, origin, axisLength, thickness, arrowSize, new Vector2(0f, -1f), Color.Green,
                BASE_OPACITY);
            DrawAxesPass(sprites, origin, axisLength, thickness, arrowSize, new Vector2(0f, 1f), Color.Green,
                BASE_OPACITY);
            DrawAxesPass(sprites, origin, axisLength, thickness, arrowSize, zDir, Color.Blue, BASE_OPACITY);
            DrawAxesPass(sprites, origin, axisLength, thickness, arrowSize, -zDir, Color.Blue, BASE_OPACITY);
            DrawAxesPass(sprites, origin, axisLength, thickness, arrowSize, xDir, Color.Red, BASE_OPACITY);
            DrawAxesPass(sprites, origin, axisLength, thickness, arrowSize, -xDir, Color.Red, BASE_OPACITY);

            DrawAxesPass(sprites, origin, axisLength * FillForDirection(fills, blockUp), thickness, arrowSize,
                new Vector2(0f, -1f), Color.Green, 1f);
            DrawAxesPass(sprites, origin, axisLength * FillForDirection(fills, blockDown), thickness, arrowSize,
                new Vector2(0f, 1f), Color.Green, 1f);
            DrawAxesPass(sprites, origin, axisLength * FillForDirection(fills, blockForward), thickness, arrowSize,
                zDir, Color.Blue, 1f);
            DrawAxesPass(sprites, origin, axisLength * FillForDirection(fills, blockBackward), thickness, arrowSize,
                -zDir, Color.Blue, 1f);
            DrawAxesPass(sprites, origin, axisLength * FillForDirection(fills, blockRight), thickness, arrowSize, xDir,
                Color.Red, 1f);
            DrawAxesPass(sprites, origin, axisLength * FillForDirection(fills, blockLeft), thickness, arrowSize, -xDir,
                Color.Red, 1f);

            if (gravityVector.HasValue)
            {
                var gravity = gravityVector.Value;
                double gLenSq = gravity.LengthSquared();
                if (gLenSq > 1e-6)
                {
                    var blockMatrix = Block.WorldMatrix;
                    float gX = (float)Vector3D.Dot(gravity, blockMatrix.Right);
                    float gY = (float)Vector3D.Dot(gravity, blockMatrix.Up);
                    float gZ = (float)Vector3D.Dot(gravity, blockMatrix.Forward);

                    // Project block-local gravity components into the same isometric basis used by axes.
                    Vector2 projected = xDir * gX + new Vector2(0f, -1f) * gY + zDir * gZ;
                    float projectedLen = projected.Length();
                    if (projectedLen > 1e-4f)
                    {
                        projected /= projectedLen;
                        float gravityStrength = (float)Math.Sqrt(gLenSq);
                        float normalizedStrength = MathHelper.Clamp(gravityStrength / GRAVITY_NORMALIZE_MPS2, 0f, 1f);
                        Color gravityColor = gravityLoad >= GRAVITY_LOAD_CRITICAL_THRESHOLD
                            ? BoostAlertColor(Config.ErrorColor)
                            : gravityLoad >= GRAVITY_LOAD_WARN_THRESHOLD
                                ? BoostAlertColor(Config.WarningColor)
                                : ForegroundColor;
                        DrawAxisRay(sprites, origin, projected,
                            axisLength * GRAVITY_ARROW_LENGTH_FACTOR * normalizedStrength,
                            thickness, arrowSize, gravityColor);
                    }
                }
            }
        }

        bool TryGetNaturalGravityAndUtilization(double[] maxThrust, out Vector3D gravity, out float utilization)
        {
            gravity = Vector3D.Zero;
            utilization = 0f;
            var cockpit = ResolveCockpitForGravity();
            if (cockpit == null) return false;

            gravity = cockpit.GetNaturalGravity();
            if (gravity.LengthSquared() <= 1e-6) return false;

            var shipMass = cockpit.CalculateShipMass();
            utilization = ComputeGravityCounterUtilization(maxThrust, gravity, shipMass.PhysicalMass);
            return true;
        }

        IMyCockpit ResolveCockpitForGravity()
        {
            var cockpit = Block as IMyCockpit;
            if (cockpit != null) return cockpit;

            var myGrid = Block.CubeGrid as Sandbox.Game.Entities.MyCubeGrid;
            var mainCockpit = myGrid?.MainCockpit as IMyCockpit;
            if (mainCockpit != null) return mainCockpit;

            return null;
        }

        void DrawGravityLoadWarning(List<MySprite> sprites, bool hasGravity, float gravityLoad)
        {
            if (!hasGravity) return;

            if (gravityLoad >= GRAVITY_LOAD_CRITICAL_THRESHOLD)
            {
                bool visibleThisTick = (GetTimeStep(GRAVITY_ERROR_FLASH_SECONDS) % 2) == 0;
                if (!visibleThisTick) return;

                DrawMessage(sprites,
                    string.Format(LocHelper.GetLoc("LCDMod_Critical"), LocHelper.GetLoc("LCDMod_Thrust_GravityLoad")),
                    "Warning",
                    BoostAlertColor(Config.ErrorColor),
                    0.7f * Config.Scale);
                return;
            }

            if (gravityLoad >= GRAVITY_LOAD_WARN_THRESHOLD)
            {
                DrawMessage(sprites,
                    string.Format(LocHelper.GetLoc("LCDMod_Warning"), LocHelper.GetLoc("LCDMod_Thrust_GravityLoad")),
                    "Warning",
                    BoostAlertColor(Config.WarningColor),
                    0.7f * Config.Scale);
            }
        }

        static Color BoostAlertColor(Color color)
        {
            return color.MulValue(2f).MulSaturation(2f);
        }

        float ComputeGravityCounterUtilization(double[] maxThrust, Vector3D gravity, double shipMass)
        {
            if (maxThrust == null || maxThrust.Length < 6 || shipMass <= 0d) return 0f;

            double gravityMagnitude = gravity.Length();
            if (gravityMagnitude <= 1e-6) return 0f;

            Vector3D antiGravityDir = -gravity / gravityMagnitude;
            var gridMatrix = Block.CubeGrid.WorldMatrix;

            double desiredForward = Vector3D.Dot(antiGravityDir, gridMatrix.Forward);
            double desiredRight = Vector3D.Dot(antiGravityDir, gridMatrix.Right);
            double desiredUp = Vector3D.Dot(antiGravityDir, gridMatrix.Up);

            // Per-axis force availability in the exact anti-gravity sign direction.
            double availableForwardAxis = desiredForward >= 0d ? maxThrust[DIR_FORWARD] : maxThrust[DIR_BACKWARD];
            double availableRightAxis = desiredRight >= 0d ? maxThrust[DIR_RIGHT] : maxThrust[DIR_LEFT];
            double availableUpAxis = desiredUp >= 0d ? maxThrust[DIR_UP] : maxThrust[DIR_DOWN];

            // Resultant along anti-gravity is constrained by the weakest axis contribution (bottleneck).
            const double eps = 1e-6;
            double lambdaMax = double.PositiveInfinity;

            if (Math.Abs(desiredForward) > eps)
                lambdaMax = Math.Min(lambdaMax, availableForwardAxis / Math.Abs(desiredForward));
            if (Math.Abs(desiredRight) > eps)
                lambdaMax = Math.Min(lambdaMax, availableRightAxis / Math.Abs(desiredRight));
            if (Math.Abs(desiredUp) > eps)
                lambdaMax = Math.Min(lambdaMax, availableUpAxis / Math.Abs(desiredUp));

            double availableForce = double.IsInfinity(lambdaMax) ? 0d : Math.Max(0d, lambdaMax);
            if (availableForce <= 1e-3) return 1f;

            double requiredForce = shipMass * gravityMagnitude;
            return (float)(requiredForce / availableForce);
        }

        void DrawBottomLegend(List<MySprite> sprites, double[] maxThrust)
        {
            float legendHeight = LEGEND_HEIGHT_BASE * LayoutScale;
            float margin = 0f;
            float left = ViewBox.X + margin;
            float right = ViewBox.Right - margin;
            float width = Math.Max(1f, right - left);
            float top = ViewBox.Bottom - legendHeight;
            float padX = 6f * Scale;
            float rowH = legendHeight * 0.5f;
            float colW = width / 3f;
            float textScale = 0.46f * Scale * FontScale;
            string forward = LocHelper.GetLoc("Thrust_Forward");
            string backward = LocHelper.GetLoc("Thrust_Back");
            string leftLabel = LocHelper.GetLoc("Thrust_Left");
            string rightLabel = LocHelper.GetLoc("Thrust_Right");
            string up = LocHelper.GetLoc("Thrust_Up");
            string down = LocHelper.GetLoc("Thrust_Down");

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(left + width * 0.5f, top + legendHeight * 0.5f),
                Size = new Vector2(width, legendHeight),
                Color = new Color(BackgroundColor.MulValue(0.8f), 0.5f),
                Alignment = TextAlignment.CENTER
            });

            DrawLegendCell(sprites, left, top, colW, rowH, padX, forward,
                maxThrust[DIR_FORWARD], Color.Blue, ForegroundColor, textScale);
            DrawLegendCell(sprites, left + colW, top, colW, rowH, padX, leftLabel, maxThrust[DIR_LEFT],
                Color.Red, ForegroundColor, textScale);
            DrawLegendCell(sprites, left + 2f * colW, top, colW, rowH, padX, up, maxThrust[DIR_UP],
                Color.Green, ForegroundColor, textScale);

            DrawLegendCell(sprites, left, top + rowH, colW, rowH, padX, backward,
                maxThrust[DIR_BACKWARD], Color.Blue, ForegroundColor, textScale);
            DrawLegendCell(sprites, left + colW, top + rowH, colW, rowH, padX, rightLabel,
                maxThrust[DIR_RIGHT], Color.Red, ForegroundColor, textScale);
            DrawLegendCell(sprites, left + 2f * colW, top + rowH, colW, rowH, padX, down, maxThrust[DIR_DOWN],
                Color.Green, ForegroundColor, textScale);
        }

        static void DrawLegendCell(List<MySprite> sprites, float x, float y, float w, float h, float padX,
            string label, double maxValue, Color markerColor, Color textColor, float textScale)
        {
            float yPos = y + (h - 14f * textScale) * 0.5f;
            string valueText = FormatingHelper.NewtonForceToString(maxValue);
            float markerSize = 8f * textScale / 0.46f;
            float markerCenterY = y + h * 0.5f;
            float textLeftX = x + padX + markerSize + 4f * ScaleFromText(textScale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(x + padX + markerSize, markerCenterY + markerSize * 0.5f),
                Size = new Vector2(markerSize),
                Color = markerColor,
                Alignment = TextAlignment.RIGHT
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = label + ":",
                Position = new Vector2(textLeftX, yPos),
                Color = textColor,
                Alignment = TextAlignment.LEFT,
                FontId = "White",
                RotationOrScale = textScale
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = valueText,
                Position = new Vector2(x + w - padX, yPos),
                Color = textColor,
                Alignment = TextAlignment.RIGHT,
                FontId = "White",
                RotationOrScale = textScale
            });
        }

        static float ScaleFromText(float textScale)
        {
            return textScale / 0.46f;
        }

        static float SafeFill(float[] fills, int idx)
        {
            if (fills == null || idx < 0 || idx >= fills.Length) return 0f;
            return MathHelper.Clamp(fills[idx], 0f, 1f);
        }

        static float FillForDirection(float[] fills, Base6Directions.Direction direction)
        {
            return SafeFill(fills, DirIndex(direction));
        }

        void DrawAxesPass(List<MySprite> sprites, Vector2 origin, float length, float thickness, float arrowSize,
            Vector2 direction, Color color, float opacity)
        {
            if (length <= 0f) return;

            DrawAxisRay(sprites, origin, direction, length, thickness, arrowSize, WithOpacity(color, opacity));
        }

        static Color WithOpacity(Color color, float opacity)
        {
            float a = MathHelper.Clamp(opacity, 0f, 1f);
            return new Color(color.R, color.G, color.B, (byte)(255f * a));
        }

        static void DrawAxisRay(List<MySprite> sprites, Vector2 origin, Vector2 direction, float length, float width,
            float arrowSize, Color color)
        {
            float dirLength = direction.Length();
            if (dirLength <= 0f) return;
            direction /= dirLength;

            Vector2 tip = origin + (direction * length);
            DrawLine(sprites, origin, tip, width, color);
            Vector2 arrowPosition = tip + (direction * (arrowSize * 0.5f));
            DrawArrowTip(sprites, arrowPosition, direction, arrowSize, color);
        }

        static void DrawArrowTip(List<MySprite> sprites, Vector2 position, Vector2 direction, float size, Color color)
        {
            float angle = (float)Math.Atan2(direction.Y, direction.X);
            float rotation = angle + MathHelper.PiOver2;

            sprites.Add(new MySprite(SpriteType.TEXTURE, "Triangle", position, new Vector2(size, size), color, null,
                TextAlignment.CENTER, rotation));
        }

        static void DrawLine(List<MySprite> sprites, Vector2 point1, Vector2 point2, float width, Color color)
        {
            Vector2 position = 0.5f * (point1 + point2);
            Vector2 diff = point1 - point2;
            float length = diff.Length();
            if (length <= 0f) return;

            diff /= length;
            float angle = (float)Math.Acos(Vector2.Dot(diff, Vector2.UnitX));
            angle *= Math.Sign(Vector2.Dot(diff, Vector2.UnitY));

            sprites.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", position, new Vector2(length, width), color,
                null, TextAlignment.CENTER, angle));
        }

        static int DirIndex(Base6Directions.Direction dir)
        {
            switch (dir)
            {
                case Base6Directions.Direction.Forward: return DIR_FORWARD;
                case Base6Directions.Direction.Backward: return DIR_BACKWARD;
                case Base6Directions.Direction.Left: return DIR_LEFT;
                case Base6Directions.Direction.Right: return DIR_RIGHT;
                case Base6Directions.Direction.Up: return DIR_UP;
                case Base6Directions.Direction.Down: return DIR_DOWN;
                default: return -1;
            }
        }
    }
}
