using Acrux.Controls;
using Acrux.Layout;
using System;
using System.ComponentModel;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Rendering;
using VRageMath;

namespace LcdMod.Client.Apps.Inventory
{
    internal sealed partial class InventoryView : UserControl
    {
        InventoryViewModel _model;
        long _frame, _manualOverrideUntil = long.MinValue;
        bool _automaticScroll;
        public InventoryView()
        {
            Resources.Add("FontSizeMultiplier", new Views.FontSizeMultiplierConverter());
            Resources.Add("EnumClass", new Views.EnumClassConverter());
            Resources.Add("inventoryTableStripe", (Func<Color>)(() =>
            {
                Color color;
                return TryFindResource("surfaceContainerColor", out color) ? new Color(color, .18f) : Color.Transparent;
            }));
            InitializeComponent();
            WireScroller();
        }

        void WireScroller()
        {
            Scroller.ScrollChanged = _ => { if (!_automaticScroll) _manualOverrideUntil = _frame + 300; };
        }

        partial void OnHotReloaded()
        {
            WireScroller();
        }

        public InventoryView(InventoryViewModel model) : this()
        {
            _model = model;
            DataContext = model;
            ((INotifyPropertyChanged)model).PropertyChanged += OnModelChanged;
        }

        void OnModelChanged(object sender, PropertyChangedEventArgs args)
        {
            Invalidate(UiInvalidation.Measure);
        }

        InventoryLayout _layout;
        float _cellHeight = 30f;

        // The display mode (Layout="{Binding Layout}"), so the cell layout follows any view model with that member, a
        // preview clone too; the view's class (the same binding) restyles the items.
        public InventoryLayout Layout
        {
            get { return _layout; }
            set
            {
                if (_layout == value) return;
                _layout = value;
                Invalidate(UiInvalidation.Measure);
            }
        }

        float FontScale
        {
            get { float value; return TryFindResource("fontScale", out value) && value > 0f ? value : 1f; }
        }

        // The cells and headers, from the available size, the display mode and the font scale.
        bool ApplyLayout(Vector2 viewport)
        {
            float font = FontScale;
            bool tiles = InventoryLayouts.IsTiles(_layout);
            float headerHeight = 30f * font;
            float cellHeight = (tiles ? 90f : 30f) * font;
            float amountWidth = Math.Min(viewport.X, Math.Max(105f * font, viewport.X * .22f));
            int count = ItemList.ItemCount;
            float width = Math.Max(0f, viewport.X);
            int columns = Columns(width, tiles, font, count);
            float height = Math.Max(0f, viewport.Y - (tiles ? 0f : headerHeight));
            if (((count + columns - 1) / columns) * cellHeight > height)
            {
                width = Math.Max(0f, width - 5f);
                columns = Columns(width, tiles, font, count);
            }
            float cellWidth = Math.Max(0f, width / columns - .001f);

            _cellHeight = cellHeight;
            Header.IsVisible = !tiles;
            Header.Height = headerHeight;
            NameSort.Margin = new Vector4(38f * font, 0f, amountWidth, 0f);
            AmountSort.Width = amountWidth;
            var panel = ItemList.ItemsPanelRoot as WrapPanel;
            if (panel == null) return false;
            bool changed = panel.ItemWidth != cellWidth || panel.ItemHeight != cellHeight;
            panel.ItemWidth = cellWidth;
            panel.ItemHeight = cellHeight;
            return changed;
        }

        static int Columns(float width, bool tiles, float font, int count)
        {
            int columns = tiles ? Math.Max(1, (int)(width / (220f * font))) : 1;
            return Math.Min(columns, Math.Max(1, count));
        }

        public bool AdvanceAutoScroll(long frame, float secondsPerRow)
        {
            _frame = frame;
            if (_model == null || secondsPerRow <= 0f || frame <= _manualOverrideUntil || Scroller.MaxVerticalOffset <= 0f) return false;
            long framesPerRow = Math.Max(1L, (long)Math.Round(secondsPerRow * 60f));
            int lastRow = (int)Math.Floor(Scroller.MaxVerticalOffset / _cellHeight);
            float next = (frame / framesPerRow % (lastRow + 1)) * _cellHeight;
            if (Math.Abs(Scroller.Offset.Y - next) < .001f) return false;
            _automaticScroll = true;
            try { Scroller.Offset = new Vector2(0f, next); }
            finally { _automaticScroll = false; }
            return true;
        }

        public override void Dispose()
        {
            if (_model != null) ((INotifyPropertyChanged)_model).PropertyChanged -= OnModelChanged;
            _model = null;
            base.Dispose();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            ApplyLayout(availableSize);
            Vector2 size = base.MeasureOverride(availableSize);
            // The items panel exists once the items control has measured: size its cells and measure again.
            if (ApplyLayout(availableSize))
                size = base.MeasureOverride(availableSize);
            return size;
        }

        protected override IDisposable BeginChildDraw(RenderContext context, VisualElement child) => context.PushClip(Bounds);
    }
}
