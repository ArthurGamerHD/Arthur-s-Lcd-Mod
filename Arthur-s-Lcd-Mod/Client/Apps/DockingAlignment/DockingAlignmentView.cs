using System;
using Acrux.Controls;
using Acrux.Core;
using VRageMath;

namespace LcdMod.Client.Apps.DockingAlignment
{
    internal sealed partial class DockingAlignmentView : UserControl
    {
        public DockingAlignmentView()
        {
            InitializeComponent();
        }

        public DockingAlignmentView(DockingAlignmentViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var model = DataContext as DockingAlignmentViewModel;
            if (model != null && !float.IsInfinity(availableSize.X) && !float.IsInfinity(availableSize.Y))
                model.SetViewport(availableSize);
            return base.MeasureOverride(availableSize);
        }

        protected override void ArrangeChildren(RectangleF bounds)
        {
            base.ArrangeChildren(bounds);
            var model = DataContext as DockingAlignmentViewModel;
            if (model == null) return;

            // Keep the reference instrument's anchors, but arrange real controls.
            // Text anchors must be converted to rectangles using measured size.
            float footerHeight = model.FooterHeight;
            float footerTop = bounds.Bottom - footerHeight;
            float contentHeight = Math.Max(0f, bounds.Height - footerHeight);
            float side = Math.Min(bounds.Width, contentHeight);
            var square = new RectangleF(bounds.Center.X - side * .5f,
                bounds.Y + (contentHeight - side) * .5f, side, side);
            if (model.HasTarget)
            {
                Instrument.Arrange(square);
                FooterBackground.Arrange(new RectangleF(bounds.X, footerTop, bounds.Width, footerHeight));
                float font = model.InstrumentFont;
                float bottomY = square.Bottom - side * .08f;
                Place(PitchLabelText, square.Center + new Vector2(side * .35f, -side * .06f), 1f);
                Place(YawLabelText, new Vector2(square.Center.X, bottomY - font * 32f), .5f);
                Place(RollLabelText, new Vector2(square.Center.X, square.Center.Y - side * .42f - font * 22f), .5f);
                Place(PitchText, square.Center + new Vector2(side * .39f, -side * .02f), 0f);
                Place(YawText, new Vector2(square.Center.X, bottomY), .5f);
                Place(RollText, square.Center + new Vector2(-side * .32f, -side * .36f), 1f);

                float margin = 8f * model.FontScale;
                float nameY = footerTop - bounds.Height * .02f - margin;
                float yawHalfWidth = YawText.DesiredSize.X * .5f + margin;
                float nameWidth = Math.Max(0f, bounds.Width * .5f - margin - Math.Max(margin, yawHalfWidth));
                PlaceName(ReferenceNameText, bounds.X + margin, nameY, nameWidth);
                PlaceName(TargetNameText, bounds.Right - margin - nameWidth, nameY, nameWidth);

                var firstRow = new[] { LeftRightText, ForwardBackText, UpDownText, DistanceText };
                var secondRow = new[] { AxisDistanceText, DistanceRateText, ClosingVelocityText, FooterRollText };
                for (int i = 0; i < 4; i++)
                {
                    float x = bounds.X + bounds.Width * (i + .5f) / 4f;
                    Place(firstRow[i], new Vector2(x, footerTop + footerHeight * .36f), .5f, true);
                    Place(secondRow[i], new Vector2(x, footerTop + footerHeight * .78f), .5f, true);
                }
            }
            if (model.HasWarning)
            {
                var center = new Vector2(bounds.Center.X, bounds.Y + bounds.Height * .45f);
                float iconSize = Math.Min(bounds.Width, bounds.Height) * .4f * model.RenderScale;
                WarningIconShadow.Arrange(new RectangleF(center + new Vector2(2f) - new Vector2(iconSize * .5f), new Vector2(iconSize)));
                WarningIcon.Arrange(new RectangleF(center - new Vector2(iconSize * .5f), new Vector2(iconSize)));
                Place(WarningTextShadow, center + new Vector2(2f, iconSize * .5f + 2f), .5f);
                Place(WarningText, center + new Vector2(0f, iconSize * .5f), .5f);
            }
        }

        static void Place(VisualElement element, Vector2 anchor, float horizontalAnchor, bool centerY = false)
        {
            var size = element.DesiredSize;
            element.Arrange(new RectangleF(anchor - new Vector2(size.X * horizontalAnchor,
                centerY ? size.Y * .5f : 0f), size));
        }

        static void PlaceName(TextBlock text, float left, float centerY, float width)
        {
            // Re-measure against the actual slot so TextTrimming owns fitting and
            // caches it by text/font/width instead of accumulating app cache keys.
            text.Measure(new Vector2(width, float.PositiveInfinity));
            text.Arrange(new RectangleF(left, centerY - text.DesiredSize.Y * .5f, width, text.DesiredSize.Y));
        }
    }
}
