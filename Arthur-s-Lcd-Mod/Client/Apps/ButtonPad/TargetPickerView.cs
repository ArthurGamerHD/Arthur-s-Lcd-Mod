using Adk.Gui.Controls;
using Adk.Gui.Core;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using VRageMath;

namespace LcdMod.Client.Apps.ButtonPad
{
    internal sealed partial class TargetPickerView : Window
    {
        readonly TargetPickerViewModel _model;
        readonly ComboBox<PickActionTargetKind> _kind;

        public TargetPickerView(TargetPickerViewModel model)
        {
            _model = model;
            DataContext = model;
            InitializeComponent();
            _kind = new ComboBox<PickActionTargetKind>(TargetPickerViewModel.Kinds, TargetPickerViewModel.KindLabel, model.SelectKind)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _kind.SetSelectedValue(model.Kind);
            KindHost.Child = _kind;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            Border host = _model.IsCompact ? CompactKindHost : KindHost;
            if (!ReferenceEquals(_kind.Parent, host))
            {
                KindHost.Child = null;
                CompactKindHost.Child = null;
                host.Child = _kind;
            }
            _kind.TextScale = _model.FontSize;
            return base.MeasureOverride(availableSize);
        }
    }
}
