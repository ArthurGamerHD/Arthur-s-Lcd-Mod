using System;
using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    public sealed class ScrollPanel : ControlBase
    {
        public const float DefaultScrollerWidthPixels = 8f;
        const long MANUAL_SCROLL_OVERRIDE_FRAMES = 300L;
        const float DEFAULT_MANUAL_SCROLL_PIXEL_MULTIPLIER = 0.08f;
        const float MANUAL_SCROLL_VELOCITY_IMPULSE = 0.12f;
        const float INERTIA_DECAY_PER_FRAME = 0.88f;
        const float STOP_VELOCITY_PIXELS_PER_FRAME = 0.05f;
        const long HOVER_LIFETIME_FRAMES = 2L;
        const float SCROLLBAR_CONTENT_MARGIN_RATIO = 0.5f;

        readonly ScrollBarTrackControl _scrollBarTrack;
        readonly ScrollBarThumbControl _scrollBarThumb;
        Panel _content;
        bool _automaticContentMode;
        bool _manualConfigured;
        float _contentExtentHeight;

        public ScrollPanel(CursorType? cursor = null, object dataContext = null)
            : base(cursor, dataContext)
        {
            _scrollBarTrack = new ScrollBarTrackControl(this);
            _scrollBarThumb = new ScrollBarThumbControl(this);

            AddScrollBarChildren();
        }

        public ScrollPanel(ControlBase parent)
            : this()
        {
            AttachTo(parent);
        }

        public ScrollPanel(ControlBase parent, RectangleF bounds)
            : this(parent)
        {
            SetRect(bounds);
        }

        public override void ClearChildren()
        {
            _content = null;
            _automaticContentMode = false;
            _manualConfigured = false;
            base.ClearChildren();
            AddScrollBarChildren();
        }

        void AddScrollBarChildren()
        {
            base.AddChild(_scrollBarTrack);
            base.AddChild(_scrollBarThumb);
        }

        public Panel Content { get { return _content; } }
        public RectangleF ViewBox { get; private set; }
        public RectangleF PanelBounds { get; private set; }
        public RectangleF ContentViewportBounds { get; private set; }
        public RectangleF ContentBounds { get; private set; }
        public float RowHeight { get; private set; }
        public float ScrollerWidthPixels { get; private set; }
        public float AutomaticScrollerWidthPixels { get; set; } = DefaultScrollerWidthPixels;
        public float ScrollStepPixels { get; set; } = 32f;
        public float AutoScrollSecondsPerStep { get; private set; }
        public float ManualScrollPixelMultiplier { get; set; } = DEFAULT_MANUAL_SCROLL_PIXEL_MULTIPLIER;
        public bool ManualScrollInertiaEnabled { get; set; } = true;
        public float ScrollOffsetPixels { get; private set; }
        public float RowOffsetPixels { get; private set; }
        public float ScrollVelocityPixelsPerFrame { get; private set; }
        public bool IsAnimating { get; private set; }
        public Action<ScrollPanel> ScrollChanged { get; set; }
        public int TotalRows { get; private set; }
        public int MaxVisibleRows { get; private set; }
        public int VisibleRows { get; private set; }
        public int RenderRows { get; private set; }
        public int StartRow { get; private set; }
        public bool IsScrollable { get; private set; }

        float _manualScrollPixels;
        long _manualOverrideUntilFrame = long.MinValue;
        long _lastInertiaFrame = long.MinValue;
        long _scrollBarThumbHoverFrame = long.MinValue;
        bool _scrollBarThumbDragging;
        bool _hasCustomScrollBarColors;
        Color _scrollBarTrackColor;
        Color _scrollBarThumbColor;

        struct ScrollBarMetrics
        {
            public RectangleF TrackHitBounds;
            public RectangleF TrackBounds;
            public RectangleF ThumbBounds;
            public float ThumbTravelPixels;
            public float MaxScrollOffsetPixels;
        }

        public override RectangleF Bounds => PanelBounds;

        public override bool CanScroll
        {
            get
            {
                if (_automaticContentMode)
                    EnsureAutomaticLayout();

                return Visible && IsScrollable;
            }
        }

        public override void AddChild(ControlBase child)
        {
            var panel = child as Panel;
            if (!_automaticContentMode && !IsManualConfigured() && panel != null)
            {
                SetContent(panel);
                return;
            }

            base.AddChild(child);
        }

        public override bool RemoveChild(ControlBase child)
        {
            if (ReferenceEquals(child, _content))
            {
                _content = null;
                _automaticContentMode = false;
            }

            return base.RemoveChild(child);
        }

        public void SetContent(Panel content)
        {
            if (ReferenceEquals(_content, content))
                return;

            if (_content != null)
                base.RemoveChild(_content);

            _content = content;
            _automaticContentMode = _content != null;
            if (_automaticContentMode)
                _manualConfigured = false;

            if (_content != null)
                base.AddChild(_content);

            InvalidateLayout();
        }

        public void SetRect(RectangleF bounds)
        {
            ViewBox = bounds;
            PanelBounds = bounds;
            InvalidateLayout();
        }

        public override void Arrange(RectangleF bounds)
        {
            SetRect(bounds);
            EnsureAutomaticLayout();
        }

        public void ConfigureAutomatic(
            RectangleF bounds,
            float scrollerWidthPixels,
            float scrollStepPixels,
            float autoScrollSecondsPerStep)
        {
            ViewBox = bounds;
            PanelBounds = bounds;
            AutomaticScrollerWidthPixels = Math.Max(0f, scrollerWidthPixels);
            ScrollStepPixels = Math.Max(1f, scrollStepPixels);
            RowHeight = ScrollStepPixels;
            AutoScrollSecondsPerStep = Math.Max(0f, autoScrollSecondsPerStep);
            _automaticContentMode = _content != null;
            _manualConfigured = false;
            InvalidateLayout();
            EnsureAutomaticLayout();
        }

        internal static ScrollPanel Create(
            RectangleF viewBox,
            float contentTop,
            float footerHeight,
            float rowHeight,
            int totalRows,
            float scrollerWidthPixels,
            int scrollStep)
        {
            var panel = new ScrollPanel();
            panel.Configure(viewBox, contentTop, footerHeight, rowHeight, totalRows, scrollerWidthPixels, scrollStep);
            return panel;
        }

        internal void Configure(
            RectangleF viewBox,
            float contentTop,
            float footerHeight,
            float rowHeight,
            int totalRows,
            float scrollerWidthPixels,
            float autoScrollSecondsPerStep)
        {
            AutoScrollSecondsPerStep = Math.Max(0f, autoScrollSecondsPerStep);

            ConfigureCore(
                viewBox,
                contentTop,
                footerHeight,
                rowHeight,
                totalRows,
                scrollerWidthPixels,
                GetScrollOffsetForCurrentMode);
        }

        void Configure(
            RectangleF viewBox,
            float contentTop,
            float footerHeight,
            float rowHeight,
            int totalRows,
            float scrollerWidthPixels,
            int autoScrollStep)
        {
            AutoScrollSecondsPerStep = 0f;

            ConfigureCore(
                viewBox,
                contentTop,
                footerHeight,
                rowHeight,
                totalRows,
                scrollerWidthPixels,
                maxScrollOffset => GetRowOffsetFromStep(autoScrollStep, maxScrollOffset));
        }

        void ConfigureCore(
            RectangleF viewBox,
            float contentTop,
            float footerHeight,
            float rowHeight,
            int totalRows,
            float scrollerWidthPixels,
            Func<float, float> scrollOffsetProvider)
        {
            _automaticContentMode = false;
            _manualConfigured = true;
            ViewBox = viewBox;
            RowHeight = Math.Max(1f, rowHeight);
            ScrollerWidthPixels = Math.Max(0f, scrollerWidthPixels);
            TotalRows = Math.Max(0, totalRows);

            float viewportHeight = Math.Max(0f, viewBox.Bottom - contentTop - Math.Max(0f, footerHeight));
            MaxVisibleRows = Math.Max(1, (int)Math.Floor(viewportHeight / RowHeight));

            float totalContentHeight = TotalRows * RowHeight;
            _contentExtentHeight = totalContentHeight;
            IsScrollable = totalContentHeight > viewportHeight + 0.001f;
            PanelBounds = new RectangleF(viewBox.X, contentTop, viewBox.Width, viewportHeight);

            float contentGutterWidth = IsScrollable
                ? ScrollerWidthPixels + GetScrollbarContentMarginPixels()
                : 0f;
            float contentWidth = Math.Max(1f, viewBox.Width - contentGutterWidth);
            ContentViewportBounds = new RectangleF(viewBox.X, contentTop, contentWidth, viewportHeight);

            if (!IsScrollable && TotalRows > 0)
            {
                _manualScrollPixels = 0f;
                _scrollBarThumbDragging = false;
                _scrollBarThumbHoverFrame = long.MinValue;
                ScrollVelocityPixelsPerFrame = 0f;
                IsAnimating = false;
            }

            float maxScrollOffset = GetMaxScrollOffsetPixels();

            if (TotalRows > 0 && IsScrollable)
            {
                _manualScrollPixels = Clamp(_manualScrollPixels, 0f, maxScrollOffset);
                UpdateManualScrollInertia(maxScrollOffset);
            }

            ScrollOffsetPixels = IsScrollable && scrollOffsetProvider != null
                ? Clamp(scrollOffsetProvider(maxScrollOffset), 0f, maxScrollOffset)
                : 0f;

            StartRow = Clamp((int)Math.Floor(ScrollOffsetPixels / RowHeight), 0, GetMaxStartRow());
            RowOffsetPixels = Math.Max(0f, ScrollOffsetPixels - StartRow * RowHeight);

            int maxRowsInViewport = Math.Max(1, (int)Math.Ceiling(viewportHeight / RowHeight));
            VisibleRows = TotalRows == 0 ? 0 : Math.Min(TotalRows - StartRow, maxRowsInViewport);

            int renderRowsForViewport = Math.Max(1, (int)Math.Ceiling((viewportHeight + RowOffsetPixels) / RowHeight) + 1);
            RenderRows = TotalRows == 0
                ? 0
                : Math.Min(TotalRows - StartRow, renderRowsForViewport);

            ContentBounds = new RectangleF(
                viewBox.X,
                contentTop - RowOffsetPixels,
                contentWidth,
                RenderRows * RowHeight);
        }

        void EnsureAutomaticLayout()
        {
            if (!_automaticContentMode)
                return;

            if (!IsLayoutDirty)
                return;

            ArrangeAutomaticContent();
        }

        void ArrangeAutomaticContent()
        {
            ScrollerWidthPixels = Math.Max(0f, AutomaticScrollerWidthPixels);

            if (_content == null)
            {
                ContentViewportBounds = PanelBounds;
                ContentBounds = PanelBounds;
                TotalRows = 0;
                MaxVisibleRows = 0;
                VisibleRows = 0;
                RenderRows = 0;
                StartRow = 0;
                RowOffsetPixels = 0f;
                ScrollOffsetPixels = 0f;
                _manualScrollPixels = 0f;
                _contentExtentHeight = 0f;
                IsScrollable = false;
                ValidateLayout();
                return;
            }

            var scrollContent = _content as IScrollContent;
            // Measure once without chrome, then again with the scrollbar gutter if content overflows.
            // This keeps wrapping panels stable when the scrollbar changes the available width.
            var viewport = CalculateAutomaticViewport(false);
            var desired = scrollContent != null
                ? scrollContent.MeasureContent(viewport.Size)
                : _content.Measure(viewport.Size);
            bool showScrollbar = desired.Y > viewport.Height + 0.001f;

            if (showScrollbar)
            {
                viewport = CalculateAutomaticViewport(true);
                desired = scrollContent != null
                    ? scrollContent.MeasureContent(viewport.Size)
                    : _content.Measure(viewport.Size);
            }

            ContentViewportBounds = viewport;
            _contentExtentHeight = Math.Max(0f, desired.Y);
            IsScrollable = _contentExtentHeight > viewport.Height + 0.001f;

            if (!IsScrollable)
            {
                _manualScrollPixels = 0f;
                _scrollBarThumbDragging = false;
                _scrollBarThumbHoverFrame = long.MinValue;
                ScrollVelocityPixelsPerFrame = 0f;
                IsAnimating = false;
            }

            float maxScrollOffset = GetMaxScrollOffsetPixels();

            if (IsScrollable)
            {
                _manualScrollPixels = Clamp(_manualScrollPixels, 0f, maxScrollOffset);
                UpdateManualScrollInertia(maxScrollOffset);
            }

            ScrollOffsetPixels = IsScrollable
                ? Clamp(GetScrollOffsetForCurrentMode(maxScrollOffset), 0f, maxScrollOffset)
                : 0f;

            RowHeight = Math.Max(1f, RowHeight);
            StartRow = Clamp((int)Math.Floor(ScrollOffsetPixels / RowHeight), 0, GetMaxStartRow());
            RowOffsetPixels = Math.Max(0f, ScrollOffsetPixels - StartRow * RowHeight);

            TotalRows = _content.HasChildren ? _content.Children.Count : 0;
            MaxVisibleRows = Math.Max(1, (int)Math.Floor(viewport.Height / RowHeight));
            VisibleRows = TotalRows;
            RenderRows = TotalRows;

            ContentBounds = new RectangleF(
                viewport.X,
                viewport.Y - ScrollOffsetPixels,
                viewport.Width,
                _contentExtentHeight);

            // Virtualized panels receive viewport geometry so they can bind only visible pooled controls.
            if (scrollContent != null)
                scrollContent.ArrangeViewport(viewport, ScrollOffsetPixels);
            else
                _content.Arrange(ContentBounds);
            ValidateLayout();
        }

        RectangleF CalculateAutomaticViewport(bool showScrollbar)
        {
            float gutterWidth = showScrollbar
                ? Math.Max(0f, ScrollerWidthPixels + GetScrollbarContentMarginPixels())
                : 0f;
            return new RectangleF(
                PanelBounds.X,
                PanelBounds.Y,
                Math.Max(0f, PanelBounds.Width - gutterWidth),
                Math.Max(0f, PanelBounds.Height));
        }

        float GetScrollbarContentMarginPixels()
        {
            return Math.Max(0f, ScrollerWidthPixels * SCROLLBAR_CONTENT_MARGIN_RATIO);
        }

        public override bool Scroll(object sender, int delta)
        {
            if (!CanScroll)
                return false;

            float pixelDelta = _automaticContentMode
                ? GetAutomaticScrollPixelDelta(delta)
                : GetManualScrollPixelDelta(delta);

            if (Math.Abs(pixelDelta) <= 0.001f)
                return false;

            _manualScrollPixels = Clamp(ScrollOffsetPixels + pixelDelta, 0f, GetMaxScrollOffsetPixels());
            AddScrollVelocity(pixelDelta);
            ScrollOffsetPixels = _manualScrollPixels;
            StartRow = Clamp((int)Math.Floor(ScrollOffsetPixels / RowHeight), 0, GetMaxStartRow());
            RowOffsetPixels = Math.Max(0f, ScrollOffsetPixels - StartRow * RowHeight);

            if (AutoScrollSecondsPerStep > 0f)
                _manualOverrideUntilFrame = GetFrameCounter() + MANUAL_SCROLL_OVERRIDE_FRAMES;

            if (_automaticContentMode)
                InvalidateLayout();
            else
                MarkDirty();

            ScrollChanged?.Invoke(this);
            return true;
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            if (_automaticContentMode)
            {
                EnsureAutomaticLayout();
                // Content is clipped separately from scrollbar chrome so the scrollbar remains interactive/visible.
                BeginClip(sprites, ContentViewportBounds);

                if (_content != null)
                    _content.Render(context, sprites);

                EndClip(sprites);
            }

            RenderScrollBar(sprites, GetScrollBarTrackColor(context), GetScrollBarThumbColor(context));
        }

        protected override bool IsDirtyAfterRender()
        {
            return IsAnimating || _scrollBarThumbDragging || IsScrollBarThumbHovered();
        }

        protected override bool HitCore(Vector2 point)
        {
            return PanelBounds.Contains(point);
        }

        public void SetScrollBarColors(Color trackColor, Color thumbColor)
        {
            _scrollBarTrackColor = trackColor;
            _scrollBarThumbColor = thumbColor;
            _hasCustomScrollBarColors = true;
            MarkDirty();
        }

        public void ResetScroll(bool notify = true)
        {
            StopScrollInertia();
            _manualScrollPixels = 0f;
            ScrollOffsetPixels = 0f;
            StartRow = 0;
            RowOffsetPixels = 0f;
            MarkDirty();
            if (notify)
                ScrollChanged?.Invoke(this);
        }

        public void ClearScrollBarColors()
        {
            if (!_hasCustomScrollBarColors)
                return;

            _hasCustomScrollBarColors = false;
            MarkDirty();
        }

        public void RenderScrollBar(List<MySprite> sprites, Color trackColor, Color thumbColor)
        {
            if (sprites == null)
                return;

            ScrollBarMetrics metrics;
            if (!TryGetScrollBarMetrics(out metrics))
                return;

            float barXCenter = metrics.TrackBounds.X + metrics.TrackBounds.Width / 2f;
            int barWidth = Math.Max(1, (int)Math.Round(metrics.TrackBounds.Width, MidpointRounding.AwayFromZero));

            var trackCenter = new Vector2(
                barXCenter,
                (float)Math.Round(metrics.TrackBounds.Y + metrics.TrackBounds.Height / 2f, MidpointRounding.ToEven));
            DrawCapsule(sprites, trackCenter, barWidth, metrics.TrackBounds.Height, trackColor);

            var thumbCenter = new Vector2(
                barXCenter,
                (float)Math.Round(metrics.ThumbBounds.Y + metrics.ThumbBounds.Height / 2f, MidpointRounding.ToEven));
            DrawCapsule(sprites, thumbCenter, barWidth, metrics.ThumbBounds.Height, GetThumbColor(thumbColor));
        }

        bool TryGetScrollBarMetrics(out ScrollBarMetrics metrics)
        {
            metrics = new ScrollBarMetrics();

            if (!IsScrollable || ScrollerWidthPixels <= 0f || GetTotalContentHeightPixels() <= 0f)
                return false;

            float gutterWidth = Math.Max(1f, ScrollerWidthPixels);
            float barWidth = GetScrollBarVisualWidthPixels(gutterWidth);
            float horizontalPadding = Math.Max(0f, (gutterWidth - barWidth) * 0.5f);
            float trackHeight = Math.Max(1f, ContentViewportBounds.Height - ScrollerWidthPixels * 2f);
            float totalContentHeight = Math.Max(1f, GetTotalContentHeightPixels());
            float thumbHeight = Math.Max(1f, Math.Min(trackHeight, ContentViewportBounds.Height / totalContentHeight * trackHeight));
            float maxScrollOffset = GetMaxScrollOffsetPixels();
            float thumbTravel = Math.Max(0f, trackHeight - thumbHeight);
            float scrollFraction = maxScrollOffset > 0f ? Clamp(ScrollOffsetPixels / maxScrollOffset, 0f, 1f) : 0f;
            float initialY = ContentViewportBounds.Y + ScrollerWidthPixels;
            float gutterX = ViewBox.X + ViewBox.Width - gutterWidth;
            float trackX = gutterX + horizontalPadding;
            float thumbY = initialY + thumbTravel * scrollFraction;

            metrics.TrackHitBounds = new RectangleF(gutterX, initialY, gutterWidth, trackHeight);
            metrics.TrackBounds = new RectangleF(trackX, initialY, barWidth, trackHeight);
            metrics.ThumbBounds = new RectangleF(trackX, thumbY, barWidth, thumbHeight);
            metrics.ThumbTravelPixels = thumbTravel;
            metrics.MaxScrollOffsetPixels = maxScrollOffset;
            return true;
        }

        bool TryGetScrollBarTrackBounds(out RectangleF bounds)
        {
            ScrollBarMetrics metrics;
            if (!TryGetScrollBarMetrics(out metrics))
            {
                bounds = default(RectangleF);
                return false;
            }

            bounds = metrics.TrackHitBounds;
            return true;
        }

        bool TryGetScrollBarThumbBounds(out RectangleF bounds)
        {
            ScrollBarMetrics metrics;
            if (!TryGetScrollBarMetrics(out metrics))
            {
                bounds = default(RectangleF);
                return false;
            }

            bounds = metrics.ThumbBounds;
            return true;
        }

        bool JumpScrollBarThumbToPoint(Vector2 point)
        {
            ScrollBarMetrics metrics;
            if (!TryGetScrollBarMetrics(out metrics) || !metrics.TrackHitBounds.Contains(point))
                return false;

            StopScrollInertia();
            return SetScrollOffsetFromThumbTop(point.Y - metrics.ThumbBounds.Height * 0.5f, metrics);
        }

        bool DragScrollBarThumbByDelta(Vector2 delta)
        {
            if (float.IsNaN(delta.X) || float.IsNaN(delta.Y))
                return false;

            ScrollBarMetrics metrics;
            if (!TryGetScrollBarMetrics(out metrics) || metrics.ThumbTravelPixels <= 0.001f)
                return false;

            float scrollDelta = metrics.MaxScrollOffsetPixels * (delta.Y / metrics.ThumbTravelPixels);
            return SetManualScrollOffsetPixels(ScrollOffsetPixels + scrollDelta);
        }

        void BeginScrollBarThumbDrag()
        {
            StopScrollInertia();
            _scrollBarThumbDragging = true;
            MarkDirty();
        }

        void EndScrollBarThumbDrag()
        {
            if (!_scrollBarThumbDragging)
                return;

            _scrollBarThumbDragging = false;
            MarkDirty();
        }

        void MarkScrollBarThumbHovered()
        {
            _scrollBarThumbHoverFrame = GetFrameCounter();
            MarkDirty();
        }

        bool IsScrollBarThumbHovered()
        {
            return _scrollBarThumbHoverFrame != long.MinValue &&
                   GetFrameCounter() - _scrollBarThumbHoverFrame <= HOVER_LIFETIME_FRAMES;
        }

        Color GetScrollBarTrackColor(ControlRenderContext context)
        {
            if (_hasCustomScrollBarColors)
                return _scrollBarTrackColor;

            var color = context != null ? context.TextColor : Color.White;
            return new Color(color.R, color.G, color.B, 127);
        }

        Color GetScrollBarThumbColor(ControlRenderContext context)
        {
            if (_hasCustomScrollBarColors)
                return _scrollBarThumbColor;

            var color = context != null ? context.PanelColor : Color.White;
            return new Color(color.R, color.G, color.B, 250);
        }

        static float GetScrollBarVisualWidthPixels(float gutterWidth) => gutterWidth <= 1f ? 1f : Math.Max(1f, Math.Min(gutterWidth, (float)Math.Round(gutterWidth * 0.5f, MidpointRounding.AwayFromZero)));

        Color GetThumbColor(Color thumbColor)
        {
            if (_scrollBarThumbDragging)
                return thumbColor.DeriveAccentColor().DeriveAccentColor();

            return IsScrollBarThumbHovered() ? thumbColor.DeriveAccentColor() : thumbColor;
        }

        bool SetScrollOffsetFromThumbTop(float thumbTop, ScrollBarMetrics metrics)
        {
            float scrollFraction = metrics.ThumbTravelPixels <= 0f
                ? 0f
                : Clamp((thumbTop - metrics.TrackBounds.Y) / metrics.ThumbTravelPixels, 0f, 1f);

            return SetManualScrollOffsetPixels(metrics.MaxScrollOffsetPixels * scrollFraction);
        }

        bool SetManualScrollOffsetPixels(float offsetPixels)
        {
            float maxScrollOffset = GetMaxScrollOffsetPixels();
            float clampedOffset = Clamp(offsetPixels, 0f, maxScrollOffset);
            float previousOffset = ScrollOffsetPixels;

            StopScrollInertia();
            _manualScrollPixels = clampedOffset;
            ScrollOffsetPixels = clampedOffset;
            StartRow = Clamp((int)Math.Floor(ScrollOffsetPixels / RowHeight), 0, GetMaxStartRow());
            RowOffsetPixels = Math.Max(0f, ScrollOffsetPixels - StartRow * RowHeight);

            if (AutoScrollSecondsPerStep > 0f)
                _manualOverrideUntilFrame = GetFrameCounter() + MANUAL_SCROLL_OVERRIDE_FRAMES;

            if (Math.Abs(previousOffset - ScrollOffsetPixels) <= 0.001f)
                return false;

            MarkDirty();
            ScrollChanged?.Invoke(this);
            return true;
        }

        void StopScrollInertia()
        {
            ScrollVelocityPixelsPerFrame = 0f;
            IsAnimating = false;
            _lastInertiaFrame = GetFrameCounter();
        }

        float GetScrollOffsetForCurrentMode(float maxScrollOffset)
        {
            if (maxScrollOffset <= 0f)
                return 0f;

            if (IsAutoScrolling())
                return GetAutoScrollOffset(AutoScrollSecondsPerStep, maxScrollOffset);

            return _manualScrollPixels;
        }

        int GetMaxStartRow()
        {
            return RowHeight <= 0f ? 0 : Math.Max(0, (int)Math.Floor(GetMaxScrollOffsetPixels() / RowHeight));
        }

        float GetMaxScrollOffsetPixels()
        {
            return Math.Max(0f, GetTotalContentHeightPixels() - ContentViewportBounds.Height);
        }

        float GetTotalContentHeightPixels()
        {
            return _automaticContentMode
                ? _contentExtentHeight
                : TotalRows * RowHeight;
        }

        bool IsManualConfigured()
        {
            return _manualConfigured;
        }

        float GetManualScrollPixelDelta(int wheelDelta)
        {
            if (wheelDelta == 0)
                return 0f;

            float multiplier = ManualScrollPixelMultiplier > 0f
                ? ManualScrollPixelMultiplier
                : DEFAULT_MANUAL_SCROLL_PIXEL_MULTIPLIER;

            float pixels = Math.Abs(wheelDelta) * multiplier;
            return wheelDelta > 0 ? -pixels : pixels;
        }

        float GetAutomaticScrollPixelDelta(int wheelDelta)
        {
            if (wheelDelta == 0)
                return 0f;

            float pixels = ScrollStepPixels > 0f ? ScrollStepPixels : 32f;
            return wheelDelta > 0 ? -pixels : pixels;
        }

        void AddScrollVelocity(float pixelDelta)
        {
            if (!ManualScrollInertiaEnabled)
            {
                StopScrollInertia();
                return;
            }

            float maxVelocity = Math.Max(1f, RowHeight);
            ScrollVelocityPixelsPerFrame = Clamp(
                ScrollVelocityPixelsPerFrame + pixelDelta * MANUAL_SCROLL_VELOCITY_IMPULSE,
                -maxVelocity,
                maxVelocity);

            IsAnimating = Math.Abs(ScrollVelocityPixelsPerFrame) > STOP_VELOCITY_PIXELS_PER_FRAME;
            _lastInertiaFrame = GetFrameCounter();
        }

        void UpdateManualScrollInertia(float maxScrollOffset)
        {
            if (_scrollBarThumbDragging)
                return;

            var previousScroll = _manualScrollPixels;
            var previousVelocity = ScrollVelocityPixelsPerFrame;

            if (Math.Abs(ScrollVelocityPixelsPerFrame) <= STOP_VELOCITY_PIXELS_PER_FRAME)
            {
                StopScrollInertia();
                return;
            }

            if (AutoScrollSecondsPerStep > 0f && GetFrameCounter() > _manualOverrideUntilFrame)
            {
                StopScrollInertia();
                return;
            }

            long frame = GetFrameCounter();

            if (_lastInertiaFrame == long.MinValue)
                _lastInertiaFrame = frame;

            long elapsed = Math.Max(0L, Math.Min(12L, frame - _lastInertiaFrame));
            _lastInertiaFrame = frame;

            for (long i = 0; i < elapsed; i++)
            {
                _manualScrollPixels = Clamp(_manualScrollPixels + ScrollVelocityPixelsPerFrame, 0f, maxScrollOffset);

                bool hitTop = _manualScrollPixels <= 0f && ScrollVelocityPixelsPerFrame < 0f;
                bool hitBottom = _manualScrollPixels >= maxScrollOffset && ScrollVelocityPixelsPerFrame > 0f;

                if (hitTop || hitBottom)
                {
                    ScrollVelocityPixelsPerFrame = 0f;
                    break;
                }

                ScrollVelocityPixelsPerFrame *= INERTIA_DECAY_PER_FRAME;
            }

            if (Math.Abs(ScrollVelocityPixelsPerFrame) <= STOP_VELOCITY_PIXELS_PER_FRAME)
                ScrollVelocityPixelsPerFrame = 0f;

            IsAnimating = Math.Abs(ScrollVelocityPixelsPerFrame) > STOP_VELOCITY_PIXELS_PER_FRAME;

            if (Math.Abs(_manualScrollPixels - previousScroll) > 0.001f ||
                Math.Abs(ScrollVelocityPixelsPerFrame - previousVelocity) > 0.001f ||
                IsAnimating)
                MarkDirty();
        }

        bool IsAutoScrolling()
        {
            return IsScrollable && AutoScrollSecondsPerStep > 0f && GetFrameCounter() > _manualOverrideUntilFrame;
        }

        float GetAutoScrollOffset(float secondsPerRow, float maxScrollOffset)
        {
            if (secondsPerRow <= 0f || maxScrollOffset <= 0f)
                return 0f;

            var framesPerStep = Math.Max(1, (int)Math.Round(secondsPerRow * 60f));
            var step = (int)(GetFrameCounter() / framesPerStep);
            return GetRowOffsetFromStep(step, maxScrollOffset);
        }

        float GetRowOffsetFromStep(int scrollStep, float maxScrollOffset)
        {
            if (maxScrollOffset <= 0f)
                return 0f;

            int maxStartRow = (int)Math.Floor(maxScrollOffset / RowHeight);
            return (maxStartRow <= 0 ? 0 : scrollStep % (maxStartRow + 1)) * RowHeight;
        }

        static long GetFrameCounter()
        {
            try
            {
                return MyAPIGateway.Session?.GameplayFrameCounter ?? 0L;
            }
            catch
            {
                return 0L;
            }
        }

        static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        static float Clamp(float value, float min, float max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        static void BeginClip(List<MySprite> sprites, RectangleF bounds)
        {
            if (sprites == null)
                return;

            int x = (int)Math.Floor(bounds.X);
            int y = (int)Math.Floor(bounds.Y);
            int right = (int)Math.Ceiling(bounds.Right);
            int bottom = (int)Math.Ceiling(bounds.Bottom);

            sprites.Add(MySprite.CreateClipRect(new Rectangle(
                x,
                y,
                Math.Max(0, right - x),
                Math.Max(0, bottom - y))));
        }

        static void EndClip(List<MySprite> sprites)
        {
            if (sprites != null)
                sprites.Add(MySprite.CreateClearClipRect());
        }

        static void DrawCapsule(List<MySprite> sprites, Vector2 center, int width, float height, Color color)
        {
            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SquareSimple",
                Position = center,
                Size = new Vector2(width, height + .5f),
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            var capsSize = new Vector2(width);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = new Vector2(center.X, center.Y - height / 2f),
                Size = capsSize,
                RotationOrScale = 0f,
                Color = color,
                Alignment = TextAlignment.CENTER
            });

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = "SemiCircle",
                Position = new Vector2(center.X, center.Y + height / 2f),
                Size = capsSize,
                RotationOrScale = (float)Math.PI,
                Color = color,
                Alignment = TextAlignment.CENTER
            });
        }

        sealed class ScrollBarTrackControl : ControlBase
        {
            readonly ScrollPanel _owner;

            public ScrollBarTrackControl(ScrollPanel owner)
                : base(CursorType.Arrow, owner)
            {
                _owner = owner;
                SetClickOnPress();
                SetOnClick((dataContext, sender) => { });
            }

            public override RectangleF Bounds
            {
                get
                {
                    RectangleF bounds;
                    return _owner != null && _owner.TryGetScrollBarTrackBounds(out bounds)
                        ? bounds
                        : default(RectangleF);
                }
            }

            public override bool CanPrimaryClick
            {
                get { return Visible && _owner != null && _owner.IsScrollable; }
            }

            public override bool ClickAt(Vector2 point, object sender)
            {
                return _owner != null && _owner.JumpScrollBarThumbToPoint(point);
            }

            protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
            {
            }

            protected override bool HitCore(Vector2 point)
            {
                RectangleF bounds;
                return _owner != null && _owner.TryGetScrollBarTrackBounds(out bounds) && bounds.Contains(point);
            }
        }

        sealed class ScrollBarThumbControl : ControlBase
        {
            readonly ScrollPanel _owner;

            public ScrollBarThumbControl(ScrollPanel owner)
                : base(CursorType.Arrow, owner)
            {
                _owner = owner;
                SetDraggable();
                SetOnBeginDrag((dataContext, sender) => _owner.BeginScrollBarThumbDrag());
                SetOnDrag((dataContext, sender, delta) => _owner.DragScrollBarThumbByDelta(delta));
                SetOnEndDrag((dataContext, sender) => _owner.EndScrollBarThumbDrag());
                SetOnHover((dataContext, sender) =>
                {
                    _owner.MarkScrollBarThumbHovered();
                    return true;
                });
            }

            public override RectangleF Bounds
            {
                get
                {
                    RectangleF bounds;
                    return _owner != null && _owner.TryGetScrollBarThumbBounds(out bounds)
                        ? bounds
                        : default(RectangleF);
                }
            }

            public override bool CanHover
            {
                get { return Visible && _owner != null && _owner.IsScrollable; }
            }

            public override bool CanDrag
            {
                get { return Visible && _owner != null && _owner.IsScrollable && Draggable; }
            }

            protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
            {
            }

            protected override bool HitCore(Vector2 point)
            {
                RectangleF bounds;
                return _owner != null && _owner.TryGetScrollBarThumbBounds(out bounds) && bounds.Contains(point);
            }
        }
    }
}
