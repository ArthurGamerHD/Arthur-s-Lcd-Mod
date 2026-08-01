using System;
using System.Collections.Generic;
using LcdMod.Client.Animation;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Camera
{
    /// <summary>
    /// Invisible interaction surface that turns secondary-button drags into a
    /// stable yaw/pitch orbit offset. Applications provide their own current
    /// target direction and up reference, then request the resulting camera basis.
    /// </summary>
    public sealed class OrbitCameraControl : RectangleControl
    {
        const float DEFAULT_SENSITIVITY_RADIANS_PER_PIXEL = 0.009f;
        const float DEFAULT_ZOOM_STEP = 1.1f;
        const float ORBIT_INERTIA_DECAY_PER_FRAME = 0.9f;
        const float ZOOM_INERTIA_DECAY_PER_FRAME = 0.88f;
        const float ZOOM_VELOCITY_IMPULSE = 0.09f;
        const float STOP_ORBIT_VELOCITY_RADIANS_PER_FRAME = 0.00008f;
        const float STOP_ZOOM_VELOCITY_LOG_PER_FRAME = 0.0006f;
        const float MAXIMUM_ORBIT_VELOCITY_RADIANS_PER_FRAME = 0.35f;
        const float MAXIMUM_ZOOM_VELOCITY_LOG_PER_FRAME = 0.09f;
        const long MAXIMUM_ORBIT_VELOCITY_SAMPLE_AGE_FRAMES = 1L;
        const string ORBIT_INERTIA_CHANNEL = "OrbitCameraOrbitInertia";
        const string ZOOM_INERTIA_CHANNEL = "OrbitCameraZoomInertia";
        const float MAXIMUM_PITCH_RADIANS = 1.553343f; // 89 degrees
        const float VECTOR_EPSILON = 0.000001f;
        const float ZOOM_VALUE_EPSILON = 0.0001f;

        float _yawRadians;
        float _pitchRadians;
        Vector2 _orbitVelocityRadiansPerFrame;
        float _zoomVelocityLogPerFrame;
        long _lastOrbitVelocityFrame = long.MinValue;
        bool _secondaryOrbitDragActive;
        AnimationHandle _orbitInertiaAnimation;
        AnimationHandle _zoomInertiaAnimation;

        sealed class OrbitInertiaKeyframe : IAnimationStep
        {
            readonly OrbitCameraControl _owner;
            Vector2 _startOrbit;
            Vector2 _startVelocity;
            int _yawDurationFrames;
            int _pitchDurationFrames;

            public OrbitInertiaKeyframe(OrbitCameraControl owner)
            {
                _owner = owner;
            }

            public int DurationFrames { get; private set; }

            public bool RequiresRedraw => true;

            public void Begin()
            {
                _startOrbit = new Vector2(_owner._yawRadians, _owner._pitchRadians);
                _startVelocity = _owner._orbitVelocityRadiansPerFrame;
                _yawDurationFrames = CalculateDuration(Math.Abs(_startVelocity.X));
                _pitchDurationFrames = CalculatePitchDuration(
                    _startOrbit.Y,
                    _startVelocity.Y);
                DurationFrames = Math.Max(_yawDurationFrames, _pitchDurationFrames);
            }

            public void Apply(float progress)
            {
                int elapsedFrames = GetElapsedFrames(DurationFrames, progress);

                float yaw;
                float yawVelocity;
                EvaluateFreeAxis(
                    _startOrbit.X,
                    _startVelocity.X,
                    _yawDurationFrames,
                    elapsedFrames,
                    out yaw,
                    out yawVelocity);

                float pitch;
                float pitchVelocity;
                EvaluateBoundedAxis(
                    _startOrbit.Y,
                    _startVelocity.Y,
                    -MAXIMUM_PITCH_RADIANS,
                    MAXIMUM_PITCH_RADIANS,
                    _pitchDurationFrames,
                    elapsedFrames,
                    out pitch,
                    out pitchVelocity);

                _owner.ApplyOrbitInertiaState(
                    yaw,
                    pitch,
                    new Vector2(yawVelocity, pitchVelocity));
            }
        }

        sealed class ZoomInertiaKeyframe : IAnimationStep
        {
            readonly OrbitCameraControl _owner;
            float _startZoom;
            float _startVelocity;

            public ZoomInertiaKeyframe(OrbitCameraControl owner)
            {
                _owner = owner;
            }

            public int DurationFrames { get; private set; }

            public bool RequiresRedraw => true;

            public void Begin()
            {
                _startZoom = _owner.GetNormalizedZoomValue();
                _startVelocity = _owner._zoomVelocityLogPerFrame;
                DurationFrames = CalculateDuration(
                    Math.Abs(_startVelocity),
                    STOP_ZOOM_VELOCITY_LOG_PER_FRAME,
                    ZOOM_INERTIA_DECAY_PER_FRAME);
            }

            public void Apply(float progress)
            {
                int elapsedFrames = GetElapsedFrames(DurationFrames, progress);
                float displacement;
                float velocity;
                EvaluateInertia(
                    _startVelocity,
                    ZOOM_INERTIA_DECAY_PER_FRAME,
                    DurationFrames,
                    elapsedFrames,
                    out displacement,
                    out velocity);

                _owner.ApplyZoomInertiaState(_startZoom, displacement, velocity);
            }
        }

        public OrbitCameraControl(RectangleF bounds)
            : base(bounds, CursorType.Default)
        {
            DragSensitivityRadiansPerPixel = DEFAULT_SENSITIVITY_RADIANS_PER_PIXEL;
            SetSecondaryDraggable();
            SetOnDrag(OnOrbitDragged);
        }

        public Action<OrbitCameraControl> CameraChanged { get; set; }

        public ControlDragHandler PrimaryDrag { get; set; }

        public Func<float> ZoomValueProvider { get; set; }

        public Func<float, float> NormalizeZoomValue { get; set; }

        public Func<OrbitCameraControl, float, bool> ZoomChanged { get; set; }

        public override bool CanDrag
        {
            get { return base.CanDrag && PrimaryDrag != null; }
        }

        public float DragSensitivityRadiansPerPixel { get; set; }

        public float ZoomStep { get; set; } = DEFAULT_ZOOM_STEP;

        public bool CameraInertiaEnabled { get; set; } = true;

        public float YawRadians
        {
            get { return _yawRadians; }
        }

        public float PitchRadians
        {
            get { return _pitchRadians; }
        }

        public void ResetOrbit()
        {
            SetOrbit(0f, 0f, true);
        }

        public bool SetOrbit(float yawRadians, float pitchRadians, bool raiseCameraChanged)
        {
            StopOrbitInertia();
            return SetOrbitCore(yawRadians, pitchRadians, raiseCameraChanged);
        }

        public bool ZoomByWheelDelta(int wheelDelta)
        {
            if (wheelDelta == 0 || ZoomValueProvider == null || ZoomChanged == null)
                return false;

            float zoomStep = IsFinite(ZoomStep) && ZoomStep > 1f ? ZoomStep : DEFAULT_ZOOM_STEP;
            float direction = wheelDelta > 0 ? 1f : -1f;
            float logDelta = (float)Math.Log(zoomStep) * direction;

            if (!ApplyZoomLogDelta(logDelta))
            {
                StopZoomInertia();
                return false;
            }

            AddZoomVelocity(logDelta * ZOOM_VELOCITY_IMPULSE);
            return true;
        }

        public void StopCameraInertia()
        {
            StopOrbitInertia();
            StopZoomInertia();
        }

        bool SetOrbitCore(float yawRadians, float pitchRadians, bool raiseCameraChanged)
        {
            float nextYaw = IsFinite(yawRadians) ? WrapRadians(yawRadians) : 0f;
            float nextPitch = IsFinite(pitchRadians)
                ? MathHelper.Clamp(pitchRadians, -MAXIMUM_PITCH_RADIANS, MAXIMUM_PITCH_RADIANS)
                : 0f;

            if (Math.Abs(nextYaw - _yawRadians) <= VECTOR_EPSILON &&
                Math.Abs(nextPitch - _pitchRadians) <= VECTOR_EPSILON)
            {
                return false;
            }

            _yawRadians = nextYaw;
            _pitchRadians = nextPitch;
            if (raiseCameraChanged)
                RaiseCameraChanged();
            else
                MarkDirty();
            return true;
        }

        public void BuildProjection(
            Vector3 baseViewDirection,
            Vector3 referenceUpDirection,
            out Vector3 viewDirection,
            out Vector3 screenRightDirection,
            out Vector3 screenUpDirection)
        {
            Vector3 baseView = NormalizeOrFallback(baseViewDirection, Vector3.Backward);
            Vector3 referenceUp = NormalizeOrFallback(referenceUpDirection, Vector3.Up);

            Vector3 baseRight = Vector3.Cross(referenceUp, baseView);
            if (baseRight.Normalize() <= VECTOR_EPSILON)
            {
                referenceUp = Math.Abs(Vector3.Dot(baseView, Vector3.Up)) > 0.98f
                    ? Vector3.Forward
                    : Vector3.Up;
                baseRight = Vector3.Cross(referenceUp, baseView);
                if (baseRight.Normalize() <= VECTOR_EPSILON)
                    baseRight = Vector3.Right;
            }

            Vector3 baseUp = Vector3.Cross(baseView, baseRight);
            if (baseUp.Normalize() <= VECTOR_EPSILON)
                baseUp = referenceUp;

            float cosYaw = (float)Math.Cos(_yawRadians);
            float sinYaw = (float)Math.Sin(_yawRadians);
            float cosPitch = (float)Math.Cos(_pitchRadians);
            float sinPitch = (float)Math.Sin(_pitchRadians);

            viewDirection = baseView * (cosYaw * cosPitch) +
                            baseRight * (sinYaw * cosPitch) +
                            baseUp * sinPitch;
            viewDirection = NormalizeOrFallback(viewDirection, baseView);

            screenRightDirection = Vector3.Cross(referenceUp, viewDirection);
            if (screenRightDirection.Normalize() <= VECTOR_EPSILON)
            {
                screenRightDirection = baseRight * cosYaw - baseView * sinYaw;
                screenRightDirection = NormalizeOrFallback(screenRightDirection, baseRight);
            }

            screenUpDirection = Vector3.Cross(viewDirection, screenRightDirection);
            screenUpDirection = NormalizeOrFallback(screenUpDirection, baseUp);
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            // The application renders the camera affordances; this control only
            // contributes an interactive hit area.
        }

        public override bool Drag(object sender, Vector2 delta)
        {
            if (!CanDrag || !IsFinite(delta))
                return false;

            return PrimaryDrag(DataContext ?? this, sender, delta);
        }

        public override bool BeginDrag(object sender, bool secondary)
        {
            bool began = base.BeginDrag(sender, secondary);
            if (!began)
                return false;

            StopOrbitInertia();
            _orbitVelocityRadiansPerFrame = Vector2.Zero;
            _secondaryOrbitDragActive = secondary;
            return true;
        }

        public override void EndDrag(object sender)
        {
            bool startOrbitInertia = _secondaryOrbitDragActive;
            _secondaryOrbitDragActive = false;
            base.EndDrag(sender);

            if (startOrbitInertia)
                StartOrbitInertia();
            else
                ClearOrbitVelocity();
        }

        bool OnOrbitDragged(object dataContext, object sender, Vector2 delta)
        {
            StopOrbitInertia();

            float sensitivity = Math.Max(0f, DragSensitivityRadiansPerPixel);
            if (sensitivity <= 0f)
                return false;

            float yawDelta = -delta.X * sensitivity;
            float nextYaw = WrapRadians(_yawRadians + yawDelta);
            float nextPitch = MathHelper.Clamp(
                _pitchRadians + delta.Y * sensitivity,
                -MAXIMUM_PITCH_RADIANS,
                MAXIMUM_PITCH_RADIANS);
            float pitchDelta = nextPitch - _pitchRadians;

            if (Math.Abs(nextYaw - _yawRadians) <= VECTOR_EPSILON &&
                Math.Abs(nextPitch - _pitchRadians) <= VECTOR_EPSILON)
            {
                return false;
            }

            _yawRadians = nextYaw;
            _pitchRadians = nextPitch;
            _orbitVelocityRadiansPerFrame = new Vector2(
                ClampMagnitude(yawDelta, MAXIMUM_ORBIT_VELOCITY_RADIANS_PER_FRAME),
                ClampMagnitude(pitchDelta, MAXIMUM_ORBIT_VELOCITY_RADIANS_PER_FRAME));
            _lastOrbitVelocityFrame = GetCurrentGameFrame();
            RaiseCameraChanged();
            return true;
        }

        void StartOrbitInertia()
        {
            if (!CameraInertiaEnabled ||
                AnimationController == null ||
                !HasFreshOrbitVelocitySample() ||
                Math.Abs(_orbitVelocityRadiansPerFrame.X) <= STOP_ORBIT_VELOCITY_RADIANS_PER_FRAME &&
                Math.Abs(_orbitVelocityRadiansPerFrame.Y) <= STOP_ORBIT_VELOCITY_RADIANS_PER_FRAME)
            {
                ClearOrbitVelocity();
                return;
            }

            _orbitInertiaAnimation = this.RunAnimation(
                InvalidateCameraAnimation,
                ORBIT_INERTIA_CHANNEL,
                AnimationConflict.Replace,
                new OrbitInertiaKeyframe(this),
                new ActionKeyframe(CompleteOrbitInertia, false));
        }

        void StopOrbitInertia()
        {
            ClearOrbitVelocity();
            if (_orbitInertiaAnimation != null)
            {
                this.CancelAnimation(ORBIT_INERTIA_CHANNEL, false);
                _orbitInertiaAnimation = null;
            }
        }

        void CompleteOrbitInertia()
        {
            ClearOrbitVelocity();
            _orbitInertiaAnimation = null;
        }

        void ApplyOrbitInertiaState(float yawRadians, float pitchRadians, Vector2 velocity)
        {
            _orbitVelocityRadiansPerFrame = velocity;
            if (SetOrbitCore(yawRadians, pitchRadians, false))
                RaiseCameraChanged();
            else
                MarkDirty();
        }

        void ClearOrbitVelocity()
        {
            _orbitVelocityRadiansPerFrame = Vector2.Zero;
            _lastOrbitVelocityFrame = long.MinValue;
        }

        bool HasFreshOrbitVelocitySample()
        {
            if (_lastOrbitVelocityFrame == long.MinValue)
                return false;

            long frame = GetCurrentGameFrame();
            return frame <= _lastOrbitVelocityFrame ||
                   frame - _lastOrbitVelocityFrame <= MAXIMUM_ORBIT_VELOCITY_SAMPLE_AGE_FRAMES;
        }

        bool ApplyZoomLogDelta(float logDelta)
        {
            if (!IsFinite(logDelta))
                return false;

            float current = GetNormalizedZoomValue();
            float multiplier = (float)Math.Exp(logDelta);
            if (!IsFinite(multiplier) || multiplier <= 0f)
                return false;

            return SetZoomValue(current * multiplier);
        }

        void AddZoomVelocity(float velocityLogDelta)
        {
            if (!CameraInertiaEnabled || AnimationController == null)
                return;

            _zoomVelocityLogPerFrame = MathHelper.Clamp(
                _zoomVelocityLogPerFrame + velocityLogDelta,
                -MAXIMUM_ZOOM_VELOCITY_LOG_PER_FRAME,
                MAXIMUM_ZOOM_VELOCITY_LOG_PER_FRAME);

            if (Math.Abs(_zoomVelocityLogPerFrame) <= STOP_ZOOM_VELOCITY_LOG_PER_FRAME)
                return;

            _zoomInertiaAnimation = this.RunAnimation(
                InvalidateCameraAnimation,
                ZOOM_INERTIA_CHANNEL,
                AnimationConflict.Replace,
                new ZoomInertiaKeyframe(this),
                new ActionKeyframe(CompleteZoomInertia, false));
        }

        void StopZoomInertia()
        {
            _zoomVelocityLogPerFrame = 0f;
            if (_zoomInertiaAnimation != null)
            {
                this.CancelAnimation(ZOOM_INERTIA_CHANNEL, false);
                _zoomInertiaAnimation = null;
            }
        }

        void CompleteZoomInertia()
        {
            _zoomVelocityLogPerFrame = 0f;
            _zoomInertiaAnimation = null;
        }

        void ApplyZoomInertiaState(float startZoom, float logDisplacement, float velocity)
        {
            float multiplier = (float)Math.Exp(logDisplacement);
            if (!IsFinite(multiplier) || multiplier <= 0f)
            {
                _zoomVelocityLogPerFrame = 0f;
                return;
            }

            bool changed = SetZoomValue(startZoom * multiplier);
            _zoomVelocityLogPerFrame = changed ? velocity : 0f;
        }

        float GetNormalizedZoomValue()
        {
            var provider = ZoomValueProvider;
            float value = provider == null ? 1f : provider();
            return NormalizeZoom(value, 1f);
        }

        bool SetZoomValue(float value)
        {
            var handler = ZoomChanged;
            if (handler == null)
                return false;

            float current = GetNormalizedZoomValue();
            float next = NormalizeZoom(value, current);
            if (Math.Abs(next - current) <= Math.Max(ZOOM_VALUE_EPSILON, Math.Abs(current) * ZOOM_VALUE_EPSILON))
                return false;

            return handler(this, next);
        }

        float NormalizeZoom(float value, float fallback)
        {
            if (!IsFinite(value) || value <= 0f)
                value = fallback;

            var normalizer = NormalizeZoomValue;
            if (normalizer != null)
                value = normalizer(value);

            return IsFinite(value) && value > 0f ? value : fallback;
        }

        void InvalidateCameraAnimation()
        {
            MarkDirty();
        }

        void RaiseCameraChanged()
        {
            MarkDirty();
            var handler = CameraChanged;
            if (handler != null)
                handler(this);
        }

        static int GetElapsedFrames(int durationFrames, float progress)
        {
            if (durationFrames <= 0)
                return 0;

            return progress >= 1f
                ? durationFrames
                : Math.Max(0, Math.Min(
                    durationFrames,
                    (int)Math.Floor(progress * durationFrames)));
        }

        static long GetCurrentGameFrame()
        {
            return MyAPIGateway.Session != null
                ? MyAPIGateway.Session.GameplayFrameCounter
                : 0L;
        }

        static int CalculateDuration(float absoluteVelocity)
        {
            return CalculateDuration(
                absoluteVelocity,
                STOP_ORBIT_VELOCITY_RADIANS_PER_FRAME,
                ORBIT_INERTIA_DECAY_PER_FRAME);
        }

        static int CalculateDuration(float absoluteVelocity, float stopVelocity, float decayPerFrame)
        {
            if (absoluteVelocity <= stopVelocity ||
                decayPerFrame <= 0f ||
                decayPerFrame >= 1f)
            {
                return 0;
            }

            return Math.Min(
                600,
                Math.Max(1, (int)Math.Ceiling(
                    Math.Log(stopVelocity / absoluteVelocity) /
                    Math.Log(decayPerFrame))));
        }

        static int CalculatePitchDuration(float startPitch, float velocity)
        {
            int duration = CalculateDuration(Math.Abs(velocity));
            if (duration <= 0 ||
                startPitch <= -MAXIMUM_PITCH_RADIANS && velocity < 0f ||
                startPitch >= MAXIMUM_PITCH_RADIANS && velocity > 0f)
            {
                return 0;
            }

            float availableDistance = velocity > 0f
                ? MAXIMUM_PITCH_RADIANS - startPitch
                : startPitch + MAXIMUM_PITCH_RADIANS;
            double totalPossibleDistance = Math.Abs(velocity) /
                                           (1d - ORBIT_INERTIA_DECAY_PER_FRAME);

            if (availableDistance < totalPossibleDistance)
            {
                double ratio = availableDistance *
                               (1d - ORBIT_INERTIA_DECAY_PER_FRAME) /
                               Math.Abs(velocity);
                if (ratio <= 0d)
                    return 0;

                if (ratio < 1d)
                {
                    int boundaryDuration = Math.Max(1, (int)Math.Ceiling(
                        Math.Log(1d - ratio) /
                        Math.Log(ORBIT_INERTIA_DECAY_PER_FRAME)));
                    duration = Math.Min(duration, boundaryDuration);
                }
            }

            return duration;
        }

        static void EvaluateFreeAxis(
            float startValue,
            float startVelocity,
            int durationFrames,
            int elapsedFrames,
            out float value,
            out float velocity)
        {
            float displacement;
            EvaluateInertia(
                startVelocity,
                ORBIT_INERTIA_DECAY_PER_FRAME,
                durationFrames,
                elapsedFrames,
                out displacement,
                out velocity);
            value = WrapRadians(startValue + displacement);
        }

        static void EvaluateBoundedAxis(
            float startValue,
            float startVelocity,
            float minValue,
            float maxValue,
            int durationFrames,
            int elapsedFrames,
            out float value,
            out float velocity)
        {
            float displacement;
            EvaluateInertia(
                startVelocity,
                ORBIT_INERTIA_DECAY_PER_FRAME,
                durationFrames,
                elapsedFrames,
                out displacement,
                out velocity);

            value = MathHelper.Clamp(startValue + displacement, minValue, maxValue);
            if (value <= minValue && velocity < 0f ||
                value >= maxValue && velocity > 0f)
            {
                velocity = 0f;
            }
        }

        static void EvaluateInertia(
            float startVelocity,
            float decayPerFrame,
            int durationFrames,
            int elapsedFrames,
            out float displacement,
            out float velocity)
        {
            if (durationFrames <= 0)
            {
                displacement = 0f;
                velocity = 0f;
                return;
            }

            int axisElapsedFrames = Math.Min(elapsedFrames, durationFrames);
            double decayPower = Math.Pow(decayPerFrame, axisElapsedFrames);
            displacement = startVelocity *
                           (1f - (float)decayPower) /
                           (1f - decayPerFrame);
            velocity = axisElapsedFrames >= durationFrames
                ? 0f
                : startVelocity * (float)decayPower;
        }

        static float ClampMagnitude(float value, float maximumMagnitude)
        {
            if (maximumMagnitude <= 0f)
                return 0f;

            return MathHelper.Clamp(value, -maximumMagnitude, maximumMagnitude);
        }

        static float WrapRadians(float radians)
        {
            while (radians > MathHelper.Pi)
                radians -= MathHelper.TwoPi;
            while (radians < -MathHelper.Pi)
                radians += MathHelper.TwoPi;
            return radians;
        }

        static Vector3 NormalizeOrFallback(Vector3 value, Vector3 fallback)
        {
            if (value.Normalize() > VECTOR_EPSILON)
                return value;

            if (fallback.Normalize() > VECTOR_EPSILON)
                return fallback;

            return Vector3.Backward;
        }

        static bool IsFinite(Vector2 value)
        {
            return !float.IsNaN(value.X) &&
                   !float.IsInfinity(value.X) &&
                   !float.IsNaN(value.Y) &&
                   !float.IsInfinity(value.Y);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
