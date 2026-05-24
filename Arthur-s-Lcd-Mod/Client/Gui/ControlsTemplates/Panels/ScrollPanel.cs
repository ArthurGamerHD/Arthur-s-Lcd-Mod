using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates;
using Sandbox.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    public sealed class ScrollPanel : ControlBase
    {
        const long ManualScrollOverrideFrames = 300L;
        const float DefaultManualScrollPixelMultiplier = 0.08f;
        const float ManualScrollVelocityImpulse = 0.12f;
        const float InertiaDecayPerFrame = 0.88f;
        const float StopVelocityPixelsPerFrame = 0.05f;

        public ScrollPanel(CursorType? cursor = null, object dataContext = null)
            : base(cursor, dataContext)
        {
        }

        public RectangleF ViewBox { get; private set; }
        public RectangleF PanelBounds { get; private set; }
        public RectangleF ContentViewportBounds { get; private set; }
        public RectangleF ContentBounds { get; private set; }
        public float RowHeight { get; private set; }
        public float ScrollerWidthPixels { get; private set; }
        public float AutoScrollSecondsPerStep { get; private set; }
        public float ManualScrollPixelMultiplier { get; set; } = DefaultManualScrollPixelMultiplier;
        public float ScrollOffsetPixels { get; private set; }
        public float RowOffsetPixels { get; private set; }
        public float ScrollVelocityPixelsPerFrame { get; private set; }
        public bool IsAnimating { get; private set; }
        public Action<ScrollPanel> ScrollChanged { get; set; }
        public int TotalRows { get; private set; }
        public int MaxVisibleRows { get; private set; }
        public int VisibleRows { get; private set; }
        public int StartRow { get; private set; }
        public bool IsScrollable { get; private set; }
        float _manualScrollPixels;
        long _manualOverrideUntilFrame = long.MinValue;
        long _lastInertiaFrame = long.MinValue;

        public override RectangleF Bounds
        {
            get { return PanelBounds; }
        }

        public override bool CanScroll
        {
            get { return Visible && IsScrollable; }
        }

        public static ScrollPanel Create(
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

        public void Configure(
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
            ViewBox = viewBox;
            RowHeight = Math.Max(1f, rowHeight);
            ScrollerWidthPixels = Math.Max(0f, scrollerWidthPixels);
            TotalRows = Math.Max(0, totalRows);

            float availableHeight = Math.Max(0f, viewBox.Bottom - contentTop - Math.Max(0f, footerHeight));
            MaxVisibleRows = Math.Max(1, (int)Math.Floor(availableHeight / RowHeight));
            IsScrollable = TotalRows > MaxVisibleRows;
            VisibleRows = TotalRows == 0 ? 0 : Math.Min(TotalRows, MaxVisibleRows);

            if (!IsScrollable && TotalRows > 0)
            {
                _manualScrollPixels = 0f;
                ScrollVelocityPixelsPerFrame = 0f;
                IsAnimating = false;
            }

            int maxStartRow = GetMaxStartRow();
            float maxScrollOffset = GetMaxScrollOffsetPixels();
            if (TotalRows > 0 && IsScrollable)
            {
                _manualScrollPixels = Clamp(_manualScrollPixels, 0f, maxScrollOffset);
                UpdateManualScrollInertia(maxScrollOffset);
            }

            ScrollOffsetPixels = IsScrollable && scrollOffsetProvider != null
                ? Clamp(scrollOffsetProvider(maxScrollOffset), 0f, maxScrollOffset)
                : 0f;
            StartRow = Clamp((int)Math.Floor(ScrollOffsetPixels / RowHeight), 0, maxStartRow);
            RowOffsetPixels = Math.Max(0f, ScrollOffsetPixels - StartRow * RowHeight);

            float panelHeight = MaxVisibleRows * RowHeight;
            PanelBounds = new RectangleF(viewBox.X, contentTop, viewBox.Width, panelHeight);

            float contentWidth = Math.Max(1f, viewBox.Width - (IsScrollable ? ScrollerWidthPixels : 0f));
            ContentViewportBounds = new RectangleF(viewBox.X, contentTop, contentWidth, panelHeight);

            int renderRows = MaxVisibleRows + (IsScrollable && RowOffsetPixels > 0.001f ? 1 : 0);
            ContentBounds = new RectangleF(
                viewBox.X,
                contentTop - RowOffsetPixels,
                contentWidth,
                renderRows * RowHeight);
        }

        public int GetStartIndex(int columns)
        {
            return StartRow * Math.Max(1, columns);
        }

        public override bool Scroll(object sender, int delta)
        {
            if (!CanScroll)
                return false;

            float pixelDelta = GetManualScrollPixelDelta(delta);
            if (Math.Abs(pixelDelta) <= 0.001f)
                return false;

            _manualScrollPixels = Clamp(ScrollOffsetPixels + pixelDelta, 0f, GetMaxScrollOffsetPixels());
            AddScrollVelocity(pixelDelta);
            ScrollOffsetPixels = _manualScrollPixels;
            StartRow = Clamp((int)Math.Floor(ScrollOffsetPixels / RowHeight), 0, GetMaxStartRow());
            RowOffsetPixels = Math.Max(0f, ScrollOffsetPixels - StartRow * RowHeight);

            if (AutoScrollSecondsPerStep > 0f)
                _manualOverrideUntilFrame = GetFrameCounter() + ManualScrollOverrideFrames;

            MarkDirty();
            ScrollChanged?.Invoke(this);
            return true;
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
        }

        protected override bool IsDirtyAfterRender()
        {
            return IsAnimating;
        }

        protected override bool HitCore(Vector2 point)
        {
            return PanelBounds.Contains(point);
        }

        public void RenderScrollBar(List<MySprite> sprites, Color trackColor, Color thumbColor)
        {
            if (!IsScrollable || sprites == null || TotalRows <= 0)
                return;

            float viewportHeight = Math.Max(1f, MaxVisibleRows * RowHeight - ScrollerWidthPixels * 2f);
            float scrollBarHeight = Math.Max(1f, (float)MaxVisibleRows / TotalRows * viewportHeight);
            float maxScrollOffset = GetMaxScrollOffsetPixels();
            float scrollFraction = maxScrollOffset > 0f ? ScrollOffsetPixels / maxScrollOffset : 0f;
            float scrollBarTravel = Math.Max(0f, viewportHeight - scrollBarHeight);
            float scrollBarCenter = scrollFraction * scrollBarTravel + scrollBarHeight / 2f;
            float initialY = ContentViewportBounds.Y + ScrollerWidthPixels;
            float barXCenter = ViewBox.X + ViewBox.Width - ScrollerWidthPixels / 2f;
            int barWidth = Math.Max(1, (int)ScrollerWidthPixels);

            var trackCenter = new Vector2(
                barXCenter,
                (float)Math.Round(initialY + viewportHeight / 2f, MidpointRounding.ToEven));
            DrawCapsule(sprites, trackCenter, barWidth, viewportHeight, trackColor);

            var thumbCenter = new Vector2(
                barXCenter,
                (float)Math.Round(initialY + scrollBarCenter, MidpointRounding.ToEven));
            DrawCapsule(sprites, thumbCenter, barWidth, scrollBarHeight, thumbColor);
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
            return Math.Max(0, TotalRows - MaxVisibleRows);
        }

        float GetMaxScrollOffsetPixels()
        {
            return GetMaxStartRow() * RowHeight;
        }

        float GetManualScrollPixelDelta(int wheelDelta)
        {
            if (wheelDelta == 0)
                return 0f;

            float multiplier = ManualScrollPixelMultiplier > 0f
                ? ManualScrollPixelMultiplier
                : DefaultManualScrollPixelMultiplier;
            float pixels = Math.Abs(wheelDelta) * multiplier;
            return wheelDelta > 0 ? -pixels : pixels;
        }

        void AddScrollVelocity(float pixelDelta)
        {
            float maxVelocity = Math.Max(1f, RowHeight);
            ScrollVelocityPixelsPerFrame = Clamp(
                ScrollVelocityPixelsPerFrame + pixelDelta * ManualScrollVelocityImpulse,
                -maxVelocity,
                maxVelocity);
            IsAnimating = Math.Abs(ScrollVelocityPixelsPerFrame) > StopVelocityPixelsPerFrame;
            _lastInertiaFrame = GetFrameCounter();
        }

        void UpdateManualScrollInertia(float maxScrollOffset)
        {
            var previousScroll = _manualScrollPixels;
            var previousVelocity = ScrollVelocityPixelsPerFrame;

            if (Math.Abs(ScrollVelocityPixelsPerFrame) <= StopVelocityPixelsPerFrame)
            {
                ScrollVelocityPixelsPerFrame = 0f;
                IsAnimating = false;
                _lastInertiaFrame = GetFrameCounter();
                return;
            }

            if (AutoScrollSecondsPerStep > 0f && GetFrameCounter() > _manualOverrideUntilFrame)
            {
                ScrollVelocityPixelsPerFrame = 0f;
                IsAnimating = false;
                _lastInertiaFrame = GetFrameCounter();
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

                ScrollVelocityPixelsPerFrame *= InertiaDecayPerFrame;
            }

            if (Math.Abs(ScrollVelocityPixelsPerFrame) <= StopVelocityPixelsPerFrame)
                ScrollVelocityPixelsPerFrame = 0f;

            IsAnimating = Math.Abs(ScrollVelocityPixelsPerFrame) > StopVelocityPixelsPerFrame;

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
    }
}
