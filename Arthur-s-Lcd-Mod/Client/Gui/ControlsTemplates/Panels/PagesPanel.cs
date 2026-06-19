using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    /// <summary>
    /// Horizontal page host with circular paging and bottom navigation controls.
    /// </summary>
    public sealed class PagesPanel : Panel
    {
        const float DEFAULT_PAGE_WIDTH_PIXELS = 480f;
        const float CONTROL_ROW_HEIGHT_PIXELS = 34f;
        const float CONTROL_GAP_PIXELS = 8f;
        const float BUTTON_WIDTH_PIXELS = 42f;
        const float INDICATOR_WIDTH_PIXELS = 136f;
        const float INDICATOR_DOT_PIXELS = 8f;
        const float INDICATOR_DOT_GAP_PIXELS = 8f;
        const float ARROW_ICON_PIXELS = 18f;

        RectangleF _leftButtonBounds;
        RectangleF _rightButtonBounds;
        RectangleF _indicatorBounds;
        RectangleF _navigationViewportBounds;
        RectangleF[] _indicatorButtonBounds = new RectangleF[0];
        bool _showControls;
        int _firstVisiblePage;
        int _visiblePageCount = 1;
        readonly ArrowControl _leftButton;
        readonly ArrowControl _rightButton;
        readonly IndicatorControl _indicatorControl;

        public PagesPanel()
            : base(default(RectangleF), CursorType.Default)
        {
            _leftButton = new ArrowControl(this, -1);
            _rightButton = new ArrowControl(this, 1);
            _indicatorControl = new IndicatorControl(this);
            base.AddChild(_leftButton);
            base.AddChild(_indicatorControl);
            base.AddChild(_rightButton);
        }

        public float PageWidthPixels { get; set; } = DEFAULT_PAGE_WIDTH_PIXELS;
        public Action<int> PageChanged { get; set; }
        public Func<RectangleF, int> PageProvider { get; set; }
        public RectangleF ViewBox { get; private set; }
        public RectangleF ContentViewportBounds { get; private set; }
        public int PageCount => GetPageCount();
        public bool CanNavigate => _showControls && GetPageCount() > 1;

        protected override bool ClipContent => true;

        protected override RectangleF ClipContentBounds => ContentViewportBounds;

        static float GetNavigationHeight(float scale)
        {
            return (CONTROL_ROW_HEIGHT_PIXELS + CONTROL_GAP_PIXELS) * Math.Max(0.01f, scale);
        }

        public override void AddChild(ControlTemplate child)
        {
            base.AddChild(child);
            EnsureControlsZOrder();
        }

        public override void SetRect(RectangleF bounds)
        {
            ViewBox = bounds;
            base.SetRect(bounds);
        }

        public int FirstVisiblePage
        {
            get { return _firstVisiblePage; }
            set
            {
                int pageCount = GetPageCount();
                int next = pageCount > 0 ? NormalizePageIndex(value, pageCount) : Math.Max(0, value);
                if (_firstVisiblePage == next)
                    return;

                _firstVisiblePage = next;
                InvalidateLayout();
                if (PageChanged != null)
                    PageChanged(_firstVisiblePage);
            }
        }

        bool MovePage(int direction)
        {
            if (!_showControls || GetPageCount() <= 1)
                return false;

            FirstVisiblePage = _firstVisiblePage + (direction < 0 ? -1 : 1);
            return true;
        }

        bool SelectPage(int pageIndex)
        {
            if (!_showControls)
                return false;

            int pageCount = GetPageCount();
            if (pageIndex < 0 || pageIndex >= pageCount)
                return false;

            FirstVisiblePage = pageIndex;
            return true;
        }

        protected override void ArrangeChildren()
        {
            var children = Children;
            float scale = Math.Max(0.01f, LayoutScale);
            int pageCount = ConfigureProvidedPages(scale);
            if (pageCount <= 0)
            {
                _showControls = false;
                _visiblePageCount = 1;
                ContentViewportBounds = ViewBox;
                _navigationViewportBounds = default(RectangleF);
                _leftButtonBounds = default(RectangleF);
                _rightButtonBounds = default(RectangleF);
                _indicatorBounds = default(RectangleF);
                _indicatorButtonBounds = new RectangleF[0];
                _leftButton.SetVisible(false);
                _indicatorControl.SetVisible(false);
                _rightButton.SetVisible(false);
                return;
            }

            float targetPageWidth = Math.Max(1f, PageWidthPixels * scale);
            int fullViewVisiblePages = Math.Max(1, Math.Min(pageCount, (int)Math.Floor(ViewBox.Width / targetPageWidth)));
            bool allPagesFitInView = fullViewVisiblePages >= pageCount;
            CalculateViewports(pageCount, scale, allPagesFitInView);

            int fullWidthVisiblePages = Math.Max(1, Math.Min(pageCount, (int)Math.Floor(ContentViewportBounds.Width / targetPageWidth)));
            bool allPagesFit = fullWidthVisiblePages >= pageCount;
            _visiblePageCount = fullWidthVisiblePages;
            float pageWidth = allPagesFit ? ContentViewportBounds.Width / _visiblePageCount : Math.Min(targetPageWidth, ContentViewportBounds.Width / _visiblePageCount);
            float totalWidth = pageWidth * _visiblePageCount;
            float startX = ContentViewportBounds.X + Math.Max(0f, (ContentViewportBounds.Width - totalWidth) * 0.5f);
            _firstVisiblePage = NormalizePageIndex(_firstVisiblePage, pageCount);

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child == null || IsNavigationControl(child))
                    continue;

                child.SetVisible(false);
            }

            for (int visibleIndex = 0; visibleIndex < _visiblePageCount; visibleIndex++)
            {
                int pageIndex = NormalizePageIndex(_firstVisiblePage + visibleIndex, pageCount);
                var child = GetPageAtIndex(pageIndex) as ControlTemplate;
                if (child == null)
                    continue;

                child.SetVisible(true);
                child.Arrange(new RectangleF(startX + visibleIndex * pageWidth, ContentViewportBounds.Y, pageWidth, ContentViewportBounds.Height));
            }

            ArrangeControls(scale);
            _leftButton.SetVisible(_showControls);
            _indicatorControl.SetVisible(_showControls);
            _rightButton.SetVisible(_showControls);
        }

        int ConfigureProvidedPages(float scale)
        {
            if (PageProvider == null)
                return GetPageCount();

            int pageCount = Math.Max(0, PageProvider(ViewBox));
            for (int i = 0; i < 3; i++)
            {
                float targetPageWidth = Math.Max(1f, PageWidthPixels * scale);
                int fullViewVisiblePages = Math.Max(1, Math.Min(Math.Max(1, pageCount),
                    (int)Math.Floor(ViewBox.Width / targetPageWidth)));
                bool allPagesFit = pageCount > 0 && fullViewVisiblePages >= pageCount;
                RectangleF contentBounds = CalculateContentViewport(pageCount, scale, allPagesFit);
                int nextPageCount = Math.Max(0, PageProvider(contentBounds));
                if (nextPageCount == pageCount)
                    return pageCount;

                pageCount = nextPageCount;
            }

            return pageCount;
        }

        void CalculateViewports(int pageCount, float scale, bool allPagesFit)
        {
            float controlGap = CONTROL_GAP_PIXELS * scale;
            float controlRowHeight = CONTROL_ROW_HEIGHT_PIXELS * scale;
            _showControls = pageCount > 1 && !allPagesFit && ViewBox.Height > GetNavigationHeight(scale);

            if (_showControls)
            {
                ContentViewportBounds = CalculateContentViewport(pageCount, scale, allPagesFit);
                _navigationViewportBounds = new RectangleF(
                    ViewBox.X,
                    ContentViewportBounds.Bottom + controlGap,
                    ViewBox.Width,
                    controlRowHeight);
                return;
            }

            ContentViewportBounds = ViewBox;
            _navigationViewportBounds = default(RectangleF);
        }

        RectangleF CalculateContentViewport(int pageCount, float scale, bool allPagesFit)
        {
            float controlGap = CONTROL_GAP_PIXELS * scale;
            float controlRowHeight = CONTROL_ROW_HEIGHT_PIXELS * scale;
            bool showControls = pageCount > 1 && !allPagesFit && ViewBox.Height > GetNavigationHeight(scale);
            if (!showControls)
                return ViewBox;

            return new RectangleF(
                ViewBox.X,
                ViewBox.Y,
                ViewBox.Width,
                Math.Max(1f, ViewBox.Height - controlRowHeight - controlGap));
        }

        void ArrangeControls(float scale)
        {
            if (!_showControls)
            {
                _leftButtonBounds = default(RectangleF);
                _rightButtonBounds = default(RectangleF);
                _indicatorBounds = default(RectangleF);
                _indicatorButtonBounds = new RectangleF[0];
                return;
            }

            float buttonWidth = BUTTON_WIDTH_PIXELS * scale;
            float indicatorWidth = INDICATOR_WIDTH_PIXELS * scale;
            float controlGap = CONTROL_GAP_PIXELS * scale;
            float maxGroupWidth = Math.Max(1f, _navigationViewportBounds.Width);
            float groupWidth = buttonWidth * 2f + indicatorWidth + controlGap * 2f;
            if (groupWidth > maxGroupWidth)
            {
                buttonWidth = Math.Max(18f * scale, Math.Min(buttonWidth, maxGroupWidth * 0.22f));
                indicatorWidth = Math.Max(1f, maxGroupWidth - buttonWidth * 2f - controlGap * 2f);
                groupWidth = buttonWidth * 2f + indicatorWidth + controlGap * 2f;
            }

            float x = _navigationViewportBounds.X + Math.Max(0f, (_navigationViewportBounds.Width - groupWidth) * 0.5f);
            _leftButtonBounds = new RectangleF(x, _navigationViewportBounds.Y, buttonWidth, _navigationViewportBounds.Height);
            _indicatorBounds = new RectangleF(_leftButtonBounds.Right + controlGap, _navigationViewportBounds.Y, indicatorWidth, _navigationViewportBounds.Height);
            _rightButtonBounds = new RectangleF(_indicatorBounds.Right + controlGap, _navigationViewportBounds.Y, buttonWidth, _navigationViewportBounds.Height);
            ArrangeIndicatorButtons(scale);
        }

        void ArrangeIndicatorButtons(float scale)
        {
            int pageCount = GetPageCount();
            if (pageCount <= 0 || _indicatorBounds.Width <= 0f || _indicatorBounds.Height <= 0f)
            {
                _indicatorButtonBounds = new RectangleF[0];
                return;
            }

            float diameter;
            float gap;
            float totalWidth;
            GetIndicatorLayout(scale, pageCount, out diameter, out gap, out totalWidth);

            if (_indicatorButtonBounds.Length != pageCount)
                _indicatorButtonBounds = new RectangleF[pageCount];

            float hitSize = Math.Min(_indicatorBounds.Height, Math.Max(diameter + gap, 16f * scale));
            float startX = _indicatorBounds.Center.X - totalWidth * 0.5f + diameter * 0.5f;
            for (int i = 0; i < pageCount; i++)
            {
                var center = new Vector2(startX + i * (diameter + gap), _indicatorBounds.Center.Y);
                _indicatorButtonBounds[i] = new RectangleF(center.X - hitSize * 0.5f, center.Y - hitSize * 0.5f, hitSize, hitSize);
            }
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            EnsureLayout();
            RenderPageChildren(sprites);
            RenderControls(sprites);
        }

        void RenderPageChildren(List<MySprite> sprites)
        {
            var children = Children;
            if (children == null)
                return;

            BeginContentClip(sprites, ContentViewportBounds);
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i] as ControlTemplate;
                if (child != null && !IsNavigationControl(child))
                    child.Render(sprites);
            }

            EndContentClip(sprites);
        }

        protected override bool HitCore(Vector2 point)
        {
            return ViewBox.Contains(point);
        }

        protected override bool CanResolveChildren(Vector2 point, bool selfHit)
        {
            return selfHit;
        }

        void RenderControls(List<MySprite> sprites)
        {
            if (!_showControls || sprites == null)
                return;

            RenderArrow(sprites, _leftButtonBounds, "LeftArrow", CanMove(-1));
            RenderIndicators(sprites);
            RenderArrow(sprites, _rightButtonBounds, "RightArrow", CanMove(1));
        }

        void RenderArrow(List<MySprite> sprites, RectangleF rect, string texture, bool enabled)
        {
            var fg = ResolveColor(ThemeResources.OnSurfaceColor);
            var color = enabled
                ? new Color(fg.R, fg.G, fg.B, 180)
                : new Color(fg.R, fg.G, fg.B, 45);
            float iconSize = Math.Min(Math.Min(rect.Width, rect.Height) * 0.72f, ARROW_ICON_PIXELS * LayoutScale);

            sprites.Add(new MySprite
            {
                Type = SpriteType.TEXTURE,
                Data = texture,
                Position = rect.Center,
                Size = new Vector2(iconSize, iconSize),
                Color = color,
                Alignment = TextAlignment.CENTER,
            });
        }

        void RenderIndicators(List<MySprite> sprites)
        {
            int pageCount = GetPageCount();
            if (pageCount <= 0 || _indicatorBounds.Width <= 0f || _indicatorBounds.Height <= 0f)
                return;

            float scale = Math.Max(0.01f, LayoutScale);
            float diameter;
            float gap;
            float totalWidth;
            GetIndicatorLayout(scale, pageCount, out diameter, out gap, out totalWidth);

            var hostFontColor = TextSurface?.ScriptForegroundColor ?? ResolveColor(ThemeResources.OnSurfaceColor);
            var primary = GetResourceColor(ThemeResources.SecondaryContainerColor, hostFontColor);
            var secondary = GetResourceColor(ThemeResources.AccentColor, new Color(hostFontColor.R, hostFontColor.G, hostFontColor.B, 150));
            var hollow = hostFontColor;
            float startX = _indicatorBounds.Center.X - totalWidth * 0.5f + diameter * 0.5f;
            for (int i = 0; i < pageCount; i++)
            {
                bool selected = i == _firstVisiblePage;
                bool visible = IsPageVisible(i, pageCount);
                var position = new Vector2(startX + i * (diameter + gap), _indicatorBounds.Center.Y);
                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = selected || visible ? "Circle" : "CircleHollow",
                    Position = position,
                    Size = new Vector2(diameter),
                    Color = selected ? hollow : visible ? secondary : hollow,
                    Alignment = TextAlignment.CENTER
                });

                if (!selected)
                    continue;

                sprites.Add(new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "Circle",
                    Position = position,
                    Size = new Vector2(Math.Max(1f, diameter * 0.75f)),
                    Color = primary,
                    Alignment = TextAlignment.CENTER
                });
            }
        }

        void GetIndicatorLayout(float scale, int pageCount, out float diameter, out float gap, out float totalWidth)
        {
            diameter = Math.Min(INDICATOR_DOT_PIXELS * scale, Math.Min(_indicatorBounds.Width, _indicatorBounds.Height) * 0.45f);
            gap = INDICATOR_DOT_GAP_PIXELS * scale;
            totalWidth = pageCount * diameter + Math.Max(0, pageCount - 1) * gap;
            if (totalWidth <= _indicatorBounds.Width)
                return;

            float unit = _indicatorBounds.Width / Math.Max(1f, pageCount + Math.Max(0, pageCount - 1) * 0.75f);
            diameter = Math.Max(2f * scale, unit);
            gap = Math.Max(1f * scale, unit * 0.75f);
            totalWidth = pageCount * diameter + Math.Max(0, pageCount - 1) * gap;
        }

        static int NormalizePageIndex(int value, int pageCount)
        {
            if (pageCount <= 0)
                return 0;

            int result = value % pageCount;
            return result < 0 ? result + pageCount : result;
        }

        int GetPageCount()
        {
            var children = Children;
            if (children == null)
                return 0;

            int count = 0;
            for (int i = 0; i < children.Count; i++)
            {
                if (!IsNavigationControl(children[i]))
                    count++;
            }

            return count;
        }

        bool IsPageVisible(int pageIndex, int pageCount)
        {
            if (pageCount <= 0)
                return false;

            for (int i = 0; i < _visiblePageCount; i++)
            {
                if (NormalizePageIndex(_firstVisiblePage + i, pageCount) == pageIndex)
                    return true;
            }

            return false;
        }

        Control GetPageAtIndex(int pageIndex)
        {
            var children = Children;
            if (children == null || pageIndex < 0)
                return null;

            int currentPageIndex = 0;
            for (int i = 0; i < children.Count; i++)
            {
                var current = children[i];
                if (IsNavigationControl(current))
                    continue;
                if (currentPageIndex == pageIndex)
                    return current;
                currentPageIndex++;
            }

            return null;
        }

        static bool IsNavigationControl(Control control)
        {
            return control is ArrowControl || control is IndicatorControl;
        }

        bool TryGetIndicatorPage(Vector2 point, out int pageIndex)
        {
            pageIndex = -1;
            if (!_showControls || _indicatorButtonBounds == null)
                return false;

            for (int i = 0; i < _indicatorButtonBounds.Length; i++)
            {
                if (_indicatorButtonBounds[i].Contains(point))
                {
                    pageIndex = i;
                    return true;
                }
            }

            return false;
        }

        void EnsureControlsZOrder()
        {
            var children = Children;
            if (children == null || _leftButton == null || _rightButton == null)
                return;

            MoveChild(_leftButton, Math.Max(0, children.Count - 3));
            MoveChild(_indicatorControl, Math.Max(0, children.Count - 2));
            MoveChild(_rightButton, Math.Max(0, children.Count - 1));
        }

        bool TryGetButtonBounds(int direction, out RectangleF bounds)
        {
            bounds = direction < 0 ? _leftButtonBounds : _rightButtonBounds;
            return _showControls && bounds.Width > 0f && bounds.Height > 0f;
        }

        bool CanMove(int direction)
        {
            return _showControls && GetPageCount() > 1;
        }

        bool CanSelectIndicator()
        {
            return _showControls && GetPageCount() > 1;
        }

        sealed class ArrowControl : ControlTemplate
        {
            readonly PagesPanel _owner;
            readonly int _direction;

            public ArrowControl(PagesPanel owner, int direction)
                : base(CursorType.Hand, owner)
            {
                _owner = owner;
                _direction = direction;
                SetClickOnPress();
                SetOnClick((dataContext, sender) => { });
            }

            public override RectangleF Bounds
            {
                get
                {
                    RectangleF bounds;
                    return _owner != null && _owner.TryGetButtonBounds(_direction, out bounds) ? bounds : default(RectangleF);
                }
            }

            public override bool CanPrimaryClick => Visible && _owner != null && _owner.CanMove(_direction);

            public override bool ClickAt(Vector2 point, object sender)
            {
                return _owner != null && _owner.MovePage(_direction);
            }

            public override bool Click(object sender)
            {
                return _owner != null && _owner.MovePage(_direction);
            }

            protected override void RenderDefault(List<MySprite> sprites)
            {
            }

            protected override bool HitCore(Vector2 point)
            {
                RectangleF bounds;
                return _owner != null && _owner.TryGetButtonBounds(_direction, out bounds) && bounds.Contains(point);
            }
        }

        sealed class IndicatorControl : ControlTemplate
        {
            readonly PagesPanel _owner;

            public IndicatorControl(PagesPanel owner)
                : base(CursorType.Hand, owner)
            {
                _owner = owner;
                SetClickOnPress();
                SetOnClick((dataContext, sender) => { });
            }

            public override RectangleF Bounds => _owner?._indicatorBounds ?? default(RectangleF);

            public override bool CanPrimaryClick => Visible && _owner != null && _owner.CanSelectIndicator();

            public override bool ClickAt(Vector2 point, object sender)
            {
                int pageIndex;
                return _owner != null &&
                       _owner.TryGetIndicatorPage(point, out pageIndex) &&
                       _owner.SelectPage(pageIndex);
            }

            public override bool Click(object sender)
            {
                return false;
            }

            protected override void RenderDefault(List<MySprite> sprites)
            {
            }

            protected override bool HitCore(Vector2 point)
            {
                int pageIndex;
                return _owner != null && _owner.TryGetIndicatorPage(point, out pageIndex);
            }
        }
    }
}
