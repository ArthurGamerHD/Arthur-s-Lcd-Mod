using System;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Core.PropertySystem;
using Adk.Gui.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.MediaPlayer
{
    internal enum MediaGlyphKind
    {
        None,
        Folder,
        StopSquare,
        Playlist,
        PreviousTrack,
        NextTrack,
        Play,
        Pause,
        Shuffle,
        Repeat,
        Save,
        FileIcon
    }

    static class MediaDrawing
    {
        public static void Pill(RenderContext context, RectangleF rect, Color color)
        {
            if (rect.Width <= 0f || rect.Height <= 0f || color.A == 0)
                return;
            float diameter = Math.Min(rect.Height, rect.Width);
            float radius = diameter * .5f;
            context.Texture("Circle", new RectangleF(rect.X, rect.Center.Y - radius, diameter, diameter), color);
            context.Texture("Circle", new RectangleF(rect.Right - diameter, rect.Center.Y - radius, diameter, diameter), color);
            if (rect.Width > diameter)
                context.Fill(new RectangleF(rect.X + radius, rect.Y, rect.Width - diameter, rect.Height), color);
        }

        public static void Capsule(RenderContext context, RectangleF rect, Color color, bool roundLeft, bool roundRight)
        {
            float radius = Math.Max(0f, Math.Min(rect.Width, rect.Height) * .5f);
            float left = rect.X, right = rect.Right;
            if (roundLeft)
            {
                context.Texture("Circle", new RectangleF(left, rect.Y, radius * 2f, rect.Height), color);
                left += radius;
            }
            if (roundRight)
            {
                context.Texture("Circle", new RectangleF(right - radius * 2f, rect.Y, radius * 2f, rect.Height), color);
                right -= radius;
            }
            if (right > left)
                context.Fill(new RectangleF(left, rect.Y, right - left, rect.Height), color);
        }

        public static void Centered(RenderContext context, string texture, Vector2 center, Vector2 size, Color color,
            float rotation = 0f)
        {
            context.Texture(texture, new RectangleF(center - size * .5f, size), color, false, rotation);
        }

        public static void Circle(RenderContext context, Vector2 center, float diameter, Color color)
        {
            Centered(context, "Circle", center, new Vector2(diameter), color);
        }
    }

    internal sealed class MediaGlyph : VisualElement
    {
        public static readonly StyledProperty<MediaGlyphKind> KindProperty =
            StyledProperty.Register<MediaGlyph, MediaGlyphKind>(nameof(Kind), MediaGlyphKind.None, UiInvalidation.Render);
        public MediaGlyphKind Kind { get { return GetValue(KindProperty); } set { SetValue(KindProperty, value); } }

        public static readonly StyledProperty<Color> GlyphColorProperty =
            StyledProperty.Register<MediaGlyph, Color>(nameof(GlyphColor), Color.White, UiInvalidation.Render);
        public Color GlyphColor { get { return GetValue(GlyphColorProperty); } set { SetValue(GlyphColorProperty, value); } }

        public static readonly StyledProperty<string> TextureProperty =
            StyledProperty.Register<MediaGlyph, string>(nameof(Texture), string.Empty, UiInvalidation.Render);
        public string Texture { get { return GetValue(TextureProperty); } set { SetValue(TextureProperty, value); } }

        public static readonly StyledProperty<float> SizeRatioProperty =
            StyledProperty.Register<MediaGlyph, float>(nameof(SizeRatio), .62f, UiInvalidation.Render);
        public float SizeRatio { get { return GetValue(SizeRatioProperty); } set { SetValue(SizeRatioProperty, value); } }

        public static readonly StyledProperty<string> SuffixProperty =
            StyledProperty.Register<MediaGlyph, string>(nameof(Suffix), string.Empty, UiInvalidation.Render);
        public string Suffix { get { return GetValue(SuffixProperty); } set { SetValue(SuffixProperty, value); } }

        public static readonly StyledProperty<float> SuffixFontSizeProperty =
            StyledProperty.Register<MediaGlyph, float>(nameof(SuffixFontSize), 1f, UiInvalidation.Render);
        public float SuffixFontSize { get { return GetValue(SuffixFontSizeProperty); } set { SetValue(SuffixFontSizeProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<MediaGlyph, string>(nameof(FontFamily), "White", UiInvalidation.Render);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        protected override bool HitTestSelf(Vector2 point)
        {
            return false;
        }

        protected override void DrawSelf(RenderContext context)
        {
            RectangleF rect = Bounds;
            if (rect.Width <= 0f || rect.Height <= 0f)
                return;
            float size = Math.Max(1f, Math.Min(rect.Width, rect.Height));
            Vector2 center = rect.Center;
            Color color = GlyphColor;
            switch (Kind)
            {
                case MediaGlyphKind.Folder:
                    MediaDrawing.Centered(context, "Folder", center, new Vector2(size * .62f), color);
                    break;
                case MediaGlyphKind.Shuffle:
                    MediaDrawing.Centered(context, "Shuffle", center, new Vector2(size * .62f), color);
                    break;
                case MediaGlyphKind.Save:
                    MediaDrawing.Centered(context, "Diskette", center, new Vector2(size * .62f), color);
                    break;
                case MediaGlyphKind.FileIcon:
                    MediaDrawing.Centered(context, string.IsNullOrEmpty(Texture) ? "FileWav" : Texture, center,
                        new Vector2(size * SizeRatio), color);
                    break;
                case MediaGlyphKind.StopSquare:
                    context.Fill(new RectangleF(center - new Vector2(size * SizeRatio * .5f), new Vector2(size * SizeRatio)), color);
                    break;
                case MediaGlyphKind.Playlist:
                    DrawPlaylist(context, center, size, color);
                    break;
                case MediaGlyphKind.PreviousTrack:
                case MediaGlyphKind.NextTrack:
                    DrawTrack(context, center, size, color, Kind == MediaGlyphKind.NextTrack);
                    break;
                case MediaGlyphKind.Play:
                    float triangle = size * .38f;
                    MediaDrawing.Centered(context, "Triangle", center + new Vector2(size * .035f, 0f), new Vector2(triangle),
                        color, MathHelper.PiOver2);
                    break;
                case MediaGlyphKind.Pause:
                    float barWidth = Math.Max(1f, size * .105f);
                    float barHeight = size * .43f;
                    float gap = size * .12f;
                    float offset = (barWidth + gap) * .5f;
                    context.Fill(new RectangleF(center.X - offset - barWidth * .5f, center.Y - barHeight * .5f, barWidth,
                        barHeight), color);
                    context.Fill(new RectangleF(center.X + offset - barWidth * .5f, center.Y - barHeight * .5f, barWidth,
                        barHeight), color);
                    break;
                case MediaGlyphKind.Repeat:
                    MediaDrawing.Centered(context, "Repeat", center, new Vector2(size * .62f), color);
                    string suffix = Suffix;
                    if (!string.IsNullOrEmpty(suffix) && TextMetrics != null)
                    {
                        Vector2 text = TextMetrics.Measure(FontFamily, suffix, SuffixFontSize);
                        context.Text(FontFamily, suffix, new Vector2(center.X + size * .20f, center.Y - text.Y * .5f + size * .12f),
                            SuffixFontSize, color, HorizontalAlignment.Left, VerticalAlignment.Top);
                    }
                    break;
            }
        }

        static void DrawPlaylist(RenderContext context, Vector2 center, float size, Color color)
        {
            float width = size * .44f;
            float height = Math.Max(1f, size * .055f);
            float gap = size * .15f;
            for (int i = -1; i <= 1; i++)
                context.Fill(new RectangleF(center.X - width * .5f, center.Y + gap * i - height * .5f, width, height), color);
        }

        static void DrawTrack(RenderContext context, Vector2 center, float size, Color color, bool forward)
        {
            float triangle = size * .27f;
            float overlap = triangle * .16f;
            float step = Math.Max(1f, triangle - overlap);
            float groupWidth = triangle * 2f - overlap - triangle * .08f;
            float first = center.X - groupWidth * .5f + triangle * .5f;
            float rotation = forward ? MathHelper.PiOver2 : -MathHelper.PiOver2;
            MediaDrawing.Centered(context, "Triangle", new Vector2(first, center.Y), new Vector2(triangle), color, rotation);
            MediaDrawing.Centered(context, "Triangle", new Vector2(first + step, center.Y), new Vector2(triangle), color, rotation);
        }
    }

    internal sealed class MediaTransportBackground : VisualElement
    {
        public static readonly StyledProperty<float> GapProperty =
            StyledProperty.Register<MediaTransportBackground, float>(nameof(Gap), 8f, UiInvalidation.Render);
        public float Gap { get { return GetValue(GapProperty); } set { SetValue(GapProperty, value); } }

        public static readonly StyledProperty<Color> PreviousColorProperty =
            StyledProperty.Register<MediaTransportBackground, Color>(nameof(PreviousColor), Color.Gray, UiInvalidation.Render);
        public Color PreviousColor { get { return GetValue(PreviousColorProperty); } set { SetValue(PreviousColorProperty, value); } }

        public static readonly StyledProperty<Color> NextColorProperty =
            StyledProperty.Register<MediaTransportBackground, Color>(nameof(NextColor), Color.Gray, UiInvalidation.Render);
        public Color NextColor { get { return GetValue(NextColorProperty); } set { SetValue(NextColorProperty, value); } }

        public static readonly StyledProperty<Color> PlayColorProperty =
            StyledProperty.Register<MediaTransportBackground, Color>(nameof(PlayColor), Color.Gray, UiInvalidation.Render);
        public Color PlayColor { get { return GetValue(PlayColorProperty); } set { SetValue(PlayColorProperty, value); } }

        /// <summary>The buttons these shapes belong to; each shape follows its button's render scale, e.g. while pressed.</summary>
        public Control PreviousButton { get; set; }
        public Control PlayButton { get; set; }
        public Control NextButton { get; set; }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        static float ScaleOf(Control button)
        {
            var scale = button == null ? null : button.RenderTransform as Adk.Gui.Styling.ScaleTransform;
            return scale == null ? 1f : Math.Max(0f, Math.Min(scale.ScaleX, scale.ScaleY));
        }

        static RectangleF ScaleAround(RectangleF rect, Vector2 origin, float scale)
        {
            return new RectangleF(origin + (rect.Position - origin) * scale, rect.Size * scale);
        }

        protected override void DrawSelf(RenderContext context)
        {
            RectangleF area = Bounds;
            if (area.Width <= 0f || area.Height <= 0f)
                return;
            float gap = Gap;
            float width = Math.Max(1f, (area.Width - gap * 6f) / 7f);
            var previous = new RectangleF(area.X + 2f * (width + gap), area.Y, width, area.Height);
            var play = new RectangleF(area.X + 3f * (width + gap), area.Y, width, area.Height);
            var next = new RectangleF(area.X + 4f * (width + gap), area.Y, width, area.Height);
            float decoratorHeight = Math.Max(1f, area.Height * .82f);
            float decoratorY = area.Center.Y - decoratorHeight * .5f;
            MediaDrawing.Capsule(context, ScaleAround(new RectangleF(previous.X, decoratorY,
                Math.Max(1f, play.Center.X - previous.X), decoratorHeight), previous.Center, ScaleOf(PreviousButton)),
                PreviousColor, true, false);
            MediaDrawing.Capsule(context, ScaleAround(new RectangleF(play.Center.X, decoratorY,
                Math.Max(1f, next.Right - play.Center.X), decoratorHeight), next.Center, ScaleOf(NextButton)),
                NextColor, false, true);
            MediaDrawing.Circle(context, play.Center,
                Math.Max(1f, Math.Min(play.Width, play.Height) * 1.1f * ScaleOf(PlayButton)), PlayColor);
        }
    }

    internal sealed class MediaProgress : VisualElement
    {
        public static readonly StyledProperty<double> PositionSecondsProperty =
            StyledProperty.Register<MediaProgress, double>(nameof(PositionSeconds), 0d, UiInvalidation.Render);
        public double PositionSeconds { get { return GetValue(PositionSecondsProperty); } set { SetValue(PositionSecondsProperty, value); } }

        public static readonly StyledProperty<double> DurationSecondsProperty =
            StyledProperty.Register<MediaProgress, double>(nameof(DurationSeconds), 0d, UiInvalidation.Render);
        public double DurationSeconds { get { return GetValue(DurationSecondsProperty); } set { SetValue(DurationSecondsProperty, value); } }

        public static readonly StyledProperty<bool> CanSeekProperty =
            StyledProperty.Register<MediaProgress, bool>(nameof(CanSeek), false, UiInvalidation.Render);
        public bool CanSeek { get { return GetValue(CanSeekProperty); } set { SetValue(CanSeekProperty, value); } }

        public static readonly StyledProperty<Color> TextColorProperty =
            StyledProperty.Register<MediaProgress, Color>(nameof(TextColor), Color.White, UiInvalidation.Render);
        public Color TextColor { get { return GetValue(TextColorProperty); } set { SetValue(TextColorProperty, value); } }

        public static readonly StyledProperty<Color> TrackColorProperty =
            StyledProperty.Register<MediaProgress, Color>(nameof(TrackColor), Color.Gray, UiInvalidation.Render);
        public Color TrackColor { get { return GetValue(TrackColorProperty); } set { SetValue(TrackColorProperty, value); } }

        public static readonly StyledProperty<Color> FillColorProperty =
            StyledProperty.Register<MediaProgress, Color>(nameof(FillColor), Color.White, UiInvalidation.Render);
        public Color FillColor { get { return GetValue(FillColorProperty); } set { SetValue(FillColorProperty, value); } }

        public static readonly StyledProperty<string> FontFamilyProperty =
            StyledProperty.Register<MediaProgress, string>(nameof(FontFamily), "White", UiInvalidation.Render);
        public string FontFamily { get { return GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }

        public static readonly StyledProperty<float> LayoutScaleProperty =
            StyledProperty.Register<MediaProgress, float>(nameof(LayoutScale), 1f, UiInvalidation.Render);
        public float LayoutScale { get { return GetValue(LayoutScaleProperty); } set { SetValue(LayoutScaleProperty, value); } }

        public Action<double> SeekRequested { get; set; }

        bool _dragging;
        double _dragSeconds;

        float TextScale => Math.Max(.4f, .55f * LayoutScale);

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        protected override bool HitTestSelf(Vector2 point)
        {
            return CanSeek;
        }

        internal override void PointerPressed(Vector2 point)
        {
            if (!CanSeek)
                return;
            _dragging = true;
            _dragSeconds = SecondsAt(point);
            Invalidate(UiInvalidation.Render);
        }

        internal override void PointerMoved(Vector2 point, Vector2 delta)
        {
            if (!_dragging)
                return;
            _dragSeconds = SecondsAt(point);
            Invalidate(UiInvalidation.Render);
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            if (!_dragging)
                return;
            _dragging = false;
            _dragSeconds = SecondsAt(point);
            if (SeekRequested != null)
                SeekRequested(_dragSeconds);
            Invalidate(UiInvalidation.Render);
        }

        protected override void DrawSelf(RenderContext context)
        {
            RectangleF row = Bounds;
            if (row.Width <= 0f || row.Height <= 0f || TextMetrics == null)
                return;
            double duration = Math.Max(0d, DurationSeconds);
            double position = Math.Max(0d, _dragging ? _dragSeconds : PositionSeconds);
            if (duration > 0d && position > duration)
                position = duration;
            float fraction = duration <= 0d ? 0f : (float)MathHelper.Clamp(position / duration, 0d, 1d);
            if (fraction > .99f)
                fraction = 1f;

            string current = FormatTime(position);
            string total = FormatTime(duration);
            float timeWidth = TimeWidth(current, total);
            float textHeight = TextMetrics.Measure(FontFamily, "M", TextScale).Y;
            float textY = row.Center.Y - textHeight * .5f;
            context.Text(FontFamily, current, new Vector2(row.X, textY), TextScale, TextColor, HorizontalAlignment.Left,
                VerticalAlignment.Top);
            context.Text(FontFamily, total, new Vector2(row.Right, textY), TextScale, TextColor, HorizontalAlignment.Right,
                VerticalAlignment.Top);

            RectangleF bar = BarBounds(row, timeWidth);
            if (fraction < 1f)
                MediaDrawing.Pill(context, bar, TrackColor);
            float fillWidth = bar.Width * fraction;
            if (fillWidth > .001f)
            {
                using (context.PushClip(new RectangleF(bar.X, bar.Y, fillWidth, bar.Height)))
                    MediaDrawing.Pill(context, bar, FillColor);
            }
            float diameter = MathHelper.Clamp(bar.Height * 2.2f, 8f * LayoutScale, 14f * LayoutScale);
            MediaDrawing.Circle(context, new Vector2(bar.X + bar.Width * fraction, bar.Center.Y), diameter, FillColor);
        }

        double SecondsAt(Vector2 point)
        {
            RectangleF bar = BarBounds(Bounds, TimeWidth(FormatTime(PositionSeconds), FormatTime(DurationSeconds)));
            if (bar.Width <= 0f)
                return 0d;
            return MathHelper.Clamp((point.X - bar.X) / bar.Width, 0f, 1f) * Math.Max(0d, DurationSeconds);
        }

        float TimeWidth(string current, string total)
        {
            if (TextMetrics == null)
                return 38f * LayoutScale;
            return Math.Max(38f * LayoutScale,
                Math.Max(TextMetrics.Measure(FontFamily, current, TextScale).X, TextMetrics.Measure(FontFamily, total, TextScale).X) +
                4f * LayoutScale);
        }

        RectangleF BarBounds(RectangleF row, float timeWidth)
        {
            float gap = Math.Max(4f, 8f * LayoutScale);
            float height = MathHelper.Clamp(5f * LayoutScale, 3f, 8f);
            return new RectangleF(row.X + timeWidth + gap, row.Center.Y - height * .5f,
                Math.Max(1f, row.Width - timeWidth * 2f - gap * 2f), height);
        }

        static string FormatTime(double seconds)
        {
            int total = (int)Math.Round(Math.Max(0d, double.IsNaN(seconds) || double.IsInfinity(seconds) ? 0d : seconds));
            int hours = total / 3600;
            int minutes = total / 60 % 60;
            int secs = total % 60;
            return hours > 0
                ? hours + ":" + minutes.ToString("00") + ":" + secs.ToString("00")
                : minutes + ":" + secs.ToString("00");
        }
    }

    internal sealed class MediaVolumeSlider : VisualElement
    {
        public static readonly StyledProperty<float> ValueProperty =
            StyledProperty.Register<MediaVolumeSlider, float>(nameof(Value), 1f, UiInvalidation.Render);
        public float Value { get { return GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }

        public static readonly StyledProperty<Color> TrackColorProperty =
            StyledProperty.Register<MediaVolumeSlider, Color>(nameof(TrackColor), Color.Gray, UiInvalidation.Render);
        public Color TrackColor { get { return GetValue(TrackColorProperty); } set { SetValue(TrackColorProperty, value); } }

        public static readonly StyledProperty<Color> FillColorProperty =
            StyledProperty.Register<MediaVolumeSlider, Color>(nameof(FillColor), Color.White, UiInvalidation.Render);
        public Color FillColor { get { return GetValue(FillColorProperty); } set { SetValue(FillColorProperty, value); } }

        public static readonly StyledProperty<float> LayoutScaleProperty =
            StyledProperty.Register<MediaVolumeSlider, float>(nameof(LayoutScale), 1f, UiInvalidation.Render);
        public float LayoutScale { get { return GetValue(LayoutScaleProperty); } set { SetValue(LayoutScaleProperty, value); } }

        public Action<float> ValueChanged { get; set; }

        bool _dragging;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        protected override bool HitTestSelf(Vector2 point)
        {
            return true;
        }

        internal override void PointerPressed(Vector2 point)
        {
            _dragging = true;
            Commit(point);
        }

        internal override void PointerMoved(Vector2 point, Vector2 delta)
        {
            if (_dragging)
                Commit(point);
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            if (_dragging)
                Commit(point);
            _dragging = false;
        }

        void Commit(Vector2 point)
        {
            RectangleF track = TrackBounds();
            if (track.Width <= 0f)
                return;
            float value = MathHelper.Clamp((point.X - track.X) / track.Width, 0f, 1f);
            Value = value;
            if (ValueChanged != null)
                ValueChanged(value);
        }

        RectangleF TrackBounds()
        {
            RectangleF rect = Bounds;
            float height = Math.Min(Math.Max(2f, rect.Height * .5f), Math.Max(2f, 4f * LayoutScale));
            float inset = Math.Min(Math.Max(2f, rect.Width * .08f), Math.Max(6f, 8f * LayoutScale));
            return new RectangleF(rect.X + inset, rect.Center.Y - height * .5f, Math.Max(1f, rect.Width - inset * 2f), height);
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (Bounds.Width <= 0f || Bounds.Height <= 0f)
                return;
            RectangleF track = TrackBounds();
            float value = MathHelper.Clamp(Value, 0f, 1f);
            context.Fill(track, TrackColor);
            if (track.Width * value > .25f)
                context.Fill(new RectangleF(track.X, track.Y, track.Width * value, track.Height), FillColor);
            float diameter = MathHelper.Clamp(track.Height * 2.8f, 7f * LayoutScale, Math.Max(8f * LayoutScale, Bounds.Height));
            MediaDrawing.Circle(context, new Vector2(track.X + track.Width * value, track.Center.Y), diameter, FillColor);
        }
    }

    internal sealed class MediaVisualizer : VisualElement
    {
        public static readonly StyledProperty<string> SourceProperty =
            StyledProperty.Register<MediaVisualizer, string>(nameof(Source), null, UiInvalidation.Render);
        public string Source { get { return GetValue(SourceProperty); } set { SetValue(SourceProperty, value); } }

        public static readonly StyledProperty<int> VersionProperty =
            StyledProperty.Register<MediaVisualizer, int>(nameof(Version), 0, UiInvalidation.Render);
        public int Version { get { return GetValue(VersionProperty); } set { SetValue(VersionProperty, value); } }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            return Vector2.Zero;
        }

        protected override bool HitTestSelf(Vector2 point)
        {
            return Bounds.Contains(point);
        }

        protected override void DrawSelf(RenderContext context)
        {
            if (string.IsNullOrEmpty(Source) || Bounds.Width <= 0f || Bounds.Height <= 0f)
                return;
            // The texture is square; drawing it square keeps circles round on any screen shape.
            float side = Math.Min(Bounds.Width, Bounds.Height);
            context.DrawTexture(TextureSource.FromKey(Source),
                new RectangleF(Bounds.Center - new Vector2(side * .5f), new Vector2(side)), Color.White);
        }
    }
}

namespace LcdMod.Client.Apps.MediaPlayer
{
    internal sealed class MediaPlaylistRow : Adk.Gui.Controls.Button
    {
        public static readonly StyledProperty<bool> IsCurrentProperty =
            StyledProperty.Register<MediaPlaylistRow, bool>(nameof(IsCurrent), false, UiInvalidation.Style);
        public bool IsCurrent { get { return GetValue(IsCurrentProperty); } set { SetValue(IsCurrentProperty, value); } }

        public static readonly StyledProperty<float> HandleWidthProperty =
            StyledProperty.Register<MediaPlaylistRow, float>(nameof(HandleWidth), 30f, UiInvalidation.None);
        public float HandleWidth { get { return GetValue(HandleWidthProperty); } set { SetValue(HandleWidthProperty, value); } }

        bool _dragging;
        float _pressY;

        protected override void UpdatePseudoClasses()
        {
            base.UpdatePseudoClasses();
            PseudoClasses.Set(":selected", IsCurrent);
        }

        internal override void PointerPressed(Vector2 point)
        {
            _dragging = point.X >= Bounds.Right - HandleWidth - 12f;
            _pressY = point.Y;
            if (!_dragging)
                base.PointerPressed(point);
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            if (!_dragging)
            {
                base.PointerReleased(point, clicked);
                return;
            }
            _dragging = false;
            var row = DataContext as MediaPlaylistRowModel;
            if (row != null && Bounds.Height > 0f)
                row.Move((int)Math.Round((point.Y - _pressY) / Bounds.Height));
        }
    }
}
