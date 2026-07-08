#if EXPERIMENTAL
using System;
using System.Collections.Generic;
using LcdMod.Client.Utility;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Progress
{
    public sealed class AudioProgressModel : ControlModelBase
    {
        public AudioProgressModel()
        {
            Cursor = CursorType.Hand;
        }

        public double PositionSeconds { get; set; }
        public double DurationSeconds { get; set; }
        public bool SeekEnabled { get; set; }
        public Action<double> SeekRequested { get; set; }
        public Color? TextColor { get; set; }
        public Color? FillColor { get; set; }
        public Color? BackgroundColor { get; set; }
        public Color? ThumbColor { get; set; }

        public override bool CanClick
        {
            get { return SeekEnabled && SeekRequested != null; }
        }
    }

    /// <summary>
    /// Compact audio timeline row with current time, progress bar, duration, and
    /// an interactive rounded thumb at the fill tip.
    /// </summary>
    public sealed class AudioProgress : RectangleControl
    {
        const float MIN_BAR_HEIGHT = 3f;
        const float MAX_BAR_HEIGHT = 8f;
        const long DRAG_COMMIT_INTERVAL_TICKS = TimeSpan.TicksPerMillisecond * 150L;

        bool _dragging;
        bool _dragMoved;
        double _dragPositionSeconds;
        long _lastDragCommitTicks;

        public AudioProgress(RectangleF bounds, AudioProgressModel model)
            : base(bounds, CursorType.Hand, model ?? new AudioProgressModel())
        {
        }

        AudioProgressModel ProgressModel
        {
            get { return DataContext as AudioProgressModel; }
        }

        public override bool CanPrimaryClick
        {
            get
            {
                var model = ProgressModel;
                return Visible && Enabled && model != null && model.CanClick;
            }
        }

        public override bool CanDrag
        {
            get
            {
                var model = ProgressModel;
                return Visible && Enabled && model != null && model.CanClick;
            }
        }

        public override bool ClickAt(Vector2 point, object sender)
        {
            CommitSeekFromPoint(point, true);
            return true;
        }

        public override bool BeginDrag(object sender)
        {
            if (!CanDrag)
                return false;

            Vector2 point;
            if (!TryGetHitTestPoint(sender, out point))
                return true;

            _dragging = true;
            _dragMoved = false;
            _lastDragCommitTicks = 0L;
            _dragPositionSeconds = SecondsFromPoint(point);
            CommitSeek(_dragPositionSeconds, true);
            return true;
        }

        public override bool Drag(object sender, Vector2 delta)
        {
            if (!CanDrag)
                return false;

            Vector2 point;
            if (!TryGetHitTestPoint(sender, out point))
                return false;

            _dragging = true;
            _dragMoved = true;
            _dragPositionSeconds = SecondsFromPoint(point);
            CommitSeek(_dragPositionSeconds, false);
            return true;
        }

        public override void EndDrag(object sender)
        {
            if (_dragging && _dragMoved)
                CommitSeek(_dragPositionSeconds, true);

            _dragging = false;
            _dragMoved = false;
            base.EndDrag(sender);
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            var model = ProgressModel;
            var row = GetViewBox();
            float layoutScale = LayoutScale;
            double duration = NormalizeSeconds(model == null ? 0.0 : model.DurationSeconds);
            double position = NormalizeSeconds(_dragging ? _dragPositionSeconds : (model == null ? 0.0 : model.PositionSeconds));

            if (duration > 0.0 && position > duration)
                position = duration;

            float fraction = duration <= 0.0
                ? 0f
                : MathHelper.Clamp((float)(position / duration), 0f, 1f);

            var textScale = Math.Max(.4f, .55f * layoutScale);
            var currentText = FormatTime(position);
            var totalText = FormatTime(duration);
            var gap = Math.Max(4f, 8f * layoutScale);
            var timeWidth = Math.Max(
                38f * layoutScale,
                Math.Max(MeasureText(currentText, textScale).X, MeasureText(totalText, textScale).X) + 4f * layoutScale);
            var textY = row.Center.Y - GetLineHeight(textScale) * .5f;
            var textColor = model != null && model.TextColor.HasValue ? model.TextColor.Value : TextColor;
            var background = model != null && model.BackgroundColor.HasValue ? model.BackgroundColor.Value : BackgroundColor;
            var fill = model != null && model.FillColor.HasValue ? model.FillColor.Value : TextColor;
            var thumb = model != null && model.ThumbColor.HasValue ? model.ThumbColor.Value : fill;

            DrawAlignedText(sprites, currentText, row.X, textY, timeWidth, textScale, textColor, TextAlignment.LEFT);
            DrawAlignedText(sprites, totalText, row.Right - timeWidth, textY, timeWidth, textScale, textColor, TextAlignment.RIGHT);

            RectangleF bar;
            GetBarBounds(row, timeWidth, gap, layoutScale, out bar);
            BarPanel.CreateBackgroundSprites(
                sprites,
                new Vector2(bar.X, bar.Y),
                bar.Size,
                background,
                fraction,
                -1f,
                ProgressBarStyle.PillBleed);

            float fillWidth = bar.Width * (fraction > .99f ? 1f : MathHelper.Clamp(fraction, 0f, 1f));
            if (fillWidth > 0.001f)
            {
                var fillClip = new RectangleF(bar.X, bar.Y, fillWidth, bar.Height);
                if (BeginContentClip(sprites, fillClip))
                {
                    BarPanel.CreateFillSprites(
                        sprites,
                        new Vector2(bar.X, bar.Y),
                        bar.Size,
                        fill,
                        -1f,
                        ProgressBarStyle.PillBleed);
                    EndContentClip(sprites);
                }
            }

            DrawThumb(sprites, bar, fraction, thumb, layoutScale);
        }

        void DrawThumb(List<MySprite> sprites, RectangleF bar, float fraction, Color color, float layoutScale)
        {
            if (bar.Width <= 0f || bar.Height <= 0f)
                return;

            var clamped = fraction > .99f ? 1f : MathHelper.Clamp(fraction, 0f, 1f);
            var diameter = MathHelper.Clamp(bar.Height * 2.2f, 8f * layoutScale, 14f * layoutScale);
            var centerX = bar.X + bar.Width * clamped;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "Circle",
                Position = new Vector2(centerX, bar.Center.Y),
                Size = new Vector2(diameter, diameter),
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        void CommitSeekFromPoint(Vector2 point, bool force)
        {
            CommitSeek(SecondsFromPoint(point), force);
        }

        double SecondsFromPoint(Vector2 point)
        {
            var model = ProgressModel;
            if (model == null)
                return 0.0;

            RectangleF bar;
            GetBarBounds(GetViewBox(), GetTimeWidth(), Math.Max(4f, 8f * LayoutScale), LayoutScale, out bar);
            if (bar.Width <= 0f)
                return 0.0;

            var fraction = MathHelper.Clamp((point.X - bar.X) / bar.Width, 0f, 1f);
            return NormalizeSeconds(model.DurationSeconds) * fraction;
        }

        void CommitSeek(double seconds, bool force)
        {
            var model = ProgressModel;
            if (model == null || !model.CanClick)
                return;

            seconds = NormalizeSeconds(seconds);
            var duration = NormalizeSeconds(model.DurationSeconds);
            if (duration > 0.0 && seconds > duration)
                seconds = duration;

            var now = DateTime.UtcNow.Ticks;
            if (!force && _lastDragCommitTicks > 0L && now - _lastDragCommitTicks < DRAG_COMMIT_INTERVAL_TICKS)
                return;

            _lastDragCommitTicks = now;
            model.SeekRequested(seconds);
        }

        float GetTimeWidth()
        {
            var model = ProgressModel;
            double duration = NormalizeSeconds(model == null ? 0.0 : model.DurationSeconds);
            double position = NormalizeSeconds(_dragging ? _dragPositionSeconds : (model == null ? 0.0 : model.PositionSeconds));
            var textScale = Math.Max(.4f, .55f * LayoutScale);
            return Math.Max(
                38f * LayoutScale,
                Math.Max(MeasureText(FormatTime(position), textScale).X, MeasureText(FormatTime(duration), textScale).X) + 4f * LayoutScale);
        }

        static void GetBarBounds(RectangleF row, float timeWidth, float gap, float layoutScale, out RectangleF bar)
        {
            var barX = row.X + timeWidth + gap;
            var barWidth = Math.Max(1f, row.Width - timeWidth * 2f - gap * 2f);
            var barHeight = MathHelper.Clamp(5f * layoutScale, MIN_BAR_HEIGHT, MAX_BAR_HEIGHT);
            var barY = row.Center.Y - barHeight * .5f;
            bar = new RectangleF(barX, barY, barWidth, barHeight);
        }

        void DrawAlignedText(List<MySprite> sprites, string text, float x, float y, float width, float scale, Color color, TextAlignment alignment)
        {
            var data = TrimToWidth(text, width, scale);
            var positionX = x;
            if (alignment == TextAlignment.RIGHT)
                positionX = x + width;
            else if (alignment == TextAlignment.CENTER)
                positionX = x + width * .5f;

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXT,
                Data = data,
                Position = new Vector2(positionX, y),
                Color = color,
                FontId = TextFont,
                Alignment = alignment,
                RotationOrScale = scale
            });
        }

        string TrimToWidth(string text, float width, float scale)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (width <= 0f)
                return text;

            if (MeasureText(text, scale).X <= width)
                return text;

            const string ellipsis = "...";
            var max = text.Length;
            while (max > 0)
            {
                var candidate = text.Substring(0, max) + ellipsis;
                if (MeasureText(candidate, scale).X <= width)
                    return candidate;
                max--;
            }

            return ellipsis;
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

        static double NormalizeSeconds(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
                return 0.0;
            return seconds;
        }

        static string FormatTime(double seconds)
        {
            seconds = NormalizeSeconds(seconds);
            var totalSeconds = (int)Math.Round(seconds);
            var hours = totalSeconds / 3600;
            var minutes = (totalSeconds / 60) % 60;
            var secs = totalSeconds % 60;

            if (hours > 0)
                return hours + ":" + minutes.ToString("00") + ":" + secs.ToString("00");

            return minutes + ":" + secs.ToString("00");
        }
    }
}
#endif
