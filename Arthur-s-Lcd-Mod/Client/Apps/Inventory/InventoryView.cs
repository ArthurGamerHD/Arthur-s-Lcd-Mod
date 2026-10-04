using Adk.Gui.Controls;
using System;
using System.ComponentModel;
using Adk.Gui.Core;
using Adk.Gui.Core.Invalidation;
using Adk.Gui.Rendering;
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

        public bool AdvanceAutoScroll(long frame, float secondsPerRow)
        {
            _frame = frame;
            if (_model == null || secondsPerRow <= 0f || frame <= _manualOverrideUntil || Scroller.MaxVerticalOffset <= 0f) return false;
            long framesPerRow = Math.Max(1L, (long)Math.Round(secondsPerRow * 60f));
            int lastRow = (int)Math.Floor(Scroller.MaxVerticalOffset / _model.CellHeight);
            float next = (frame / framesPerRow % (lastRow + 1)) * _model.CellHeight;
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
            var model = DataContext as InventoryViewModel;
            if (model != null)
            {
                model.SetViewport(availableSize);
            }
            return base.MeasureOverride(availableSize);
        }

        protected override IDisposable BeginChildDraw(RenderContext context, VisualElement child) => context.PushClip(Bounds);
    }
}
