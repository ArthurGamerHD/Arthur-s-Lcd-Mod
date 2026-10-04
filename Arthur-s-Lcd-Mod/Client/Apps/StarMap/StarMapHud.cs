using System;
using System.Collections.Generic;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.StarMap
{
    internal struct HudLadderStep
    {
        public Vector3 Direction;
        public string Label;
        public bool Negative;
    }

    /// <summary>
    /// Artificial horizon state. Directions are in camera space (X right, Y up, Z forward) so the overlay projects them
    /// with the same field of view as the 3D star map texture underneath.
    /// </summary>
    internal sealed class StarMapHud
    {
        public readonly List<HudLadderStep> Ladder = new List<HudLadderStep>();

        public bool Visible;
        public float VerticalFovRadians = MathHelper.ToRadians(70f);
        public Color Color = Color.White;
        public Color ErrorColor = Color.Red;
        public string Font = "White";
        public float FontScale = 1f;
        public bool EssentialOnly;
        public bool HasHorizon;
        public Vector3 HorizonDirection;
        public float Roll;
        public bool HasVelocity;
        public Vector3 VelocityDirection;
        public bool ShowSpeed;
        public int Speed;
        public bool HasAltitude;
        public int Altitude;
        public int VerticalSpeed;
        public bool PullUp;
        public bool BoreSight;
        public string Magnification;
    }

    internal sealed class StarMapHudView : VisualElement
    {
        public static readonly StyledProperty<StarMapHud> HudProperty =
            StyledProperty.Register<StarMapHudView, StarMapHud>(nameof(Hud), null, UiInvalidation.Render);
        public static readonly StyledProperty<int> VersionProperty =
            StyledProperty.Register<StarMapHudView, int>(nameof(Version), 0, UiInvalidation.Render);

        static readonly Vector2 LadderStepSize = new Vector2(150f, 31f);
        static readonly Vector2 TextBoxSize = new Vector2(89f, 32f);
        const float TEXT_BOX_PADDING = 5f;
        const float LADDER_TEXT_SCALE = .7f;
        const float HORIZON_WIDTH = 5f;

        public StarMapHud Hud
        {
            get { return GetValue(HudProperty); }
            set { SetValue(HudProperty, value); }
        }

        public int Version
        {
            get { return GetValue(VersionProperty); }
            set { SetValue(VersionProperty, value); }
        }

        /// <summary>Screen position of a camera-space direction, or false when it points behind the viewer.</summary>
        internal bool TryProject(Vector3 direction, out Vector2 point)
        {
            point = default(Vector2);
            StarMapHud hud = Hud;
            if (hud == null || direction.Z <= 1e-4f || Bounds.Height <= 0f)
                return false;
            float focal = Bounds.Height * .5f / (float)Math.Tan(Math.Max(.001f, hud.VerticalFovRadians) * .5f);
            point = Bounds.Center + new Vector2(direction.X, -direction.Y) * (focal / direction.Z);
            return true;
        }

        protected override void DrawSelf(RenderContext context)
        {
            StarMapHud hud = Hud;
            if (hud == null || !hud.Visible || Bounds.Width <= 0f || Bounds.Height <= 0f)
                return;

            using (context.PushClip(Bounds))
            {
                Vector2 point;
                if (hud.HasVelocity)
                {
                    Vector2 velocity = TryProject(hud.VelocityDirection, out point) ? point : Bounds.Center;
                    Texture(context, "AH_VelocityVector", velocity, new Vector2(50f), hud.Color, 0f);
                }
                if (!hud.EssentialOnly)
                    DrawInstruments(context, hud);
            }

            if (!string.IsNullOrEmpty(hud.Magnification))
                context.Text(hud.Font, hud.Magnification, new Vector2(Bounds.Right - 8f, Bounds.Bottom - 8f), .55f * hud.FontScale,
                    hud.Color, HorizontalAlignment.Right, VerticalAlignment.Bottom);
        }

        void DrawInstruments(RenderContext context, StarMapHud hud)
        {
            Vector2 point;
            if (hud.HasHorizon && TryProject(hud.HorizonDirection, out point))
            {
                float length = (Bounds.Width + Bounds.Height) * 4f;
                Texture(context, "SquareTapered", point, new Vector2(length, HORIZON_WIDTH), hud.Color, hud.Roll);
            }

            float ladderText = LADDER_TEXT_SCALE * hud.FontScale;
            for (int i = 0; i < hud.Ladder.Count; i++)
            {
                HudLadderStep step = hud.Ladder[i];
                if (!TryProject(step.Direction, out point))
                    continue;
                Texture(context, step.Negative ? "AH_GravityHudNegativeDegrees" : "AH_GravityHudPositiveDegrees", point,
                    LadderStepSize, hud.Color, hud.Roll);
                Vector2 side = Rotate(new Vector2(LadderStepSize.X * .55f, 0f), hud.Roll);
                context.Text(hud.Font, step.Label, point - side, ladderText, hud.Color, HorizontalAlignment.Right,
                    VerticalAlignment.Center);
                context.Text(hud.Font, step.Label, point + side, ladderText, hud.Color, HorizontalAlignment.Left,
                    VerticalAlignment.Center);
            }

            if (hud.HasAltitude)
            {
                Vector2 altimeter = Bounds.Center + new Vector2(115f, 80f) + TextBoxSize * .5f;
                TextBox(context, hud, altimeter, hud.Altitude.ToString(), true);
                TextBox(context, hud, altimeter - new Vector2(0f, TextBoxSize.Y), hud.VerticalSpeed.ToString(), false);
            }
            if (hud.PullUp)
                Texture(context, "AH_PullUp", Bounds.Center, new Vector2(150f, 180f), hud.ErrorColor, hud.Roll);
            if (hud.ShowSpeed)
                TextBox(context, hud, Bounds.Center + new Vector2(-205f, 80f) + TextBoxSize * .5f, hud.Speed.ToString(), true);
            if (hud.BoreSight)
                Texture(context, "AH_BoreSight", Bounds.Center + new Vector2(0f, 19f), new Vector2(50f), hud.Color,
                    -MathHelper.PiOver2);
        }

        static void TextBox(RenderContext context, StarMapHud hud, Vector2 center, string text, bool frame)
        {
            if (frame)
                Texture(context, "AH_TextBox", center, TextBoxSize, hud.Color, 0f);
            context.Text(hud.Font, text, center + new Vector2(TextBoxSize.X * .5f - TEXT_BOX_PADDING, 0f), 1f, hud.Color,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        static void Texture(RenderContext context, string texture, Vector2 center, Vector2 size, Color color, float rotation)
        {
            context.Texture(texture, new RectangleF(center - size * .5f, size), color, false, rotation);
        }

        static Vector2 Rotate(Vector2 vector, float angle)
        {
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
        }
    }
}
