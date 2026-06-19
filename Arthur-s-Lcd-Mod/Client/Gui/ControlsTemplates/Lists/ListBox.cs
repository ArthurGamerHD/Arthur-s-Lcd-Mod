using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Client.Gui.Styling;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace LcdMod.Client.Gui.ControlsTemplates.Lists
{
    public sealed class ListBox<T> : RectangleControl
    {
        readonly ScrollPanel _scrollPanel;
        readonly Dictionary<int, ListBoxItemModel<T>> _rowModelsByIndex =
            new Dictionary<int, ListBoxItemModel<T>>();
        readonly Dictionary<int, ListBoxItem<T>> _rowControlsByIndex =
            new Dictionary<int, ListBoxItem<T>>();
        readonly List<int> _rowIndexesToRemove = new List<int>();
        ListBoxModel<T> _cachedListModel;

        public ListBox(RectangleF bounds, ListBoxModel<T> model = null)
            : base(bounds, CursorType.Default, model ?? new ListBoxModel<T>())
        {
            _scrollPanel = new ScrollPanel();
            _scrollPanel.ManualScrollInertiaEnabled = false;
            AddChild(_scrollPanel);
            ConfigureScrollPanel();
        }

        public ListBoxModel<T> ListModel => DataContext as ListBoxModel<T>;

        public ScrollPanel ScrollPanel => _scrollPanel;

        public override void SetRect(RectangleF bounds)
        {
            base.SetRect(bounds);
            ConfigureScrollPanel();
        }

        protected override void RenderDefault(List<MySprite> sprites)
        {
            ConfigureScrollPanel();

            ApplyListStyleScope();
            var viewBox = GetViewBox();
            var backgroundColor = GetRenderBackgroundColor();
            Border.CreateSpritesFromRect(viewBox, sprites, backgroundColor,
                Border.ScaleRadius(GetRenderBorderRadiusPixels(), LayoutScale));

            BeginContentClip(sprites, _scrollPanel.ContentViewportBounds);
            RenderRows(sprites);
            EndContentClip(sprites);

            _scrollPanel.Render(sprites);
        }

        void ApplyListStyleScope()
        {
            var containerColor = ResolveColor(ThemeResources.SecondaryContainerColor);
            var textColor = ResolveColor(ThemeResources.OnSecondaryContainerColor);

            var children = _scrollPanel.Children;
            if (children != null)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    var item = children[i] as ControlTemplate;
                    if (item == null)
                        continue;

                    item.BackgroundColor = containerColor;
                    item.TextColor = textColor;
                    item.BorderRadiusPixels = BorderRadiusPixels;
                    item.Padding = Padding;
                }
            }
        }

        void ConfigureScrollPanel()
        {
            var model = ListModel;
            float rowHeight = model != null && model.RowHeight > 0f ? model.RowHeight : 32f;
            float scrollerWidth = model != null && model.ScrollerWidthPixels > 0f ? model.ScrollerWidthPixels : 6f;
            int count = model == null ? 0 : model.Count;

            var viewBox = GetViewBox();
            _scrollPanel.Configure(viewBox, viewBox.Y, 0f, rowHeight, count, scrollerWidth, 0f);
            RebuildVisibleRows();
        }

        void RebuildVisibleRows()
        {
            _scrollPanel.ClearChildren();

            var model = ListModel;
            if (!ReferenceEquals(_cachedListModel, model))
            {
                _rowModelsByIndex.Clear();
                _rowControlsByIndex.Clear();
                _cachedListModel = model;
            }

            if (model == null || model.Count <= 0)
            {
                ClearRowCache();
                return;
            }

            int start = _scrollPanel.StartRow;
            int renderRows = _scrollPanel.RenderRows;
            int end = Math.Min(model.Count, start + renderRows);
            PruneRowCache(start, end);

            for (int itemIndex = start; itemIndex < end; itemIndex++)
            {
                int visibleIndex = itemIndex - start;
                var rowBounds = new RectangleF(
                    _scrollPanel.ContentViewportBounds.X,
                    _scrollPanel.ContentBounds.Y + visibleIndex * _scrollPanel.RowHeight,
                    _scrollPanel.ContentViewportBounds.Width,
                    _scrollPanel.RowHeight);

                ListBoxItemModel<T> itemModel;
                if (!_rowModelsByIndex.TryGetValue(itemIndex, out itemModel))
                {
                    itemModel = new ListBoxItemModel<T>(model, model.GetItem(itemIndex), itemIndex);
                    _rowModelsByIndex[itemIndex] = itemModel;
                }
                else
                {
                    itemModel.Update(model, model.GetItem(itemIndex), itemIndex);
                }

                ListBoxItem<T> item;
                if (!_rowControlsByIndex.TryGetValue(itemIndex, out item))
                {
                    item = new ListBoxItem<T>(rowBounds, itemModel);
                    _rowControlsByIndex[itemIndex] = item;
                }
                else
                {
                    item.SetRect(rowBounds);
                    item.SetDataContext(itemModel);
                }

                _scrollPanel.AddChild(item);
            }
        }

        void PruneRowCache(int start, int end)
        {
            _rowIndexesToRemove.Clear();
            foreach (var pair in _rowModelsByIndex)
            {
                if (pair.Key < start || pair.Key >= end)
                    _rowIndexesToRemove.Add(pair.Key);
            }

            for (int i = 0; i < _rowIndexesToRemove.Count; i++)
            {
                int key = _rowIndexesToRemove[i];
                _rowModelsByIndex.Remove(key);
                _rowControlsByIndex.Remove(key);
            }

            _rowIndexesToRemove.Clear();
        }

        void ClearRowCache()
        {
            _rowModelsByIndex.Clear();
            _rowControlsByIndex.Clear();
            _rowIndexesToRemove.Clear();
        }

        void RenderRows(List<MySprite> sprites)
        {
            var children = _scrollPanel.Children;
            if (children == null)
                return;

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i] as ControlTemplate;
                child?.Render(sprites);
            }
        }

    }
}
