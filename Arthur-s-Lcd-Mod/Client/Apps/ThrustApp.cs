using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using System.Text;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui;
using LcdMod.Client.Helpers;
using LcdMod.Common.Config.Models;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using IMyCockpit = Sandbox.ModAPI.IMyCockpit;


using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(22)]
    internal sealed partial class ThrustApp : App
    {
        const float AXIS_THICKNESS = 6f;
        const float ARROW_SIZE_MULTIPLIER = 3f;
        const float BASE_OPACITY = 0.25f;
        const float LEGEND_HEIGHT_BASE = 56f;
        const float TELEMETRY_PANEL_WIDTH_BASE = 122f;
        const float TELEMETRY_CELL_HEIGHT_BASE = 52f;
        const float TELEMETRY_PANEL_GAP_BASE = 8f;
        const float TELEMETRY_PANEL_MARGIN_BASE = 8f;
        const float GRAVITY_ARROW_LENGTH_FACTOR = 0.85f;
        const float GRAVITY_NORMALIZE_MPS2 = 10f;
        const float GRAVITY_LOAD_WARN_THRESHOLD = 0.80f;
        const float GRAVITY_LOAD_CRITICAL_THRESHOLD = 0.95f;
        const float GRAVITY_ERROR_FLASH_SECONDS = 0.5f;
        const double STOP_SPEED_EPSILON = 5e-2d;
        const double STOP_FORCE_EPSILON = 1e-3d;
        const double STOP_ACCELERATION_EPSILON = 1e-3d;
        const int DIR_FORWARD = 0;
        const int DIR_BACKWARD = 1;
        const int DIR_LEFT = 2;
        const int DIR_RIGHT = 3;
        const int DIR_UP = 4;
        const int DIR_DOWN = 5;

        readonly double[] _maxThrust = new double[6];
        readonly double[] _curThrust = new double[6];
        readonly float[] _fills = new float[6];
        Vector3D _gravityVector;
        float _gravityLoad;
        bool _hasGravity;
        double _stopSpeed;
        double _stopNetForce;
        double _stopSeconds;
        double _stopDistance;
        bool _hasStopEstimate;

        IMyCubeBlock Block => Host.Block;
        Sandbox.ModAPI.Ingame.IMyTextSurface Surface => Host.Surface;
        RectangleF ViewBox => Host.ViewBox;
        float Scale => GeneralComponent.GetScale();
        float FontScale => Host.Surface.FontSize;
        float LayoutScale => Scale * FontScale;
        Color ForegroundColor => Host.ForegroundColor;
        Color BackgroundColor => Host.BackgroundColor;
        float ContentTop => Host.TitleVisible ? Host.ViewBox.Y + 40f * LayoutScale : Host.ViewBox.Y;

        public bool HasData { get; private set; }

        public ThrustApp(IAppHost host)
            : base(host)
        {
        }

        public override void Update()
        {
            Array.Clear(_maxThrust, 0, _maxThrust.Length);
            Array.Clear(_curThrust, 0, _curThrust.Length);
            Array.Clear(_fills, 0, _fills.Length);
            HasData = false;
            _hasGravity = false;
            _hasStopEstimate = false;
            _stopSpeed = 0d;
            _stopNetForce = 0d;
            _stopSeconds = 0d;
            _stopDistance = 0d;

            try
            {
                var grid = Block?.CubeGrid;
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

                        double max;
                        double cur;
                        max = thr.MaxEffectiveThrust;
                        cur = thr.CurrentThrust;

                        _maxThrust[idx] += max;
                        _curThrust[idx] += cur;
                        if (max > 0d) HasData = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"[LcdMod] ThrustGraph error: {ex.Message}");
            }

            if (!HasData)
                return;
            
            for (int i = 0; i < _fills.Length; i++)
            {
                if (_maxThrust[i] <= 0d) continue;
                _fills[i] = MathHelper.Clamp((float)(_curThrust[i] / _maxThrust[i]), 0f, 1f);
            }

            _hasGravity = TryGetNaturalGravityAndUtilization(_maxThrust, out _gravityVector, out _gravityLoad);
            UpdateStopEstimate(_maxThrust);
        }

        public override List<MySprite> GetSprites()
        {
            var sprites = new List<MySprite>();
            DrawIsometricAxes(sprites, _fills, _hasGravity ? (Vector3D?)_gravityVector : null, _gravityLoad);
            DrawTelemetrySidePanels(sprites);
            DrawBottomLegend(sprites, _maxThrust);
            DrawGravityLoadWarning(sprites, _hasGravity, _gravityLoad);
            return sprites;
        }

        void DrawIsometricAxes(List<MySprite> sprites, float[] fills, Vector3D? gravityVector, float gravityLoad)
        {
            float contentTop = ContentTop;
            float legendHeight = GetBottomPanelHeight();
            float contentBottom = ViewBox.Bottom - legendHeight;
            float contentHeight = contentBottom - contentTop;
            if (contentHeight <= 0f) return;

            var origin = new Vector2(ViewBox.Center.X, contentTop + (contentHeight * 0.5f));
            float visualWidth = Math.Max(1f, ViewBox.Width - (GetTelemetryPanelWidth() + GetTelemetryPanelMargin()) * 2f);
            float axisLength = Math.Min(visualWidth, contentHeight) * 0.35f;
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
                            ? BoostAlertColor(ColorComponent.ResolveErrorColor())
                            : gravityLoad >= GRAVITY_LOAD_WARN_THRESHOLD
                                ? BoostAlertColor(ColorComponent.ResolveWarningColor())
                                : ForegroundColor;
                        DrawAxisRay(sprites, origin, projected,
                            axisLength * GRAVITY_ARROW_LENGTH_FACTOR * normalizedStrength,
                            thickness, arrowSize, gravityColor);
                    }
                }
            }
        }

        float GetBottomPanelHeight()
        {
            return LEGEND_HEIGHT_BASE * LayoutScale;
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
                    string.Format(LocHelper.GetLoc(MOD_PREFIX + "Critical"), LocHelper.GetLoc(MOD_PREFIX + "Thrust_GravityLoad")),
                    BoostAlertColor(ColorComponent.ResolveErrorColor()),
                    0.7f * GeneralComponent.GetScale());
                return;
            }

            if (gravityLoad >= GRAVITY_LOAD_WARN_THRESHOLD)
            {
                DrawMessage(sprites,
                    string.Format(LocHelper.GetLoc(MOD_PREFIX + "Warning"), LocHelper.GetLoc(MOD_PREFIX + "Thrust_GravityLoad")),
                    BoostAlertColor(ColorComponent.ResolveWarningColor()),
                    0.7f * GeneralComponent.GetScale());
            }
        }

        static Color BoostAlertColor(Color color)
        {
            return color.MulValue(2f).MulSaturation(2f);
        }

        void DrawMessage(List<MySprite> sprites, string message, Color color, float scale)
        {
            var textScale = Math.Max(0.1f, scale) * FontScale;
            var size = Surface.MeasureStringInPixels(new StringBuilder(message), TextFont, textScale);
            var center = new Vector2(ViewBox.Center.X, (ContentTop + ViewBox.Bottom) * 0.5f);
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = message,
                Position = new Vector2(center.X, center.Y - size.Y * 0.5f),
                RotationOrScale = textScale,
                Color = color,
                Alignment = TextAlignment.CENTER,
                FontId = TextFont
            });
        }

        static int GetTimeStep(float secondsPerStep)
        {
            try
            {
                var session = MyAPIGateway.Session;
                if (session == null)
                    return 0;

                if (secondsPerStep <= 0f)
                    secondsPerStep = 1f / 60f;

                var ticksPerStep = Math.Max(1, (int)Math.Round(secondsPerStep * 60f));
                return session.GameplayFrameCounter / ticksPerStep;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine($"[LcdMod] ThrustGraph GetTimeStep error: {ex.Message}");
                return 0;
            }
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
            double availableForwardAxis = GetAxisForce(maxThrust,
                desiredForward >= 0d ? DIR_FORWARD : DIR_BACKWARD);
            double availableRightAxis = GetAxisForce(maxThrust,
                desiredRight >= 0d ? DIR_RIGHT : DIR_LEFT);
            double availableUpAxis = GetAxisForce(maxThrust,
                desiredUp >= 0d ? DIR_UP : DIR_DOWN);

            double availableForce = ComputeAxisConstrainedForce(
                desiredForward,
                desiredRight,
                desiredUp,
                availableForwardAxis,
                availableRightAxis,
                availableUpAxis);
            if (availableForce <= 1e-3) return 1f;

            double requiredForce = shipMass * gravityMagnitude;
            return (float)(requiredForce / availableForce);
        }

        void UpdateStopEstimate(double[] maxThrust)
        {
            var grid = Block?.CubeGrid;
            if (grid == null) return;

            Vector3D velocity = grid.LinearVelocity;
            _stopSpeed = velocity.Length();
            if (_stopSpeed <= STOP_SPEED_EPSILON)
            {
                _hasStopEstimate = true;
                _stopSeconds = 0d;
                _stopNetForce = 0d;
                _stopDistance = 0d;
                return;
            }

            Vector3D brakeDirection = -velocity / _stopSpeed;
            double opposingThrust = ComputeAvailableForceInDirection(maxThrust, brakeDirection);
            if (opposingThrust <= STOP_FORCE_EPSILON)
                return;

            double mass;
            if (!TryGetShipMass(out mass))
                return;

            Vector3D gravity;
            double gravityDeceleration = TryGetGravityAcceleration(out gravity)
                ? Vector3D.Dot(gravity, brakeDirection)
                : 0d;

            double netDeceleration = opposingThrust / mass + gravityDeceleration;
            if (netDeceleration <= STOP_ACCELERATION_EPSILON)
                return;

            _stopNetForce = netDeceleration * mass;
            _stopSeconds = _stopSpeed / netDeceleration;
            _stopDistance = _stopSpeed * _stopSpeed / (2d * netDeceleration);
            _hasStopEstimate = true;
        }

        bool TryGetGravityAcceleration(out Vector3D gravity)
        {
            gravity = Vector3D.Zero;

            var cockpit = ResolveCockpitForGravity();
            if (cockpit != null)
            {
                gravity = cockpit.GetTotalGravity();
                return gravity.LengthSquared() > 1e-8;
            }

            var grid = Block?.CubeGrid;
            if (grid == null)
                return false;

            gravity = grid.NaturalGravity;
            return gravity.LengthSquared() > 1e-8;
        }

        bool TryGetShipMass(out double mass)
        {
            mass = 0d;

            var cockpit = ResolveCockpitForGravity();
            if (cockpit != null)
            {
                var shipMass = cockpit.CalculateShipMass();
                mass = shipMass.PhysicalMass;
            }

            if (mass <= 0d)
            {
                var grid = Block?.CubeGrid;
                if (grid != null && grid.Physics != null)
                    mass = grid.Physics.Mass;
            }

            return mass > 0d;
        }

        double ComputeAvailableForceInDirection(
            double[] maxThrust,
            Vector3D desiredWorldDirection)
        {
            if (maxThrust == null || maxThrust.Length < 6 || desiredWorldDirection.LengthSquared() <= 1e-8)
                return 0d;

            var grid = Block?.CubeGrid;
            if (grid == null) return 0d;

            desiredWorldDirection.Normalize();
            var gridMatrix = grid.WorldMatrix;

            double desiredForward = Vector3D.Dot(desiredWorldDirection, gridMatrix.Forward);
            double desiredRight = Vector3D.Dot(desiredWorldDirection, gridMatrix.Right);
            double desiredUp = Vector3D.Dot(desiredWorldDirection, gridMatrix.Up);

            double availableForwardAxis = GetAxisForce(maxThrust,
                desiredForward >= 0d ? DIR_FORWARD : DIR_BACKWARD);
            double availableRightAxis = GetAxisForce(maxThrust,
                desiredRight >= 0d ? DIR_RIGHT : DIR_LEFT);
            double availableUpAxis = GetAxisForce(maxThrust,
                desiredUp >= 0d ? DIR_UP : DIR_DOWN);

            return ComputeAxisConstrainedForce(
                desiredForward,
                desiredRight,
                desiredUp,
                availableForwardAxis,
                availableRightAxis,
                availableUpAxis);
        }

        static double ComputeAxisConstrainedForce(
            double desiredForward,
            double desiredRight,
            double desiredUp,
            double availableForwardAxis,
            double availableRightAxis,
            double availableUpAxis)
        {
            // Resultant force along a requested direction is limited by the weakest axis contribution.
            const double eps = 1e-6;
            double lambdaMax = double.PositiveInfinity;

            if (Math.Abs(desiredForward) > eps)
                lambdaMax = Math.Min(lambdaMax, availableForwardAxis / Math.Abs(desiredForward));
            if (Math.Abs(desiredRight) > eps)
                lambdaMax = Math.Min(lambdaMax, availableRightAxis / Math.Abs(desiredRight));
            if (Math.Abs(desiredUp) > eps)
                lambdaMax = Math.Min(lambdaMax, availableUpAxis / Math.Abs(desiredUp));

            return double.IsInfinity(lambdaMax) ? 0d : Math.Max(0d, lambdaMax);
        }

        static double GetAxisForce(double[] maxThrust, int index)
        {
            if (maxThrust == null || index < 0 || index >= maxThrust.Length)
                return 0d;

            return Math.Max(0d, maxThrust[index]);
        }

        void DrawBottomLegend(List<MySprite> sprites, double[] maxThrust)
        {
            float legendRowsHeight = LEGEND_HEIGHT_BASE * LayoutScale;
            float margin = 0f;
            float left = ViewBox.X + margin;
            float right = ViewBox.Right - margin;
            float width = Math.Max(1f, right - left);
            float top = ViewBox.Bottom - legendRowsHeight;
            float padX = 6f * Scale;
            float rowH = legendRowsHeight * 0.5f;
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
                Position = new Vector2(left + width * 0.5f, top + legendRowsHeight * 0.5f),
                Size = new Vector2(width, legendRowsHeight),
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

        void DrawTelemetrySidePanels(List<MySprite> sprites)
        {
            float contentTop = ContentTop;
            float contentBottom = ViewBox.Bottom - GetBottomPanelHeight();
            float contentHeight = contentBottom - contentTop;
            if (contentHeight <= 0f)
                return;

            float panelWidth = GetTelemetryPanelWidth();
            float margin = GetTelemetryPanelMargin();
            float gap = TELEMETRY_PANEL_GAP_BASE * LayoutScale;
            float cellHeight = Math.Min(
                TELEMETRY_CELL_HEIGHT_BASE * LayoutScale,
                Math.Max(30f * Scale, (contentHeight - gap) * 0.36f));
            float panelHeight = cellHeight * 2f + gap;
            if (panelHeight <= 0f || panelWidth <= 0f)
                return;

            float top = contentTop + Math.Max(0f, (contentHeight - panelHeight) * 0.5f);
            float leftX = ViewBox.X + margin;
            float rightX = ViewBox.Right - margin - panelWidth;

            if(_stopSpeed == 0)
                return;

            DrawTelemetryCell(sprites, new RectangleF(leftX, top, panelWidth, cellHeight), _stopSpeed.ToString("0", FormatingHelper.Culture));
            DrawTelemetryCell(sprites, new RectangleF(leftX, top + cellHeight + gap, panelWidth, cellHeight), FormatStopDistance());
            DrawTelemetryCell(sprites, new RectangleF(rightX, top, panelWidth, cellHeight), FormatStopTimeValue());
            DrawTelemetryCell(sprites, new RectangleF(rightX, top + cellHeight + gap, panelWidth, cellHeight), FormatStopForce());
        }

        float GetTelemetryPanelWidth()
        {
            return Math.Min(TELEMETRY_PANEL_WIDTH_BASE * LayoutScale, ViewBox.Width * 0.25f);
        }

        float GetTelemetryPanelMargin()
        {
            return TELEMETRY_PANEL_MARGIN_BASE * Scale;
        }

        void DrawTelemetryCell(List<MySprite> sprites, RectangleF rect, string value)
        {
            float hudScale = Math.Max(0.1f, Scale);
            float valueScale = hudScale;
            float textOffset = 5f * hudScale;
            var boxSize = new Vector2(89f, 32f) * hudScale;
            var rightCenter = rect.Center + new Vector2(boxSize.X * 0.5f, 0f);
            var valueText = TrimText(value, boxSize.X - textOffset - 4f * hudScale, valueScale);
            var valueSize = FormatingHelper.GetSizeInPixel(valueText, TextFont, valueScale, Surface);

            var color = _stopSpeed > 1 ? ForegroundColor : new Color(ForegroundColor, (float)_stopSpeed);
            
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "AH_TextBox",
                Position = rightCenter,
                Size = boxSize,
                Color = color,
                Alignment = TextAlignment.RIGHT
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = valueText,
                Position = new Vector2(rightCenter.X - textOffset, rect.Center.Y - valueSize.Y * 0.5f),
                Size = boxSize,
                Color = color,
                Alignment = TextAlignment.RIGHT,
                FontId = TextFont,
                RotationOrScale = valueScale
            });
        }

        string TrimText(string value, float availableWidth, float textScale)
        {
            if (string.IsNullOrEmpty(value) || availableWidth <= 0f || Surface == null)
                return string.Empty;

            var size = FormatingHelper.GetSizeInPixel(value, TextFont, textScale, Surface);
            if (size.X <= availableWidth)
                return value;

            return FormatingHelper.TrimName(value,
                Math.Max(1, (int)(value.Length * availableWidth / Math.Max(1f, size.X))));
        }

        string FormatStopDistance() => _hasStopEstimate ? FormatingHelper.DistanceToString((float)_stopDistance, "0") : LocHelper.GetLoc(MOD_PREFIX + "Common_Value_Unavailable");

        string FormatStopTimeValue() => _hasStopEstimate ? FormatStopTime(_stopSeconds) : LocHelper.GetLoc(MOD_PREFIX + "Common_Value_Unavailable");

        string FormatStopForce() => _hasStopEstimate ? FormatingHelper.NewtonForceToString(_stopNetForce, "0") : LocHelper.GetLoc(MOD_PREFIX + "Common_Value_Unavailable");
        
        static string FormatStopTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                return LocHelper.GetLoc(MOD_PREFIX + "Common_Value_Unavailable");

            if (seconds <= 0d)
                return LocHelper.GetLoc(MOD_PREFIX + "Common_Time_ZeroSeconds");

            return seconds < 1d
                ? LocHelper.GetLoc(MOD_PREFIX + "Common_Time_LessThanOneSecond")
                : FormatingHelper.FormatTimeHours((float)(seconds / 3600d));
        }

        void DrawLegendCell(List<MySprite> sprites, float x, float y, float w, float h, float padX,
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
                Data = string.Format(FormatingHelper.Culture, LocHelper.GetLoc(MOD_PREFIX + "Common_Label_WithColon"), label),
                Position = new Vector2(textLeftX, yPos),
                Color = textColor,
                Alignment = TextAlignment.LEFT,
                FontId = TextFont,
                RotationOrScale = textScale
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = valueText,
                Position = new Vector2(x + w - padX, yPos),
                Color = textColor,
                Alignment = TextAlignment.RIGHT,
                FontId = TextFont,
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

        // todo: convert to interactive app
        public override IReadOnlyList<Control> VisualChildren { get; } = new Control[]{};
    }
}
