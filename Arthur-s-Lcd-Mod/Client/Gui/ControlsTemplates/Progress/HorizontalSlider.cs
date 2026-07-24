using System;
using System.Collections.Generic;
using LcdMod.Client.Utility;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Progress
{
    public sealed class HorizontalSliderModel : ControlModelBase
    {
        public HorizontalSliderModel()
        {
            Cursor = CursorType.Hand;
            Value = 1f;
        }

        public float Value { get; set; }
        public Action<float> ValueChanged { get; set; }
        public Color? TrackColor { get; set; }
        public Color? FillColor { get; set; }
        public Color? ThumbColor { get; set; }

        public override bool CanClick
        {
            get { return ValueChanged != null; }
        }
    }

    public sealed class HorizontalSlider : RectangleControl
    {
        public HorizontalSlider(RectangleF bounds, HorizontalSliderModel model)
            : base(bounds, CursorType.Hand, model ?? new HorizontalSliderModel())
        {
        }

        HorizontalSliderModel SliderModel
        {
            get { return DataContext as HorizontalSliderModel; }
        }

        public override bool CanPrimaryClick
        {
            get
            {
                var model = SliderModel;
                return Visible && Enabled && model != null && model.CanClick;
            }
        }

        public override bool CanDrag
        {
            get
            {
                var model = SliderModel;
                return Visible && Enabled && model != null && model.CanClick;
            }
        }

        public override bool ClickAt(Vector2 point, object sender)
        {
            CommitValueFromPoint(point);
            return true;
        }

        public override bool BeginDrag(object sender)
        {
            if (!CanDrag)
                return false;

            Vector2 point;
            if (TryGetHitTestPoint(sender, out point))
                CommitValueFromPoint(point);

            return true;
        }

        public override bool Drag(object sender, Vector2 delta)
        {
            if (!CanDrag)
                return false;

            Vector2 point;
            if (!TryGetHitTestPoint(sender, out point))
                return false;

            CommitValueFromPoint(point);
            return true;
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var model = SliderModel;
            var rect = GetViewBox();
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;

            float layoutScale = LayoutScale <= 0f ? 1f : LayoutScale;
            float value = MathHelper.Clamp(model == null ? 0f : model.Value, 0f, 1f);
            var track = model != null && model.TrackColor.HasValue ? model.TrackColor.Value : BackgroundColor;
            var fill = model != null && model.FillColor.HasValue ? model.FillColor.Value : TextColor;
            var thumb = model != null && model.ThumbColor.HasValue ? model.ThumbColor.Value : fill;

            RectangleF trackRect;
            GetTrackBounds(rect, layoutScale, out trackRect);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = new Vector2(trackRect.X, trackRect.Center.Y),
                Size = trackRect.Size,
                Color = track,
                Alignment = TextAlignment.LEFT
            });

            float fillWidth = trackRect.Width * value;
            if (fillWidth > .25f)
            {
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = new Vector2(trackRect.X, trackRect.Center.Y),
                    Size = new Vector2(fillWidth, trackRect.Height),
                    Color = fill,
                    Alignment = TextAlignment.LEFT
                });
            }

            float thumbDiameter = MathHelper.Clamp(trackRect.Height * 2.8f, 7f * layoutScale, Math.Max(8f * layoutScale, rect.Height));
            float thumbX = trackRect.X + trackRect.Width * value;
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = new Vector2(thumbX, trackRect.Center.Y),
                Size = new Vector2(thumbDiameter, thumbDiameter),
                Color = thumb,
                Alignment = TextAlignment.CENTER
            });
        }

        void CommitValueFromPoint(Vector2 point)
        {
            var model = SliderModel;
            if (model == null || model.ValueChanged == null)
                return;

            RectangleF track;
            GetTrackBounds(GetViewBox(), LayoutScale <= 0f ? 1f : LayoutScale, out track);
            if (track.Width <= 0f)
                return;

            float value = MathHelper.Clamp((point.X - track.X) / track.Width, 0f, 1f);
            model.Value = value;
            model.ValueChanged(value);
        }

        static void GetTrackBounds(RectangleF rect, float layoutScale, out RectangleF track)
        {
            float maxHeight = Math.Max(2f, rect.Height * .5f);
            float height = Math.Min(maxHeight, Math.Max(2f, 4f * layoutScale));
            float maxInsetX = Math.Max(2f, rect.Width * .08f);
            float insetX = Math.Min(maxInsetX, Math.Max(6f, 8f * layoutScale));
            track = new RectangleF(
                rect.X + insetX,
                rect.Center.Y - height * .5f,
                Math.Max(1f, rect.Width - insetX * 2f),
                height);
        }

        static bool TryGetHitTestPoint(object sender, out Vector2 point)
        {
            point = default(Vector2);
            var screen = sender as IEyeTracking;
            if (screen == null)
                return false;

            point = screen.CursorPosition + screen.HitTestOffset;
            return !float.IsNaN(point.X) && !float.IsNaN(point.Y);
        }
    }
}
