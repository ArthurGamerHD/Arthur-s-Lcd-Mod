using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Panels
{
    /// <summary>
    /// Horizontal page host. Pages are 480px scaled by default; when the bounds
    /// cannot fit all pages, only a window of pages is visible and side arrows
    /// switch the active window.
    /// </summary>
    public sealed class CarouselPanel : Panel
    {
        const float DEFAULT_PAGE_WIDTH_PIXELS = 480f;
        const float ARROW_WIDTH_PIXELS = 28f;
        const float ARROW_ICON_PIXELS = 18f;
        const float ARROW_ACTIVE_WIDTH_RATIO = 156f / 256f;

        RectangleF _leftArrowBounds;
        RectangleF _rightArrowBounds;
        bool _showArrows;
        int _firstVisiblePage;
        int _visiblePageCount = 1;
        readonly ArrowControl _leftArrow;
        readonly ArrowControl _rightArrow;

        public CarouselPanel()
            : base(default(RectangleF), CursorType.Default)
        {
            _leftArrow = new ArrowControl(this, -1);
            _rightArrow = new ArrowControl(this, 1);
            base.AddChild(_leftArrow);
            base.AddChild(_rightArrow);
        }

        public float PageWidthPixels { get; set; } = DEFAULT_PAGE_WIDTH_PIXELS;
        public float LayoutScale { get; set; } = 1f;
        public Action<int> PageChanged { get; set; }

        public override void AddChild(ControlBase child)
        {
            base.AddChild(child);
            EnsureArrowZOrder();
        }

        public int FirstVisiblePage
        {
            get { return _firstVisiblePage; }
            set
            {
                int next = ClampFirstVisiblePage(value, _visiblePageCount);
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
            if (!_showArrows)
                return false;

            if (direction < 0 && _firstVisiblePage > 0)
            {
                FirstVisiblePage = _firstVisiblePage - 1;
                return true;
            }

            int maxFirst = ClampFirstVisiblePage(int.MaxValue, _visiblePageCount);
            if (direction > 0 && _firstVisiblePage < maxFirst)
            {
                FirstVisiblePage = _firstVisiblePage + 1;
                return true;
            }

            return false;
        }

        protected override void ArrangeChildren()
        {
            var children = Children;
            int pageCount = GetPageCount();
            if (pageCount <= 0)
            {
                _showArrows = false;
                _visiblePageCount = 1;
                _leftArrow.SetVisible(false);
                _rightArrow.SetVisible(false);
                return;
            }

            float scale = Math.Max(0.01f, LayoutScale);
            float targetPageWidth = Math.Max(1f, PageWidthPixels * scale);
            int fullWidthVisiblePages = Math.Max(1, Math.Min(pageCount, (int)Math.Floor(Rect.Width / targetPageWidth)));
            bool allPagesFit = fullWidthVisiblePages >= pageCount;

            float arrowWidth = 0f;
            float contentX = Rect.X;
            float contentWidth = Rect.Width;
            if (allPagesFit)
            {
                _showArrows = false;
                _visiblePageCount = pageCount;
                _firstVisiblePage = 0;
            }
            else
            {
                _showArrows = true;
                arrowWidth = Math.Min(Rect.Width * 0.18f, Math.Max(18f * scale, ARROW_WIDTH_PIXELS * scale));
                contentX = Rect.X + arrowWidth;
                contentWidth = Math.Max(1f, Rect.Width - arrowWidth * 2f);
                _visiblePageCount = Math.Max(1, Math.Min(pageCount, (int)Math.Floor(contentWidth / targetPageWidth)));
                _firstVisiblePage = ClampFirstVisiblePage(_firstVisiblePage, _visiblePageCount);
            }

            float pageWidth = allPagesFit ? contentWidth / _visiblePageCount : Math.Min(targetPageWidth, contentWidth / _visiblePageCount);
            float totalWidth = pageWidth * _visiblePageCount;
            float startX = contentX + Math.Max(0f, (contentWidth - totalWidth) * 0.5f);

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child == null || IsArrowControl(child))
                    continue;

                int pageIndex = GetPageIndex(child);
                bool visible = pageIndex >= _firstVisiblePage && pageIndex < _firstVisiblePage + _visiblePageCount;
                child.SetVisible(visible);
                if (!visible)
                    continue;

                int visibleIndex = pageIndex - _firstVisiblePage;
                child.Arrange(new RectangleF(startX + visibleIndex * pageWidth, Rect.Y, pageWidth, Rect.Height));
            }

            if (_showArrows)
            {
                float activeArrowWidth = Math.Max(1f, arrowWidth * ARROW_ACTIVE_WIDTH_RATIO);
                _leftArrowBounds = new RectangleF(Rect.X + (arrowWidth - activeArrowWidth) * 0.5f, Rect.Y, activeArrowWidth, Rect.Height);
                _rightArrowBounds = new RectangleF(Rect.Right - arrowWidth + (arrowWidth - activeArrowWidth) * 0.5f, Rect.Y, activeArrowWidth, Rect.Height);
            }
            else
            {
                _leftArrowBounds = default(RectangleF);
                _rightArrowBounds = default(RectangleF);
            }

            _leftArrow.SetVisible(_showArrows);
            _rightArrow.SetVisible(_showArrows);
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            EnsureLayout();
            RenderChildren(context, sprites);
            RenderArrows(context, sprites);
        }

        protected override bool HitCore(Vector2 point)
        {
            return Rect.Contains(point);
        }

        protected override bool CanResolveChildren(Vector2 point, bool selfHit)
        {
            return selfHit;
        }

        void RenderArrows(ControlRenderContext context, List<MySprite> sprites)
        {
            if (!_showArrows || sprites == null || context == null)
                return;

            RenderArrow(context, sprites, _leftArrowBounds, "LeftArrow", _firstVisiblePage > 0);
            RenderArrow(context, sprites, _rightArrowBounds, "RightArrow", _firstVisiblePage < ClampFirstVisiblePage(int.MaxValue, _visiblePageCount));
        }

        void RenderArrow(ControlRenderContext context, List<MySprite> sprites, RectangleF rect, string texture, bool enabled)
        {
            var fg = context.TextColor;
            var color = enabled
                ? new Color(fg.R, fg.G, fg.B, 180)
                : new Color(fg.R, fg.G, fg.B, 45);
            float iconSize = Math.Min(Math.Min(rect.Width, rect.Height) * 0.72f, ARROW_ICON_PIXELS * context.Scale);

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

        int ClampFirstVisiblePage(int value, int visiblePageCount)
        {
            int pageCount = GetPageCount();
            int maxFirst = Math.Max(0, pageCount - Math.Max(1, visiblePageCount));
            if (value < 0)
                return 0;
            if (value > maxFirst)
                return maxFirst;
            return value;
        }

        int GetPageCount()
        {
            var children = Children;
            if (children == null)
                return 0;

            int count = 0;
            for (int i = 0; i < children.Count; i++)
            {
                if (!IsArrowControl(children[i]))
                    count++;
            }

            return count;
        }

        int GetPageIndex(ControlBase child)
        {
            var children = Children;
            if (children == null)
                return -1;

            int pageIndex = 0;
            for (int i = 0; i < children.Count; i++)
            {
                var current = children[i];
                if (IsArrowControl(current))
                    continue;
                if (ReferenceEquals(current, child))
                    return pageIndex;
                pageIndex++;
            }

            return -1;
        }

        static bool IsArrowControl(ControlBase control)
        {
            return control is ArrowControl;
        }

        void EnsureArrowZOrder()
        {
            var children = Children;
            if (children == null || _leftArrow == null || _rightArrow == null)
                return;

            MoveChild(_leftArrow, Math.Max(0, children.Count - 2));
            MoveChild(_rightArrow, Math.Max(0, children.Count - 1));
        }

        bool TryGetArrowBounds(int direction, out RectangleF bounds)
        {
            bounds = direction < 0 ? _leftArrowBounds : _rightArrowBounds;
            return _showArrows && bounds.Width > 0f && bounds.Height > 0f;
        }

        bool CanMove(int direction)
        {
            if (!_showArrows)
                return false;

            if (direction < 0)
                return _firstVisiblePage > 0;

            return _firstVisiblePage < ClampFirstVisiblePage(int.MaxValue, _visiblePageCount);
        }

        sealed class ArrowControl : ControlBase
        {
            readonly CarouselPanel _owner;
            readonly int _direction;

            public ArrowControl(CarouselPanel owner, int direction)
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
                    return _owner != null && _owner.TryGetArrowBounds(_direction, out bounds) ? bounds : default(RectangleF);
                }
            }

            public override bool CanPrimaryClick
            {
                get { return Visible && _owner != null && _owner.CanMove(_direction); }
            }

            public override bool ClickAt(Vector2 point, object sender)
            {
                return _owner != null && _owner.MovePage(_direction);
            }

            public override bool Click(object sender)
            {
                return _owner != null && _owner.MovePage(_direction);
            }

            protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
            {
            }

            protected override bool HitCore(Vector2 point)
            {
                RectangleF bounds;
                return _owner != null && _owner.TryGetArrowBounds(_direction, out bounds) && bounds.Contains(point);
            }
        }
    }
}
