using System;
using System.Collections.Generic;
using LcdMod.Client.Extensions;
using LcdMod.Client.Gui.Styling;
using LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    public enum ScrollWheelRouting : byte
    {
        Automatic,
        Vertical,
        Horizontal,
        ShiftForHorizontal
    }

    [Flags]
    public enum ScrollAxis : byte
    {
        None = 0,
        Horizontal = 1,
        Vertical = 2
    }

    public sealed partial class ScrollPanel : ControlTemplate
    {
        public const float DefaultScrollerWidthPixels = 5f;
        const long MANUAL_SCROLL_OVERRIDE_FRAMES = 300L;
        const float DEFAULT_MANUAL_SCROLL_PIXEL_MULTIPLIER = 0.08f;
        const float MANUAL_SCROLL_VELOCITY_IMPULSE = 0.12f;
        const float INERTIA_DECAY_PER_FRAME = 0.88f;
        const float STOP_VELOCITY_PIXELS_PER_FRAME = 0.05f;
        const long HOVER_LIFETIME_FRAMES = 2L;
        const float SCROLLBAR_CONTENT_MARGIN_RATIO = 0.5f;

        public static readonly StyleProperty<Color> ScrollBarTrackColorProperty =
            StyleProperty.Register<ScrollPanel, Color>("ScrollBarTrackColor", null);

        public static readonly StyleProperty<Color> ScrollBarThumbColorProperty =
            StyleProperty.Register<ScrollPanel, Color>("ScrollBarThumbColor", null);

        public static readonly StyleProperty<Color> ScrollBarThumbHoverColorProperty =
            StyleProperty.Register<ScrollPanel, Color>("ScrollBarThumbHoverColor", null);

        public static readonly StyleProperty<Color> ScrollBarThumbPressedColorProperty =
            StyleProperty.Register<ScrollPanel, Color>("ScrollBarThumbPressedColor", null);

        readonly ScrollBarTrackControl _verticalScrollBarTrack;
        readonly ScrollBarThumbControl _verticalScrollBarThumb;
        readonly ScrollBarTrackControl _horizontalScrollBarTrack;
        readonly ScrollBarThumbControl _horizontalScrollBarThumb;
        Panel _content;
        bool _automaticContentMode;
        bool _manualConfigured;
        Vector2 _contentExtentPixels;
        Vector2 _manualScrollOffsetPixels;
        Vector2 _scrollVelocityPixelsPerFrame;
        float _configuredAutomaticScrollerWidthPixels = DefaultScrollerWidthPixels;
        float? _localAutoScrollSecondsPerStep;
        float _resolvedAutoScrollSecondsPerStep;

        public ScrollPanel(CursorType? cursor = null, object dataContext = null)
            : base(cursor, dataContext)
        {
            _verticalScrollBarTrack = new ScrollBarTrackControl(this, ScrollAxis.Vertical);
            _verticalScrollBarThumb = new ScrollBarThumbControl(this, ScrollAxis.Vertical);
            _horizontalScrollBarTrack = new ScrollBarTrackControl(this, ScrollAxis.Horizontal);
            _horizontalScrollBarThumb = new ScrollBarThumbControl(this, ScrollAxis.Horizontal);
            AddScrollBarChildren();
        }

        public ScrollPanel(ControlTemplate parent)
            : this()
        {
            AttachTo(parent);
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
            base.AddChild(_verticalScrollBarTrack);
            base.AddChild(_verticalScrollBarThumb);
            base.AddChild(_horizontalScrollBarTrack);
            base.AddChild(_horizontalScrollBarThumb);
        }

        public Panel Content => _content;
        public RectangleF ViewBox { get; private set; }
        public RectangleF PanelBounds { get; private set; }
        public RectangleF ContentViewportBounds { get; private set; }
        public RectangleF ContentBounds { get; private set; }
        public float RowHeight { get; private set; }
        public float ScrollerWidthPixels { get; private set; }
        public float AutomaticScrollerWidthPixels { get; set; } = DefaultScrollerWidthPixels;
        public float ScrollStepPixels { get; set; } = 32f;
        public float AutoScrollSecondsPerStep
        {
            get
            {
                UpdateResolvedAutoScrollSecondsPerStep();
                return _resolvedAutoScrollSecondsPerStep;
            }
            set { SetAutoScrollSecondsPerStep(value); }
        }
        public float ManualScrollPixelMultiplier { get; set; } = DEFAULT_MANUAL_SCROLL_PIXEL_MULTIPLIER;
        public bool ManualScrollInertiaEnabled { get; set; } = true;
        public Vector2 ScrollOffsetPixels2D { get; private set; }
        public float ScrollOffsetPixels => VerticalScrollOffsetPixels;
        public float HorizontalScrollOffsetPixels => ScrollOffsetPixels2D.X;
        public float VerticalScrollOffsetPixels => ScrollOffsetPixels2D.Y;
        public float RowOffsetPixels { get; private set; }
        public float ScrollVelocityPixelsPerFrame { get { return _scrollVelocityPixelsPerFrame.Y; } private set { _scrollVelocityPixelsPerFrame.Y = value; } }
        public bool IsAnimating { get; private set; }
        public Action<ScrollPanel> ScrollChanged { get; set; }
        public int TotalRows { get; private set; }
        public int MaxVisibleRows { get; private set; }
        public int VisibleRows { get; private set; }
        public int RenderRows { get; private set; }
        public int StartRow { get; private set; }
        public bool IsHorizontallyScrollable { get; private set; }
        public bool IsVerticallyScrollable { get; private set; }
        public bool IsScrollable => IsHorizontallyScrollable || IsVerticallyScrollable;
        public ScrollAxis EnabledScrollAxes { get; private set; } = ScrollAxis.Vertical;
        public ScrollWheelRouting WheelRouting { get; set; } = ScrollWheelRouting.Vertical;

        protected override bool ClipContent => true;

        protected override RectangleF ClipContentBounds => ContentViewportBounds;

        long _manualOverrideUntilFrame = long.MinValue;
        long _lastInertiaFrame = long.MinValue;
        long _scrollBarThumbHoverFrame = long.MinValue;
        bool _scrollBarThumbDragging;
        ScrollAxis _scrollBarThumbDraggingAxis = ScrollAxis.Vertical;
        struct ScrollBarMetrics
        {
            public ScrollAxis Axis;
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

        public override void AddChild(ControlTemplate child)
        {
            var panel = child as Panel;
            if (!_automaticContentMode && !IsManualConfigured() && panel != null)
            {
                SetContent(panel);
                return;
            }

            base.AddChild(child);
        }

        public bool RemoveChild(ControlTemplate child)
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

        public void ConfigureAutomatic(RectangleF bounds, float scrollerWidthPixels, float scrollStepPixels)
        {
            ConfigureAutomatic(bounds, scrollerWidthPixels, scrollStepPixels, ScrollAxis.Vertical);
        }

        public void ConfigureAutomatic(RectangleF bounds, float scrollerWidthPixels, float scrollStepPixels, ScrollAxis enabledAxis)
        {
            ViewBox = bounds;
            PanelBounds = bounds;
            _configuredAutomaticScrollerWidthPixels = Math.Max(0f, scrollerWidthPixels);
            ScrollStepPixels = Math.Max(1f, scrollStepPixels);
            RowHeight = ScrollStepPixels;
            UpdateResolvedAutoScrollSecondsPerStep();
            SetEnabledScrollAxes(enabledAxis);
            _automaticContentMode = _content != null;
            _manualConfigured = false;
            InvalidateLayout();
            EnsureAutomaticLayout();
        }

        internal static ScrollPanel Create(RectangleF viewBox, float contentTop, float footerHeight, float rowHeight, int totalRows, float scrollerWidthPixels, int scrollStep)
        {
            var panel = new ScrollPanel();
            panel.Configure(viewBox, contentTop, footerHeight, rowHeight, totalRows, scrollerWidthPixels, scrollStep);
            return panel;
        }

        internal void Configure(RectangleF viewBox, float contentTop, float footerHeight, float rowHeight, int totalRows, float scrollerWidthPixels, float autoScrollSecondsPerStep)
        {
            SetAutoScrollSecondsPerStep(autoScrollSecondsPerStep);
            ConfigureCore(viewBox, contentTop, footerHeight, rowHeight, totalRows, scrollerWidthPixels, GetVerticalScrollOffsetForCurrentMode);
        }

        void Configure(RectangleF viewBox, float contentTop, float footerHeight, float rowHeight, int totalRows, float scrollerWidthPixels, int autoScrollStep)
        {
            SetAutoScrollSecondsPerStep(0f);
            ConfigureCore(viewBox, contentTop, footerHeight, rowHeight, totalRows, scrollerWidthPixels, maxScrollOffset => GetRowOffsetFromStep(autoScrollStep, maxScrollOffset));
        }

        public void SetAutoScrollSecondsPerStep(float secondsPerStep)
        {
            _localAutoScrollSecondsPerStep = NormalizeAutoScrollSecondsPerStep(secondsPerStep);
            UpdateResolvedAutoScrollSecondsPerStep();
        }

        void UpdateResolvedAutoScrollSecondsPerStep()
        {
            float secondsPerStep = _localAutoScrollSecondsPerStep.HasValue
                ? _localAutoScrollSecondsPerStep.Value
                : GetResourceAutoScrollSecondsPerStep();

            if (Math.Abs(_resolvedAutoScrollSecondsPerStep - secondsPerStep) <= 0.0001f)
                return;

            if (_resolvedAutoScrollSecondsPerStep > 0f && secondsPerStep <= 0f)
                _manualScrollOffsetPixels = ScrollOffsetPixels2D;

            _resolvedAutoScrollSecondsPerStep = secondsPerStep;

            if (_automaticContentMode)
                InvalidateLayout();
            else
                MarkDirty();
        }

        float GetResourceAutoScrollSecondsPerStep()
        {
            float value;
            return ScopedResourceResolver.TryResolve(this, ThemeResources.AutoScrollSecondsPerStep, out value)
                ? NormalizeAutoScrollSecondsPerStep(value)
                : 0f;
        }

        static float NormalizeAutoScrollSecondsPerStep(float secondsPerStep)
        {
            if (float.IsNaN(secondsPerStep) || float.IsInfinity(secondsPerStep))
                return 0f;

            return Math.Max(0f, secondsPerStep);
        }

        void ConfigureCore(RectangleF viewBox, float contentTop, float footerHeight, float rowHeight, int totalRows, float scrollerWidthPixels, Func<float, float> scrollOffsetProvider)
        {
            SetEnabledScrollAxes(ScrollAxis.Vertical);
            _automaticContentMode = false;
            _manualConfigured = true;
            ViewBox = viewBox;
            RowHeight = Math.Max(1f, rowHeight);
            ScrollerWidthPixels = Math.Max(0f, scrollerWidthPixels);
            TotalRows = Math.Max(0, totalRows);

            float viewportHeight = Math.Max(0f, viewBox.Bottom - contentTop - Math.Max(0f, footerHeight));
            MaxVisibleRows = Math.Max(1, (int)Math.Floor(viewportHeight / RowHeight));

            float totalContentHeight = TotalRows * RowHeight;
            _contentExtentPixels = new Vector2(viewBox.Width, totalContentHeight);
            IsHorizontallyScrollable = false;
            IsVerticallyScrollable = IsAxisEnabled(ScrollAxis.Vertical) && totalContentHeight > viewportHeight + 0.001f;
            PanelBounds = new RectangleF(viewBox.X, contentTop, viewBox.Width, viewportHeight);

            float contentGutterWidth = IsVerticallyScrollable ? ScrollerWidthPixels + GetScrollbarContentMarginPixels() : 0f;
            float contentWidth = Math.Max(1f, viewBox.Width - contentGutterWidth);
            ContentViewportBounds = new RectangleF(viewBox.X, contentTop, contentWidth, viewportHeight);

            if (!IsVerticallyScrollable && TotalRows > 0)
                ResetAxisState(ScrollAxis.Vertical);

            float maxScrollOffset = GetMaxScrollOffsetPixels(ScrollAxis.Vertical);
            if (TotalRows > 0 && IsVerticallyScrollable)
            {
                _manualScrollOffsetPixels.Y = Clamp(_manualScrollOffsetPixels.Y, 0f, maxScrollOffset);
                UpdateManualScrollInertia(maxScrollOffset, ScrollAxis.Vertical);
            }

            var y = IsVerticallyScrollable && scrollOffsetProvider != null ? Clamp(scrollOffsetProvider(maxScrollOffset), 0f, maxScrollOffset) : 0f;
            ScrollOffsetPixels2D = new Vector2(0f, y);
            _manualScrollOffsetPixels.X = 0f;

            StartRow = Clamp((int)Math.Floor(ScrollOffsetPixels / RowHeight), 0, GetMaxStartRow());
            RowOffsetPixels = Math.Max(0f, ScrollOffsetPixels - StartRow * RowHeight);

            int maxRowsInViewport = Math.Max(1, (int)Math.Ceiling(viewportHeight / RowHeight));
            VisibleRows = TotalRows == 0 ? 0 : Math.Min(TotalRows - StartRow, maxRowsInViewport);

            int renderRowsForViewport = Math.Max(1, (int)Math.Ceiling((viewportHeight + RowOffsetPixels) / RowHeight) + 1);
            RenderRows = TotalRows == 0 ? 0 : Math.Min(TotalRows - StartRow, renderRowsForViewport);

            ContentBounds = new RectangleF(viewBox.X, contentTop - RowOffsetPixels, contentWidth, RenderRows * RowHeight);
        }

        void EnsureAutomaticLayout()
        {
            if (!_automaticContentMode || !IsLayoutDirty)
                return;

            ArrangeAutomaticContent();
        }

        void ArrangeAutomaticContent()
        {
            ScrollerWidthPixels = Math.Max(0f, _configuredAutomaticScrollerWidthPixels);

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
                ScrollOffsetPixels2D = Vector2.Zero;
                _manualScrollOffsetPixels = Vector2.Zero;
                _contentExtentPixels = Vector2.Zero;
                IsHorizontallyScrollable = false;
                IsVerticallyScrollable = false;
                ValidateLayout();
                return;
            }

            var scrollContent2D = _content as IScrollContent2D;
            var scrollContent = _content as IScrollContent;
            var showHorizontal = false;
            var showVertical = false;
            var desired = Vector2.Zero;
            var viewport = default(RectangleF);

            for (var pass = 0; pass < 4; pass++)
            {
                viewport = CalculateAutomaticViewport(showHorizontal, showVertical);
                desired = MeasureAutomaticContent(viewport.Size, scrollContent2D, scrollContent);
                var nextHorizontal = IsAxisEnabled(ScrollAxis.Horizontal) && desired.X > viewport.Width + 0.001f;
                var nextVertical = IsAxisEnabled(ScrollAxis.Vertical) && desired.Y > viewport.Height + 0.001f;
                if (nextHorizontal == showHorizontal && nextVertical == showVertical)
                    break;

                showHorizontal = nextHorizontal;
                showVertical = nextVertical;
            }

            ContentViewportBounds = viewport;
            _contentExtentPixels = new Vector2(Math.Max(0f, desired.X), Math.Max(0f, desired.Y));
            IsHorizontallyScrollable = IsAxisEnabled(ScrollAxis.Horizontal) && showHorizontal;
            IsVerticallyScrollable = IsAxisEnabled(ScrollAxis.Vertical) && showVertical;

            if (!IsHorizontallyScrollable)
                ResetAxisState(ScrollAxis.Horizontal);
            if (!IsVerticallyScrollable)
                ResetAxisState(ScrollAxis.Vertical);

            if (IsHorizontallyScrollable)
                UpdateManualScrollInertia(GetMaxScrollOffsetPixels(ScrollAxis.Horizontal), ScrollAxis.Horizontal);
            if (IsVerticallyScrollable)
                UpdateManualScrollInertia(GetMaxScrollOffsetPixels(ScrollAxis.Vertical), ScrollAxis.Vertical);

            ClampScrollOffsets();
            RowHeight = Math.Max(1f, RowHeight);
            StartRow = Clamp((int)Math.Floor(ScrollOffsetPixels / RowHeight), 0, GetMaxStartRow());
            RowOffsetPixels = Math.Max(0f, ScrollOffsetPixels - StartRow * RowHeight);
            TotalRows = _content.HasChildren ? _content.Children.Count : 0;
            MaxVisibleRows = Math.Max(1, (int)Math.Floor(viewport.Height / RowHeight));
            VisibleRows = TotalRows;
            RenderRows = TotalRows;

            var offset = GetEnabledScrollOffsetPixels();
            ContentBounds = new RectangleF(
                viewport.X - offset.X,
                viewport.Y - offset.Y,
                IsAxisEnabled(ScrollAxis.Horizontal) ? _contentExtentPixels.X : viewport.Width,
                IsAxisEnabled(ScrollAxis.Vertical) ? _contentExtentPixels.Y : viewport.Height);

            if (scrollContent2D != null)
                scrollContent2D.ArrangeViewport(viewport, offset);
            else if (scrollContent != null)
                scrollContent.ArrangeViewport(viewport, offset.Y);
            else
                _content.Arrange(ContentBounds);

            ValidateLayout();
        }

        Vector2 MeasureAutomaticContent(Vector2 availableSize, IScrollContent2D scrollContent2D, IScrollContent scrollContent)
        {
            if (scrollContent2D != null)
                return scrollContent2D.MeasureContent(availableSize);
            if (scrollContent != null)
                return scrollContent.MeasureContent(availableSize);
            return _content.Measure(availableSize);
        }

        RectangleF CalculateAutomaticViewport(bool showHorizontal, bool showVertical)
        {
            var verticalGutter = showVertical ? ScrollerWidthPixels + GetScrollbarContentMarginPixels() : 0f;
            var horizontalGutter = showHorizontal ? ScrollerWidthPixels + GetScrollbarContentMarginPixels() : 0f;
            return new RectangleF(
                PanelBounds.X,
                PanelBounds.Y,
                Math.Max(0f, PanelBounds.Width - verticalGutter),
                Math.Max(0f, PanelBounds.Height - horizontalGutter));
        }

        float GetScrollbarContentMarginPixels()
        {
            return Math.Max(0f, ScrollerWidthPixels * SCROLLBAR_CONTENT_MARGIN_RATIO);
        }

        public override bool Scroll(object sender, int delta)
        {
            if (!CanScroll)
                return false;

            var axis = ResolveWheelAxis();
            if (!IsScrollableAxis(axis))
                return false;

            float pixelDelta = _automaticContentMode ? GetAutomaticScrollPixelDelta(delta) : GetManualScrollPixelDelta(delta);
            if (Math.Abs(pixelDelta) <= 0.001f)
                return false;

            var offset = ScrollOffsetPixels2D;
            if (axis == ScrollAxis.Horizontal)
                offset.X = Clamp(offset.X + pixelDelta, 0f, GetMaxScrollOffsetPixels(axis));
            else
                offset.Y = Clamp(offset.Y + pixelDelta, 0f, GetMaxScrollOffsetPixels(axis));

            _manualScrollOffsetPixels = offset;
            AddScrollVelocity(pixelDelta, axis);
            ScrollOffsetPixels2D = offset;
            UpdateVerticalRowState();

            if (AutoScrollSecondsPerStep > 0f)
                _manualOverrideUntilFrame = GetFrameCounter() + MANUAL_SCROLL_OVERRIDE_FRAMES;

            if (_automaticContentMode)
                InvalidateLayout();
            else
                MarkDirty();

            ScrollChanged?.Invoke(this);
            return true;
        }

        ScrollAxis ResolveWheelAxis()
        {
            if (IsSingleScrollAxis(EnabledScrollAxes))
                return EnabledScrollAxes;

            switch (WheelRouting)
            {
                case ScrollWheelRouting.Horizontal:
                    return ScrollAxis.Horizontal;
                case ScrollWheelRouting.ShiftForHorizontal:
                    return IsShiftPressed() ? ScrollAxis.Horizontal : ScrollAxis.Vertical;
                case ScrollWheelRouting.Automatic:
                    if (IsHorizontallyScrollable && !IsVerticallyScrollable)
                        return ScrollAxis.Horizontal;
                    if (IsVerticallyScrollable && !IsHorizontallyScrollable)
                        return ScrollAxis.Vertical;
                    return IsShiftPressed() ? ScrollAxis.Horizontal : ScrollAxis.Vertical;
                default:
                    return ScrollAxis.Vertical;
            }
        }

        static bool IsShiftPressed()
        {
            try
            {
                return MyAPIGateway.Input != null && MyAPIGateway.Input.IsAnyShiftKeyPressed();
            }
            catch
            {
                return false;
            }
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            if (_automaticContentMode)
            {
                EnsureAutomaticLayout();
                BeginClip(sprites, ContentViewportBounds);
                if (_content != null)
                    _content.Render(sprites);
                EndClip(sprites);
            }

            RenderScrollBar(sprites, ScrollBarTrackColor, ScrollBarThumbColor);
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
            ScrollBarTrackColor = trackColor;
            ScrollBarThumbColor = thumbColor;
        }

        public void ResetScroll(bool notify = true)
        {
            StopScrollInertia();
            _manualScrollOffsetPixels = Vector2.Zero;
            ScrollOffsetPixels2D = Vector2.Zero;
            UpdateVerticalRowState();
            MarkDirty();
            if (notify)
                ScrollChanged?.Invoke(this);
        }

        public bool SetScrollOffsetPixels(float offsetPixels, bool notify = true)
        {
            return SetManualScrollOffsetPixels(ScrollAxis.Vertical, offsetPixels, notify);
        }

        public bool SetScrollOffsetPixels(Vector2 offsetPixels, bool notify = true)
        {
            return SetManualScrollOffsetPixels(offsetPixels, notify);
        }

        public bool SetHorizontalScrollOffsetPixels(float offsetPixels, bool notify = true)
        {
            return SetManualScrollOffsetPixels(ScrollAxis.Horizontal, offsetPixels, notify);
        }

        public bool SetVerticalScrollOffsetPixels(float offsetPixels, bool notify = true)
        {
            return SetManualScrollOffsetPixels(ScrollAxis.Vertical, offsetPixels, notify);
        }

        public void ClearScrollBarColors()
        {
            if (!_scrollBarTrackColorValue.LocalOverride && !_scrollBarThumbColorValue.LocalOverride &&
                !_scrollBarThumbHoverColorValue.LocalOverride && !_scrollBarThumbPressedColorValue.LocalOverride)
                return;

            _scrollBarTrackColorValue.ClearLocal();
            _scrollBarThumbColorValue.ClearLocal();
            _scrollBarThumbHoverColorValue.ClearLocal();
            _scrollBarThumbPressedColorValue.ClearLocal();
            MarkDirty();
        }

        public void RenderScrollBar(List<MySprite> sprites, Color trackColor, Color thumbColor)
        {
            if (sprites == null)
                return;

            RenderScrollBar(sprites, trackColor, thumbColor, ScrollAxis.Vertical);
            RenderScrollBar(sprites, trackColor, thumbColor, ScrollAxis.Horizontal);
        }

        void RenderScrollBar(List<MySprite> sprites, Color trackColor, Color thumbColor, ScrollAxis axis)
        {
            ScrollBarMetrics metrics;
            if (!TryGetScrollBarMetrics(axis, out metrics))
                return;

            if (axis == ScrollAxis.Vertical)
            {
                float barXCenter = metrics.TrackBounds.X + metrics.TrackBounds.Width / 2f;
                int barWidth = Math.Max(1, (int)Math.Round(metrics.TrackBounds.Width, MidpointRounding.AwayFromZero));
                var trackCenter = new Vector2(barXCenter, (float)Math.Round(metrics.TrackBounds.Y + metrics.TrackBounds.Height / 2f, MidpointRounding.ToEven));
                DrawCapsule(sprites, trackCenter, barWidth, metrics.TrackBounds.Height, trackColor, axis);
                var thumbCenter = new Vector2(barXCenter, (float)Math.Round(metrics.ThumbBounds.Y + metrics.ThumbBounds.Height / 2f, MidpointRounding.ToEven));
                DrawCapsule(sprites, thumbCenter, barWidth, metrics.ThumbBounds.Height, GetThumbColor(thumbColor, axis), axis);
                return;
            }

            float barYCenter = metrics.TrackBounds.Y + metrics.TrackBounds.Height / 2f;
            int barHeight = Math.Max(1, (int)Math.Round(metrics.TrackBounds.Height, MidpointRounding.AwayFromZero));
            var horizontalTrackCenter = new Vector2((float)Math.Round(metrics.TrackBounds.X + metrics.TrackBounds.Width / 2f, MidpointRounding.ToEven), barYCenter);
            DrawCapsule(sprites, horizontalTrackCenter, barHeight, metrics.TrackBounds.Width, trackColor, axis);
            var horizontalThumbCenter = new Vector2((float)Math.Round(metrics.ThumbBounds.X + metrics.ThumbBounds.Width / 2f, MidpointRounding.ToEven), barYCenter);
            DrawCapsule(sprites, horizontalThumbCenter, barHeight, metrics.ThumbBounds.Width, GetThumbColor(thumbColor, axis), axis);
        }

        bool TryGetScrollBarMetrics(ScrollAxis axis, out ScrollBarMetrics metrics)
        {
            metrics = new ScrollBarMetrics { Axis = axis };
            if (!IsAxisEnabled(axis) || !IsScrollableAxis(axis) || ScrollerWidthPixels <= 0f)
                return false;

            var total = axis == ScrollAxis.Vertical ? GetTotalContentHeightPixels() : GetTotalContentWidthPixels();
            if (total <= 0f)
                return false;

            float gutter = Math.Max(1f, ScrollerWidthPixels);
            float visual = GetScrollBarVisualWidthPixels(gutter);
            float padding = Math.Max(0f, (gutter - visual) * 0.5f);
            var maxOffset = GetMaxScrollOffsetPixels(axis);

            if (axis == ScrollAxis.Vertical)
            {
                float trackHeight = Math.Max(1f, ContentViewportBounds.Height - ScrollerWidthPixels * 2f);
                float thumbHeight = Math.Max(1f, Math.Min(trackHeight, ContentViewportBounds.Height / Math.Max(1f, total) * trackHeight));
                float thumbTravel = Math.Max(0f, trackHeight - thumbHeight);
                float fraction = maxOffset > 0f ? Clamp(VerticalScrollOffsetPixels / maxOffset, 0f, 1f) : 0f;
                float initialY = ContentViewportBounds.Y + ScrollerWidthPixels;
                float gutterX = PanelBounds.Right - gutter;
                float trackX = gutterX + padding;
                float thumbY = initialY + thumbTravel * fraction;
                metrics.TrackHitBounds = new RectangleF(gutterX, initialY, gutter, trackHeight);
                metrics.TrackBounds = new RectangleF(trackX, initialY, visual, trackHeight);
                metrics.ThumbBounds = new RectangleF(trackX, thumbY, visual, thumbHeight);
                metrics.ThumbTravelPixels = thumbTravel;
                metrics.MaxScrollOffsetPixels = maxOffset;
                return true;
            }

            float trackWidth = Math.Max(1f, ContentViewportBounds.Width - ScrollerWidthPixels * 2f);
            float thumbWidth = Math.Max(1f, Math.Min(trackWidth, ContentViewportBounds.Width / Math.Max(1f, total) * trackWidth));
            float horizontalTravel = Math.Max(0f, trackWidth - thumbWidth);
            float horizontalFraction = maxOffset > 0f ? Clamp(HorizontalScrollOffsetPixels / maxOffset, 0f, 1f) : 0f;
            float initialX = ContentViewportBounds.X + ScrollerWidthPixels;
            float gutterY = PanelBounds.Bottom - gutter;
            float trackY = gutterY + padding;
            float thumbX = initialX + horizontalTravel * horizontalFraction;
            metrics.TrackHitBounds = new RectangleF(initialX, gutterY, trackWidth, gutter);
            metrics.TrackBounds = new RectangleF(initialX, trackY, trackWidth, visual);
            metrics.ThumbBounds = new RectangleF(thumbX, trackY, thumbWidth, visual);
            metrics.ThumbTravelPixels = horizontalTravel;
            metrics.MaxScrollOffsetPixels = maxOffset;
            return true;
        }

        bool TryGetScrollBarTrackBounds(ScrollAxis axis, out RectangleF bounds)
        {
            ScrollBarMetrics metrics;
            if (!TryGetScrollBarMetrics(axis, out metrics))
            {
                bounds = default(RectangleF);
                return false;
            }

            bounds = metrics.TrackHitBounds;
            return true;
        }

        bool TryGetScrollBarThumbBounds(ScrollAxis axis, out RectangleF bounds)
        {
            ScrollBarMetrics metrics;
            if (!TryGetScrollBarMetrics(axis, out metrics))
            {
                bounds = default(RectangleF);
                return false;
            }

            bounds = metrics.ThumbBounds;
            return true;
        }

        bool JumpScrollBarThumbToPoint(Vector2 point, ScrollAxis axis)
        {
            ScrollBarMetrics metrics;
            if (!IsAxisEnabled(axis) || !TryGetScrollBarMetrics(axis, out metrics) || !metrics.TrackHitBounds.Contains(point))
                return false;

            StopScrollInertia();
            var thumbStart = axis == ScrollAxis.Vertical
                ? point.Y - metrics.ThumbBounds.Height * 0.5f
                : point.X - metrics.ThumbBounds.Width * 0.5f;
            return SetScrollOffsetFromThumbStart(thumbStart, metrics);
        }

        bool DragScrollBarThumbByDelta(Vector2 delta, ScrollAxis axis)
        {
            if (float.IsNaN(delta.X) || float.IsNaN(delta.Y))
                return false;

            ScrollBarMetrics metrics;
            if (!IsAxisEnabled(axis) || !TryGetScrollBarMetrics(axis, out metrics) || metrics.ThumbTravelPixels <= 0.001f)
                return false;

            var pointerDelta = axis == ScrollAxis.Vertical ? delta.Y : delta.X;
            float scrollDelta = metrics.MaxScrollOffsetPixels * (pointerDelta / metrics.ThumbTravelPixels);
            var current = axis == ScrollAxis.Vertical ? VerticalScrollOffsetPixels : HorizontalScrollOffsetPixels;
            return SetManualScrollOffsetPixels(axis, current + scrollDelta);
        }

        void BeginScrollBarThumbDrag(ScrollAxis axis)
        {
            StopScrollInertia();
            _scrollBarThumbDragging = true;
            _scrollBarThumbDraggingAxis = axis;
            MarkDirty();
        }

        void EndScrollBarThumbDrag()
        {
            if (!_scrollBarThumbDragging)
                return;

            _scrollBarThumbDragging = false;
            MarkDirty();
        }

        void MarkScrollBarThumbHovered(ScrollAxis axis)
        {
            _scrollBarThumbHoverFrame = GetFrameCounter();
            _scrollBarThumbDraggingAxis = axis;
            MarkDirty();
        }

        bool IsScrollBarThumbHovered()
        {
            return _scrollBarThumbHoverFrame != long.MinValue && GetFrameCounter() - _scrollBarThumbHoverFrame <= HOVER_LIFETIME_FRAMES;
        }

        static float GetScrollBarVisualWidthPixels(float gutterWidth)
        {
            if (gutterWidth <= 1f)
                return 1f;

            return Math.Max(1f, (float)Math.Round(gutterWidth, MidpointRounding.AwayFromZero));
        }

        Color GetThumbColor(Color thumbColor, ScrollAxis axis)
        {
            if (_scrollBarThumbDragging && _scrollBarThumbDraggingAxis == axis)
                return _scrollBarThumbPressedColorValue.LocalOverride
                    ? ScrollBarThumbPressedColor
                    : thumbColor.DeriveAccentColor().DeriveAccentColor();

            return IsScrollBarThumbHovered() && _scrollBarThumbDraggingAxis == axis
                ? (_scrollBarThumbHoverColorValue.LocalOverride ? ScrollBarThumbHoverColor : thumbColor.DeriveAccentColor())
                : thumbColor;
        }

        bool SetScrollOffsetFromThumbStart(float thumbStart, ScrollBarMetrics metrics)
        {
            var trackStart = metrics.Axis == ScrollAxis.Vertical ? metrics.TrackBounds.Y : metrics.TrackBounds.X;
            float scrollFraction = metrics.ThumbTravelPixels <= 0f ? 0f : Clamp((thumbStart - trackStart) / metrics.ThumbTravelPixels, 0f, 1f);
            return SetManualScrollOffsetPixels(metrics.Axis, metrics.MaxScrollOffsetPixels * scrollFraction);
        }

        bool SetManualScrollOffsetPixels(ScrollAxis axis, float offsetPixels, bool notify = true)
        {
            if (!IsAxisEnabled(axis))
                return false;

            var offset = ScrollOffsetPixels2D;
            if (axis == ScrollAxis.Vertical)
                offset.Y = offsetPixels;
            else
                offset.X = offsetPixels;
            return SetManualScrollOffsetPixels(offset, notify);
        }

        bool SetManualScrollOffsetPixels(Vector2 offsetPixels, bool notify = true)
        {
            var previousOffset = ScrollOffsetPixels2D;
            StopScrollInertia();
            _manualScrollOffsetPixels = ClampToEnabledAxes(offsetPixels);
            ScrollOffsetPixels2D = _manualScrollOffsetPixels;
            UpdateVerticalRowState();

            if (AutoScrollSecondsPerStep > 0f)
                _manualOverrideUntilFrame = GetFrameCounter() + MANUAL_SCROLL_OVERRIDE_FRAMES;

            if (Math.Abs(previousOffset.X - ScrollOffsetPixels2D.X) <= 0.001f &&
                Math.Abs(previousOffset.Y - ScrollOffsetPixels2D.Y) <= 0.001f)
                return false;

            if (_automaticContentMode)
                InvalidateLayout();
            else
                MarkDirty();

            if (notify)
                ScrollChanged?.Invoke(this);
            return true;
        }

        void StopScrollInertia()
        {
            _scrollVelocityPixelsPerFrame = Vector2.Zero;
            IsAnimating = false;
            _lastInertiaFrame = GetFrameCounter();
        }

        float GetVerticalScrollOffsetForCurrentMode(float maxScrollOffset)
        {
            if (maxScrollOffset <= 0f)
                return 0f;

            if (IsAutoScrolling())
                return GetAutoScrollOffset(AutoScrollSecondsPerStep, maxScrollOffset);

            return _manualScrollOffsetPixels.Y;
        }

        int GetMaxStartRow()
        {
            return RowHeight <= 0f ? 0 : Math.Max(0, (int)Math.Floor(GetMaxScrollOffsetPixels(ScrollAxis.Vertical) / RowHeight));
        }

        float GetMaxScrollOffsetPixels(ScrollAxis axis)
        {
            if (!IsAxisEnabled(axis))
                return 0f;

            return axis == ScrollAxis.Vertical
                ? Math.Max(0f, GetTotalContentHeightPixels() - ContentViewportBounds.Height)
                : Math.Max(0f, GetTotalContentWidthPixels() - ContentViewportBounds.Width);
        }

        float GetMaxScrollOffsetPixels()
        {
            return GetMaxScrollOffsetPixels(ScrollAxis.Vertical);
        }

        float GetTotalContentHeightPixels()
        {
            return _automaticContentMode ? _contentExtentPixels.Y : TotalRows * RowHeight;
        }

        float GetTotalContentWidthPixels()
        {
            return _automaticContentMode ? _contentExtentPixels.X : ContentViewportBounds.Width;
        }

        bool IsManualConfigured()
        {
            return _manualConfigured;
        }

        void SetEnabledScrollAxes(ScrollAxis enabledAxis)
        {
            EnabledScrollAxes = IsSingleScrollAxis(enabledAxis) ? enabledAxis : ScrollAxis.Vertical;
            ResetDisabledAxisState();
        }

        bool IsAxisEnabled(ScrollAxis axis)
        {
            return axis != ScrollAxis.None && (EnabledScrollAxes & axis) == axis;
        }

        static bool IsSingleScrollAxis(ScrollAxis axis)
        {
            return axis == ScrollAxis.Horizontal || axis == ScrollAxis.Vertical;
        }

        void ResetDisabledAxisState()
        {
            if (!IsAxisEnabled(ScrollAxis.Horizontal))
            {
                IsHorizontallyScrollable = false;
                ResetAxisState(ScrollAxis.Horizontal);
            }

            if (!IsAxisEnabled(ScrollAxis.Vertical))
            {
                IsVerticallyScrollable = false;
                ResetAxisState(ScrollAxis.Vertical);
            }
        }

        bool IsScrollableAxis(ScrollAxis axis)
        {
            return IsAxisEnabled(axis) && (axis == ScrollAxis.Vertical ? IsVerticallyScrollable : IsHorizontallyScrollable);
        }

        void ClampScrollOffsets()
        {
            _manualScrollOffsetPixels = ClampToEnabledAxes(_manualScrollOffsetPixels);
            ScrollOffsetPixels2D = GetEnabledScrollOffsetPixels();
            UpdateVerticalRowState();
        }

        Vector2 ClampToEnabledAxes(Vector2 offset)
        {
            return new Vector2(
                IsScrollableAxis(ScrollAxis.Horizontal) ? Clamp(offset.X, 0f, GetMaxScrollOffsetPixels(ScrollAxis.Horizontal)) : 0f,
                IsScrollableAxis(ScrollAxis.Vertical) ? Clamp(offset.Y, 0f, GetMaxScrollOffsetPixels(ScrollAxis.Vertical)) : 0f);
        }

        Vector2 GetEnabledScrollOffsetPixels()
        {
            var offset = ClampToEnabledAxes(_manualScrollOffsetPixels);
            if (IsAutoScrolling())
            {
                var maxScrollOffset = GetMaxScrollOffsetPixels(EnabledScrollAxes);
                var autoOffset = GetAutoScrollOffset(AutoScrollSecondsPerStep, maxScrollOffset);
                if (EnabledScrollAxes == ScrollAxis.Horizontal)
                    offset.X = autoOffset;
                else
                    offset.Y = autoOffset;
            }

            return ClampToEnabledAxes(offset);
        }

        void ResetAxisState(ScrollAxis axis)
        {
            if (axis == ScrollAxis.Vertical)
            {
                _manualScrollOffsetPixels.Y = 0f;
                _scrollVelocityPixelsPerFrame.Y = 0f;
            }
            else
            {
                _manualScrollOffsetPixels.X = 0f;
                _scrollVelocityPixelsPerFrame.X = 0f;
            }

            if (Math.Abs(_scrollVelocityPixelsPerFrame.X) <= STOP_VELOCITY_PIXELS_PER_FRAME &&
                Math.Abs(_scrollVelocityPixelsPerFrame.Y) <= STOP_VELOCITY_PIXELS_PER_FRAME)
                IsAnimating = false;
        }

        void UpdateVerticalRowState()
        {
            StartRow = Clamp((int)Math.Floor(ScrollOffsetPixels / Math.Max(1f, RowHeight)), 0, GetMaxStartRow());
            RowOffsetPixels = Math.Max(0f, ScrollOffsetPixels - StartRow * Math.Max(1f, RowHeight));
        }

        float GetManualScrollPixelDelta(int wheelDelta)
        {
            if (wheelDelta == 0)
                return 0f;

            float multiplier = ManualScrollPixelMultiplier > 0f ? ManualScrollPixelMultiplier : DEFAULT_MANUAL_SCROLL_PIXEL_MULTIPLIER;
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

        void AddScrollVelocity(float pixelDelta, ScrollAxis axis)
        {
            if (!IsAxisEnabled(axis))
                return;

            if (!ManualScrollInertiaEnabled)
            {
                StopScrollInertia();
                return;
            }

            float maxVelocity = Math.Max(1f, RowHeight);
            if (axis == ScrollAxis.Vertical)
                _scrollVelocityPixelsPerFrame.Y = Clamp(_scrollVelocityPixelsPerFrame.Y + pixelDelta * MANUAL_SCROLL_VELOCITY_IMPULSE, -maxVelocity, maxVelocity);
            else
                _scrollVelocityPixelsPerFrame.X = Clamp(_scrollVelocityPixelsPerFrame.X + pixelDelta * MANUAL_SCROLL_VELOCITY_IMPULSE, -maxVelocity, maxVelocity);

            IsAnimating = Math.Abs(_scrollVelocityPixelsPerFrame.X) > STOP_VELOCITY_PIXELS_PER_FRAME ||
                          Math.Abs(_scrollVelocityPixelsPerFrame.Y) > STOP_VELOCITY_PIXELS_PER_FRAME;
            _lastInertiaFrame = GetFrameCounter();
        }

        void UpdateManualScrollInertia(float maxScrollOffset, ScrollAxis axis)
        {
            if (!IsAxisEnabled(axis))
            {
                ResetAxisState(axis);
                return;
            }

            if (_scrollBarThumbDragging && _scrollBarThumbDraggingAxis == axis)
                return;

            var previousScroll = axis == ScrollAxis.Vertical ? _manualScrollOffsetPixels.Y : _manualScrollOffsetPixels.X;
            var previousVelocity = axis == ScrollAxis.Vertical ? _scrollVelocityPixelsPerFrame.Y : _scrollVelocityPixelsPerFrame.X;

            if (Math.Abs(previousVelocity) <= STOP_VELOCITY_PIXELS_PER_FRAME)
            {
                if (axis == ScrollAxis.Vertical)
                    _scrollVelocityPixelsPerFrame.Y = 0f;
                else
                    _scrollVelocityPixelsPerFrame.X = 0f;
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
            var offset = axis == ScrollAxis.Vertical ? _manualScrollOffsetPixels.Y : _manualScrollOffsetPixels.X;
            var velocity = axis == ScrollAxis.Vertical ? _scrollVelocityPixelsPerFrame.Y : _scrollVelocityPixelsPerFrame.X;

            for (long i = 0; i < elapsed; i++)
            {
                offset = Clamp(offset + velocity, 0f, maxScrollOffset);
                bool hitStart = offset <= 0f && velocity < 0f;
                bool hitEnd = offset >= maxScrollOffset && velocity > 0f;
                if (hitStart || hitEnd)
                {
                    velocity = 0f;
                    break;
                }

                velocity *= INERTIA_DECAY_PER_FRAME;
            }

            if (Math.Abs(velocity) <= STOP_VELOCITY_PIXELS_PER_FRAME)
                velocity = 0f;

            if (axis == ScrollAxis.Vertical)
            {
                _manualScrollOffsetPixels.Y = offset;
                _scrollVelocityPixelsPerFrame.Y = velocity;
            }
            else
            {
                _manualScrollOffsetPixels.X = offset;
                _scrollVelocityPixelsPerFrame.X = velocity;
            }

            IsAnimating = Math.Abs(_scrollVelocityPixelsPerFrame.X) > STOP_VELOCITY_PIXELS_PER_FRAME ||
                          Math.Abs(_scrollVelocityPixelsPerFrame.Y) > STOP_VELOCITY_PIXELS_PER_FRAME;

            if (Math.Abs(offset - previousScroll) > 0.001f || Math.Abs(velocity - previousVelocity) > 0.001f || IsAnimating)
                MarkDirty();
        }

        bool IsAutoScrolling()
        {
            return IsSingleScrollAxis(EnabledScrollAxes) &&
                   IsScrollableAxis(EnabledScrollAxes) &&
                   AutoScrollSecondsPerStep > 0f &&
                   GetFrameCounter() > _manualOverrideUntilFrame;
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
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
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
            sprites.Add(MySprite.CreateClipRect(new Rectangle(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y))));
        }

        static void EndClip(List<MySprite> sprites)
        {
            if (sprites != null)
                sprites.Add(MySprite.CreateClearClipRect());
        }

        static void DrawCapsule(List<MySprite> sprites, Vector2 center, int thickness, float length, Color color, ScrollAxis axis)
        {
            if (axis == ScrollAxis.Vertical)
            {
                sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = center, Size = new Vector2(thickness, length + .5f), Color = color, Alignment = TextAlignment.CENTER });
                var capsSize = new Vector2(thickness);
                sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SemiCircle", Position = new Vector2(center.X, center.Y - length / 2f), Size = capsSize, RotationOrScale = 0f, Color = color, Alignment = TextAlignment.CENTER });
                sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SemiCircle", Position = new Vector2(center.X, center.Y + length / 2f), Size = capsSize, RotationOrScale = (float)Math.PI, Color = color, Alignment = TextAlignment.CENTER });
                return;
            }

            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SquareSimple", Position = center, Size = new Vector2(length + .5f, thickness), Color = color, Alignment = TextAlignment.CENTER });
            var horizontalCapsSize = new Vector2(thickness);
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SemiCircle", Position = new Vector2(center.X - length / 2f, center.Y), Size = horizontalCapsSize, RotationOrScale = -MathHelper.PiOver2, Color = color, Alignment = TextAlignment.CENTER });
            sprites.Add(new MySprite { Type = SpriteType.TEXTURE, Data = "SemiCircle", Position = new Vector2(center.X + length / 2f, center.Y), Size = horizontalCapsSize, RotationOrScale = MathHelper.PiOver2, Color = color, Alignment = TextAlignment.CENTER });
        }

        sealed class ScrollBarTrackControl : ControlTemplate
        {
            readonly ScrollPanel _owner;
            readonly ScrollAxis _axis;

            public ScrollBarTrackControl(ScrollPanel owner, ScrollAxis axis)
                : base(CursorType.Arrow, owner)
            {
                _owner = owner;
                _axis = axis;
                SetClickOnPress();
                SetOnClick((dataContext, sender) => { });
            }

            public override RectangleF Bounds
            {
                get
                {
                    RectangleF bounds;
                    return _owner != null && _owner.TryGetScrollBarTrackBounds(_axis, out bounds) ? bounds : default(RectangleF);
                }
            }

            public override bool CanPrimaryClick => Visible && _owner != null && _owner.IsScrollableAxis(_axis);

            public override bool ClickAt(Vector2 point, object sender)
            {
                return _owner != null && _owner.JumpScrollBarThumbToPoint(point, _axis);
            }

            protected override void RenderDefault(List<MySprite> sprites)
            {
            }

            protected override bool HitCore(Vector2 point)
            {
                RectangleF bounds;
                return _owner != null && _owner.TryGetScrollBarTrackBounds(_axis, out bounds) && bounds.Contains(point);
            }
        }

        sealed class ScrollBarThumbControl : ControlTemplate
        {
            readonly ScrollPanel _owner;
            readonly ScrollAxis _axis;

            public ScrollBarThumbControl(ScrollPanel owner, ScrollAxis axis)
                : base(CursorType.Arrow, owner)
            {
                _owner = owner;
                _axis = axis;
                SetDraggable();
                SetOnBeginDrag((dataContext, sender) => _owner.BeginScrollBarThumbDrag(_axis));
                SetOnDrag((dataContext, sender, delta) => _owner.DragScrollBarThumbByDelta(delta, _axis));
                SetOnEndDrag((dataContext, sender) => _owner.EndScrollBarThumbDrag());
                SetOnHover((dataContext, sender) =>
                {
                    _owner.MarkScrollBarThumbHovered(_axis);
                    return true;
                });
            }

            public override RectangleF Bounds
            {
                get
                {
                    RectangleF bounds;
                    return _owner != null && _owner.TryGetScrollBarThumbBounds(_axis, out bounds) ? bounds : default(RectangleF);
                }
            }

            public override bool CanHover => Visible && _owner != null && _owner.IsScrollableAxis(_axis);

            public override bool CanDrag => Visible && _owner != null && _owner.IsScrollableAxis(_axis) && Draggable;

            protected override void RenderDefault(List<MySprite> sprites)
            {
            }

            protected override bool HitCore(Vector2 point)
            {
                RectangleF bounds;
                return _owner != null && _owner.TryGetScrollBarThumbBounds(_axis, out bounds) && bounds.Contains(point);
            }
        }
    }
}
