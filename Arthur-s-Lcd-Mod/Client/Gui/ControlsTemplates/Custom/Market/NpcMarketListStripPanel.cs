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
        int _rowsRevision = -1;
        float _listWidth;
        int _rowsPerPage = 1;
        RectangleF _viewportBounds;
        PageLayoutKey _pageLayoutKey;
        bool _hasPageLayoutKey;

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
            set { SetRows(value, _rowsRevision + 1); }
        }

        public void SetRows(IList<NpcMarketRow> rows, int revision)
        {
            var normalizedRows = rows ?? new List<NpcMarketRow>();
            if (ReferenceEquals(_rows, normalizedRows) && _rowsRevision == revision)
                return;

            _rows = normalizedRows;
            _rowsRevision = revision;
            InvalidateLayout();
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
            RebuildPages(viewport.Size);
            pagesPanel.PageWidthPixels = _listWidth / Math.Max(0.01f, pagesPanel.LayoutScale);

            EnsurePageContexts();
            for (var i = 0; i < _pages.Count; i++)
            {
                if (pagesPanel.ShouldUpdatePage(i, _pages.Count, viewport))
                    UpdatePageContext(i);
            }

            int pageCount = _pageRepeater.BindTo(
                pagesPanel,
                _pageContexts,
                i => pagesPanel.ShouldUpdatePage(i, _pages.Count, viewport));

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

        void EnsurePageContexts()
        {
            while (_pageContexts.Count < _pages.Count)
                _pageContexts.Add(new NpcMarketListPageContext());

            while (_pageContexts.Count > _pages.Count)
                _pageContexts.RemoveAt(_pageContexts.Count - 1);
        }

        void UpdatePageContext(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= _pages.Count || pageIndex >= _pageContexts.Count)
                return;

            var context = _pageContexts[pageIndex];
            context.Host = _host;
            context.Rows = _rows;
            context.RowsRevision = _rowsRevision;
            context.Page = _pages[pageIndex];
            context.Mode = Mode;
            context.SortColumn = SortColumn;
            context.SortDescending = SortDescending;
            context.HeaderHeight = RepeatedHeaderHeight;
            context.RowHeight = RowHeight;
            context.TextScale = TextScale;
            context.LayoutScale = LayoutScale;
            context.MutedColor = MutedColor;
            context.SortClicked = SortClicked;
            context.SearchClicked = SearchClicked;
            context.RowClicked = RowClicked;
        }

        void RebuildPages(Vector2 availableSize)
        {
            var minimum = Math.Max(1f, LogicalMinimumListWidth);
            var gap = Math.Max(0f, HorizontalGap);
            var listWidth = ResolveDistributedListWidth(availableSize.X, minimum, gap);
            var rowsPerPage = ResolveRowsPerPage(availableSize.Y, RepeatedHeaderHeight, RowHeight);
            var key = new PageLayoutKey(
                _rowsRevision,
                _rows != null ? _rows.Count : 0,
                availableSize,
                minimum,
                gap,
                RepeatedHeaderHeight,
                RowHeight,
                listWidth,
                rowsPerPage);

            if (_hasPageLayoutKey && _pageLayoutKey.Equals(key))
                return;

            _pageLayoutKey = key;
            _hasPageLayoutKey = true;
            _listWidth = listWidth;
            _rowsPerPage = rowsPerPage;
            _pages.Clear();

            var rowCount = _rows?.Count ?? 0;
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

            EnsurePageContexts();
        }

        struct PageLayoutKey : IEquatable<PageLayoutKey>
        {
            readonly int _rowsRevision;
            readonly int _rowCount;
            readonly Vector2 _availableSize;
            readonly float _minimumWidth;
            readonly float _gap;
            readonly float _headerHeight;
            readonly float _rowHeight;
            readonly float _listWidth;
            readonly int _rowsPerPage;

            public PageLayoutKey(
                int rowsRevision,
                int rowCount,
                Vector2 availableSize,
                float minimumWidth,
                float gap,
                float headerHeight,
                float rowHeight,
                float listWidth,
                int rowsPerPage)
            {
                _rowsRevision = rowsRevision;
                _rowCount = rowCount;
                _availableSize = availableSize;
                _minimumWidth = minimumWidth;
                _gap = gap;
                _headerHeight = headerHeight;
                _rowHeight = rowHeight;
                _listWidth = listWidth;
                _rowsPerPage = rowsPerPage;
            }

            public bool Equals(PageLayoutKey other)
            {
                return _rowsRevision == other._rowsRevision &&
                       _rowCount == other._rowCount &&
                       _availableSize.Equals(other._availableSize) &&
                       _minimumWidth.Equals(other._minimumWidth) &&
                       _gap.Equals(other._gap) &&
                       _headerHeight.Equals(other._headerHeight) &&
                       _rowHeight.Equals(other._rowHeight) &&
                       _listWidth.Equals(other._listWidth) &&
                       _rowsPerPage == other._rowsPerPage;
            }

            public override bool Equals(object obj)
            {
                return obj is PageLayoutKey && Equals((PageLayoutKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _rowsRevision;
                    hash = (hash * 397) ^ _rowCount;
                    hash = (hash * 397) ^ _availableSize.GetHashCode();
                    hash = (hash * 397) ^ _minimumWidth.GetHashCode();
                    hash = (hash * 397) ^ _gap.GetHashCode();
                    hash = (hash * 397) ^ _headerHeight.GetHashCode();
                    hash = (hash * 397) ^ _rowHeight.GetHashCode();
                    hash = (hash * 397) ^ _listWidth.GetHashCode();
                    hash = (hash * 397) ^ _rowsPerPage;
                    return hash;
                }
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
