using System;
using System.Collections.Generic;
using LcdMod.Client.Apps.Abstract;
using LcdMod.Client.Gui;
using LcdMod.Client.Gui.ControlsTemplates;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.ControlsTemplates.Panels.Virtualized;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Market.Gui
{
    internal sealed class NpcMarketListStripPanel : Panel, IScrollContent2D
    {
        readonly List<NpcMarketListPage> _pages = new List<NpcMarketListPage>();
        readonly List<NpcMarketListPanel> _cards = new List<NpcMarketListPanel>();
        readonly IAppHost _host;
        IList<NpcMarketRow> _rows = new List<NpcMarketRow>();
        float _listWidth;
        int _rowsPerPage = 1;
        Vector2 _lastAvailableSize;
        RectangleF _viewportBounds;

        public NpcMarketListStripPanel(IAppHost host)
        {
            _host = host;
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
        public float LayoutScale { get; set; } = 1f;
        public Color MutedColor { get; set; }
        public ControlStyle SortHeaderStyle { get; set; }
        public Action<NpcMarketSortColumn> SortClicked { get; set; }
        public Action SearchClicked { get; set; }
        public Action<NpcMarketRowClickTarget> RowClicked { get; set; }

        public int PageCount { get { return _pages.Count; } }
        public int RowsPerPage { get { return _rowsPerPage; } }
        public int FirstVisiblePageIndex { get; private set; }
        public int LastVisiblePageIndex { get; private set; }
        public float ListWidth { get { return _listWidth; } }

        public Vector2 MeasureContent(Vector2 availableSize)
        {
            _lastAvailableSize = availableSize;
            RebuildPages(availableSize);
            if (_pages.Count <= 0)
                return new Vector2(0f, availableSize.Y);

            var contentWidth = _pages.Count * _listWidth + Math.Max(0, _pages.Count - 1) * HorizontalGap;
            return new Vector2(contentWidth, availableSize.Y);
        }

        public void ArrangeViewport(RectangleF viewport, Vector2 scrollOffsetPixels)
        {
            _viewportBounds = viewport;
            RebuildPages(viewport.Size);
            SetRect(new RectangleF(viewport.X - scrollOffsetPixels.X, viewport.Y - scrollOffsetPixels.Y,
                Math.Max(viewport.Width, _pages.Count * _listWidth + Math.Max(0, _pages.Count - 1) * HorizontalGap),
                viewport.Height));

            foreach (var card in _cards)
                card.SetVisible(false);

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

            EnsureCards(visiblePageCount);
            for (var i = 0; i < visiblePageCount; i++)
            {
                var pageIndex = startPage + i;
                if (pageIndex < 0 || pageIndex >= _pages.Count)
                    continue;

                var page = _pages[pageIndex];
                var card = _cards[i];
                var x = viewport.X + pageIndex * stride - scrollOffsetPixels.X;
                var rect = new RectangleF(x, viewport.Y, _listWidth, viewport.Height);
                card.Configure(rect, _rows, page, Mode, SortColumn, SortDescending, RepeatedHeaderHeight, RowHeight,
                    TextScale, LayoutScale, MutedColor, SortHeaderStyle);
                card.SortClicked = SortClicked;
                card.SearchClicked = SearchClicked;
                card.RowClicked = RowClicked;
                card.SetVisible(true);
            }
        }

        protected override bool HitCore(Vector2 point)
        {
            return _viewportBounds.Width > 0f && _viewportBounds.Height > 0f
                ? _viewportBounds.Contains(point)
                : base.HitCore(point);
        }

        void EnsureCards(int count)
        {
            while (_cards.Count < count)
            {
                var card = new NpcMarketListPanel(_host);
                _cards.Add(card);
                AddChild(card);
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
