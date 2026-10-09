using System;
using Adk.Gui.Controls;
using Adk.Gui.Core;
using LcdMod.Common.Mvvm;
using VRageMath;

namespace LcdMod.Client.Apps.Generators
{
    internal sealed partial class GeneratorsView : Views.CardGridView
    {
        const float RANGE_MIN_WIDTH = 64f;
        const float RANGE_TEXT_PADDING = 34f;
        const float RANGE_MIN_HEIGHT = 28f;
        static readonly int[] RangeTiers = { 0, 1, 2, 3, 4, 5 };

        ComboBox<int> _rangeComboBox;
        GeneratorsViewModel _observed;

        public GeneratorsView()
        {
            InitializeComponent();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var viewModel = DataContext as GeneratorsViewModel;
            if (viewModel != null)
            {
                Observe(viewModel);
                SyncRangeComboBox();
            }
            return base.MeasureOverride(availableSize);
        }

        partial void OnHotReloaded()
        {
            if (_rangeComboBox != null)
                RangeHost.Child = _rangeComboBox;
        }

        void Observe(GeneratorsViewModel viewModel)
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
            if (property == nameof(GeneratorsViewModel.RangeTier) || property == nameof(GeneratorsViewModel.FontScale))
                SyncRangeComboBox();
        }

        void SyncRangeComboBox()
        {
            var viewModel = _observed;
            if (viewModel == null)
                return;
            if (_rangeComboBox == null)
            {
                _rangeComboBox = new ComboBox<int>(RangeTiers, EnergyDashboardApp.GetScaleLabel,
                    viewModel.SetRangeTier)
                {
                    OpenDirection = ComboBoxOpenDirection.Up
                };
                RangeHost.Child = _rangeComboBox;
            }
            _rangeComboBox.SetSelectedValue(viewModel.RangeTier);
            float fontSize = viewModel.FontScale * 0.72f;
            _rangeComboBox.TextScale = fontSize;
            _rangeComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            SizeRangeComboBox(fontSize);
        }

        void SizeRangeComboBox(float fontSize)
        {
            if (TextMetrics == null)
                return;
            float width = 0f, height = 0f;
            foreach (int tier in RangeTiers)
            {
                Vector2 size = TextMetrics.Measure("White", EnergyDashboardApp.GetScaleLabel(tier), fontSize);
                width = Math.Max(width, size.X);
                height = Math.Max(height, size.Y);
            }
            _rangeComboBox.Width = Math.Max(RANGE_MIN_WIDTH, width + RANGE_TEXT_PADDING);
            _rangeComboBox.Height = Math.Max(RANGE_MIN_HEIGHT, height + 10f);
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
