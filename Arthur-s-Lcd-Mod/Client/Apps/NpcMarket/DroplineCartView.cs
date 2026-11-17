using Acrux.Controls;
using VRageMath;

namespace LcdMod.Client.Apps.NpcMarket
{
    internal sealed partial class DroplineCartView : Window
    {
        readonly DroplineCartViewModel _model;

        public DroplineCartView()
        {
            InitializeComponent();
        }

        public DroplineCartView(DroplineCartViewModel model) : this()
        {
            _model = model;
            DataContext = model;
        }

        void OnBuyNowEntered(object sender, Acrux.Core.Input.PointerEventArgs e)
        {
            var model = DataContext as DroplineCartViewModel;
            if (model != null)
                model.PrepareBuy();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            if (_model != null)
                Destination.TextScale = _model.FontSize;
            return base.MeasureOverride(availableSize);
        }
    }
}
