using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using LcdMod.Client.Gui.UserControls.Power;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    /// <summary>A farm plot: masked plot frame with the crop, growth and water bars, and a status label.</summary>
    internal sealed class FarmPlotSlot : VisualElement
    {
        const string MASK_TEXTURE = "FarmPlotMask";
        static readonly FillableTexture PlotTexture = new FillableTexture("FarmPlot", 0f, 0f, 0f, 0f, 70f);

        public static readonly StyledProperty<string> OutputSpriteProperty =
            StyledProperty.Register<FarmPlotSlot, string>(nameof(OutputSprite), string.Empty, UiInvalidation.Render);
        public string OutputSprite { get { return GetValue(OutputSpriteProperty); } set { SetValue(OutputSpriteProperty, value); } }

        public static readonly StyledProperty<float> GrowthProperty =
            StyledProperty.Register<FarmPlotSlot, float>(nameof(Growth), 0f, UiInvalidation.Render);
        public float Growth { get { return GetValue(GrowthProperty); } set { SetValue(GrowthProperty, value); } }

        public static readonly StyledProperty<float> WaterProperty =
            StyledProperty.Register<FarmPlotSlot, float>(nameof(Water), 0f, UiInvalidation.Render);
        public float Water { get { return GetValue(WaterProperty); } set { SetValue(WaterProperty, value); } }

        public static readonly StyledProperty<Color> GrowthColorProperty =
            StyledProperty.Register<FarmPlotSlot, Color>(nameof(GrowthColor), new Color(68, 210, 92), UiInvalidation.Render);
        public Color GrowthColor { get { return GetValue(GrowthColorProperty); } set { SetValue(GrowthColorProperty, value); } }

        public static readonly StyledProperty<Color> WaterColorProperty =
            StyledProperty.Register<FarmPlotSlot, Color>(nameof(WaterColor), new Color(64, 156, 255), UiInvalidation.Render);
        public Color WaterColor { get { return GetValue(WaterColorProperty); } set { SetValue(WaterColorProperty, value); } }

        public static readonly StyledProperty<Color> MaskColorProperty =
            StyledProperty.Register<FarmPlotSlot, Color>(nameof(MaskColor), Color.Gray, UiInvalidation.Render);
        public Color MaskColor { get { return GetValue(MaskColorProperty); } set { SetValue(MaskColorProperty, value); } }

        public static readonly StyledProperty<Color> FrameColorProperty =
            StyledProperty.Register<FarmPlotSlot, Color>(nameof(FrameColor), Color.White, UiInvalidation.Render);
        public Color FrameColor { get { return GetValue(FrameColorProperty); } set { SetValue(FrameColorProperty, value); } }

        public static readonly StyledProperty<string> LabelProperty =
            StyledProperty.Register<FarmPlotSlot, string>(nameof(Label), string.Empty, UiInvalidation.Render);
        public string Label { get { return GetValue(LabelProperty); } set { SetValue(LabelProperty, value ?? string.Empty); } }

        public static readonly StyledProperty<Color> LabelColorProperty =
            StyledProperty.Register<FarmPlotSlot, Color>(nameof(LabelColor), Color.White, UiInvalidation.Render);
        public Color LabelColor { get { return GetValue(LabelColorProperty); } set { SetValue(LabelColorProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<FarmPlotSlot, string>(nameof(FontFamily), "White", UiInvalidation.Render);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<float> FontSizeProperty =
            StyledProperty.Register<FarmPlotSlot, float>(nameof(FontSize), 1f, UiInvalidation.Render);
        public float FontSize { get { return GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        protected override void DrawSelf(RenderContext context)
        {
            RectangleF bounds = Bounds;
            if (bounds.Width <= 0f || bounds.Height <= 0f)
                return;

            const float labelGap = 2f, barHeight = 4f, barGap = 2f, iconBarGap = 3f;
            const float barsHeight = barHeight * 2f + barGap;
            string label = Label;
            float labelHeight = 0f, labelScale = 0f;
            if (!string.IsNullOrEmpty(label) && TextMetrics != null)
            {
                Vector2 unit = TextMetrics.Measure(FontFamily, label, 1f);
                labelScale = Math.Min(bounds.Width * .82f / Math.Max(1f, unit.X), bounds.Height * .22f / Math.Max(1f, unit.Y)) *
                             Math.Min(FontSize, 1f);
                labelHeight = unit.Y * labelScale;
            }

            float iconSize = Math.Max(0f, Math.Min(bounds.Width, bounds.Height - labelHeight - labelGap - barsHeight - iconBarGap));
            var center = new Vector2(bounds.Center.X, bounds.Y + iconSize * .5f);
            float frame = iconSize * .84f;
            var frameRect = new RectangleF(center - new Vector2(frame * .5f), new Vector2(frame));
            context.Texture(MASK_TEXTURE, frameRect, MaskColor);
            context.Texture(PlotTexture.Name, frameRect, FrameColor);
            float item = iconSize * .58f;
            if (!string.IsNullOrEmpty(OutputSprite) && item > 0f)
            {
                Vector2 itemCenter = PlotTexture.GetInnerRect(center, frame).Center;
                context.Texture(OutputSprite, new RectangleF(itemCenter - new Vector2(item * .5f), new Vector2(item)),
                    Color.White, true);
            }

            float barWidth = Math.Max(1f, iconSize * .62f);
            float barsTop = bounds.Y + iconSize + iconBarGap;
            var growthBar = new RectangleF(center.X - barWidth * .5f, barsTop, barWidth, barHeight);
            DrawBar(context, growthBar, Growth, GrowthColor, MaskColor);
            DrawBar(context, new RectangleF(growthBar.X, barsTop + barHeight + barGap, barWidth, barHeight), Water, WaterColor,
                MaskColor);
            if (labelScale > 0f)
                context.Text(FontFamily, label, new Vector2(center.X, barsTop + barsHeight + labelGap), labelScale, LabelColor,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        static void DrawBar(RenderContext context, RectangleF rect, float ratio, Color fill, Color track)
        {
            ratio = MathHelper.Clamp(ratio, 0f, 1f);
            if (ratio < .99f)
                SegmentedBar.Pill(context, rect, track);
            float width = rect.Width * (ratio > .99f ? 1f : ratio);
            if (width <= .001f)
                return;
            using (context.PushClip(new RectangleF(rect.X, rect.Y, width, rect.Height)))
                SegmentedBar.Pill(context, rect, fill);
        }
    }
}
