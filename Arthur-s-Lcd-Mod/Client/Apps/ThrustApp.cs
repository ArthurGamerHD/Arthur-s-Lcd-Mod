using LcdMod.Common.Config.Components;
using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Extensions;
using LcdMod.Client.GridData;
using LcdMod.Client.Helpers;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using static LcdMod.Common.Helpers.Constants;
using IMyCockpit = Sandbox.ModAPI.IMyCockpit;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

using LcdMod.Common.Config.Generation;
namespace LcdMod.Client.Apps
{
    [LcdApp(22)]
    // ReSharper disable once PartialTypeWithSinglePart
    internal sealed partial class ThrustApp : App
    {
        const float GRAVITY_LOAD_WARN_THRESHOLD = 0.80f;
        const float GRAVITY_LOAD_CRITICAL_THRESHOLD = 0.95f;
        const float GRAVITY_ERROR_FLASH_SECONDS = 0.5f;
        const double STOP_SPEED_EPSILON = 5e-2d;
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
        readonly Thrust.ThrustViewModel _viewModel;
        readonly Func<string, float, float, string> _trimTelemetryText;

        IMyCubeBlock Block => Host.Block;
        IMyTextSurface Surface => Host.Surface;
        float Scale => GeneralComponent.GetScale();
        internal override float WindowRenderScaling => Scale > 0f ? Scale : 1f;
        float FontScale => Host.Surface.FontSize;
        Color ForegroundColor => Host.ForegroundColor;
        Color BackgroundColor => Host.BackgroundColor;

        public bool HasData { get; private set; }

