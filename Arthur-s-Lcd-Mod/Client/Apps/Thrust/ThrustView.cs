using Acrux.Controls;
using System;
using Acrux.Core;
using Acrux.Rendering;
using Acrux.Styling;
using VRageMath;

namespace LcdMod.Client.Apps.Thrust
{
    internal sealed partial class ThrustView : UserControl
    {
        readonly ThrustViewModel _viewModel;

        public ThrustView()
        {
            Resources.Add("FontSizeMultiplier", new LcdMod.Client.Apps.Views.FontSizeMultiplierConverter());
            InitializeComponent();
        }

        // The cells bind the app's per-axis models; without them (a preview clone) the cells show their defaults.
        public ThrustView(ThrustViewModel viewModel) : this()
        {
            _viewModel = viewModel;
            BindCells(viewModel);
            DataContext = viewModel;
        }

        partial void OnHotReloaded()
        {
            if (_viewModel != null)
                BindCells(_viewModel);
        }

        void BindCells(ThrustViewModel viewModel)
        {
            // These model identities are stable for the life of the dashboard.
            // Give cells their local context before the parent context is inherited.
            // Templates bind to styled properties, so runtime replacement preserves
            // the model bindings and does not depend on template child names.
            BindCell(ForwardCell, viewModel.Forward);
            BindCell(BackwardCell, viewModel.Backward);
            BindCell(LeftCell, viewModel.Left);
            BindCell(RightCell, viewModel.Right);
            BindCell(UpCell, viewModel.Up);
            BindCell(DownCell, viewModel.Down);
            BindCell(SpeedCell, viewModel.Speed);
            BindCell(StopDistanceCell, viewModel.StopDistance);
            BindCell(StopTimeCell, viewModel.StopTime);
            BindCell(StopForceCell, viewModel.StopForce);
        }

        static void BindCell(ThrustLegendCellControl control, ThrustLegendEntry model)
        {
            control.Bind<ThrustLegendEntry, string>(source => source.LabelSuffix, value => control.LabelSuffix = value, nameof(ThrustLegendEntry.LabelSuffix));
            control.Bind<ThrustLegendEntry, string>(source => source.Value, value => control.Value = value, nameof(ThrustLegendEntry.Value));
            control.Bind<ThrustLegendEntry, string>(source => source.FontFamily, value => control.FontFamily = value, nameof(ThrustLegendEntry.FontFamily));
            control.Bind<ThrustLegendEntry, Color>(source => source.Foreground, value => control.Foreground = value, nameof(ThrustLegendEntry.Foreground));
            control.Bind<ThrustLegendEntry, float>(source => source.MarkerSize, value => control.MarkerSize = value, nameof(ThrustLegendEntry.MarkerSize));
            control.Bind<ThrustLegendEntry, Vector4>(source => source.MarkerMargin, value => control.MarkerMargin = value, nameof(ThrustLegendEntry.MarkerMargin));
            control.Bind<ThrustLegendEntry, Vector4>(source => source.LabelMargin, value => control.LabelMargin = value, nameof(ThrustLegendEntry.LabelMargin));
            control.Bind<ThrustLegendEntry, Vector4>(source => source.ValueMargin, value => control.ValueMargin = value, nameof(ThrustLegendEntry.ValueMargin));
            control.DataContext = model;
        }

        static void BindCell(ThrustTelemetryControl control, ThrustTelemetryEntry model)
        {
            control.Bind<ThrustTelemetryEntry, string>(source => source.Value, value => control.Value = value, nameof(ThrustTelemetryEntry.Value));
            control.Bind<ThrustTelemetryEntry, string>(source => source.FontFamily, value => control.FontFamily = value, nameof(ThrustTelemetryEntry.FontFamily));
            control.Bind<ThrustTelemetryEntry, Color>(source => source.Foreground, value => control.Foreground = value, nameof(ThrustTelemetryEntry.Foreground));
            control.Bind<ThrustTelemetryEntry, BackgroundBrush>(source => source.Background, value => control.Background = value, nameof(ThrustTelemetryEntry.Background));
            control.Bind<ThrustTelemetryEntry, float>(source => source.Width, value => control.BoxWidth = value, nameof(ThrustTelemetryEntry.Width));
            control.Bind<ThrustTelemetryEntry, float>(source => source.Height, value => control.BoxHeight = value, nameof(ThrustTelemetryEntry.Height));
            control.Bind<ThrustTelemetryEntry, Vector4>(source => source.TextMargin, value => control.TextMargin = value, nameof(ThrustTelemetryEntry.TextMargin));
            control.DataContext = model;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            // Refresh size-dependent state before measuring children, including
            // the first frame and host resizes without a telemetry update.
            (DataContext as ThrustViewModel)?.SetViewport(availableSize);
            return base.MeasureOverride(availableSize);
        }

        protected override IDisposable BeginChildDraw(RenderContext context, VisualElement child)
        {
            return context.PushClip(Bounds);
        }
    }
}
