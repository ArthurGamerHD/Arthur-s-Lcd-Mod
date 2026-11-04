using Acrux.Controls;
using Acrux.Core;
using LcdMod.Client.Gui.ControlsTemplates.Interactive;
using VRageMath;

namespace LcdMod.Client.Apps.ButtonPad
{
    internal sealed partial class TargetPickerView : Window
    {
        readonly TargetPickerViewModel _model;
        readonly ComboBox<PickActionTargetKind> _kind;

        // Any view model with the bound members works (the IDE preview's clone, for one); the kind picker needs the
        // app's model, so it is there only with the typed constructor.
        public TargetPickerView()
        {
            InitializeComponent();
        }

        public TargetPickerView(TargetPickerViewModel model) : this()
        {
            _model = model;
            DataContext = model;
            _kind = new ComboBox<PickActionTargetKind>(TargetPickerViewModel.Kinds, TargetPickerViewModel.KindLabel, model.SelectKind)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _kind.SetSelectedValue(model.Kind);
            KindHost.Child = _kind;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_kind == null)
                return base.MeasureOverride(availableSize);
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
