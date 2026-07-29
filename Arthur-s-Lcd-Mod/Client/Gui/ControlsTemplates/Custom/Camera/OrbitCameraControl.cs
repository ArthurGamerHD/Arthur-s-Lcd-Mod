using System;
using System.Collections.Generic;
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
        const float MAXIMUM_PITCH_RADIANS = 1.553343f; // 89 degrees
        const float VECTOR_EPSILON = 0.000001f;

        float _yawRadians;
        float _pitchRadians;

        public OrbitCameraControl(RectangleF bounds)
            : base(bounds, CursorType.Default)
        {
            DragSensitivityRadiansPerPixel = DEFAULT_SENSITIVITY_RADIANS_PER_PIXEL;
            SetSecondaryDraggable();
            SetOnDrag(OnOrbitDragged);
        }

        public Action<OrbitCameraControl> CameraChanged { get; set; }

        public ControlDragHandler PrimaryDrag { get; set; }

        public override bool CanDrag
        {
            get { return base.CanDrag && PrimaryDrag != null; }
        }

        public float DragSensitivityRadiansPerPixel { get; set; }

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
            if (Math.Abs(_yawRadians) <= VECTOR_EPSILON &&
                Math.Abs(_pitchRadians) <= VECTOR_EPSILON)
            {
                return;
            }

            _yawRadians = 0f;
            _pitchRadians = 0f;
            RaiseCameraChanged();
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

        bool OnOrbitDragged(object dataContext, object sender, Vector2 delta)
        {
            float sensitivity = Math.Max(0f, DragSensitivityRadiansPerPixel);
            if (sensitivity <= 0f)
                return false;

            float nextYaw = WrapRadians(_yawRadians - delta.X * sensitivity);
            float nextPitch = MathHelper.Clamp(
                _pitchRadians + delta.Y * sensitivity,
                -MAXIMUM_PITCH_RADIANS,
                MAXIMUM_PITCH_RADIANS);

            if (Math.Abs(nextYaw - _yawRadians) <= VECTOR_EPSILON &&
                Math.Abs(nextPitch - _pitchRadians) <= VECTOR_EPSILON)
            {
                return false;
            }

            _yawRadians = nextYaw;
            _pitchRadians = nextPitch;
            RaiseCameraChanged();
            return true;
        }

        void RaiseCameraChanged()
        {
            MarkDirty();
            var handler = CameraChanged;
            if (handler != null)
                handler(this);
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
    }
}
