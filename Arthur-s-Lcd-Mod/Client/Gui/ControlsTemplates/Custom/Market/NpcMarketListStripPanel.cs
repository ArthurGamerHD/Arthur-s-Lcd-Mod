using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized;
using LcdMod.Client.Gui.ControlsTemplates.Templates;
using LcdMod.Client.Market;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Custom.Market
{
    internal sealed class NpcMarketListStripPanel : Panel, IScrollContent2D
    {
        readonly List<NpcMarketListPage> _pages = new List<NpcMarketListPage>();
        readonly List<NpcMarketListPageContext> _pageContexts = new List<NpcMarketListPageContext>();
        readonly PageRepeater<NpcMarketListPageContext, NpcMarketListPanel> _pageRepeater;
        readonly IAppHost _host;
        IList<NpcMarketRow> _rows = new List<NpcMarketRow>();
        float _listWidth;
        int _rowsPerPage = 1;
        Vector2 _lastAvailableSize;
        RectangleF _viewportBounds;

        public NpcMarketListStripPanel(IAppHost host)
        {
            _host = host;
            _pageRepeater = new PageRepeater<NpcMarketListPageContext, NpcMarketListPanel>();
            _pageRepeater.ItemTemplate = Template.For<NpcMarketListPageContext>(CreatePageControl);
            _pageRepeater.BindControl = BindPageControl;
        }

        public IList<NpcMarketRow> Rows
        {
            get { return _rows; }
            set
            {
                _rows = value ?? new List<NpcMarketRow>();
                InvalidateLayout();
            }
        }

        public NpcMarketMode Mode { get; set; }
        public NpcMarketSortColumn SortColumn { get; set; }
        public bool SortDescending { get; set; }
        public float LogicalMinimumListWidth { get; set; }
        public float HorizontalGap { get; set; }
        public float RepeatedHeaderHeight { get; set; }
        public float RowHeight { get; set; }
        public float TextScale { get; set; }
        public Color MutedColor { get; set; }
        public Action<NpcMarketSortColumn> SortClicked { get; set; }
        public Action SearchClicked { get; set; }
        public Action<NpcMarketRowClickTarget> RowClicked { get; set; }

        public int PageCount => _pages.Count;
        public int RowsPerPage => _rowsPerPage;
        public int FirstVisiblePageIndex { get; private set; }
        public int LastVisiblePageIndex { get; private set; }
        public float ListWidth => _listWidth;

        public Vector2 MeasureContent(Vector2 availableSize)
        {
            _lastAvailableSize = availableSize;
            RebuildPages(availableSize);
            if (_pages.Count <= 0)
                return new Vector2(0f, availableSize.Y);

            var contentWidth = _pages.Count * _listWidth + Math.Max(0, _pages.Count - 1) * HorizontalGap;
            return new Vector2(contentWidth, availableSize.Y);
        }

        public int ConfigurePages(PagesPanel pagesPanel, RectangleF viewport)
        {
            if (pagesPanel == null)
                return 0;

            _viewportBounds = viewport;
            _lastAvailableSize = viewport.Size;
            RebuildPages(viewport.Size);
            pagesPanel.PageWidthPixels = _listWidth / Math.Max(0.01f, pagesPanel.LayoutScale);

            BuildPageContexts(viewport, 0, _pages.Count);
            int pageCount = _pageRepeater.BindTo(pagesPanel, _pageContexts);

            FirstVisiblePageIndex = _pages.Count > 0 ? 0 : -1;
            LastVisiblePageIndex = _pages.Count - 1;
            return pageCount;
        }

        public void ArrangeViewport(RectangleF viewport, Vector2 scrollOffsetPixels)
        {
            _viewportBounds = viewport;
            RebuildPages(viewport.Size);
            SetRect(new RectangleF(viewport.X - scrollOffsetPixels.X, viewport.Y - scrollOffsetPixels.Y,
                Math.Max(viewport.Width, _pages.Count * _listWidth + Math.Max(0, _pages.Count - 1) * HorizontalGap),
                viewport.Height));

            if (_pages.Count <= 0)
            {
                FirstVisiblePageIndex = 0;
                LastVisiblePageIndex = -1;
                return;
            }

            var stride = Math.Max(1f, _listWidth + HorizontalGap);
            var startPage = Math.Max(0, (int)Math.Floor(scrollOffsetPixels.X / stride));
            var remainder = Math.Max(0f, scrollOffsetPixels.X - startPage * stride);
            var visiblePageCount = Math.Min(_pages.Count - startPage,
                Math.Max(1, (int)Math.Ceiling((viewport.Width + remainder) / stride) + 1));
            FirstVisiblePageIndex = startPage;
            LastVisiblePageIndex = Math.Min(_pages.Count - 1, startPage + visiblePageCount - 1);
        }

        protected override bool HitCore(Vector2 point)
        {
            return _viewportBounds.Width > 0f && _viewportBounds.Height > 0f
                ? _viewportBounds.Contains(point)
                : base.HitCore(point);
        }

        ControlTemplate CreatePageControl(NpcMarketListPageContext context, int index)
        {
            return new NpcMarketListPanel(_host);
        }

        void BindPageControl(NpcMarketListPanel panel, NpcMarketListPageContext context, int index)
        {
            if (panel == null || context == null)
                return;

            panel.Configure(context);
        }

        void BuildPageContexts(RectangleF viewport, int startPage, int pageCount)
        {
            _pageContexts.Clear();

            for (var i = 0; i < pageCount; i++)
            {
                var pageIndex = startPage + i;
                if (pageIndex < 0 || pageIndex >= _pages.Count)
                    continue;

                var page = _pages[pageIndex];
                _pageContexts.Add(new NpcMarketListPageContext
                {
                    Host = _host,
                    Rows = _rows,
                    Page = page,
                    Mode = Mode,
                    SortColumn = SortColumn,
                    SortDescending = SortDescending,
                    HeaderHeight = RepeatedHeaderHeight,
                    RowHeight = RowHeight,
                    TextScale = TextScale,
                    LayoutScale = LayoutScale,
                    MutedColor = MutedColor,
                    SortClicked = SortClicked,
                    SearchClicked = SearchClicked,
                    RowClicked = RowClicked
                });
            }
        }

        void RebuildPages(Vector2 availableSize)
        {
            var minimum = Math.Max(1f, LogicalMinimumListWidth);
            var gap = Math.Max(0f, HorizontalGap);
            _listWidth = ResolveDistributedListWidth(availableSize.X, minimum, gap);
            _rowsPerPage = ResolveRowsPerPage(availableSize.Y, RepeatedHeaderHeight, RowHeight);
            _pages.Clear();

            var rowCount = _rows == null ? 0 : _rows.Count;
            if (rowCount <= 0)
                return;

            var pageCount = (int)Math.Ceiling(rowCount / (float)_rowsPerPage);
            for (var i = 0; i < pageCount; i++)
            {
                var start = i * _rowsPerPage;
                _pages.Add(new NpcMarketListPage
                {
                    PageIndex = i,
                    StartRowIndex = start,
                    RowCount = Math.Min(_rowsPerPage, rowCount - start),
                    Width = _listWidth,
                    Height = availableSize.Y
                });
            }
        }

        static int ResolveVisibleListCount(float viewportWidth, float minimumWidth, float gap)
        {
            return Math.Max(1, (int)Math.Floor((viewportWidth + gap) / (minimumWidth + gap)));
        }

        static float ResolveDistributedListWidth(float viewportWidth, float minimumWidth, float gap)
        {
            var visibleCount = ResolveVisibleListCount(viewportWidth, minimumWidth, gap);
            var distributed = (viewportWidth - gap * Math.Max(0, visibleCount - 1)) / visibleCount;
            return Math.Max(minimumWidth, distributed);
        }

        static int ResolveRowsPerPage(float viewportHeight, float repeatedHeaderHeight, float rowHeight)
        {
            return Math.Max(1, (int)Math.Floor(Math.Max(0f, viewportHeight - repeatedHeaderHeight) / Math.Max(1f, rowHeight)));
        }
    }
}
