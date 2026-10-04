using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using LcdMod.Client.Gui.UserControls.Power;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    /// <summary>
    /// A fillable texture (battery, tank, jump drive) whose interior fills from the bottom with
    /// <see cref="FillColor"/>, optionally captioned with an auto-fitted label underneath.
    /// </summary>
    internal sealed class FillableSlot : VisualElement
    {
        public static readonly StyledProperty<FillableTexture> TextureProperty =
            StyledProperty.Register<FillableSlot, FillableTexture>(nameof(Texture), null, UiInvalidation.Render);
        public FillableTexture Texture { get { return GetValue(TextureProperty); } set { SetValue(TextureProperty, value); } }

        public static readonly StyledProperty<float> RatioProperty =
            StyledProperty.Register<FillableSlot, float>(nameof(Ratio), 0f, UiInvalidation.Render);
        public float Ratio { get { return GetValue(RatioProperty); } set { SetValue(RatioProperty, value); } }

        public static readonly StyledProperty<Color> FillColorProperty =
            StyledProperty.Register<FillableSlot, Color>(nameof(FillColor), Color.White, UiInvalidation.Render);
        public Color FillColor { get { return GetValue(FillColorProperty); } set { SetValue(FillColorProperty, value); } }

        public static readonly StyledProperty<Color> IconColorProperty =
            StyledProperty.Register<FillableSlot, Color>(nameof(IconColor), Color.White, UiInvalidation.Render);
        public Color IconColor { get { return GetValue(IconColorProperty); } set { SetValue(IconColorProperty, value); } }

        public static readonly StyledProperty<bool> DrawCenterIconProperty =
            StyledProperty.Register<FillableSlot, bool>(nameof(DrawCenterIcon), true, UiInvalidation.Render);
        public bool DrawCenterIcon { get { return GetValue(DrawCenterIconProperty); } set { SetValue(DrawCenterIconProperty, value); } }

        public static readonly StyledProperty<float> CenterIconRotationProperty =
            StyledProperty.Register<FillableSlot, float>(nameof(CenterIconRotation), 0f, UiInvalidation.Render);
        public float CenterIconRotation { get { return GetValue(CenterIconRotationProperty); } set { SetValue(CenterIconRotationProperty, value); } }

        public static readonly StyledProperty<float> CenterIconScaleProperty =
            StyledProperty.Register<FillableSlot, float>(nameof(CenterIconScale), 1f, UiInvalidation.Render);
        public float CenterIconScale { get { return GetValue(CenterIconScaleProperty); } set { SetValue(CenterIconScaleProperty, value); } }

        public static readonly StyledProperty<string> LabelProperty =
            StyledProperty.Register<FillableSlot, string>(nameof(Label), string.Empty, UiInvalidation.Render);
        public string Label { get { return GetValue(LabelProperty); } set { SetValue(LabelProperty, value ?? string.Empty); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<FillableSlot, string>(nameof(FontFamily), "White", UiInvalidation.Render);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<FillableSlot, float>(nameof(FontSize), 1f, UiInvalidation.Render);
        public float FontSize { get { return GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        protected override void DrawSelf(RenderContext context)
        {
            FillableTexture texture = Texture;
            RectangleF bounds = Bounds;
            if (texture == null || bounds.Width <= 0f || bounds.Height <= 0f)
                return;

            string label = Label;
            float labelHeight = 0f, labelScale = 0f, labelGap = 0f;
            if (!string.IsNullOrEmpty(label) && TextMetrics != null)
            {
                labelGap = 2f;
                Vector2 unit = TextMetrics.Measure(FontFamily, label, 1f);
                labelScale = Math.Min(bounds.Width * .6f / Math.Max(1f, unit.X), bounds.Height * .22f / Math.Max(1f, unit.Y)) *
                             Math.Min(FontSize, 1f);
                labelHeight = unit.Y * labelScale;
            }

            float iconSize = Math.Max(0f, Math.Min(bounds.Width, bounds.Height - labelHeight - labelGap));
            var center = new Vector2(bounds.Center.X, bounds.Y + iconSize * .5f);
            Draw(context, texture, center, iconSize, Ratio, FillColor, IconColor, DrawCenterIcon, CenterIconRotation,
                CenterIconScale);
            if (labelScale > 0f)
                context.Text(FontFamily, label, new Vector2(center.X, bounds.Y + iconSize + labelGap), labelScale,
                    IconColor, HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        internal static void Draw(RenderContext context, FillableTexture texture, Vector2 center, float iconSize, float ratio,
            Color fillColor, Color iconColor, bool drawCenterIcon, float centerIconRotation, float centerIconScale)
        {
            ratio = MathHelper.Clamp(ratio, 0f, 1f);
            RectangleF inner = texture.GetInnerRect(center, iconSize);
            if (ratio > .005f && inner.Width > 0f && inner.Height > 0f)
            {
                float fill = inner.Height * ratio;
                context.Fill(new RectangleF(inner.X, inner.Bottom - fill, inner.Width, fill), fillColor);
            }

            context.Texture(texture.Name, new RectangleF(center - new Vector2(iconSize * .5f), new Vector2(iconSize)), iconColor);
            if (!drawCenterIcon || string.IsNullOrEmpty(texture.CenterIconTexture))
                return;

            float innerMin = Math.Min(inner.Width, inner.Height);
            float size = innerMin > 0f ? innerMin * centerIconScale : iconSize * centerIconScale;
            Vector2 position = innerMin > 0f ? inner.Center : center;
            context.Texture(texture.CenterIconTexture, new RectangleF(position - new Vector2(size * .5f), new Vector2(size)),
                iconColor, false, centerIconRotation);
        }
    }
}
