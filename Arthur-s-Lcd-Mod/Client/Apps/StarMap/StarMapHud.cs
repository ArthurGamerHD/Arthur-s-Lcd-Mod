using System;
using System.Collections.Generic;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.StarMap
{
    // A ship on the HUD: its centre in camera space (metres: X right, Y up, Z forward), its radius, and a border in
    // its relation color with its name above and distance below.
    internal struct HudContact
    {
        public Vector3 Center;
        public float Radius;
        public string Name;
        public string Distance;
        public Color Color;
        // What the lock key would lock now: bracketed in the HUD color, as the game marks it.
        public bool IsLockCandidate;
    }

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
        public readonly List<HudContact> Contacts = new List<HudContact>();

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
        // A grid being locked on, drawn like the radar's lock: corners closing in with the progress (0 to 1), and a
        // diamond once it is locked.
        public bool HasLock;
        public Vector3 LockDirection;
        public float LockProgress;
        public Color LockColor = Color.White;
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
        // As the radar draws its lock, in pixels.
        const float LOCK_ICON_SIZE = 16f;
        const float LOCK_ANIM_DISTANCE = 20f;
        const float CONTACT_MINIMUM_HALF_SIZE = 10f;
        const float CONTACT_TEXT_SCALE = .5f;
        const float CANDIDATE_GAP = 6f;
        const float CANDIDATE_CORNER = 8f;

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

        // Screen position of a camera-space direction, or false when it points behind the viewer.
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
                for (int i = 0; i < hud.Contacts.Count; i++)
                    DrawContact(context, hud, hud.Contacts[i]);
                if (hud.HasLock && TryProject(hud.LockDirection, out point))
                    DrawLock(context, hud, point);
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

        void DrawContact(RenderContext context, StarMapHud hud, HudContact contact)
        {
            Vector2 center;
            if (!TryProject(contact.Center, out center))
                return;
            float focal = Bounds.Height * .5f / (float)Math.Tan(Math.Max(.001f, hud.VerticalFovRadians) * .5f);
            float h = Math.Max(CONTACT_MINIMUM_HALF_SIZE, focal * contact.Radius / contact.Center.Z);
            var topLeft = center + new Vector2(-h, -h);
            var topRight = center + new Vector2(h, -h);
            var bottomRight = center + new Vector2(h, h);
            var bottomLeft = center + new Vector2(-h, h);
            Line(context, topLeft, topRight, 2f, contact.Color);
            Line(context, topRight, bottomRight, 2f, contact.Color);
            Line(context, bottomRight, bottomLeft, 2f, contact.Color);
            Line(context, bottomLeft, topLeft, 2f, contact.Color);
            float text = CONTACT_TEXT_SCALE * hud.FontScale;
            if (!string.IsNullOrEmpty(contact.Name))
                context.Text(hud.Font, contact.Name, new Vector2(center.X, topLeft.Y - 3f), text, contact.Color,
                    HorizontalAlignment.Center, VerticalAlignment.Bottom);
            context.Text(hud.Font, contact.Distance, new Vector2(center.X, bottomLeft.Y + 3f), text, contact.Color,
                HorizontalAlignment.Center, VerticalAlignment.Top);
            if (contact.IsLockCandidate)
                DrawCorners(context, center, h + CANDIDATE_GAP, CANDIDATE_CORNER, 2f, hud.Color);
        }

        static void DrawCorners(RenderContext context, Vector2 center, float half, float arm, float width, Color color)
        {
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    Vector2 corner = center + new Vector2(sx * half, sy * half);
                    Line(context, corner, corner - new Vector2(sx * arm, 0f), width, color);
                    Line(context, corner, corner - new Vector2(0f, sy * arm), width, color);
                }
        }

        static void DrawLock(RenderContext context, StarMapHud hud, Vector2 center)
        {
            float progress = MathHelper.Clamp(hud.LockProgress, 0f, 1f);
            float box = LOCK_ICON_SIZE + 24f + (1f - progress) * LOCK_ANIM_DISTANCE * 2f;
            DrawCorners(context, center, box * .5f, 10f, 3f, hud.LockColor);
            if (progress < .999f)
                return;
            float d = (LOCK_ICON_SIZE + 10f) * .5f;
            Vector2 top = center - new Vector2(0f, d);
            Vector2 right = center + new Vector2(d, 0f);
            Vector2 bottom = center + new Vector2(0f, d);
            Vector2 left = center - new Vector2(d, 0f);
            Line(context, top, right, 2f, hud.LockColor);
            Line(context, right, bottom, 2f, hud.LockColor);
            Line(context, bottom, left, 2f, hud.LockColor);
            Line(context, left, top, 2f, hud.LockColor);
        }

        static void Line(RenderContext context, Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 delta = to - from;
            float length = delta.Length();
            if (length < 1e-3f)
                return;
            Texture(context, "SquareSimple", (from + to) * .5f, new Vector2(length + width, width), color,
                (float)Math.Atan2(delta.Y, delta.X));
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
