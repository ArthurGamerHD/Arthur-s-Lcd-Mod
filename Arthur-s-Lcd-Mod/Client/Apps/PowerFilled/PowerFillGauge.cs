using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.PowerFilled
{
    /// <summary>
    /// A power storage icon whose interior fills from the bottom. Its frame, fill area and center icon come from the
    /// style class (<c>Battery</c> or <c>JumpDrive</c>).
    /// </summary>
    internal sealed class PowerFillGauge : TemplatedControl
    {
        public static readonly StyledProperty<float> ValueProperty =
            StyledProperty.Register<PowerFillGauge, float>(nameof(Value), 0f, UiInvalidation.Arrange | UiInvalidation.Render);
        public float Value { get { return GetValue(ValueProperty); } set { SetValue(ValueProperty, MathHelper.Clamp(value, 0f, 1f)); } }

        public static readonly StyledProperty<Color> FillColorProperty =
            StyledProperty.Register<PowerFillGauge, Color>(nameof(FillColor), Color.White, UiInvalidation.Render);
        public Color FillColor { get { return GetValue(FillColorProperty); } set { SetValue(FillColorProperty, value); } }

        public static readonly StyledProperty<Color> ForegroundProperty =
            StyledProperty.Register<PowerFillGauge, Color>(nameof(Foreground), Color.White, UiInvalidation.Render);
        public Color Foreground { get { return GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }

        public static readonly StyledProperty<bool> ShowCenterIconProperty =
            StyledProperty.Register<PowerFillGauge, bool>(nameof(ShowCenterIcon), true, UiInvalidation.Render);
        public bool ShowCenterIcon { get { return GetValue(ShowCenterIconProperty); } set { SetValue(ShowCenterIconProperty, value); } }

        // Center icon rotation in radians.
        public static readonly StyledProperty<float> CenterIconRotationProperty =
            StyledProperty.Register<PowerFillGauge, float>(nameof(CenterIconRotation), 0f, UiInvalidation.Render);
        public float CenterIconRotation { get { return GetValue(CenterIconRotationProperty); } set { SetValue(CenterIconRotationProperty, value); } }

        public static readonly StyledProperty<Transform> CenterIconTransformProperty =
            StyledProperty.Register<PowerFillGauge, Transform>(nameof(CenterIconTransform), null, UiInvalidation.Render);
        public Transform CenterIconTransform { get { return GetValue(CenterIconTransformProperty); } private set { SetValue(CenterIconTransformProperty, value); } }

        protected override void OnPropertyChanged(IStyledProperty property)
        {
            base.OnPropertyChanged(property);
            if (property == CenterIconRotationProperty)
                CenterIconTransform = CenterIconRotation == 0f ? null : new RotateTransform(MathHelper.ToDegrees(CenterIconRotation));
        }
    }
}
