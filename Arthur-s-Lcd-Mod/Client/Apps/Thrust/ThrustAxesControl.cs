using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.Thrust
{
    /// <summary>Only the isometric thrust/gravity diagram; all dashboard text is retained UI.</summary>
    internal sealed class ThrustAxesControl : VisualElement
    {
        public static readonly StyledProperty<float> ScaleProperty =
            StyledProperty.Register<ThrustAxesControl, float>(nameof(Scale), 1f, UiInvalidation.Render);
        public float Scale
        {
            get { return GetValue(ScaleProperty); }
            set { SetValue(ScaleProperty, value); }
        }

        public static readonly StyledProperty<Vector3> PositiveFillProperty =
            StyledProperty.Register<ThrustAxesControl, Vector3>(nameof(PositiveFill), Vector3.Zero, UiInvalidation.Render);
        public Vector3 PositiveFill
        {
            get { return GetValue(PositiveFillProperty); }
            set { SetValue(PositiveFillProperty, value); }
        }

        public static readonly StyledProperty<Vector3> NegativeFillProperty =
            StyledProperty.Register<ThrustAxesControl, Vector3>(nameof(NegativeFill), Vector3.Zero, UiInvalidation.Render);
        public Vector3 NegativeFill
        {
            get { return GetValue(NegativeFillProperty); }
            set { SetValue(NegativeFillProperty, value); }
        }

        public static readonly StyledProperty<Vector3> GravityProperty =
            StyledProperty.Register<ThrustAxesControl, Vector3>(nameof(Gravity), Vector3.Zero, UiInvalidation.Render);
        public Vector3 Gravity
        {
            get { return GetValue(GravityProperty); }
            set { SetValue(GravityProperty, value); }
        }

        public static readonly StyledProperty<Color> GravityColorProperty =
            StyledProperty.Register<ThrustAxesControl, Color>(nameof(GravityColor), Color.White, UiInvalidation.Render);
        public Color GravityColor
        {
            get { return GetValue(GravityColorProperty); }
            set { SetValue(GravityColorProperty, value); }
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (Bounds.Width <= 0f || Bounds.Height <= 0f) return;
            float length = Math.Min(Bounds.Width, Bounds.Height) * 0.35f;
            float thickness = 6f * Scale;
            float arrowSize = thickness * 3f;
            var x = new Vector2(0.8660254f, 0.5f);
            var y = new Vector2(0f, -1f);
            var z = new Vector2(0.8660254f, -0.5f);
            using (context.PushClip(Bounds))
            {
                DrawRay(context, y, length, thickness, arrowSize, new Color(Color.Green, 0.25f));
                DrawRay(context, -y, length, thickness, arrowSize, new Color(Color.Green, 0.25f));
                DrawRay(context, z, length, thickness, arrowSize, new Color(Color.Blue, 0.25f));
                DrawRay(context, -z, length, thickness, arrowSize, new Color(Color.Blue, 0.25f));
                DrawRay(context, x, length, thickness, arrowSize, new Color(Color.Red, 0.25f));
                DrawRay(context, -x, length, thickness, arrowSize, new Color(Color.Red, 0.25f));
                DrawRay(context, y, length * PositiveFill.Y, thickness, arrowSize, Color.Green);
                DrawRay(context, -y, length * NegativeFill.Y, thickness, arrowSize, Color.Green);
                DrawRay(context, z, length * PositiveFill.Z, thickness, arrowSize, Color.Blue);
                DrawRay(context, -z, length * NegativeFill.Z, thickness, arrowSize, Color.Blue);
                DrawRay(context, x, length * PositiveFill.X, thickness, arrowSize, Color.Red);
                DrawRay(context, -x, length * NegativeFill.X, thickness, arrowSize, Color.Red);

                Vector2 projected = x * Gravity.X + y * Gravity.Y + z * Gravity.Z;
                if (projected.LengthSquared() > 1e-8f)
                {
                    projected.Normalize();
                    float strength = MathHelper.Clamp(Gravity.Length() / 10f, 0f, 1f);
                    DrawRay(context, projected, length * 0.85f * strength, thickness, arrowSize, GravityColor);
                }
            }
        }

        void DrawRay(RenderContext context, Vector2 direction, float length, float thickness, float arrowSize, Color color)
        {
            if (length <= 0f) return;
            float angle = (float)Math.Atan2(direction.Y, direction.X);
            Vector2 tip = Bounds.Center + direction * length;
            Vector2 midpoint = (Bounds.Center + tip) * 0.5f;
            context.Texture("SquareSimple", new RectangleF(midpoint - new Vector2(length, thickness) * 0.5f,
                new Vector2(length, thickness)), color, false, angle);
            Vector2 arrowCenter = tip + direction * arrowSize * 0.5f;
            context.Texture("Triangle", new RectangleF(arrowCenter - new Vector2(arrowSize) * 0.5f,
                new Vector2(arrowSize)), color, false, angle + MathHelper.PiOver2);
        }
    }
}
