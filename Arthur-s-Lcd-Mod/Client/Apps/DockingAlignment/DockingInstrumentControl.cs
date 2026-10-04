using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.DockingAlignment
{
    /// <summary>Only the instrument geometry; labels, names, warnings and footer are retained controls.</summary>
    internal sealed class DockingInstrumentControl : VisualElement
    {
        public static readonly StyledProperty<float> FontScaleProperty =
            StyledProperty.Register<DockingInstrumentControl, float>(nameof(FontScale), 1f, UiInvalidation.Render);
        public float FontScale
        {
            get { return GetValue(FontScaleProperty); }
            set { SetValue(FontScaleProperty, value); }
        }

        public static readonly StyledProperty<Vector3D> PositionProperty =
            StyledProperty.Register<DockingInstrumentControl, Vector3D>(nameof(Position), Vector3D.Zero, UiInvalidation.Render);
        public Vector3D Position
        {
            get { return GetValue(PositionProperty); }
            set { SetValue(PositionProperty, value); }
        }

        public static readonly StyledProperty<Vector3D> RotationProperty =
            StyledProperty.Register<DockingInstrumentControl, Vector3D>(nameof(Rotation), Vector3D.Zero, UiInvalidation.Render);
        public Vector3D Rotation
        {
            get { return GetValue(RotationProperty); }
            set { SetValue(RotationProperty, value); }
        }

        public static readonly StyledProperty<Vector3D> VelocityProperty =
            StyledProperty.Register<DockingInstrumentControl, Vector3D>(nameof(Velocity), Vector3D.Zero, UiInvalidation.Render);
        public Vector3D Velocity
        {
            get { return GetValue(VelocityProperty); }
            set { SetValue(VelocityProperty, value); }
        }

        public static readonly StyledProperty<Color> AccentProperty =
            StyledProperty.Register<DockingInstrumentControl, Color>(nameof(Accent), Color.White, UiInvalidation.Render);
        public Color Accent
        {
            get { return GetValue(AccentProperty); }
            set { SetValue(AccentProperty, value); }
        }

        public static readonly StyledProperty<Color> StatusColorProperty =
            StyledProperty.Register<DockingInstrumentControl, Color>(nameof(StatusColor), Color.White, UiInvalidation.Render);
        public Color StatusColor
        {
            get { return GetValue(StatusColorProperty); }
            set { SetValue(StatusColorProperty, value); }
        }

        public static readonly StyledProperty<Color> ErrorColorProperty =
            StyledProperty.Register<DockingInstrumentControl, Color>(nameof(ErrorColor), Color.Red, UiInvalidation.Render);
        public Color ErrorColor
        {
            get { return GetValue(ErrorColorProperty); }
            set { SetValue(ErrorColorProperty, value); }
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (Bounds.Width <= 0f || Bounds.Height <= 0f) return;
            float side = Math.Min(Bounds.Width, Bounds.Height);
            var center = Bounds.Center;
            var rollCenter = center - new Vector2(0f, side * .10f);
            float radius = side * .32f;
            var mutedAccent = VRageMath.ColorExtensions.Alpha(Accent, .66f);
            for (int i = -9; i <= 9; i++)
            {
                if (i == 0) continue;
                bool major = Math.Abs(i) % 2 == 0;
                float thickness = (major ? 2.2f : 1.2f) * FontScale;
                float length = (major ? 18f : 10f) * FontScale;
                var color = VRageMath.ColorExtensions.Alpha(mutedAccent, major ? .75f : .35f);
                float offset = i * side * .04f;
                Texture(context, "SquareSimple", center + new Vector2(0f, offset),
                    new Vector2(length, thickness), color);
                Texture(context, "SquareSimple", center + new Vector2(offset, 0f),
                    new Vector2(thickness, length), color);
                float angle = MathHelper.ToRadians(i * 5f - 90f);
                Texture(context, "SquareSimple", rollCenter + Direction(angle) * radius,
                    new Vector2(length, thickness), color, angle);
            }

            Texture(context, "CircleHollow", center, new Vector2(side * .05f), mutedAccent);
            var guideColor = VRageMath.ColorExtensions.Alpha(StatusColor, .5f);
            float halfAxis = side * .36f;
            Texture(context, "SquareSimple",
                center + new Vector2(MathHelper.Clamp((float)Rotation.Y / 45f, -1f, 1f) * halfAxis, 0f),
                new Vector2(3f * FontScale, side * .72f), guideColor);
            Texture(context, "SquareSimple",
                center + new Vector2(0f, MathHelper.Clamp(-(float)Rotation.X / 45f, -1f, 1f) * halfAxis),
                new Vector2(side * .72f, 3f * FontScale), guideColor);
            float roll = MathHelper.ToRadians(MathHelper.Clamp((float)Rotation.Z, -45f, 45f) - 90f);
            Texture(context, "SquareSimple", rollCenter + Direction(roll) * radius,
                new Vector2(28f, 3.5f) * FontScale, VRageMath.ColorExtensions.Alpha(StatusColor, .95f), roll);
            Texture(context, "AH_VelocityVector",
                center + new Vector2(MathHelper.Clamp(-(float)Position.X * .025f, -.36f, .36f),
                    MathHelper.Clamp((float)Position.Y * .025f, -.36f, .36f)) * side,
                new Vector2(side * .1f * VectorScale(Position.Z)), ErrorColor);
            Texture(context, "AH_VelocityVector",
                center + new Vector2(MathHelper.Clamp(-(float)Velocity.X / 10f, -1f, 1f),
                    MathHelper.Clamp((float)Velocity.Y / 10f, -1f, 1f)) * halfAxis,
                new Vector2(side * .1f * .65f * VectorScale(Velocity.Z)),
                VRageMath.ColorExtensions.Alpha(StatusColor, MathHelper.Clamp((float)Velocity.Length(), 0f, 1f)));
        }

        static float VectorScale(double value) => MathHelper.Clamp(1f + (float)value / 20f, 0f, 2f);
        static Vector2 Direction(float angle) => new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));

        static void Texture(RenderContext context, string key, Vector2 center, Vector2 size, Color color, float rotation = 0f)
        {
            if (size.X <= 0f || size.Y <= 0f) return;
            context.Texture(key, new RectangleF(center - size * .5f, size), color, false, rotation);
        }
    }
}