        public ThrustApp(IAppHost host)
            : base(host)
        {
            _viewModel = new Thrust.ThrustViewModel();
            _trimTelemetryText = TrimText;
            SetContent(new Thrust.ThrustView(_viewModel));
            NeedGridData(GridCapability.Blocks);
            RefreshPresentationLayout();
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
                var gridLogic = Host.GridLogic;
                if (gridLogic != null)
                {
                    var thrusts = gridLogic.Blocks.Thrusts;

                    for (int i = 0; i < thrusts.Count; i++)
                    {
                        var thr = thrusts[i];
                        if (thr == null) continue;
                        if (!thr.Enabled) continue;
                        if (!thr.IsFunctional) continue;
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
            {
                RefreshPresentation();
                return;
            }
            
            for (int i = 0; i < _fills.Length; i++)
            {
                if (_maxThrust[i] <= 0d) continue;
                _fills[i] = MathHelper.Clamp((float)(_curThrust[i] / _maxThrust[i]), 0f, 1f);
            }

            _hasGravity = TryGetNaturalGravityAndUtilization(_maxThrust, out _gravityVector, out _gravityLoad);
            UpdateStopEstimate(_maxThrust);
            RefreshPresentation();
        }

        public override void LayoutChanged()
        {
            base.LayoutChanged();
            RefreshPresentationLayout();
            RefreshPresentation();
        }

        string _gravityWarning;
        string _gravityCritical;
        Color _warningColor;
        Color _criticalColor;

        void RefreshPresentationLayout()
        {
            // The footer names come from the view; only the localized colon is passed, kept apart so a trimmed
            // name still shows it. Every shipped format puts the name first.
            string labelFormat = LocHelper.GetLoc(MOD_PREFIX + "Common_Label_WithColon");
            int nameIndex = labelFormat.IndexOf("{0}", StringComparison.Ordinal);
            string labelSuffix = nameIndex < 0 ? labelFormat : labelFormat.Substring(nameIndex + 3);
            _viewModel.RefreshLayout(1f, FontScale, TextFont, ForegroundColor,
                new Color(BackgroundColor.MulValue(0.8f), 0.5f), labelSuffix);
            string gravityLabel = LocHelper.GetLoc(MOD_PREFIX + "Thrust_GravityLoad");
            _gravityWarning = string.Format(LocHelper.GetLoc(MOD_PREFIX + "Warning"), gravityLabel);
            _gravityCritical = string.Format(LocHelper.GetLoc(MOD_PREFIX + "Critical"), gravityLabel);
            _warningColor = BoostAlertColor(ColorComponent.ResolveWarningColor());
            _criticalColor = BoostAlertColor(ColorComponent.ResolveErrorColor());
        }

        void RefreshPresentation()
        {
            _viewModel.Forward.UpdateValue(FormatingHelper.NewtonForceToString(_maxThrust[DIR_FORWARD]));
            _viewModel.Backward.UpdateValue(FormatingHelper.NewtonForceToString(_maxThrust[DIR_BACKWARD]));
            _viewModel.Left.UpdateValue(FormatingHelper.NewtonForceToString(_maxThrust[DIR_LEFT]));
            _viewModel.Right.UpdateValue(FormatingHelper.NewtonForceToString(_maxThrust[DIR_RIGHT]));
            _viewModel.Up.UpdateValue(FormatingHelper.NewtonForceToString(_maxThrust[DIR_UP]));
            _viewModel.Down.UpdateValue(FormatingHelper.NewtonForceToString(_maxThrust[DIR_DOWN]));
            _viewModel.Speed.UpdateMeasuredValue(_stopSpeed.ToString("0", FormatingHelper.Culture), _trimTelemetryText);
            _viewModel.StopDistance.UpdateMeasuredValue(FormatStopDistance(), _trimTelemetryText);
            _viewModel.StopTime.UpdateMeasuredValue(FormatStopTimeValue(), _trimTelemetryText);
            _viewModel.StopForce.UpdateMeasuredValue(FormatStopForce(), _trimTelemetryText);
            _viewModel.UpdateTelemetryOpacity(_stopSpeed);

            Vector3 positive = Vector3.Zero, negative = Vector3.Zero, gravity = Vector3.Zero;
            if (Block != null)
            {
                var orientation = Block.Orientation;
                positive = new Vector3(
                    FillForDirection(_fills, Base6Directions.GetOppositeDirection(orientation.Left)),
                    FillForDirection(_fills, orientation.Up),
                    FillForDirection(_fills, orientation.Forward));
                negative = new Vector3(
                    FillForDirection(_fills, orientation.Left),
                    FillForDirection(_fills, Base6Directions.GetOppositeDirection(orientation.Up)),
                    FillForDirection(_fills, Base6Directions.GetOppositeDirection(orientation.Forward)));
                if (_hasGravity)
                {
                    var matrix = Block.WorldMatrix;
                    gravity = new Vector3((float)Vector3D.Dot(_gravityVector, matrix.Right),
                        (float)Vector3D.Dot(_gravityVector, matrix.Up),
                        (float)Vector3D.Dot(_gravityVector, matrix.Forward));
                }
            }
            bool critical = _hasGravity && _gravityLoad >= GRAVITY_LOAD_CRITICAL_THRESHOLD;
            bool warning = _hasGravity && _gravityLoad >= GRAVITY_LOAD_WARN_THRESHOLD;
            Color color = critical ? _criticalColor : warning ? _warningColor : ForegroundColor;
            _viewModel.UpdateAxes(positive, negative, gravity, color);
            bool showWarning = warning && (!critical || GetTimeStep(GRAVITY_ERROR_FLASH_SECONDS) % 2 == 0);
            _viewModel.UpdateWarning(showWarning ? (critical ? _gravityCritical : _gravityWarning) : string.Empty, color);
        }

        // The host renders Content through ADK; no legacy whole-app sprite path remains.
        public override List<MySprite> GetSprites() { return new List<MySprite>(); }

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

        static Color BoostAlertColor(Color color)
        {
            return color.MulValue(2f).MulSaturation(2f);
        }

        static int GetTimeStep(float secondsPerStep)
        {
            try
            {
                var session = MyAPIGateway.Session;
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

            double mass;
            if (!TryGetShipMass(out mass))
                return;

            Vector3D gravity;
            if (!TryGetGravityAcceleration(out gravity))
                gravity = Vector3D.Zero;

            var matrix = grid.WorldMatrix;
            double seconds, distance, force;
            if (!Common.Physics.DampenerStop.Simulate(Local(velocity, matrix), Local(gravity, matrix), mass,
                    new[] { maxThrust[DIR_FORWARD], maxThrust[DIR_RIGHT], maxThrust[DIR_UP] },
                    new[] { maxThrust[DIR_BACKWARD], maxThrust[DIR_LEFT], maxThrust[DIR_DOWN] },
                    out seconds, out distance, out force))
                return;

            _stopNetForce = force;
            _stopSeconds = seconds;
            _stopDistance = distance;
            _hasStopEstimate = true;
        }

        static double[] Local(Vector3D vector, MatrixD matrix)
        {
            return new[]
            {
                Vector3D.Dot(vector, matrix.Forward), Vector3D.Dot(vector, matrix.Right), Vector3D.Dot(vector, matrix.Up)
            };
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

        static float SafeFill(float[] fills, int idx)
        {
            if (fills == null || idx < 0 || idx >= fills.Length) return 0f;
            return MathHelper.Clamp(fills[idx], 0f, 1f);
        }

        static float FillForDirection(float[] fills, Base6Directions.Direction direction)
        {
            return SafeFill(fills, DirIndex(direction));
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
