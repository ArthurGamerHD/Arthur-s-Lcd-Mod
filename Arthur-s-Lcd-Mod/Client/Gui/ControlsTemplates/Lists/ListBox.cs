using System;
using System.Collections.Generic;
using LcdMod.Client.Gui.ControlsTemplates.Panels;
using LcdMod.Common.Helpers;
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
        ControlStyle _childStyle;

        public ControlStyle ChildStyle
        {
            get
            {
                return _childStyle;
            }
            set
            {
                _childStyle = value;
                MarkDirty();
            }
        }

        public ListBox(RectangleF bounds, ListBoxModel<T> model = null)
            : base(bounds, CursorType.Default, model ?? new ListBoxModel<T>())
        {
            _scrollPanel = new ScrollPanel();
            _scrollPanel.ManualScrollInertiaEnabled = false;
            AddChild(_scrollPanel);
            ConfigureScrollPanel();
        }

        public ListBoxModel<T> ListModel
        {
            get { return DataContext as ListBoxModel<T>; }
        }

        public ScrollPanel ScrollPanel
        {
            get { return _scrollPanel; }
        }

        public override void SetRect(RectangleF bounds)
        {
            base.SetRect(bounds);
            ConfigureScrollPanel();
        }

        protected override void RenderDefault(ControlRenderContext context, List<MySprite> sprites)
        {
            ConfigureScrollPanel();

            var listContext = CreateListRenderContext(context);
            var viewBox = GetViewBox();
            var backgroundColor = context.Style.GetPanelColor(false);
            Border.CreateSpritesFromRect(viewBox, sprites, backgroundColor,
                Border.ScaleRadius(context.Style.BorderRadiusPixels, context.Scale));

            BeginClip(sprites, _scrollPanel.ContentViewportBounds);
            RenderRows(listContext, sprites);
            EndClip(sprites);

            var outline = context.GetThemeColor(Constants.OUTLINE_VARIANT);
            var primary = context.GetThemeColor(Constants.PRIMARY);
            var trackColor = new Color(outline.R, outline.G, outline.B, 127);
            var thumbColor = new Color(primary.R, primary.G, primary.B, 250);
            _scrollPanel.SetScrollBarColors(trackColor, thumbColor);
            _scrollPanel.Render(listContext, sprites);
        }

        static ControlRenderContext CreateListRenderContext(ControlRenderContext context)
        {
            var containerColor = context.GetThemeColor(Constants.SECONDARY_CONTAINER);
            var hoverContainerColor = context.GetThemeColor(Constants.SECONDARY_CONTAINER + Constants.HOVER);
            var textColor = context.GetThemeColor(Constants.ON_SECONDARY_CONTAINER);
            var style = new ControlStyle(textColor, containerColor)
            {
                BorderRadiusPixels = context.Style.BorderRadiusPixels,
                HoverPanelColor = hoverContainerColor,
                HoverTextColor = textColor,
                Padding = context.Style.Padding
            };

            return new ControlRenderContext(
                context.Surface,
                context.Scale,
                context.FontScale,
                style,
                context.Theme,
                context.CursorPosition);
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
                    if(_childStyle != Style)
                        item.SetStyle(_childStyle);
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

        void RenderRows(ControlRenderContext context, List<MySprite> sprites)
        {
            var children = _scrollPanel.Children;
            if (children == null)
                return;

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null)
                    child.Render(context, sprites);
            }
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
    }
}
