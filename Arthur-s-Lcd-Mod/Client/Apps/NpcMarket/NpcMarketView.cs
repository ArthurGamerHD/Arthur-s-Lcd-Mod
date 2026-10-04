using System;
using Adk.Gui.Controls;
using LcdMod.Client.Market;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.NpcMarket
{
    internal sealed partial class NpcMarketView : UserControl
    {
        const float MODE_MIN_WIDTH = 82f;
        const float MODE_TEXT_PADDING = 34f;
        const float MODE_MIN_HEIGHT = 28f;

        ComboBox<NpcMarketMode> _modeComboBox;
        NpcMarketViewModel _observed;

        public NpcMarketView()
        {
            InitializeComponent();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var viewModel = DataContext as NpcMarketViewModel;
            if (viewModel != null)
            {
                Observe(viewModel);
                viewModel.SetViewport(availableSize);
                SetFontSize("marketCellFontSize", viewModel.FontScale * 0.72f);
                SetFontSize("marketHeaderFontSize", viewModel.FontScale * 0.58f);
                SetFontSize("marketFooterFontSize", viewModel.FontScale * 0.63f);
                SetFontSize("marketMessageFontSize", viewModel.FontScale);
                SyncModeComboBox();
            }
            return base.MeasureOverride(availableSize);
        }

        partial void OnHotReloaded()
        {
            if (_modeComboBox != null)
                ModeHost.Child = _modeComboBox;
        }

        void Observe(NpcMarketViewModel viewModel)
        {
            if (ReferenceEquals(_observed, viewModel))
                return;
            if (_observed != null)
                _observed.PropertyChanged -= OnViewModelChanged;
            _observed = viewModel;
            _observed.PropertyChanged += OnViewModelChanged;
        }

        void OnViewModelChanged(ObservableObject sender, string property)
        {
            SyncModeComboBox();
        }

        void SetFontSize(string key, float size)
        {
            float current;
            if (!Resources.TryGetValue(key, out current) || current != size)
                Resources.Add(key, size);
        }

        void SyncModeComboBox()
        {
            var viewModel = _observed;
            if (viewModel == null)
                return;
            if (_modeComboBox == null)
            {
                _modeComboBox = new ComboBox<NpcMarketMode>(NpcMarketApp.MarketModes, NpcMarketApp.GetModeLabel,
                    mode => viewModel.App.SetMode(mode))
                {
                    OpenDirection = ComboBoxOpenDirection.Up
                };
                ModeHost.Child = _modeComboBox;
            }
            _modeComboBox.SetSelectedValue(viewModel.App.Mode);
            _modeComboBox.Enabled = viewModel.ModeEnabled;
            _modeComboBox.TextScale = viewModel.FontScale * 0.72f;
            SizeModeComboBox(viewModel.FontScale * 0.72f);
        }

        void SizeModeComboBox(float fontSize)
        {
            if (TextMetrics == null)
                return;
            float width = 0f, height = 0f;
            foreach (NpcMarketMode mode in NpcMarketApp.MarketModes)
            {
                Vector2 size = TextMetrics.Measure("White", NpcMarketApp.GetModeLabel(mode), fontSize);
                width = Math.Max(width, size.X);
                height = Math.Max(height, size.Y);
            }
            _modeComboBox.Width = Math.Max(MODE_MIN_WIDTH, width + MODE_TEXT_PADDING);
            _modeComboBox.Height = Math.Max(MODE_MIN_HEIGHT, height + 10f);
        }

        public override void Dispose()
        {
            if (_observed != null)
                _observed.PropertyChanged -= OnViewModelChanged;
            _observed = null;
            base.Dispose();
        }
    }
}
