using System;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Camera
{
    /// <summary>
    /// Non-visual camera surface that maps secondary drags to pan deltas and
    /// mouse-wheel input to a normalized zoom value.
    /// </summary>
    public sealed class PanAndZoomCam : Panel
    {
        const float DEFAULT_ZOOM_STEP = 1.1f;
        const float ZOOM_VALUE_EPSILON = 0.0001f;

        public PanAndZoomCam(RectangleF bounds)
            : base(bounds, CursorType.Default)
        {
            SetSecondaryDraggable();
        }

        public ControlDragHandler PanChanged { get; set; }

        public Func<float> ZoomValueProvider { get; set; }

        public Func<float, float> NormalizeZoomValue { get; set; }

        public Func<PanAndZoomCam, float, bool> ZoomChanged { get; set; }

        public Func<bool> CanZoomByWheel { get; set; }

        public Action<PanAndZoomCam, int> WheelZoomed { get; set; }

        public float ZoomStep { get; set; } = DEFAULT_ZOOM_STEP;

        public override bool CanDrag
        {
            get { return false; }
        }

        public override bool CanSecondaryDrag
        {
            get { return Visible && Enabled && SecondaryDraggable && PanChanged != null; }
        }

        public override bool CanScroll
        {
            get
            {
                return Visible &&
                       Enabled &&
                       ZoomValueProvider != null &&
                       ZoomChanged != null &&
                       IsWheelZoomAllowed();
            }
        }

        public bool ZoomByWheelDelta(int wheelDelta)
        {
            if (wheelDelta == 0 || ZoomValueProvider == null || ZoomChanged == null || !IsWheelZoomAllowed())
                return false;

            float zoomStep = IsFinite(ZoomStep) && ZoomStep > 1f ? ZoomStep : DEFAULT_ZOOM_STEP;
            float direction = wheelDelta > 0 ? 1f : -1f;
            float multiplier = (float)Math.Exp(Math.Log(zoomStep) * direction);
            if (!IsFinite(multiplier) || multiplier <= 0f)
                return false;

            if (!SetZoomValue(GetNormalizedZoomValue() * multiplier))
                return false;

            var wheelZoomed = WheelZoomed;
            if (wheelZoomed != null)
                wheelZoomed(this, wheelDelta);

            return true;
        }

        public override bool Scroll(object sender, int delta)
        {
            return ZoomByWheelDelta(delta);
        }

        public override bool Drag(object sender, Vector2 delta)
        {
            return false;
        }

        public override bool Drag(object sender, Vector2 delta, bool secondary)
        {
            if (!secondary || !CanSecondaryDrag || !IsFinite(delta))
                return false;

            return PanChanged(DataContext ?? this, sender, delta);
        }

        bool IsWheelZoomAllowed()
        {
            var handler = CanZoomByWheel;
            return handler == null || handler();
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

        static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.X) && IsFinite(value.Y);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
