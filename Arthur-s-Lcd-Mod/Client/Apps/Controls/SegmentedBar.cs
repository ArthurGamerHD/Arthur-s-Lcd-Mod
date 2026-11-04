using System;
using System.Collections.Generic;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.Controls
{
    internal struct BarSegment : IEquatable<BarSegment>
    {
        public readonly float Value;
        public readonly Color Color;

        public BarSegment(float value, Color color)
        {
            Value = value;
            Color = color;
        }

        public bool Equals(BarSegment other)
        {
            return Value.Equals(other.Value) && Color.Equals(other.Color);
        }
    }

    /// <summary>
    /// A horizontal pill-shaped bar split into proportional colored segments, each separated by
    /// <see cref="Gap"/>. Values are relative to <see cref="Total"/>, or to their sum when it is zero.
    /// </summary>
    internal sealed class SegmentedBar : VisualElement
    {
        public static readonly StyledProperty<IReadOnlyList<BarSegment>> SegmentsProperty =
            StyledProperty.Register<SegmentedBar, IReadOnlyList<BarSegment>>(nameof(Segments), null, UiInvalidation.Render);
        public IReadOnlyList<BarSegment> Segments { get { return GetValue(SegmentsProperty); } set { SetValue(SegmentsProperty, value); } }

        public static readonly StyledProperty<float> TotalProperty =
            StyledProperty.Register<SegmentedBar, float>(nameof(Total), 0f, UiInvalidation.Render);
        public float Total { get { return GetValue(TotalProperty); } set { SetValue(TotalProperty, value); } }

        public static readonly StyledProperty<Color> TrackProperty =
            StyledProperty.Register<SegmentedBar, Color>(nameof(Track), Color.Gray, UiInvalidation.Render);
        public Color Track { get { return GetValue(TrackProperty); } set { SetValue(TrackProperty, value); } }

        public static readonly StyledProperty<float> GapProperty =
            StyledProperty.Register<SegmentedBar, float>(nameof(Gap), 2f, UiInvalidation.Render);
        public float Gap { get { return GetValue(GapProperty); } set { SetValue(GapProperty, value); } }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        protected override void DrawSelf(RenderContext context)
        {
            RectangleF rect = Bounds;
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            Pill(context, rect, Track);
            IReadOnlyList<BarSegment> segments = Segments;
            if (segments == null)
                return;

            int visible = 0;
            float sum = 0f;
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].Value <= 0f)
                    continue;
                visible++;
                sum += segments[i].Value;
            }

            float total = Total > 0f ? Total : sum;
            if (visible == 0 || total <= 0f)
                return;

            float gap = Math.Min(Gap, rect.Width / Math.Max(1f, visible * 2f));
            float available = Math.Max(0f, rect.Width - gap * (visible - 1));
            float x = rect.X;
            for (int i = 0; i < segments.Count; i++)
            {
                BarSegment segment = segments[i];
                if (segment.Value <= 0f)
                    continue;
                float width = available * segment.Value / total;
                Pill(context, new RectangleF(x, rect.Y, width, rect.Height), segment.Color);
                x += width + gap;
            }
        }

        internal static void Pill(RenderContext context, RectangleF rect, Color color)
        {
            if (rect.Width <= 0f || rect.Height <= 0f || color.A == 0)
                return;
            float diameter = Math.Min(rect.Width, rect.Height);
            float radius = diameter * .5f;
            float centerY = rect.Center.Y - radius;
            context.Texture("Circle", new RectangleF(rect.X, centerY, diameter, diameter), color);
            if (rect.Width <= diameter)
                return;
            context.Texture("Circle", new RectangleF(rect.Right - diameter, centerY, diameter, diameter), color);
            context.Fill(new RectangleF(rect.X + radius, rect.Y, rect.Width - diameter, rect.Height), color);
        }
    }
}
